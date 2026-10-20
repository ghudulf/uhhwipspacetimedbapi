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
        if (!OwnsSession(baseUrl, token)) throw new OperationCanceledException("Application session changed.");
        RealtimeApplicationClient client;
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
            client = _client;
        }
        finally { Gate.Release(); }
        var result = await client.CallAsync<TRequest, TResponse>(operation, payload, operationId, cancellationToken);
        // Session ownership is checked again after await; never apply a prior account's result.
        if (!ReferenceEquals(client, _client) || !OwnsSession(baseUrl, token))
            throw new OperationCanceledException("Application session changed.");
        return result;
    }

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
