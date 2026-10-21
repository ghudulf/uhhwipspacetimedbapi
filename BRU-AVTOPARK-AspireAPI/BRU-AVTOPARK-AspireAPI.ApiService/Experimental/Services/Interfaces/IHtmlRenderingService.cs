using BRU_AVTOPARK.Models.Responses;

namespace BRU_AVTOPARK.Services.Interfaces;

/// <summary>
/// Encapsulates all HTML rendering logic for browser-based authentication flows.
/// Replaces the numerous Render* methods scattered throughout the original AuthController.
/// </summary>
public interface IHtmlRenderingService
{
    /// <summary>Render the login page with optional error/success messages.</summary>
    Task<string> RenderLoginForm(string? error = null, string? message = null);

    /// <summary>Render the TOTP setup page with QR code and secret key.</summary>
    Task<string> RenderTotpSetup(string qrCodeUri, string secretKey);

    /// <summary>Render the WebAuthn registration page.</summary>
    Task<string> RenderWebAuthnRegistration(string options);

    /// <summary>Render the magic link request page.</summary>
    Task<string> RenderMagicLinkForm(string? error = null, string? message = null);

    /// <summary>Render the QR code login page.</summary>
    Task<string> RenderQrLogin(string qrCode);

    /// <summary>Render the OAuth login form during authorization flow.</summary>
    Task<string> RenderOAuthLoginForm(string requestId, string clientName, string[] scopes, string? error = null);

    /// <summary>Render the success page after authentication.</summary>
    Task<string> RenderSuccessPage(string token);

    /// <summary>Render an error page with a message.</summary>
    Task<string> RenderErrorPage(string error);

    /// <summary>Render the logout confirmation page.</summary>
    Task<string> RenderLogoutPage();

    /// <summary>Render the user registration form (admin only).</summary>
    Task<string> RenderRegisterForm(string? error = null, string? message = null, int? adminCheckAttempt = null, bool isAdmin = false);

    /// <summary>Render the account claim form.</summary>
    Task<string> RenderClaimAccountForm(string? error = null, string? message = null);

    /// <summary>Render the user profile page with security settings and roles.</summary>
    Task<string> RenderProfilePage(
        SpacetimeDB.Types.UserProfile user,
        bool totpEnabled,
        List<WebAuthnCredentialDto> webAuthnCredentials,
        List<SpacetimeDB.Types.Role> roles,
        List<SpacetimeDB.Types.Permission> permissions);

    /// <summary>Render the OIDC clients list page (admin).</summary>
    Task<string> RenderOidcClientsList(List<ClientDto> clients, string? token = null);

    /// <summary>Render the OIDC scopes list page (admin).</summary>
    Task<string> RenderOidcScopesList(List<ScopeDto> scopes, string? token = null);

    /// <summary>Render the OIDC client details page (admin).</summary>
    Task<string> RenderOidcClientDetails(GetClientResponse client, string? token = null);

    /// <summary>Render the OIDC client create/edit form (admin).</summary>
    Task<string> RenderOidcClientForm(string? clientId = null, GetClientResponse? client = null, string? token = null);
}
