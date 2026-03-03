namespace BRU_AVTOPARK_AspireAPI.ApiService.Services.Auth
{
    /// <summary>
    /// Renders server-side HTML pages used by the authentication flows
    /// (login, register, TOTP setup, WebAuthn, QR, Magic Link, OAuth consent,
    ///  profile, and OIDC admin pages).
    ///
    /// Extracting these templates from the controller keeps action methods thin
    /// and makes it straightforward to swap the renderer for a Razor/Blazor
    /// implementation later.
    /// </summary>
    public interface IAuthHtmlRenderer
    {
        // ── Auth Pages ───────────────────────────────────────────────────

        string RenderLoginForm(string? error = null, string? message = null);
        string RenderRegisterForm(string? error = null, string? message = null);
        string RenderClaimAccountForm(string? error = null, string? message = null);

        // ── Two-Factor ───────────────────────────────────────────────────

        string RenderTotpSetup(string qrCodeUri, string secretKey);
        string RenderWebAuthnRegistration(string options);
        string RenderWebAuthnLogin(string options);

        // ── Passwordless ─────────────────────────────────────────────────

        string RenderMagicLinkForm(string? error = null, string? message = null);
        string RenderQrLogin(string qrCode);

        // ── Success / Error ──────────────────────────────────────────────

        string RenderSuccess(string token);
        string RenderError(string message);

        // ── OAuth ────────────────────────────────────────────────────────

        string RenderOAuthLoginForm(string requestId, string clientName, string[] scopes, string? error = null);
        string RenderOAuthConsent(string requestId, string clientName, string[] scopes, string username);

        // ── Profile ──────────────────────────────────────────────────────

        string RenderProfile(ProfileViewModel model);

        // ── OIDC Admin ───────────────────────────────────────────────────

        string RenderClientsList(IEnumerable<ClientViewModel> clients);
        string RenderClientDetail(ClientDetailViewModel model);
        string RenderClientForm(ClientFormViewModel? model = null);
    }

    // ── View Models ──────────────────────────────────────────────────────

    public sealed class ProfileViewModel
    {
        public string Username { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string Token { get; set; } = string.Empty;
        public bool TotpEnabled { get; set; }
        public bool WebAuthnEnabled { get; set; }
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
    }

    public sealed class ClientViewModel
    {
        public string? ClientId { get; set; }
        public string? DisplayName { get; set; }
    }

    public sealed class ClientDetailViewModel
    {
        public string ClientId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string[] RedirectUris { get; set; } = Array.Empty<string>();
        public string[] PostLogoutRedirectUris { get; set; } = Array.Empty<string>();
        public string[] AllowedScopes { get; set; } = Array.Empty<string>();
        public bool RequireConsent { get; set; }
    }

    public sealed class ClientFormViewModel
    {
        public string? ClientId { get; set; }
        public string? DisplayName { get; set; }
        public string? RedirectUris { get; set; }
        public string? PostLogoutRedirectUris { get; set; }
        public string? AllowedScopes { get; set; }
        public bool RequireConsent { get; set; }
        public bool IsEdit { get; set; }
        public string? Error { get; set; }
        public string? Success { get; set; }
    }
}
