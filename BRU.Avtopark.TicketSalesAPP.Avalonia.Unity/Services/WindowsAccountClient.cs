using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

public static class WindowsAccountClient
{
    public static Uri? TrustedOrigin
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("BRU_TRUSTED_API_ORIGIN");
            return Uri.TryCreate(configured, UriKind.Absolute, out var origin) && origin.Scheme == Uri.UriSchemeHttps
                && origin.AbsolutePath == "/" && string.IsNullOrEmpty(origin.UserInfo) && string.IsNullOrEmpty(origin.Query)
                && string.IsNullOrEmpty(origin.Fragment) ? origin : null;
        }
    }

    public static Task<string> LoginAsync(CancellationToken cancellationToken) => SendAsync(null, cancellationToken);

    public static Task<string> LinkAsync(string applicationToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationToken)) throw new InvalidOperationException("Сначала войдите в учётную запись приложения.");
        return SendAsync(applicationToken, cancellationToken);
    }

    public static Task<string> GetLinkStatusAsync(string applicationToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationToken)) throw new InvalidOperationException("Сначала войдите в учётную запись приложения.");
        return SendAsync(applicationToken, cancellationToken, status: true);
    }

    private static async Task<string> SendAsync(string? applicationToken, CancellationToken cancellationToken, bool status = false)
    {
        var trusted = TrustedOrigin ?? throw new InvalidOperationException("Настройте доверенный HTTPS-сервер (BRU_TRUSTED_API_ORIGIN).");
        Uri endpoint;
        try { endpoint = BRU.Avtopark.Client.ClientEndpoints.WindowsAuthenticationEndpoint(
            ApiClientService.Instance.CurrentBaseUrl!, trusted, applicationToken != null); }
        catch (ArgumentException) { throw new InvalidOperationException("Выбранный сервер не совпадает с доверенным сервером Windows-входа."); }
        if (status) endpoint = new Uri(endpoint.AbsoluteUri + "/status");
        // This handler is isolated: credentials never reach discovery, redirects, arbitrary URLs or proxy authentication.
        using var handler = new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(new TicketSalesApp.Services.Diagnostics.TrafficLoggingHandler(handler)) { Timeout = TimeSpan.FromSeconds(45) };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (applicationToken != null) request.Content = JsonContent.Create(new { applicationToken });
        request.Headers.Add("X-BRU-Native-Login", "1");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Conflict => "Эта учётная запись уже связана. Обратитесь к администратору для изменения связи.",
                System.Net.HttpStatusCode.NotFound => "Windows-вход отключён на сервере.",
                System.Net.HttpStatusCode.Forbidden => "Учётная запись не связана с активным пользователем приложения.",
                System.Net.HttpStatusCode.Unauthorized => "Не удалось проверить учётную запись домена. Проверьте VPN и Kerberos/NTLM.",
                _ => "Сервер не смог завершить Windows-вход. Повторите попытку."
            });
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
