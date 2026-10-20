using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;

/// <summary>
/// MAUI-side authentication service.
/// Call <see cref="InitializeAsync"/> once at startup (SplashPage) to run API
/// server discovery before any OAuth or API calls are made.
/// </summary>
public sealed class MauiAuthService
{
    // ── Singleton ────────────────────────────────────────────────────────

    private static MauiAuthService? _instance;
    private static readonly object _lock = new();

    public static MauiAuthService Instance
    {
        get
        {
            if (_instance is null)
                lock (_lock)
                    _instance ??= new MauiAuthService();
            return _instance;
        }
    }

    // ── Core services ────────────────────────────────────────────────────

    private OAuthService? _oAuthService;

    /// <summary>
    /// The OAuth service, built with the discovered server URL.
    /// Falls back to localhost if <see cref="InitializeAsync"/> hasn't run yet.
    /// </summary>
    public OAuthService OAuthService
    {
        get
        {
            if (_oAuthService is null)
                _oAuthService = BuildOAuthService(ApiClientService.Instance.CurrentBaseUrl);
            return _oAuthService;
        }
    }

    public TokenStorageService TokenStorage { get; } = new TokenStorageService();

    // ── Constructor ──────────────────────────────────────────────────────

    public MauiAuthService() { }

    // ── Initialization (discovery) ───────────────────────────────────────

    /// <summary>
    /// Runs API server discovery and rebuilds <see cref="OAuthService"/> with the
    /// correct server URL. Must be awaited once at startup before any API/OAuth calls.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("[MauiAuthService] InitializeAsync: starting API server discovery");

        var baseUrl = await ApiClientService.Instance.DiscoverApiBaseUrlAsync(cancellationToken);

        // baseUrl = "http://<host>:5000/api/" — derive server root
        var serverRoot = baseUrl.EndsWith("api/", StringComparison.OrdinalIgnoreCase)
            ? baseUrl[..^4]
            : baseUrl.TrimEnd('/') + "/";

        Log.Information("[MauiAuthService] InitializeAsync: server root = {Root}", serverRoot);

        _oAuthService = BuildOAuthService(serverRoot);
    }

    // ── Token helpers ────────────────────────────────────────────────────

    public async Task<bool> RestoreSessionAsync()
    {
        try
        {
            Log.Information("[MauiAuthService] RestoreSessionAsync: loading tokens from disk");
            var tokens = await TokenStorage.GetTokensAsync();

            if (tokens is null)
            {
                Log.Information("[MauiAuthService] RestoreSessionAsync: no token file found");
                return false;
            }

            Log.Debug("[MauiAuthService] RestoreSessionAsync: token file found, AccessToken.Length={Len}, ExpiresAt={Exp:u}",
                tokens.AccessToken?.Length ?? 0, tokens.ExpiresAt);

            if (string.IsNullOrEmpty(tokens.AccessToken))
            {
                Log.Warning("[MauiAuthService] RestoreSessionAsync: token file exists but AccessToken is empty");
                return false;
            }

            if (tokens.ExpiresAt <= DateTime.UtcNow.AddMinutes(5))
            {
                Log.Warning("[MauiAuthService] RestoreSessionAsync: token expired at {Exp:u} (now={Now:u}), skipping restore",
                    tokens.ExpiresAt, DateTime.UtcNow);
                return false;
            }

            ApiClientService.Instance.AuthToken = tokens.AccessToken;
            TokenExpiresAt = tokens.ExpiresAt;

            Log.Information("[MauiAuthService] RestoreSessionAsync: session restored, token valid until {Exp:u}",
                tokens.ExpiresAt);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MauiAuthService] RestoreSessionAsync threw — treating as no session");
            return false;
        }
    }

    public async Task<bool> HasValidTokenAsync()
    {
        try
        {
            var tokens = await TokenStorage.GetTokensAsync();
            bool valid = tokens is not null
                && !string.IsNullOrEmpty(tokens.AccessToken)
                && tokens.ExpiresAt > DateTime.UtcNow.AddMinutes(5);
            Log.Debug("[MauiAuthService] HasValidTokenAsync={Valid}, ExpiresAt={Exp:u}",
                valid, tokens?.ExpiresAt ?? DateTime.MinValue);
            return valid;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MauiAuthService] HasValidTokenAsync threw — treating as no token");
            return false;
        }
    }

    public bool HasValidTokenSync()
        => !string.IsNullOrEmpty(ApiClientService.Instance.AuthToken)
           && TokenExpiresAt != DateTime.MinValue
           && DateTime.UtcNow < TokenExpiresAt;

    public DateTime TokenExpiresAt { get; private set; } = DateTime.MinValue;

    public async Task<OAuthTokenResponse?> ExchangeAndPersistAsync(string code, string codeVerifier)
    {
        try
        {
            Log.Information("[MauiAuthService] ExchangeAndPersistAsync — code.Length={CodeLen}, verifier.Length={VerLen}",
                code?.Length ?? -1, codeVerifier?.Length ?? -1);

            var tokens = await OAuthService.ExchangeCodeForTokenAsync(code ?? string.Empty, codeVerifier ?? string.Empty);

            if (tokens is not null && !string.IsNullOrEmpty(tokens.AccessToken))
            {
                ApiClientService.Instance.AuthToken = tokens.AccessToken;
                TokenExpiresAt = tokens.ExpiresAt;
                Log.Information("[MauiAuthService] Token exchange OK — expires {ExpiresAt:u}", tokens.ExpiresAt);
                return tokens;
            }

            Log.Warning("[MauiAuthService] ExchangeCodeForTokenAsync returned null/empty token");
            return null;
        }
        catch (OAuthAuthorizationException ex)
        {
            Log.Error(ex, "[MauiAuthService] Authorization error: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MauiAuthService] Token exchange error: {Message}", ex.Message);
            return null;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            Log.Information("[MauiAuthService] LogoutAsync: clearing tokens");
            await TokenStorage.ClearTokensAsync();
            ApiClientService.Instance.AuthToken = null;
            ApiClientService.Instance.IsAdmin = null;
            ApiClientService.Instance.UserRole = null;
            TokenExpiresAt = DateTime.MinValue;
            Log.Information("[MauiAuthService] Logged out — tokens cleared");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MauiAuthService] LogoutAsync failed");
            throw;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private OAuthService BuildOAuthService(string? serverRoot)
    {
        // serverRoot may be the full "api/" base — strip to get server root
        var root = serverRoot ?? $"http://localhost:5000/";
        if (root.EndsWith("api/", StringComparison.OrdinalIgnoreCase))
            root = root[..^4];
        root = root.TrimEnd('/');

        var authEndpoint  = $"{root}/connect/authorize";
        var tokenEndpoint = $"{root}/connect/token";
        // Redirect URI is always localhost — it's a server-side endpoint registered in OpenIddict.
        // Only authorize/token endpoints use the discovered LAN IP.
        var redirectUri   = "http://localhost:5000/callback";

        Log.Information("[MauiAuthService] Building OAuthService: auth={Auth}, token={Token}",
            authEndpoint, tokenEndpoint);

        return new OAuthService(
            clientId: "bru-avtopark-desktop-client",
            clientSecret: "your-secure-client-secret-here-change-in-production",
            authorizationEndpoint: authEndpoint,
            tokenEndpoint: tokenEndpoint,
            redirectUri: redirectUri,
            tokenStorage: TokenStorage
        );
    }
}
