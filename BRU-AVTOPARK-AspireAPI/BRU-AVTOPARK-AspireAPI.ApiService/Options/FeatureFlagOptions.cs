using System;

namespace TicketSalesApp.AdminServer.Configuration
{
    /// <summary>
    /// Feature flags for controlling gradual rollout of refactored authentication endpoints.
    /// All flags default to false (disabled) for safety.
    /// </summary>
    public class FeatureFlagOptions
    {
        public const string FeatureFlags = "FeatureFlags";

        // ============================================
        // Traditional Authentication (2 endpoints)
        // ============================================
        
        /// <summary>
        /// GET/POST /api/auth/login - Login page (HTML) and authentication
        /// </summary>
        public bool EnableLoginRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET/POST /api/auth/register - Registration page (HTML) and account creation
        /// </summary>
        public bool EnableRegisterRefactoring { get; set; } = false;

        // ============================================
        // TOTP (4 endpoints)
        // ============================================
        
        /// <summary>
        /// GET /api/auth/totp/setup - Setup TOTP for user
        /// </summary>
        public bool EnableTotpSetupRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/totp/verify - Verify TOTP code during setup
        /// </summary>
        public bool EnableTotpVerifyRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/totp/disable - Disable TOTP for user
        /// </summary>
        public bool EnableTotpDisableRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/totp/validate - Validate TOTP code during login
        /// </summary>
        public bool EnableTotpValidateRefactoring { get; set; } = false;

        // ============================================
        // WebAuthn (7 endpoints)
        // ============================================
        
        /// <summary>
        /// POST /api/auth/webauthn/register/options - Get WebAuthn registration options
        /// </summary>
        public bool EnableWebAuthnRegisterOptionsRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/webauthn/register/complete - Complete WebAuthn registration
        /// </summary>
        public bool EnableWebAuthnRegisterCompleteRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/webauthn/login/options - Get WebAuthn login options
        /// </summary>
        public bool EnableWebAuthnLoginOptionsRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/webauthn/login/complete - Complete WebAuthn login
        /// </summary>
        public bool EnableWebAuthnLoginCompleteRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/webauthn/validate - Validate WebAuthn assertion during 2FA
        /// </summary>
        public bool EnableWebAuthnValidateRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /api/auth/webauthn/credentials - Get user's WebAuthn credentials
        /// </summary>
        public bool EnableWebAuthnCredentialsRefactoring { get; set; } = false;
        
        /// <summary>
        /// DELETE /api/auth/webauthn/credentials/{id} - Remove WebAuthn credential
        /// </summary>
        public bool EnableWebAuthnCredentialDeleteRefactoring { get; set; } = false;

        // ============================================
        // Magic Link (3 endpoints)
        // ============================================
        
        /// <summary>
        /// POST /api/auth/magic-link/send - Send magic link email
        /// </summary>
        public bool EnableMagicLinkSendRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/validate-magic-link - Validate magic link token
        /// </summary>
        public bool EnableMagicLinkValidateRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /api/auth/magic-link - Show magic link login page
        /// </summary>
        public bool EnableMagicLinkPageRefactoring { get; set; } = false;

        // ============================================
        // QR Authentication (7 endpoints)
        // ============================================
        
        /// <summary>
        /// GET /api/auth/qr-login - Show QR login page
        /// </summary>
        public bool EnableQRLoginPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/qr-login/generate - Generate QR login token
        /// </summary>
        public bool EnableQRLoginGenerateRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/qr-login/validate - Validate QR login token
        /// </summary>
        public bool EnableQRLoginValidateRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/qr-login/direct - Direct QR login (no 2FA)
        /// </summary>
        public bool EnableQRLoginDirectRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /api/auth/qr-login/status - Check QR login status
        /// </summary>
        public bool EnableQRLoginStatusRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/qr-login/cancel - Cancel QR login attempt
        /// </summary>
        public bool EnableQRLoginCancelRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/qr-login/notify - Notify device of successful login
        /// </summary>
        public bool EnableQRLoginNotifyRefactoring { get; set; } = false;

        // ============================================
        // OAuth/OIDC Core Flow (3 endpoints)
        // ============================================
        
