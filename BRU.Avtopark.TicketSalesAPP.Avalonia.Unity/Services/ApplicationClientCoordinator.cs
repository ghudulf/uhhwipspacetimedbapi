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

    public static async Task<TResponse?> ExecuteAsync<TRequest, TResponse>(string baseUrl, string token, string operation, TRequest payload,
        string? operationId = null, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(baseUrl, token, cancellationToken);
        var result = await client.CallAsync<TRequest, TResponse>(operation, payload, operationId, cancellationToken);
        // Session ownership is checked again after await; never apply a prior account's result.
        if (!ReferenceEquals(client, _client) || !OwnsSession(baseUrl, token))
            throw new OperationCanceledException("Application session changed.");
        return result;
    }

    private static async Task<RealtimeApplicationClient> GetClientAsync(string baseUrl, string token, CancellationToken cancellationToken)
    {
        if (!OwnsSession(baseUrl, token)) throw new OperationCanceledException("Application session changed.");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (!OwnsSession(baseUrl, token)) throw new OperationCanceledException("Application session changed.");
            if (_client?.IsConnected != true || _origin != baseUrl || _token != token)
            {
                if (_client != null) await _client.DisposeAsync();
                _client = null;
                var candidate = new RealtimeApplicationClient();
                candidate.Diagnostic += diagnostic => Serilog.Log.Information(
                    "Client operation {Operation}, request {RequestId}, transport websocket, generation {Generation}: {Outcome} in {ElapsedMs} ms",
                    diagnostic.Operation, diagnostic.RequestId, diagnostic.Generation, diagnostic.Outcome, diagnostic.ElapsedMs);
                try { await candidate.ConnectAsync(new Uri(baseUrl), token, cancellationToken); }
                catch { await candidate.DisposeAsync(); throw; }
                _client = candidate;
                _origin = baseUrl;
                _token = token;
                Serilog.Log.Information("Shared realtime session established; protocol bru.api.v2");
            }
            return _client;
        }
        finally { Gate.Release(); }
    }

    public static async System.Collections.Generic.IAsyncEnumerable<QueryUpdate<TResponse>> WatchAsync<TRequest, TResponse>(
        string baseUrl, string token, string operation, TRequest payload,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OwnsSession(baseUrl, token)) throw new OperationCanceledException("Application session changed.");
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
                        if (!OwnsSession(baseUrl, token)) throw new OperationCanceledException("Application session changed.");
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
        { Code: "connection_lost" or "database_unavailable" or "resync_required" or "busy" });

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
