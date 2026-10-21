using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

public static class QrClientContext
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    public static IQrLoginApi CreateApi() => new QrLoginClient(Http, ApiClientService.Instance.CurrentBaseUrl ?? throw new QrLoginException("invalid_server"));
    // Resolve the current account from the existing authenticated JSON profile endpoint, not a user-entered enrollment label.
    public static async Task<string> AccountAsync(string apiBase, string bearer, CancellationToken cancellationToken)
    {
        _ = new QrLoginClient(Http, apiBase); // Apply the same HTTPS/loopback validation before bearer use.
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ClientEndpoints.ServerRoot(apiBase), "api/auth/profile"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 65536) throw new QrLoginException("account_unavailable");
            await response.Content.LoadIntoBufferAsync(65536, cancellationToken);
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            return json.RootElement.GetProperty("data").GetProperty("user").GetProperty("login").GetString() ?? throw new QrLoginException("account_unavailable");
        }
        catch (OperationCanceledException) { throw; }
        catch { throw new QrLoginException("account_unavailable"); }
    }
}
