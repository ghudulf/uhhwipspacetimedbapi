using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

public sealed class ApplicationCommandException(string code, string? traceId = null) : Exception(code)
{
    public string Code { get; } = code;
    public string? TraceId { get; } = traceId;
}
/// <summary>Only this exception permits choosing an HTTP adapter without reconciling a mutation.</summary>
public sealed class OperationNotDispatchedException(string operation) : Exception($"Operation {operation} was not dispatched.");

/// <summary>A single origin/session transport. No automatic mutation replay or HTTP fallback after dispatch.</summary>
public sealed record RealtimeDiagnostic(string Operation, string RequestId, string Outcome, double ElapsedMs, long Generation);

public sealed partial class RealtimeApplicationClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly SemaphoreSlim _slots = new(ApiProtocol.MaxOutstanding, ApiProtocol.MaxOutstanding);
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource<Capabilities> _welcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed record Pending(TaskCompletionSource<ResultEnvelope> Completion, bool Mutation);
    private Task? _receive;
    private Task? _heartbeat;
    private long _generation;
    private int _disposed;
    private int _connected;
    private long _countedRequests;
    public long CountedRequestsSent => Interlocked.Read(ref _countedRequests);
    public int OutstandingRequests => _pending.Count;
    public bool TryBeginRotation()
    {
        var reserved = 0;
        while (reserved < ApiProtocol.MaxOutstanding && _slots.Wait(0)) reserved++;
        if (reserved != ApiProtocol.MaxOutstanding)
        {
            if (reserved > 0) _slots.Release(reserved);
            return false;
        }
        // Hold all slots until disposal: callers that raced the connected check cannot dispatch.
        Volatile.Write(ref _connected, 0);
        return true;
    }
    private HashSet<string> _operations = new(StringComparer.Ordinal);
    private HashSet<string> _mutations = new(StringComparer.Ordinal);
    public bool IsConnected => Volatile.Read(ref _connected) == 1 && !_lifetime.IsCancellationRequested && _socket.State == WebSocketState.Open;
    public Capabilities? Capabilities { get; private set; }
    public event Action<RealtimeDiagnostic>? Diagnostic;

    public async Task ConnectAsync(Uri apiBase, string? token, CancellationToken cancellationToken)
    {
        if (_receive != null) throw new InvalidOperationException("Create a new transport when reconnecting.");
        if (!apiBase.IsAbsoluteUri || apiBase.UserInfo.Length != 0 || apiBase.Query.Length != 0 || apiBase.Fragment.Length != 0 ||
            (apiBase.Scheme != "https" && !(apiBase.Scheme == "http" && apiBase.IsLoopback)))
            throw new ArgumentException("Realtime requires HTTPS (HTTP is allowed only for loopback development).", nameof(apiBase));
        var endpoint = ClientEndpoints.WebSocketEndpoint(apiBase, "api/realtime/stream");
        _socket.Options.AddSubProtocol(ApiProtocol.Subprotocol);
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        // Authentication travels inside the encrypted session; no token in URLs, logs or proxy headers.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await _socket.ConnectAsync(endpoint, deadline.Token);
            if (_socket.SubProtocol != ApiProtocol.Subprotocol) throw new OperationNotDispatchedException("connect");
            _receive = ReceiveAsync();
            Capabilities = await _welcome.Task.WaitAsync(deadline.Token);
            if (Capabilities.Protocol != ApiProtocol.Subprotocol) throw new OperationNotDispatchedException("connect");
            _operations = Capabilities.Operations.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
            _mutations = Capabilities.Operations.Where(o => o.Mutation).Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
            Volatile.Write(ref _connected, 1);
            if (token != null) await CallAsync<AttachRequest, AuthenticationReply>("auth.attach", new(token), null, deadline.Token);
            _heartbeat = HeartbeatAsync();
        }
        catch { _lifetime.Cancel(); _socket.Abort(); throw; }
    }

    public Task<TResponse?> CallAsync<TRequest, TResponse>(string operation, TRequest payload, string? operationId, CancellationToken cancellationToken) =>
        CallCoreAsync<TRequest, TResponse>(operation, payload, operationId, cancellationToken, null);

    private async Task<TResponse?> CallCoreAsync<TRequest, TResponse>(string operation, TRequest payload, string? operationId,
        CancellationToken cancellationToken, Action? onDispatch)
    {
        if (!IsConnected || !_operations.Contains(operation)) throw new OperationNotDispatchedException(operation);
        if (!await _slots.WaitAsync(0, cancellationToken)) throw new OperationNotDispatchedException(operation);
        var requestId = Guid.NewGuid().ToString("N");
        var mutation = _mutations.Contains(operation);
        var pending = new Pending(new(TaskCreationOptions.RunContinuationsAsynchronously), mutation);
        var dispatched = false;
        var frameCompleted = false;
        var started = Stopwatch.GetTimestamp();
        var outcome = "not_dispatched";
        try
        {
            if (!IsConnected) throw new OperationNotDispatchedException(operation);
            if (mutation && !Guid.TryParseExact(operationId, "N", out _)) throw new ArgumentException("A stable mutation operation ID is required.", nameof(operationId));
            var command = new CommandEnvelope(requestId, operation, JsonSerializer.SerializeToElement(payload, ApiProtocol.Json), operationId);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(command, ApiProtocol.Json);
            if (bytes.Length > ApiProtocol.MaxMessageBytes) throw new ArgumentException("Command is too large.", nameof(payload));
            _pending[requestId] = pending;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(35));
            await _send.WaitAsync(deadline.Token);
            try
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (!IsConnected) throw new OperationNotDispatchedException(operation);
                dispatched = true; // A partially written frame is also uncertain.
                onDispatch?.Invoke();
                if (operation is not ("ping" or "capabilities")) Interlocked.Increment(ref _countedRequests);
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, deadline.Token);
                frameCompleted = true;
            }
            finally { _send.Release(); }
            var result = await pending.Completion.Task.WaitAsync(deadline.Token);
            outcome = result.Code ?? "success";
            if (!result.Success) throw new ApplicationCommandException(result.Code ?? "internal_error", result.TraceId);
            return result.Data is { } data ? data.Deserialize<TResponse>(ApiProtocol.Json) : default;
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            if (dispatched)
            {
                if (frameCompleted && !_lifetime.IsCancellationRequested && ex is OperationCanceledException)
                    await SendCancellationAsync(requestId);
                else
                {
                    // A partially sent frame cannot be safely reused.
                    _lifetime.Cancel();
                    _socket.Abort();
                }
                outcome = mutation ? "commit_outcome_unknown" : "cancelled_or_disconnected";
                if (mutation) throw new ApplicationCommandException("commit_outcome_unknown");
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationNotDispatchedException(operation);
        }
        catch (ApplicationCommandException ex) { outcome = ex.Code; throw; }
        finally
        {
            _pending.TryRemove(requestId, out _);
            _slots.Release();
            try { Diagnostic?.Invoke(new(operation, requestId, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds, Interlocked.Read(ref _generation))); }
            catch { /* Diagnostics must not change a completed operation's outcome. */ }
        }
    }

    private async Task SendCancellationAsync(string requestId)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await _send.WaitAsync(deadline.Token);
            try
            {
                var command = new CommandEnvelope(Guid.NewGuid().ToString("N"), "cancel", JsonSerializer.SerializeToElement(new CancelCommand(requestId), ApiProtocol.Json));
                Interlocked.Increment(ref _countedRequests);
                await _socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(command, ApiProtocol.Json), WebSocketMessageType.Text, true, deadline.Token);
            }
            finally { _send.Release(); }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        { _lifetime.Cancel(); _socket.Abort(); }
    }

    private async Task ReceiveAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                var buffer = new byte[8192];
                WebSocketReceiveResult part;
                do
                {
                    part = await _socket.ReceiveAsync(buffer, _lifetime.Token);
                    if (part.MessageType == WebSocketMessageType.Close) throw new WebSocketException("Connection closed.");
                    if (part.MessageType != WebSocketMessageType.Text || stream.Length + part.Count > ApiProtocol.MaxResponseBytes)
                        throw new JsonException("Invalid response frame.");
                    stream.Write(buffer, 0, part.Count);
                } while (!part.EndOfMessage);
                var result = JsonSerializer.Deserialize<ResultEnvelope>(stream.ToArray(), ApiProtocol.Json) ?? throw new JsonException("Missing envelope.");
                if (result.Type == "welcome")
                {
                    if (!_welcome.TrySetResult(result.Data?.Deserialize<Capabilities>(ApiProtocol.Json) ?? throw new JsonException("Missing capabilities.")))
                        throw new JsonException("Repeated welcome.");
                    continue;
                }
                if (result.Generation < Interlocked.Read(ref _generation)) continue;
                if (result.Generation > Interlocked.Read(ref _generation)) EndSubscriptions(result.Type == "session" ? result.Code ?? "session_changed" : "session_changed");
                Interlocked.Exchange(ref _generation, result.Generation);
                if (result.Type == "invalidation") { ReceiveInvalidation(result); continue; }
                if (result.Type == "session") { EndSubscriptions(result.Code ?? "session_changed"); continue; }
                if (result.RequestId != null && _pending.TryGetValue(result.RequestId, out var pending)) pending.Completion.TrySetResult(result);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException)
        {
            _welcome.TrySetException(new OperationNotDispatchedException("connect"));
        }
        finally
        {
            EndSubscriptions("connection_lost");
            Volatile.Write(ref _connected, 0);
            _lifetime.Cancel();
            foreach (var pending in _pending.Values)
                pending.Completion.TrySetException(new ApplicationCommandException(pending.Mutation ? "commit_outcome_unknown" : "connection_lost"));
        }
    }
    private async Task HeartbeatAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(25));
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                try { await CallAsync<EmptyRequest, JsonElement>("ping", new(), null, _lifetime.Token); }
                catch (OperationNotDispatchedException) when (IsConnected) { /* Busy commands already prove liveness. */ }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ApplicationCommandException or OperationNotDispatchedException or WebSocketException) { }
        finally { _lifetime.Cancel(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _socket.Abort();
        if (_receive != null) await _receive;
        if (_heartbeat != null) await _heartbeat;
        _socket.Dispose();
        // Semaphore/CTS disposal is intentionally deferred: in-flight callers may still be unwinding.
    }
}
