namespace TicketSalesApp.AdminServer.Configuration
{
    /// <summary>
    /// After legacy AuthController removal, these flags no longer select between implementations.
    /// They now control whether each endpoint is available at all. Set to false to temporarily
    /// disable an endpoint without a deployment (e.g., security incident, performance issue).
    /// All flags default to true (enabled).
    /// </summary>
    public class FeatureFlagOptions
    {
        public const string FeatureFlags = "FeatureFlags";

        // ============================================
        // Traditional Authentication (2 endpoints)
        // ============================================

        /// <summary>
        /// GET/POST /api/auth/login - Login page (HTML) and authentication.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableLoginRefactoring { get; set; } = true;

        /// <summary>
        /// GET/POST /api/auth/register - Registration page (HTML) and account creation.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableRegisterRefactoring { get; set; } = true;

        // ============================================
        // TOTP (4 endpoints)
        // ============================================

        /// <summary>
        /// GET /api/auth/totp/setup - Setup TOTP for user.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableTotpSetupRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/totp/verify - Verify TOTP code during setup.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableTotpVerifyRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/totp/disable - Disable TOTP for user.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableTotpDisableRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/totp/validate - Validate TOTP code during login.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableTotpValidateRefactoring { get; set; } = true;

        // ============================================
        // WebAuthn (7 endpoints)
        // ============================================

        /// <summary>
        /// POST /api/auth/webauthn/register/options - Get WebAuthn registration options.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableWebAuthnRegisterOptionsRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/webauthn/register/complete - Complete WebAuthn registration.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableWebAuthnRegisterCompleteRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/webauthn/login/options - Get WebAuthn login options.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableWebAuthnLoginOptionsRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/webauthn/login/complete - Complete WebAuthn login.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableWebAuthnLoginCompleteRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/webauthn/validate - Validate WebAuthn assertion during 2FA.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableWebAuthnValidateRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/auth/webauthn/credentials - Get user's WebAuthn credentials.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableWebAuthnCredentialsRefactoring { get; set; } = true;

        /// <summary>
        /// DELETE /api/auth/webauthn/credentials/{id} - Remove WebAuthn credential.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableWebAuthnCredentialDeleteRefactoring { get; set; } = true;

        // ============================================
        // Magic Link (3 endpoints)
        // ============================================

        /// <summary>
        /// POST /api/auth/magic-link/send - Send magic link email.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableMagicLinkSendRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/validate-magic-link - Validate magic link token.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableMagicLinkValidateRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/auth/magic-link - Show magic link login page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableMagicLinkPageRefactoring { get; set; } = true;

        // ============================================
        // QR Authentication (7 endpoints)
        // ============================================

        /// <summary>
        /// GET /api/auth/qr-login - Show QR login page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableQRLoginPageRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/qr-login/generate - Generate QR login token.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableQRLoginGenerateRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/qr-login/validate - Validate QR login token.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableQRLoginValidateRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/qr-login/direct - Direct QR login (no 2FA).
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableQRLoginDirectRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/auth/qr-login/status - Check QR login status.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableQRLoginStatusRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/qr-login/cancel - Cancel QR login attempt.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableQRLoginCancelRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/qr-login/notify - Notify device of successful login.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableQRLoginNotifyRefactoring { get; set; } = true;

        // ============================================
        // OAuth/OIDC Core Flow (5 endpoints)
        // ============================================

        /// <summary>
        /// GET/POST ~/connect/authorize - OAuth authorization endpoint.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthAuthorizeRefactoring { get; set; } = true;

        /// <summary>
        /// POST ~/connect/token - OAuth token exchange endpoint.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthTokenRefactoring { get; set; } = true;

        /// <summary>
        /// GET ~/connect/userinfo - OAuth user info endpoint.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthUserInfoRefactoring { get; set; } = true;

        /// <summary>
        /// GET ~/connect/tokeninfo - Token validation endpoint for BaseController.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthTokenInfoRefactoring { get; set; } = true;

        /// <summary>
        /// POST ~/connect/authorize/callback - OAuth authorization callback form handler.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthAuthorizeCallbackRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/oauth/consent - JSON endpoint for headless OAuth consent (grant or deny).
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthConsentRefactoring { get; set; } = true;

