using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

public sealed record SessionAccess(bool IsActive, bool IsAdministrator, string[] Roles, string[] Permissions);

/// <summary>Realtime access snapshot with an HTTP adapter for a capability-declared unsupported operation.</summary>
public static class SessionAccessClient
{
    private static readonly HttpClient Client = new(new TicketSalesApp.Services.Diagnostics.TrafficLoggingHandler())
        { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<SessionAccess?> GetAsync(string baseUrl, string token, CancellationToken cancellationToken)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("BRU_TRANSPORT") == "http") throw new BRU.Avtopark.Client.OperationNotDispatchedException("session.access");
            var access = await ApplicationClientCoordinator.ExecuteAsync<BRU.Avtopark.Contracts.EmptyRequest, BRU.Avtopark.Contracts.AccessSnapshot>(
                baseUrl, token, "session.access", new(), cancellationToken: cancellationToken);
            return access == null ? null : new(access.IsActive, access.IsAdministrator, access.Roles, access.Permissions);
        }
        catch (BRU.Avtopark.Client.ApplicationCommandException ex) when (ex.Code is "forbidden" or "session_expired" or "invalid_credentials") { return null; }
        catch (System.Net.WebSockets.WebSocketException)
        {
            // Establishing the connection failed before the application read was dispatched.
        }
        catch (BRU.Avtopark.Client.OperationNotDispatchedException)
        {
            // The server did not advertise this operation; using the existing HTTP read is safe.
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "session/access"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Client.SendAsync(request, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionAccess>(cancellationToken);
    }
}
