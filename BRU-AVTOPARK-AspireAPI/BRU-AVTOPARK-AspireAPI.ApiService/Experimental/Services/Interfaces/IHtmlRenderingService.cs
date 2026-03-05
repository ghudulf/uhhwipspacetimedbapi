using TicketSalesApp.Services.client.module_bindings;

namespace BRU_AVTOPARK.Services.Interfaces;

/// <summary>
/// Encapsulates all HTML rendering logic for browser-based authentication flows.
/// Replaces the numerous Render* methods scattered throughout the original AuthController.
/// </summary>
public interface IHtmlRenderingService
{
    /// <summary>Render the login page with optional error/success messages.</summary>
    string RenderLoginForm(string? error = null, string? message = null);

    /// <summary>Render the TOTP setup page with QR code and secret key.</summary>
    string RenderTotpSetup(string qrCodeUri, string secretKey);

    /// <summary>Render the WebAuthn registration page.</summary>
    string RenderWebAuthnRegistration(string options);

    /// <summary>Render the magic link request page.</summary>
    string RenderMagicLinkForm(string? error = null, string? message = null);

    /// <summary>Render the QR code login page.</summary>
    string RenderQrLogin(string qrCode);

    /// <summary>Render the OAuth login form during authorization flow.</summary>
    string RenderOAuthLoginForm(string requestId, string clientName, string[] scopes, string? error = null);

    /// <summary>Render the success page after authentication.</summary>
    string RenderSuccessPage(string token);

    /// <summary>Render an error page with a message.</summary>
    string RenderErrorPage(string error);

    /// <summary>Render the user registration form (admin only).</summary>
    string RenderRegisterForm(string? error = null, string? message = null, int? adminCheckAttempt = null, bool isAdmin = false);

    /// <summary>Render the account claim form.</summary>
    string RenderClaimAccountForm(string? error = null, string? message = null);

    /// <summary>Render the user profile page with security settings and roles.</summary>
    string RenderProfilePage(
        UserProfile user,
        bool totpEnabled,
        List<WebAuthnCredentialDto> webAuthnCredentials,
        List<Role> roles,
        List<Permission> permissions);

    /// <summary>Render the OIDC clients list page (admin).</summary>
    string RenderOidcClientsList(List<ClientDto> clients, string? token = null);

    /// <summary>Render the OIDC scopes list page (admin).</summary>
    string RenderOidcScopesList(List<ScopeDto> scopes, string? token = null);

    /// <summary>Render the OIDC client details page (admin).</summary>
    string RenderOidcClientDetails(GetClientResponse client, string? token = null);

    /// <summary>Render the OIDC client create/edit form (admin).</summary>
    string RenderOidcClientForm(string? clientId = null, GetClientResponse? client = null, string? token = null);
}

/// <summary>
/// WebAuthn credential DTO for display purposes.
/// </summary>
public sealed record WebAuthnCredentialDto
{
    public string Id { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public bool IsActive { get; init; }
}