        /// <summary>
        /// GET/POST ~/connect/authorize - OAuth authorization endpoint
        /// </summary>
        public bool EnableOAuthAuthorizeRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST ~/connect/token - OAuth token exchange endpoint
        /// </summary>
        public bool EnableOAuthTokenRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET ~/connect/userinfo - OAuth user info endpoint
        /// </summary>
        public bool EnableOAuthUserInfoRefactoring { get; set; } = false;

        // ============================================
        // OAuth Client Management API (7 endpoints)
        // ============================================
        
        /// <summary>
        /// POST /api/oauth/clients - Register new OAuth client
        /// </summary>
        public bool EnableOAuthClientRegisterRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /api/oauth/clients - List all OAuth clients
        /// </summary>
        public bool EnableOAuthClientListRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /api/oauth/clients/{id} - Get OAuth client details
        /// </summary>
        public bool EnableOAuthClientDetailsRefactoring { get; set; } = false;
        
        /// <summary>
        /// PUT /api/oauth/clients/{id} - Update OAuth client
        /// </summary>
        public bool EnableOAuthClientUpdateRefactoring { get; set; } = false;
        
        /// <summary>
        /// DELETE /api/oauth/clients/{id} - Delete OAuth client
        /// </summary>
        public bool EnableOAuthClientDeleteRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /api/oauth/scopes - List available OAuth scopes
        /// </summary>
        public bool EnableOAuthScopesRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/oauth/clients/{id}/regenerate-secret - Regenerate client secret
        /// </summary>
        public bool EnableOAuthClientRegenerateSecretRefactoring { get; set; } = false;

        // ============================================
        // OAuth Admin HTML Pages (13 endpoints)
        // ============================================
        
        /// <summary>
        /// GET /oauth/clients - OAuth clients list page
        /// </summary>
        public bool EnableOAuthClientsPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/clients/new - New OAuth client form page
        /// </summary>
        public bool EnableOAuthClientNewPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/clients/{id} - OAuth client details page
        /// </summary>
        public bool EnableOAuthClientDetailsPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/clients/{id}/edit - Edit OAuth client form page
        /// </summary>
        public bool EnableOAuthClientEditPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/scopes - OAuth scopes list page
        /// </summary>
        public bool EnableOAuthScopesPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/authorizations - OAuth authorizations list page
        /// </summary>
        public bool EnableOAuthAuthorizationsPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/tokens - OAuth tokens list page
        /// </summary>
        public bool EnableOAuthTokensPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/dashboard - OAuth admin dashboard page
        /// </summary>
        public bool EnableOAuthDashboardPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/settings - OAuth settings page
        /// </summary>
        public bool EnableOAuthSettingsPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/logs - OAuth audit logs page
        /// </summary>
        public bool EnableOAuthLogsPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/help - OAuth help/documentation page
        /// </summary>
        public bool EnableOAuthHelpPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/test - OAuth test/playground page
        /// </summary>
        public bool EnableOAuthTestPageRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /oauth/callback - OAuth callback page
        /// </summary>
        public bool EnableOAuthCallbackPageRefactoring { get; set; } = false;

        // ============================================
        // Profile & Utility (8 endpoints)
        // ============================================
        
        /// <summary>
        /// GET /api/auth/profile - Get user profile
        /// </summary>
        public bool EnableProfileRefactoring { get; set; } = false;
        
        /// <summary>
        /// PUT /api/auth/profile - Update user profile
        /// </summary>
        public bool EnableProfileUpdateRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/change-password - Change user password
        /// </summary>
        public bool EnableChangePasswordRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/logout - Logout user
        /// </summary>
        public bool EnableLogoutRefactoring { get; set; } = false;
        
        /// <summary>
        /// POST /api/auth/refresh - Refresh JWT token
        /// </summary>
        public bool EnableRefreshTokenRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /api/auth/settings - Get user authentication settings
        /// </summary>
        public bool EnableSettingsRefactoring { get; set; } = false;
        
        /// <summary>
        /// PUT /api/auth/settings - Update user authentication settings
        /// </summary>
        public bool EnableSettingsUpdateRefactoring { get; set; } = false;
        
        /// <summary>
        /// GET /api/auth/status - Check authentication status
        /// </summary>
        public bool EnableStatusRefactoring { get; set; } = false;
    }
}
