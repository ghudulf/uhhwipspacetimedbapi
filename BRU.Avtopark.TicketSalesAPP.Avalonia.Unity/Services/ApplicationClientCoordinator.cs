using System;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

/// <summary>Shared by desktop and MAUI. An origin or account change disposes the previous transport.</summary>
public static class ApplicationClientCoordinator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static RealtimeApplicationClient? _client;
    private static string? _origin;
    private static string? _token;
    private static SessionRefreshCoordinator? _refresh;
    public static event Action? SessionExpired;
    private static readonly System.Collections.Generic.HashSet<string> Reads = new(StringComparer.Ordinal)
    { "session.access", "buses.list", "sales.list", "reports.sales", "reports.income", "routes.list", "routes.get", "employees.list", "employees.get", "employees.drivers",
      "jobs.list", "jobs.get", "maintenance.list", "maintenance.get", "tickets.list", "tickets.get", "schedules.list", "schedules.get", "users.list", "users.get", "roles.list", "roles.get", "permissions.list", "permissions.get" };
    public static void ConfigureSessionRefresher(ISessionRefresher? refresher)
    {
        if (refresher == null) { _refresh = null; return; }
        var coordinator = new SessionRefreshCoordinator(refresher, TimeProvider.System);
        coordinator.SessionExpired += () => { if (ReferenceEquals(_refresh, coordinator)) SessionExpired?.Invoke(); };
        _refresh = coordinator;
    }
    private static async Task<string> CurrentTokenAsync(string baseUrl, string token, CancellationToken ct)
    {
        if (OwnsSession(baseUrl, token)) return token;
        var refresh = _refresh;
        if (refresh?.CanReconcile(baseUrl, token) == true)
        {
            var replacement = await refresh.RefreshAsync(baseUrl, token, ct);
            if (replacement != null && OwnsSession(baseUrl, replacement)) return replacement;
        }
        throw new OperationCanceledException("Application session changed.");
    }

    public static async Task<TResponse?> ExecuteAsync<TRequest, TResponse>(string baseUrl, string token, string operation, TRequest payload,
        string? operationId = null, CancellationToken cancellationToken = default)
    {
        async Task<TResponse?> Execute(string current, CancellationToken ct)
        {
            current = await CurrentTokenAsync(baseUrl, current, ct);
            var client = await GetClientAsync(baseUrl, current, ct);
            var result = await client.CallAsync<TRequest, TResponse>(operation, payload, operationId, ct);
            if (!ReferenceEquals(client, _client) || !OwnsSession(baseUrl, current)) throw new OperationCanceledException("Application session changed.");
            return result;
        }
        var refresh = _refresh;
        return refresh == null ? await Execute(token, cancellationToken) :
            await refresh.ExecuteAsync(baseUrl, token, !Reads.Contains(operation), Execute, cancellationToken);
    }

    private static async Task<RealtimeApplicationClient> GetClientAsync(string baseUrl, string token, CancellationToken cancellationToken)
    {
        if (!OwnsSession(baseUrl, token)) throw new OperationCanceledException("Application session changed.");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await GetClientCoreAsync(baseUrl, token, cancellationToken);
        }
        finally { Gate.Release(); }
    }

    private static async Task<RealtimeApplicationClient> GetClientCoreAsync(string baseUrl, string token, CancellationToken cancellationToken)
    {
        if (!OwnsSession(baseUrl, token)) throw new OperationCanceledException("Application session changed.");
        if (_client?.IsConnected != true || _origin != baseUrl || _token != token
            || _client.CountedRequestsSent >= 9000 && _client.TryBeginRotation())
        {
            if (_client != null) await _client.DisposeAsync();
            _client = null;
            var candidate = new RealtimeApplicationClient();
            candidate.FrameLogged += (direction, frame) => Serilog.Log.Debug("WebSocket {Direction} Frame={Frame}", direction, frame);
            candidate.Diagnostic += diagnostic => Serilog.Log.Information(
                "Client operation {Operation}, request {RequestId}, transport websocket, generation {Generation}: {Outcome} in {ElapsedMs} ms",
                diagnostic.Operation, diagnostic.RequestId, diagnostic.Generation, diagnostic.Outcome, diagnostic.ElapsedMs);
            try { await candidate.ConnectAsync(new Uri(baseUrl), token, cancellationToken); }
            catch (System.Net.WebSockets.WebSocketException)
            {
                await candidate.DisposeAsync();
                throw new OperationNotDispatchedException("connect");
            }
            catch { await candidate.DisposeAsync(); throw; }
            _client = candidate;
            _origin = baseUrl;
            _token = token;
            Serilog.Log.Information("Shared realtime session established; protocol bru.api.v2");
        }
        return _client;
    }

    public static async System.Collections.Generic.IAsyncEnumerable<QueryUpdate<TResponse>> WatchAsync<TRequest, TResponse>(
        string baseUrl, string token, string operation, TRequest payload,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var refresh = _refresh;
        var stream = refresh == null ? WatchCoreAsync<TRequest, TResponse>(baseUrl, token, operation, payload, cancellationToken) :
            refresh.WatchAsync(baseUrl, token, (current, ct) => WatchCoreAsync<TRequest, TResponse>(baseUrl, current, operation, payload, ct), cancellationToken);
        await foreach (var update in stream.WithCancellation(cancellationToken)) yield return update;
    }
    private static async System.Collections.Generic.IAsyncEnumerable<QueryUpdate<TResponse>> WatchCoreAsync<TRequest, TResponse>(
        string baseUrl, string token, string operation, TRequest payload,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            token = await CurrentTokenAsync(baseUrl, token, cancellationToken);
            System.Collections.Generic.IAsyncEnumerator<QueryUpdate<TResponse>>? iterator = null;
            Exception? failure = null;
            try
            {
                var client = await GetClientAsync(baseUrl, token, cancellationToken);
                iterator = client.WatchAsync<TRequest, TResponse>(operation, payload, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex) when (Recoverable(ex, cancellationToken)) { failure = ex; }
            if (iterator != null)
            {
                await using (iterator)
                {
                    while (true)
                    {
                        bool next;
                        try { next = await iterator.MoveNextAsync(); }
                        catch (Exception ex) when (Recoverable(ex, cancellationToken)) { failure = ex; break; }
                        if (!next) break;
                        if (!OwnsSession(baseUrl, token))
                        {
                            if (_refresh?.CanReconcile(baseUrl, token) == true) throw new ApplicationCommandException("session_changed");
                            throw new OperationCanceledException("Application session changed.");
                        }
                        if (iterator.Current.IsSynchronized) attempt = 0;
                        yield return iterator.Current;
                    }
                }
            }
            if (failure == null) yield break;
            yield return new(default, false, "reconnecting");
            var delay = Math.Min(10000, 250 * (1 << Math.Min(attempt++, 5))) + Random.Shared.Next(100, 400);
            Serilog.Log.Information("Subscription {Operation} requires resynchronization; retry in {DelayMs} ms, cause {ErrorType}", operation, delay, failure.GetType().Name);
            await Task.Delay(delay, cancellationToken);
        }
    }
    private static bool Recoverable(Exception exception, CancellationToken ct) => !ct.IsCancellationRequested && exception is
        (System.Net.WebSockets.WebSocketException or OperationNotDispatchedException or OperationCanceledException or
        ApplicationCommandException
        { Code: "connection_lost" or "database_unavailable" or "resync_required" or "busy" or "cancelled" });

    private static bool OwnsSession(string baseUrl, string token) =>
        ApiClientService.Instance.AuthToken == token && ApiClientService.Instance.CurrentBaseUrl == baseUrl;

    public static async Task ResetAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var previous = _client;
            _client = null; _origin = null; _token = null;
            if (previous != null) await previous.DisposeAsync();
        }
        finally { Gate.Release(); }
    }
}
