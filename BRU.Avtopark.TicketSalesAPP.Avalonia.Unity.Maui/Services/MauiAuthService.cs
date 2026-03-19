using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;

/// <summary>
/// MAUI-side authentication service.
/// Owns the <see cref="OAuthService"/> and <see cref="TokenStorageService"/> instances
/// so MAUI pages don't need to reach into the Avalonia-side AuthenticationManager via
/// reflection. Registered as a singleton in <see cref="MauiProgram"/> and injected into
/// pages that need it.
/// </summary>
public sealed class MauiAuthService
{
    // ── Singleton ────────────────────────────────────────────────────────

    private static MauiAuthService? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Process-wide singleton — accessible from pages that can't use DI injection
    /// (e.g. XAML-constructed pages). Prefer constructor injection where possible.
    /// </summary>
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

    public OAuthService OAuthService { get; }
    public TokenStorageService TokenStorage { get; }

    // ── Constructor ──────────────────────────────────────────────────────

    public MauiAuthService()
    {
        TokenStorage = new TokenStorageService();

        OAuthService = new OAuthService(
            clientId: "bru-avtopark-desktop-client",
            clientSecret: "your-secure-client-secret-here-change-in-production",
            authorizationEndpoint: "https://localhost:5001/connect/authorize",
            tokenEndpoint: "https://localhost:5001/connect/token",
            redirectUri: "http://localhost:5000/callback",
            tokenStorage: TokenStorage
        );
    }

    // ── Token helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Loads persisted tokens from disk and restores them into ApiClientService.
    /// Must be called once at startup before any HasValidToken* checks.
    /// </summary>
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

            // Restore into in-memory state
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

    /// <summary>
    /// Returns true if a non-expired access token is stored on disk.
    /// NOTE: does NOT restore the token into ApiClientService — call RestoreSessionAsync() for that.
    /// </summary>
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

    /// <summary>
    /// Synchronous check — uses the in-memory ApiClientService token as a fast path.
    /// Suitable for UI refresh calls that must not block.
    /// </summary>
    public bool HasValidTokenSync()
        => !string.IsNullOrEmpty(ApiClientService.Instance.AuthToken)
           && TokenExpiresAt != DateTime.MinValue
           && DateTime.UtcNow < TokenExpiresAt;

    /// <summary>
    /// Expiry time of the currently cached token, or MinValue if none.
    /// Loaded lazily from the last successful exchange.
    /// </summary>
    public DateTime TokenExpiresAt { get; private set; } = DateTime.MinValue;

    /// <summary>
    /// Exchanges an authorization code for tokens, persists them, and sets the
    /// access token on <see cref="ApiClientService.Instance"/> so API calls work.
    /// Returns the token response on success, null on failure.
    /// </summary>
    public async Task<OAuthTokenResponse?> ExchangeAndPersistAsync(string code, string codeVerifier)
    {
        try
        {
            Log.Information("[MauiAuthService] ExchangeAndPersistAsync — code.Length={CodeLen}, verifier.Length={VerLen}",
                code?.Length ?? -1, codeVerifier?.Length ?? -1);
            Console.WriteLine($"[MauiAuthService] Exchanging authorization code for tokens (code={code?.Length}ch)");

            var tokens = await OAuthService.ExchangeCodeForTokenAsync(code ?? string.Empty, codeVerifier ?? string.Empty);

            if (tokens is not null && !string.IsNullOrEmpty(tokens.AccessToken))
            {
                ApiClientService.Instance.AuthToken = tokens.AccessToken;
                TokenExpiresAt = tokens.ExpiresAt;
                Log.Information("[MauiAuthService] Token exchange OK — expires {ExpiresAt:u}", tokens.ExpiresAt);
                Console.WriteLine($"[MauiAuthService] Token exchange OK — expires {tokens.ExpiresAt:u}");
                return tokens;
            }

            Log.Warning("[MauiAuthService] ExchangeCodeForTokenAsync returned null/empty token");
            Console.Error.WriteLine("[MauiAuthService] ExchangeCodeForTokenAsync returned null/empty token");
            return null;
        }
        catch (OAuthAuthorizationException ex)
        {
            Log.Error(ex, "[MauiAuthService] Authorization error: {Message}", ex.Message);
            Console.Error.WriteLine($"[MauiAuthService] Authorization error: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MauiAuthService] Token exchange error: {Message}", ex.Message);
            Console.Error.WriteLine($"[MauiAuthService] Token exchange error: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Clears all stored tokens and resets the ApiClientService auth state.
    /// </summary>
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
            Console.WriteLine("[MauiAuthService] Logged out — tokens cleared");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MauiAuthService] LogoutAsync failed");
            Console.Error.WriteLine($"[MauiAuthService] LogoutAsync failed: {ex}");
            throw;
        }
    }
}