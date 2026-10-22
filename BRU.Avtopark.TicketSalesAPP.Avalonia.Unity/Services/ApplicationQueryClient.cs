using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

/// <summary>Read-only transport policy shared by migrated screens. Mutation retries do not use this path.</summary>
internal static class ApplicationQueryClient
{
    private static readonly HttpClient Http = new(new TicketSalesApp.Services.Diagnostics.TrafficLoggingHandler()) { Timeout = TimeSpan.FromSeconds(30) };
    public static async System.Collections.Generic.IAsyncEnumerable<BRU.Avtopark.Client.QueryUpdate<TResponse>> WatchAsync<TRequest, TResponse>(
        string operation, TRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var api = ApiClientService.Instance;
        var origin = await api.DiscoverApiBaseUrlAsync(cancellationToken);
        var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        await foreach (var update in ApplicationClientCoordinator.WatchAsync<TRequest, TResponse>(origin, token, operation, request, cancellationToken))
            yield return update;
    }
    public static async Task<TResponse> ReadAsync<TRequest, TResponse>(string operation, TRequest request, CancellationToken cancellationToken)
    {
        if (operation is not ("reports.sales" or "buses.list" or "routes.list" or "routes.get" or "employees.list" or "employees.get" or "employees.drivers" or "jobs.list" or "jobs.get" or "maintenance.list" or "maintenance.get" or "tickets.list" or "tickets.get" or "schedules.list" or "schedules.get" or "sales.list" or "sales.get" or "users.get" or "roles.get" or "permissions.get" or "mutations.receipt")) throw new ArgumentException("Read operation is not registered.", nameof(operation));
        var api = ApiClientService.Instance;
        var baseUrl = await api.DiscoverApiBaseUrlAsync(cancellationToken);
        var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        try
        {
            if (Environment.GetEnvironmentVariable("BRU_TRANSPORT") == "http") throw new OperationNotDispatchedException(operation);
            var socketResult = await ApplicationClientCoordinator.ExecuteAsync<TRequest, TResponse>(baseUrl, token, operation, request,
                cancellationToken: cancellationToken);
            if (socketResult is null && operation != "mutations.receipt") throw new ApplicationCommandException("invalid_response");
            return socketResult!;
        }
        catch (System.Net.WebSockets.WebSocketException) { /* Read retries are safe; no writes are admitted here. */ }
        catch (OperationNotDispatchedException) { }
        catch (ApplicationCommandException ex) when (ex.Code == "connection_lost") { }
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "operations/" + operation));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = JsonContent.Create(request, options: ApiProtocol.Json);
        using var response = await Http.SendAsync(message, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new ApplicationCommandException("unauthenticated");
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) throw new ApplicationCommandException("forbidden");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new ApplicationCommandException("not_found");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TResponse>(ApiProtocol.Json, cancellationToken);
        if (result is null && operation != "mutations.receipt") throw new ApplicationCommandException("invalid_response");
        if (api.AuthToken != token || api.CurrentBaseUrl != baseUrl) throw new OperationCanceledException("Application session changed.");
        return result!;
    }
}
