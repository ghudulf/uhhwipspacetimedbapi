using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using SpacetimeDB;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.V2;

/// <summary>One receiver, one command worker and one writer. Secrets are never logged or retained in receipts.</summary>
public sealed class ApiSocketSession(ApplicationOperations operations, IAuthWebSocketTokenValidator tokens,
    IAuthOrchestrationService authentication, ISpacetimeDBService database, ConnectionAdmission admission,
    ILogger<ApiSocketSession> logger, TicketSalesApp.AdminServer.Experimental.Services.Interfaces.IFeatureFlagService? runtimeFlags = null)
{
    private static readonly OperationCapability[] SessionOperations =
    [new("capabilities", false, null), new("ping", false, null), new("auth.attach", false, null),
     new("auth.login", false, null), new("auth.totp", false, null), new("auth.logout", false, null), new("cancel", false, null)];
    private volatile string? _token;
    private string? _challenge;
    private string _state = "anonymous";
    private long _generation;
    private long _authenticateByTicks = DateTimeOffset.UtcNow.AddSeconds(45).UtcTicks;
    private DateTimeOffset AuthenticateBy => new(Interlocked.Read(ref _authenticateByTicks), TimeSpan.Zero);
    private sealed record QueuedCommand(CommandEnvelope Command, long Generation);
    private readonly string _connectionId = Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new(StringComparer.Ordinal);

    public async Task RunAsync(WebSocket socket, string address, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var outbound = Channel.CreateBounded<ResultEnvelope>(new BoundedChannelOptions(32) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        var commands = Channel.CreateBounded<QueuedCommand>(new BoundedChannelOptions(ApiProtocol.MaxOutstanding) { SingleReader = true, SingleWriter = true });
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["ConnectionId"] = _connectionId });
        logger.LogInformation("V2 connection opened");
        OperationTelemetry.Connections.Add(1);
        var writer = WriteAsync(socket, outbound.Reader, stop);
        var worker = WorkAsync(commands.Reader, outbound.Writer, address, stop);
        try
        {
            await outbound.Writer.WriteAsync(Result(null, true, Capabilities(), null, "welcome"), stop.Token);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var rateWindow = Stopwatch.StartNew();
            var count = 0;
            while (!stop.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var receiveDeadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var timeout = _token == null ? AuthenticateBy - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(90);
                if (timeout <= TimeSpan.Zero) break;
                receiveDeadline.CancelAfter(timeout);
                var command = await ReceiveAsync(socket, receiveDeadline.Token);
                if (command == null) break;
                if (rateWindow.Elapsed >= TimeSpan.FromMinutes(1)) { rateWindow.Restart(); count = 0; }
                if (++count > 180) { await outbound.Writer.WriteAsync(Result(command.RequestId, false, null, "rate_limited"), stop.Token); break; }
                if (!Guid.TryParseExact(command.RequestId, "N", out _) || command.Operation is not { Length: > 0 and <= 80 })
                    throw new OperationException("invalid_envelope");
                // Bound deduplication memory for this connection; clients reconnect after this many requests.
                if (seen.Count >= 10000) break;
                if (!seen.Add(command.RequestId)) throw new OperationException("duplicate_request_id");
                if (command.Operation == "cancel")
                {
                    var target = command.Payload.Deserialize<CancelCommand>(ApiProtocol.Json) ?? throw new OperationException("validation_failed");
                    if (_pending.TryGetValue(target.RequestId, out var pending))
                    {
                        // Removal/disposal occurs on the worker, so serialize Cancel with its cleanup.
                        lock (pending) { if (_pending.ContainsKey(target.RequestId)) pending.Cancel(); }
                    }
                    await outbound.Writer.WriteAsync(Result(command.RequestId, true, new { requested = true }), stop.Token);
                    continue;
                }
                if (_pending.Count >= ApiProtocol.MaxOutstanding)
                {
                    await outbound.Writer.WriteAsync(Result(command.RequestId, false, null, "busy"), stop.Token);
                    continue;
                }
                var commandCts = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                commandCts.CancelAfter(TimeSpan.FromSeconds(30)); // Queue time is part of the command deadline.
                _pending[command.RequestId] = commandCts;
                OperationTelemetry.Queued.Add(1);
                if (!commands.Writer.TryWrite(new(command, Interlocked.Read(ref _generation))))
                {
                    RemovePending(command.RequestId, commandCts);
                    await outbound.Writer.WriteAsync(Result(command.RequestId, false, null, "busy"), stop.Token);
                }
            }
        }
        catch (OperationCanceledException) { logger.LogDebug("V2 receive ended by cancellation or idle/authentication deadline"); }
        catch (WebSocketException) { logger.LogInformation("V2 peer disconnected"); }
        catch (Exception ex) { logger.LogWarning("V2 protocol rejected: {ExceptionType}", ex.GetType().Name); }
        finally
        {
            stop.Cancel();
            commands.Writer.TryComplete();
            outbound.Writer.TryComplete();
            try { await Task.WhenAll(worker, writer); } catch (OperationCanceledException) { }
            foreach (var pending in _pending.ToArray()) RemovePending(pending.Key, pending.Value);
            _token = null;
            _challenge = null;
            using var closeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Session ended; reconnect and resynchronize", closeDeadline.Token); }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { socket.Abort(); }
            OperationTelemetry.Connections.Add(-1);
            logger.LogInformation("V2 connection closed; generation {Generation}", _generation);
        }
    }

    private void RemovePending(string id, CancellationTokenSource pending)
    {
        lock (pending) { if (_pending.TryRemove(id, out _)) OperationTelemetry.Queued.Add(-1); pending.Dispose(); }
    }
    private Capabilities Capabilities() => new(ApiProtocol.Subprotocol,
        operations.Capabilities.Concat(SessionOperations).ToArray(), ["password", "totp", "bearer"],
        ApiProtocol.MaxMessageBytes, ApiProtocol.MaxOutstanding, database.State.ToString().ToLowerInvariant(), _connectionId);
    private ResultEnvelope Result(string? request, bool success, object? data, string? code = null, string type = "result") =>
        new(type, request, success, data == null ? null : JsonSerializer.SerializeToElement(data, ApiProtocol.Json), code,
            Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"), Interlocked.Read(ref _generation));

    private async Task WorkAsync(ChannelReader<QueuedCommand> commands, ChannelWriter<ResultEnvelope> output, string address, CancellationTokenSource stop)
    {
        try
        {
            await foreach (var queued in commands.ReadAllAsync(stop.Token))
            {
                var command = queued.Command;
                if (!_pending.TryGetValue(command.RequestId, out var pending)) continue;
                using var activity = OperationTelemetry.Traces.StartActivity("bru.operation") ?? new Activity("bru.operation").Start();
                using var scope = logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = command.RequestId, ["Operation"] = command.Operation, ["Generation"] = _generation });
                ResultEnvelope result;
                var started = Stopwatch.GetTimestamp();
                try
                {
                    pending.Token.ThrowIfCancellationRequested();
                    if (queued.Generation != Interlocked.Read(ref _generation)) throw new OperationException("session_changed");
                    var data = await ExecuteAsync(command, address, pending.Token);
                    result = Result(command.RequestId, true, data);
                }
                catch (Exception ex)
                {
                    var code = ex switch
                    {
                        OperationException operation => operation.Code,
                        CommitOutcomeUnknownException => "commit_outcome_unknown",
                        ReducerRejectedException => "conflict",
                        DatabaseUnavailableException => "database_unavailable",
                        OperationCanceledException => "cancelled",
                        JsonException or ArgumentException => "validation_failed",
                        _ => "internal_error"
                    };
                    // No exception messages or payloads: providers can include secrets in their exceptions.
                    logger.LogWarning("Command failed: {Code}; exception type {ExceptionType}", code, ex.GetType().Name);
                    result = Result(command.RequestId, false, null, code);
                }
                finally { RemovePending(command.RequestId, pending); }
                var operationName = operations.Contains(command.Operation) || SessionOperations.Any(o => o.Name == command.Operation) ? command.Operation : "unsupported";
                var tags = new TagList { { "operation", operationName }, { "outcome", result.Code ?? "success" } };
                OperationTelemetry.Completed.Add(1, tags);
                OperationTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
                logger.LogInformation("Command outcome {Code} in {ElapsedMs} ms", result.Code ?? "success", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                await output.WriteAsync(result, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { logger.LogError("V2 worker failed: {ExceptionType}", ex.GetType().Name); }
        finally { stop.Cancel(); }
    }

    private async Task<object?> ExecuteAsync(CommandEnvelope command, string address, CancellationToken ct)
    {
        switch (command.Operation)
        {
            case "capabilities": return Capabilities();
            case "ping": return new { state = _state, utc = DateTimeOffset.UtcNow };
            case "auth.logout": ResetSession(); return new AuthenticationReply(_state);
            case "auth.attach":
                if (!admission.TryAuthenticate(address)) throw new OperationException("rate_limited");
                var attach = command.Payload.Deserialize<AttachRequest>(ApiProtocol.Json) ?? throw new OperationException("validation_failed");
                ResetSession();
                await AttachAsync(attach.Token, ct);
                return new AuthenticationReply(_state);
            case "auth.login":
                if (runtimeFlags != null && !await runtimeFlags.GetFlagAsync("EnableLoginRefactoring")) throw new OperationException("endpoint_unavailable");
                if (!admission.TryAuthenticate(address)) throw new OperationException("rate_limited");
                var login = command.Payload.Deserialize<LoginCommand>(ApiProtocol.Json) ?? throw new OperationException("validation_failed");
                if (string.IsNullOrWhiteSpace(login.Username) || login.Username.Length > 200 || login.Password is not { Length: > 0 and <= 1024 }) throw new OperationException("validation_failed");
                ResetSession();
                var authenticated = await authentication.LoginAsync(login.Username, login.Password);
                ct.ThrowIfCancellationRequested();
                if (authenticated.RequiresTwoFactor && authenticated.TempToken is { } challenge)
                {
                    _challenge = challenge;
                    _state = "challenge-pending";
                    Interlocked.Exchange(ref _authenticateByTicks, DateTimeOffset.UtcNow.AddMinutes(2).UtcTicks);
                    return new AuthenticationReply(_state, TotpEnabled: authenticated.TotpEnabled, WebAuthnEnabled: authenticated.WebAuthnEnabled);
                }
                if (!authenticated.Success || authenticated.Token == null) throw new OperationException("invalid_credentials");
                await AttachAsync(authenticated.Token, ct);
                return new AuthenticationReply(_state, authenticated.Token);
            case "auth.totp":
                if (runtimeFlags != null && !await runtimeFlags.GetFlagAsync("EnableTotpValidateRefactoring")) throw new OperationException("endpoint_unavailable");
                if (!admission.TryAuthenticate(address)) throw new OperationException("rate_limited");
                var totp = command.Payload.Deserialize<TotpCommand>(ApiProtocol.Json) ?? throw new OperationException("validation_failed");
                if (_challenge == null || DateTimeOffset.UtcNow >= AuthenticateBy) throw new OperationException("challenge_expired");
                if (totp.Code is not { Length: 6 } || !totp.Code.All(char.IsAsciiDigit)) throw new OperationException("validation_failed");
                var completed = await authentication.ValidateTotpAsync(_challenge, totp.Code);
                ct.ThrowIfCancellationRequested();
                if (!completed.Success || completed.Token == null) throw new OperationException("invalid_credentials");
                await AttachAsync(completed.Token, ct);
                return new AuthenticationReply(_state, completed.Token);
        }
        if (!operations.Contains(command.Operation)) throw new OperationException("unsupported_operation");
        if (_token == null) throw new OperationException("unauthenticated");
        var actor = await ResolveActorAsync(_token, ct);
        if (actor == null) { ResetSession("expired"); throw new OperationException("session_expired"); }
        return await operations.ExecuteAsync(command.Operation, actor.Value, command.Payload, command.OperationId, ct);
    }
    private async Task AttachAsync(string token, CancellationToken ct)
    {
        if (await ResolveActorAsync(token, ct) is not { } actor || !(await new UserAccessService(database).GetAsync(actor, ct)).IsActive)
            throw new OperationException("invalid_credentials");
        _token = token;
        _challenge = null;
        _state = "authenticated";
        Interlocked.Increment(ref _generation);
    }
    private async Task<Identity?> ResolveActorAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 24 * 1024) return null;
        var claims = await tokens.ValidateTokenDirectAsync(token, ct);
        if (claims == null || !claims.TryGetValue("identity", out var identity) || identity is not string value) return null;
        try { return Identity.FromHexString(value); } catch (Exception ex) when (ex is ArgumentException or FormatException) { return null; }
    }
    private void ResetSession(string state = "anonymous")
    {
        _token = null; _challenge = null; _state = state;
        Interlocked.Exchange(ref _authenticateByTicks, DateTimeOffset.UtcNow.AddSeconds(45).UtcTicks);
        Interlocked.Increment(ref _generation);
    }
    private static async Task<CommandEnvelope?> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult part;
        do
        {
            part = await socket.ReceiveAsync(buffer, ct);
            if (part.MessageType == WebSocketMessageType.Close) return null;
            if (part.MessageType != WebSocketMessageType.Text || stream.Length + part.Count > ApiProtocol.MaxMessageBytes)
                throw new OperationException("invalid_message");
            stream.Write(buffer, 0, part.Count);
        } while (!part.EndOfMessage);
        return JsonSerializer.Deserialize<CommandEnvelope>(stream.ToArray(), ApiProtocol.Json) ?? throw new OperationException("invalid_envelope");
    }
    private static async Task WriteAsync(WebSocket socket, ChannelReader<ResultEnvelope> output, CancellationTokenSource stop)
    {
        try
        {
            await foreach (var result in output.ReadAllAsync(stop.Token))
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(result, ApiProtocol.Json);
                if (bytes.Length > ApiProtocol.MaxResponseBytes) throw new OperationException("response_too_large");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, deadline.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or OperationException) { }
        finally { stop.Cancel(); }
    }
}