        // ============================================
        // OAuth Client Management API (7 endpoints)
        // ============================================

        /// <summary>
        /// POST /api/oauth/clients - Register new OAuth client.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthClientRegisterRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/oauth/clients - List all OAuth clients.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthClientListRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/oauth/clients/{id} - Get OAuth client details.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthClientDetailsRefactoring { get; set; } = true;

        /// <summary>
        /// PUT /api/oauth/clients/{id} - Update OAuth client.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthClientUpdateRefactoring { get; set; } = true;

        /// <summary>
        /// DELETE /api/oauth/clients/{id} - Delete OAuth client.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthClientDeleteRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/oauth/scopes - List available OAuth scopes.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthScopesRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/oauth/clients/{id}/regenerate-secret - Regenerate client secret.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthClientRegenerateSecretRefactoring { get; set; } = true;

        // ============================================
        // OAuth Admin HTML Pages (13 endpoints)
        // ============================================

        /// <summary>
        /// GET /oauth/clients - OAuth clients list page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthClientsPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/clients/new - New OAuth client form page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthClientNewPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/clients/{id} - OAuth client details page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthClientDetailsPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/clients/{id}/edit - Edit OAuth client form page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthClientEditPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/scopes - OAuth scopes list page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthScopesPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/authorizations - OAuth authorizations list page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthAuthorizationsPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/tokens - OAuth tokens list page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthTokensPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/dashboard - OAuth admin dashboard page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthDashboardPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/settings - OAuth settings page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthSettingsPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/logs - OAuth audit logs page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthLogsPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/help - OAuth help/documentation page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthHelpPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/test - OAuth test/playground page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthTestPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /oauth/callback - OAuth callback page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthCallbackPageRefactoring { get; set; } = true;

        // ============================================
        // Profile & Utility (8 endpoints)
        // ============================================

        /// <summary>
        /// GET /api/auth/profile - Get user profile.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableProfileRefactoring { get; set; } = true;

        /// <summary>
        /// PUT /api/auth/profile - Update user profile.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableProfileUpdateRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/change-password - Change user password.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableChangePasswordRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/logout - Logout user.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableLogoutRefactoring { get; set; } = true;

        /// <summary>
        /// POST /api/auth/refresh - Refresh JWT token.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableRefreshTokenRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/auth/settings - Get user authentication settings.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableSettingsRefactoring { get; set; } = true;

        /// <summary>
        /// PUT /api/auth/settings - Update user authentication settings.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableSettingsUpdateRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/auth/status - Check authentication status.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableStatusRefactoring { get; set; } = true;

        // ============================================
        // WebSocket Authentication (1 endpoint)
        // ============================================

        /// <summary>
        /// GET /api/auth/ws - Real-time authentication over WebSocket.
        /// Supports: token validation, QR login status push, auth event streaming.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableWebSocketAuthRefactoring { get; set; } = true;

        // ============================================
        // Debug Endpoints (1 endpoint)
        // ============================================

        /// <summary>
        /// GET ~/debug/tokentest - Debug endpoint for token parsing and validation.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableDebugTokenTestRefactoring { get; set; } = true;

        // ============================================
        // Utility Pages (4 endpoints)
        // ============================================

        /// <summary>
        /// GET /api/auth/success - Success page after authentication.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableSuccessPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/auth/error - Error page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableErrorPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/auth/claim-account - Claim account page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableClaimAccountPageRefactoring { get; set; } = true;

        /// <summary>
        /// GET /api/auth/webauthn/register - WebAuthn registration page.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableWebAuthnRegisterPageRefactoring { get; set; } = true;

        // ============================================
        // Headless OAuth Flow (Task 34.3)
        // ============================================

        /// <summary>
        /// POST /api/auth/oauth/authorize - Backchannel OAuth authorize endpoint for headless/native clients.
        /// Allows non-browser clients to complete the full OAuth authorization flow without browser redirects.
        /// Only allowed for confidential/native client types.
        /// Controls endpoint availability. When false, the endpoint returns 503 Service Unavailable. Default: true (enabled).
        /// </summary>
        public bool EnableOAuthBackchannelAuthorizeRefactoring { get; set; } = true;
    }
}
