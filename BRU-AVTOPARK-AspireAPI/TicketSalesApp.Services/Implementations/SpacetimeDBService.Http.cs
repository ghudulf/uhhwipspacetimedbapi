using System.Net.Http.Headers;

namespace TicketSalesApp.Services.Implementations;

public partial class SpacetimeDBService
{
    // Process-lifetime pool; never mutate shared default authorization headers.
    private static readonly HttpClient DatabaseHttp = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { Timeout = TimeSpan.FromSeconds(30) };

    private static async Task<HttpResponseMessage> SendDatabaseHttpAsync(HttpMethod method, string uri, string scheme, string proof, int timeoutSeconds)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, proof);
        return await DatabaseHttp.SendAsync(request, deadline.Token);
    }
}
