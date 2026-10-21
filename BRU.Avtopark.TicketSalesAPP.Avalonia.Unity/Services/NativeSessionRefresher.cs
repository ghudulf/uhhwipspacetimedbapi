using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BRU.Avtopark.Client;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

public sealed class NativeSessionRefresher : ISessionRefresher
{
    private static readonly HttpClient DefaultHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient http;
    public NativeSessionRefresher(HttpClient? http = null) => this.http = http ?? DefaultHttp;
    public static async Task StoreLoginAsync(string access, string? refresh)
    {
        var api = ApiClientService.Instance;
        var origin = api.CurrentBaseUrl ?? throw new InvalidOperationException("Configured server required.");
        var expires = DateTime.UtcNow.AddMinutes(15);
        try { expires = new JwtSecurityTokenHandler().ReadJwtToken(access).ValidTo; } catch { }
        await new TokenStorageService(origin).SaveTokensAsync(new OAuthTokenResponse { AccessToken = access, RefreshToken = refresh,
            SessionKind = "native", ExpiresAt = expires, ExpiresIn = Math.Max(0, (int)(expires - DateTime.UtcNow).TotalSeconds) });
        api.AuthToken = access;
    }
    public async Task<string?> RefreshAsync(string apiBase, string expiredAccessToken, CancellationToken cancellationToken = default)
    {
        _ = new ProviderLoginClient(http, apiBase); // HTTPS/loopback, no user-info/query/fragment before any credential is sent.
        var api = ApiClientService.Instance;
        var storage = new TokenStorageService(apiBase);
        var stored = await storage.GetTokensAsync();
        if (stored?.SessionKind != "native" || stored.AccessToken != expiredAccessToken || string.IsNullOrEmpty(stored.RefreshToken) ||
            api.AuthToken != expiredAccessToken || api.CurrentBaseUrl != apiBase) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ClientEndpoints.ServerRoot(apiBase), "api/auth/refresh"));
            request.Headers.Add("X-BRU-Native-Login", "1"); request.Headers.Add("X-BRU-Device", "native");
            request.Content = JsonContent.Create(new { refreshToken = stored.RefreshToken });
            // Exactly one dispatch. Network/timeout/response uncertainty MUST NOT retry the consumed proof.
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri is { } final && final != request.RequestUri || response.Content.Headers.ContentLength > 65536)
                throw new InvalidOperationException();
            await response.Content.LoadIntoBufferAsync(65536, cancellationToken);
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            var data = document.RootElement.GetProperty("data");
            var access = data.GetProperty("token").GetString(); var refresh = data.GetProperty("refreshToken").GetString();
            if (!document.RootElement.GetProperty("success").GetBoolean() || string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) throw new InvalidOperationException();
            if (api.AuthToken != expiredAccessToken || api.CurrentBaseUrl != apiBase) return null;
            var replacement = new OAuthTokenResponse { AccessToken = access, RefreshToken = refresh, SessionKind = "native", ExpiresAt = data.GetProperty("expiresAt").GetDateTime() };
            if (!await storage.ReplaceIfAccessMatchesAsync(expiredAccessToken, replacement)) return null;
            var applied = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (api.AuthToken != expiredAccessToken || api.CurrentBaseUrl != apiBase) return false;
                api.AuthToken = access; return true;
            });
            return applied ? access : null;
        }
        catch
        {
            // Clear only the session whose proof was dispatched, never a newer account's stored session.
            await storage.ClearIfAccessMatchesAsync(expiredAccessToken);
            return null;
        }
    }
    public static async Task RevokeStoredFamilyAsync()
    {
        var origin = ApiClientService.Instance.CurrentBaseUrl;
        if (origin == null) return;
        var storage = new TokenStorageService(origin); var stored = await storage.GetTokensAsync();
        try
        {
            if (stored?.SessionKind != "native" || string.IsNullOrEmpty(stored.RefreshToken)) return;
            _ = new ProviderLoginClient(DefaultHttp, origin);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ClientEndpoints.ServerRoot(origin), "api/auth/logout"));
            request.Headers.Add("X-BRU-Native-Login", "1"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", stored.AccessToken);
            request.Content = JsonContent.Create(new { refreshToken = stored.RefreshToken });
            using var response = await DefaultHttp.SendAsync(request);
        }
        catch { } // Local logout still clears credentials; an unreachable server cannot attest revocation.
        finally { await storage.ClearTokensAsync(); }
    }
}
