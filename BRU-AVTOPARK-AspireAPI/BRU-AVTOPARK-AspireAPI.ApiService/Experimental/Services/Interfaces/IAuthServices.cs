using System.Security.Claims;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Models.ViewModels;
using BRU_AVTOPARK.Services.Implementations;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace BRU_AVTOPARK.Services.Interfaces;

// ─── Token Service ───────────────────────────────────────────────────────────

/// <summary>
/// Encapsulates all JWT token operations: generation, validation, and claim extraction.
/// Replaces the inline GenerateJwtToken / token-parsing logic scattered across the original controller.
/// </summary>
public interface ITokenService
{
    /// <summary>Generate a signed JWT for an authenticated user by Identity (queries DB for roles/permissions).</summary>
    string GenerateToken(SpacetimeDB.Identity userId);

    /// <summary>Generate a signed JWT from pre-computed user data (for modular architecture).</summary>
    string GenerateToken(UserTokenPayload payload);

    /// <summary>Validate a JWT and return the claims principal, or null if invalid.</summary>
    ClaimsPrincipal? ValidateToken(string token);

    /// <summary>Extract specific claims without full validation (for display/debugging).</summary>
    UserTokenPayload? ReadTokenPayload(string token);

    /// <summary>Extract all token claims as a dictionary for client-side logging.</summary>
    Dictionary<string, object> ExtractTokenClaims(string token);

    /// <summary>Generate a cryptographically-secure random token (for 2FA temp tokens, etc.).</summary>
    string GenerateRandomToken(int byteLength = 32);
}

/// <summary>Lightweight payload carried inside JWTs.</summary>
public record UserTokenPayload
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public int PrimaryRole { get; init; }
    public List<string> Roles { get; init; } = [];
    public List<string> Permissions { get; init; } = [];
}

// ─── Auth Orchestration Service ──────────────────────────────────────────────

/// <summary>
/// High-level authentication orchestrator. Controllers delegate to this instead of
/// containing business logic directly. Enables unit testing without HTTP context.
/// </summary>
public interface IAuthOrchestrationService
{
    /// <summary>Authenticate a user by username/password. Returns null on failure.</summary>
    Task<AuthenticationResult?> AuthenticateAsync(string username, string password);

    /// <summary>Register a new user (admin-only operation).</summary>
    Task<RegisterResult> RegisterAsync(
        string username, string password, int role,
        string? email, string? phoneNumber,
        string? adminIdentity);

    /// <summary>Claim a dormant/guest account with a new password.</summary>
    Task<ClaimResult> ClaimAccountAsync(
        string username, string password, bool generateNewIdentity);

    /// <summary>Check whether the current user has admin privileges based on token claims.</summary>
    bool IsAdmin(ClaimsPrincipal? user, string? bearerToken);

    /// <summary>Check whether the current user has a specific permission.</summary>
    bool HasPermission(ClaimsPrincipal? user, string? bearerToken, string permissionName);

    // ─── Priority 1 Orchestration Methods (Critical - Direct DB Access Elimination) ───

    /// <summary>
    /// Orchestrates the complete login flow including 2FA detection and token generation.
    /// Coordinates AuthenticationService + TwoFactorService + SettingsService.
    /// </summary>
    Task<LoginResult> LoginAsync(string username, string password);

    /// <summary>
    /// Validates a TOTP code and temporary token, then generates JWT for successful validation.
    /// Coordinates TotpService + TwoFactorService.
    /// </summary>
    Task<TotpValidationResult> ValidateTotpAsync(string tempToken, string code);

    /// <summary>
    /// Validates a WebAuthn assertion and temporary token, then generates JWT for successful validation.
    /// Coordinates WebAuthnService + TwoFactorService.
    /// </summary>
    Task<WebAuthnValidationResult> ValidateWebAuthnAsync(string tempToken, AuthenticatorAssertionRawResponse assertionResponse);

    /// <summary>
    /// Validates a magic link token and generates JWT for successful validation.
    /// Coordinates MagicLinkService + TokenService.
    /// </summary>
    Task<MagicLinkValidationResult> ValidateMagicLinkAsync(string token);

    /// <summary>
    /// Retrieves complete user profile data using ProfileService.
    /// Orchestration wrapper for existing ProfileService.
    /// </summary>
    Task<ProfileViewModel?> GetProfileAsync(string userId, string? token);

    /// <summary>
    /// Get user profile by validating token and extracting userId.
    /// Overload that accepts just the token for endpoints that receive token as query parameter.
    /// </summary>
    Task<ProfileViewModel?> GetProfileAsync(string token);

    /// <summary>
    /// Get user profile with raw SpacetimeDB types for HTML rendering.
    /// Returns data in the format expected by HtmlRenderingService.RenderProfilePage.
    /// </summary>
    Task<ProfileRenderData?> GetProfileWithSpacetimeDataAsync(string token);

    // ─── Priority 2 Orchestration Methods (High - Complete TOTP/WebAuthn Flows) ───

    /// <summary>
    /// Sets up TOTP for a user by generating secret key and QR code URI.
    /// Coordinates TotpService.SetupTotpAsync.
    /// </summary>
    Task<TotpSetupResult> SetupTotpAsync(SpacetimeDB.Identity userId, string username);

    /// <summary>
    /// Enables TOTP for a user after verifying the TOTP code.
    /// Coordinates TotpService.EnableTotpAsync + SettingsService.EnableTotpAsync.
    /// </summary>
    Task<TotpEnableResult> EnableTotpAsync(SpacetimeDB.Identity userId, string username, string code, string secretKey);

    /// <summary>
    /// Disables TOTP for a user.
    /// Coordinates TotpService.DisableTotpAsync + SettingsService.DisableTotpAsync.
    /// </summary>
    Task<TotpDisableResult> DisableTotpAsync(SpacetimeDB.Identity userId);

    /// <summary>
    /// Registers a WebAuthn credential for a user.
    /// Coordinates WebAuthnService registration flow.
    /// </summary>
    Task<WebAuthnRegisterResult> RegisterWebAuthnAsync(SpacetimeDB.Identity userId, string username, AuthenticatorAttestationRawResponse attestationResponse);

    /// <summary>
    /// Gets all WebAuthn credentials for a user.
    /// Coordinates WebAuthnService.GetUserCredentialsAsync.
    /// </summary>
    Task<WebAuthnCredentialsResult> GetWebAuthnCredentialsAsync(SpacetimeDB.Identity userId);

    /// <summary>
    /// Removes a WebAuthn credential from a user's account.
    /// Coordinates WebAuthnService.RemoveCredentialAsync.
    /// </summary>
    Task<WebAuthnRemoveResult> RemoveWebAuthnCredentialAsync(SpacetimeDB.Identity userId, string credentialId);

    // ─── Priority 3 Orchestration Methods (Medium - OAuth/OIDC Flows) ────────

    /// <summary>
    /// Validates OAuth request parameters (client_id, redirect_uri, scope).
    /// This is a HELPER method that CAN be delegated to service layer.
    /// Coordinates OpenIdConnectService client validation.
    /// </summary>
    /// <param name="clientId">The OAuth client ID</param>
    /// <param name="redirectUri">The redirect URI</param>
    /// <param name="scope">The requested scopes</param>
    /// <returns>Validation result with success/failure and error message</returns>
    Task<OAuthValidationResult> ValidateOAuthRequestAsync(string clientId, string redirectUri, string scope);

    /// <summary>
    /// Builds ClaimsIdentity for OAuth authorization with user claims, roles, and permissions.
    /// This is a HELPER method that CAN be delegated to service layer.
    /// The controller will call SignIn() with this identity.
    /// </summary>
    /// <param name="username">The username</param>
    /// <param name="scopes">The requested OAuth scopes</param>
    /// <returns>ClaimsIdentity result ready for OpenIddict SignIn</returns>
    Task<ClaimsIdentityResult> BuildOAuthClaimsIdentityAsync(string username, string[] scopes);

    /// <summary>
    /// Validates that a user exists and is active for token exchange.
    /// This is a HELPER method that CAN be delegated to service layer.
    /// </summary>
    /// <param name="userId">The user's SpacetimeDB Identity as string</param>
    /// <returns>User validation result</returns>
    Task<UserValidationResult> ValidateUserForTokenExchangeAsync(string userId);

    /// <summary>
    /// Builds a fresh ClaimsIdentity for OAuth token exchange with all user claims, roles, and permissions.
    /// This is a HELPER method that CAN be delegated to service layer.
    /// The controller should call this after validating the authorization code via OpenIddict.
    /// </summary>
    /// <param name="userId">The user's SpacetimeDB Identity</param>
    /// <param name="scopes">The OAuth scopes from the original authorization</param>
    /// <param name="resources">The OAuth resources from the original authorization</param>
    /// <returns>ClaimsIdentity ready for OpenIddict SignIn, or null if user not found</returns>
    Task<System.Security.Claims.ClaimsIdentity?> BuildOAuthTokenIdentityAsync(SpacetimeDB.Identity userId, IEnumerable<string> scopes, IEnumerable<string> resources);

    /// <summary>
    /// Orchestrates OAuth userinfo retrieval.
    /// Coordinates OpenIdConnectService.CreateIdentityFromUserAsync.
    /// Returns user claims for OAuth userinfo endpoint.
    /// </summary>
    Task<OAuthUserInfoResult> GetUserInfoAsync(string username);

    /// <summary>
    /// Orchestrates OAuth client registration.
    /// Coordinates OpenIdConnectService.RegisterClientApplicationAsync.
    /// </summary>
    Task<OAuthClientResult> RegisterOAuthClientAsync(string clientId, string clientSecret, string displayName, 
        string[] redirectUris, string[] postLogoutRedirectUris, string[] allowedScopes, bool requireConsent);

    /// <summary>
    /// Orchestrates OAuth client update.
    /// Coordinates OpenIdConnectService.UpdateClientApplicationAsync.
    /// </summary>
    Task<OAuthClientResult> UpdateOAuthClientAsync(string clientId, string? clientSecret, string? displayName,
        string[]? redirectUris, string[]? postLogoutRedirectUris, string[]? allowedScopes, bool? requireConsent);

    /// <summary>
    /// Orchestrates OAuth client deletion.
    /// Coordinates OpenIdConnectService.DeleteClientApplicationAsync.
    /// </summary>
    Task<OAuthClientResult> DeleteOAuthClientAsync(string clientId);

    /// <summary>
    /// Orchestrates OAuth clients list retrieval.
    /// Coordinates OpenIdConnectService.GetAllClientApplicationsAsync.
    /// </summary>
    Task<OAuthClientsResult> GetOAuthClientsAsync();

    /// <summary>
    /// Orchestrates OAuth scopes list retrieval.
    /// Coordinates OpenIdConnectService.GetScopeManager.
    /// </summary>
    Task<OAuthScopesResult> GetOAuthScopesAsync();

    // ─── Priority 4 Orchestration Methods (Low - Already Clean) ──────────────

    /// <summary>
    /// Generates a QR code for login.
    /// Coordinates QRAuthenticationService.GenerateQRLoginTokenAsync.
    /// </summary>
    Task<QRLoginResult> GenerateQRLoginAsync(SpacetimeDB.Identity userId);

    /// <summary>
    /// Validates a QR login token.
    /// Coordinates QRAuthenticationService.ValidateQRLoginTokenAsync.
    /// </summary>
    Task<QRValidationResult> ValidateQRLoginAsync(string token);

    /// <summary>
    /// Sends a magic link email to a user.
    /// Coordinates MagicLinkService.SendMagicLinkAsync.
    /// </summary>
    Task<MagicLinkSendResult> SendMagicLinkAsync(string email, string? userAgent, string? ipAddress);

    /// <summary>
    /// Gets a user by their ID.
    /// Coordinates UserService.GetUserByIdAsync.
    /// </summary>
    Task<UserResult> GetUserAsync(uint userId);

    /// <summary>
    /// Gets all users in the system.
    /// Coordinates UserService.GetAllUsersAsync.
    /// </summary>
    Task<UsersResult> GetAllUsersAsync();

    // ─── Additional Business Logic Methods (Non-HTML) ─────────────────────────

    /// <summary>Performs direct QR login authentication.</summary>
    Task<QRLoginResult> DirectQRLoginAsync(string username, string deviceType);

    /// <summary>Checks QR login status for a session.</summary>
    Task<QRLoginStatusResult> CheckQRLoginStatusAsync(string sessionId);

    /// <summary>Cancels an active QR login session.</summary>
    Task<QRLoginCancelResult> CancelQRLoginAsync(string sessionId);

    /// <summary>Notifies about QR login status changes.</summary>
    Task<QRLoginNotifyResult> NotifyQRLoginAsync(string sessionId, string status);

    /// <summary>Gets WebAuthn registration options.</summary>
    Task<WebAuthnRegisterOptionsResult> GetWebAuthnRegisterOptionsAsync(string username);

    /// <summary>Gets WebAuthn login options.</summary>
    Task<WebAuthnLoginOptionsResult> GetWebAuthnLoginOptionsAsync(string username);

    /// <summary>Completes WebAuthn login flow.</summary>
    Task<WebAuthnLoginResult> CompleteWebAuthnLoginAsync(string username, AuthenticatorAssertionRawResponse assertionResponse);

    /// <summary>Gets OAuth client by ID.</summary>
    Task<OAuthClientDetailsResult> GetOAuthClientAsync(string clientId);

    /// <summary>Regenerates OAuth client secret.</summary>
    Task<OAuthClientSecretResult> RegenerateOAuthClientSecretAsync(string clientId);

    /// <summary>Updates user profile.</summary>
    Task<ProfileUpdateResult> UpdateProfileAsync(SpacetimeDB.Identity userId, string? email, string? phoneNumber, string? displayName);

    /// <summary>Changes user password.</summary>
    Task<PasswordChangeResult> ChangePasswordAsync(SpacetimeDB.Identity userId, string currentPassword, string newPassword);

    /// <summary>Logs out user.</summary>
    Task<LogoutResult> LogoutAsync(SpacetimeDB.Identity userId);

    /// <summary>Refreshes JWT token.</summary>
    Task<RefreshTokenResult> RefreshTokenAsync(string refreshToken);

    /// <summary>Gets user settings.</summary>
    Task<UserSettingsResult> GetSettingsAsync(SpacetimeDB.Identity userId);

    /// <summary>Updates user settings.</summary>
    Task<SettingsUpdateResult> UpdateSettingsAsync(SpacetimeDB.Identity userId, bool? totpEnabled, bool? webAuthnEnabled, bool? emailNotifications);

    /// <summary>Checks authentication status.</summary>
    Task<AuthStatusResult> CheckAuthStatusAsync(string? token);
}

public record AuthenticationResult
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool? EmailConfirmed { get; init; }
    public bool? PhoneNumberConfirmed { get; init; }
    public bool TotpEnabled { get; init; }
    public bool WebAuthnEnabled { get; init; }
    public int PrimaryRole { get; init; }
    public List<string> Roles { get; init; } = [];
}

public record RegisterResult(bool Success, string? ErrorMessage = null, UserDto? User = null);
public record ClaimResult(bool Success, string? ErrorMessage = null);

// ─── Priority 1 Orchestration Result Types ───────────────────────────────────

/// <summary>Result of login orchestration including 2FA detection.</summary>
public record LoginResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Token { get; init; }
    public Dictionary<string, object>? Claims { get; init; }
    public UserDto? User { get; init; }
    public bool RequiresTwoFactor { get; init; }
    public string? TwoFactorType { get; init; }
    public string? TempToken { get; init; }
    public bool TotpEnabled { get; init; }
    public bool WebAuthnEnabled { get; init; }
    public AssertionOptions? WebAuthnAssertionOptions { get; init; }

    public static LoginResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static LoginResult RequiresTwoFactorAuth(string tempToken, bool totpEnabled, bool webAuthnEnabled, AssertionOptions? webAuthnAssertionOptions = null) =>
        new()
        {
            Success = false,
            RequiresTwoFactor = true,
            TwoFactorType = totpEnabled ? "totp" : "webauthn",
            TempToken = tempToken,
            TotpEnabled = totpEnabled,
            WebAuthnEnabled = webAuthnEnabled,
            WebAuthnAssertionOptions = webAuthnAssertionOptions
        };

    public static LoginResult Successful(string token, UserDto user, Dictionary<string, object>? claims = null) =>
        new() { Success = true, Token = token, User = user, Claims = claims };
}

/// <summary>Result of TOTP validation orchestration.</summary>
public record TotpValidationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Token { get; init; }
    public Dictionary<string, object>? Claims { get; init; }
    public UserDto? User { get; init; }

    public static TotpValidationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static TotpValidationResult Successful(string token, UserDto user, Dictionary<string, object>? claims = null) =>
        new() { Success = true, Token = token, User = user, Claims = claims };
}

/// <summary>Result of WebAuthn validation orchestration.</summary>
public record WebAuthnValidationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Token { get; init; }
    public Dictionary<string, object>? Claims { get; init; }
    public UserDto? User { get; init; }

    public static WebAuthnValidationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static WebAuthnValidationResult Successful(string token, UserDto user, Dictionary<string, object>? claims = null) =>
        new() { Success = true, Token = token, User = user, Claims = claims };
}

/// <summary>Result of magic link validation orchestration.</summary>
public record MagicLinkValidationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Token { get; init; }
    public Dictionary<string, object>? Claims { get; init; }
    public UserDto? User { get; init; }

    public static MagicLinkValidationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static MagicLinkValidationResult Successful(string token, UserDto user, Dictionary<string, object>? claims = null) =>
        new() { Success = true, Token = token, User = user, Claims = claims };
}

// ─── Priority 2 Orchestration Result Types ───────────────────────────────────

/// <summary>Result of TOTP setup orchestration.</summary>
public record TotpSetupResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? SecretKey { get; init; }
    public string? QrCodeUri { get; init; }

    public static TotpSetupResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static TotpSetupResult Successful(string secretKey, string qrCodeUri) =>
        new() { Success = true, SecretKey = secretKey, QrCodeUri = qrCodeUri };
}

/// <summary>Result of TOTP enable orchestration.</summary>
public record TotpEnableResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static TotpEnableResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static TotpEnableResult Successful() =>
        new() { Success = true };
}

/// <summary>Result of TOTP disable orchestration.</summary>
public record TotpDisableResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static TotpDisableResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static TotpDisableResult Successful() =>
        new() { Success = true };
}

/// <summary>Result of WebAuthn registration orchestration.</summary>
public record WebAuthnRegisterResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static WebAuthnRegisterResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static WebAuthnRegisterResult Successful() =>
        new() { Success = true };
}

/// <summary>Result of WebAuthn credentials retrieval orchestration.</summary>
public record WebAuthnCredentialsResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<WebAuthnCredentialDto> Credentials { get; init; } = [];

    public static WebAuthnCredentialsResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static WebAuthnCredentialsResult Successful(List<WebAuthnCredentialDto> credentials) =>
        new() { Success = true, Credentials = credentials };
}

/// <summary>Result of WebAuthn credential removal orchestration.</summary>
public record WebAuthnRemoveResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static WebAuthnRemoveResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static WebAuthnRemoveResult Successful() =>
        new() { Success = true };
}

// ─── Priority 3 Orchestration Result Types (OAuth/OIDC) ──────────────────────

/// <summary>Result of OAuth request validation (helper method).</summary>
public record OAuthValidationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static OAuthValidationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static OAuthValidationResult Successful() =>
        new() { Success = true };
}

/// <summary>Result of ClaimsIdentity building for OAuth (helper method).</summary>
public record ClaimsIdentityResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public System.Security.Claims.ClaimsIdentity? Identity { get; init; }

    public static ClaimsIdentityResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static ClaimsIdentityResult Successful(System.Security.Claims.ClaimsIdentity identity) =>
        new() { Success = true, Identity = identity };
}

/// <summary>Result of user validation for token exchange (helper method).</summary>
public record UserValidationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public SpacetimeDB.Identity? UserId { get; init; }

    public static UserValidationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static UserValidationResult Successful(SpacetimeDB.Identity userId) =>
        new() { Success = true, UserId = userId };
}

/// <summary>Result of OAuth authorization orchestration.</summary>
[Obsolete("This method is deprecated. Use ValidateOAuthRequestAsync and BuildOAuthClaimsIdentityAsync instead.")]
public record OAuthAuthorizeResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AuthorizationCode { get; init; }
    public string? RedirectUri { get; init; }

    public static OAuthAuthorizeResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static OAuthAuthorizeResult Successful(string authorizationCode, string redirectUri) =>
        new() { Success = true, AuthorizationCode = authorizationCode, RedirectUri = redirectUri };
}

/// <summary>Result of OAuth token exchange orchestration.</summary>
[Obsolete("This method is deprecated. Use ValidateUserForTokenExchangeAsync and BuildOAuthTokenIdentityAsync instead.")]
public record OAuthTokenResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? IdToken { get; init; }
    public int ExpiresIn { get; init; }
    public string? TokenType { get; init; }

    public static OAuthTokenResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static OAuthTokenResult Successful(string accessToken, string? refreshToken, string? idToken, int expiresIn, string tokenType = "Bearer") =>
        new() { Success = true, AccessToken = accessToken, RefreshToken = refreshToken, IdToken = idToken, ExpiresIn = expiresIn, TokenType = tokenType };
}

/// <summary>Result of OAuth userinfo orchestration.</summary>
public record OAuthUserInfoResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, object>? Claims { get; init; }

    public static OAuthUserInfoResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static OAuthUserInfoResult Successful(Dictionary<string, object> claims) =>
        new() { Success = true, Claims = claims };
}

/// <summary>Result of OAuth client registration orchestration.</summary>
public record OAuthClientResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ClientId { get; init; }
    public string? DisplayName { get; init; }

    public static OAuthClientResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static OAuthClientResult Successful(string clientId, string displayName) =>
        new() { Success = true, ClientId = clientId, DisplayName = displayName };
}

/// <summary>Result of OAuth clients list orchestration.</summary>
public record OAuthClientsResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<OAuthClientDto> Clients { get; init; } = [];

    public static OAuthClientsResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static OAuthClientsResult Successful(List<OAuthClientDto> clients) =>
        new() { Success = true, Clients = clients };
}

/// <summary>DTO for OAuth client information.</summary>
public record OAuthClientDto
{
    public required string ClientId { get; init; }
    public required string DisplayName { get; init; }
    public List<string> RedirectUris { get; init; } = [];
    public List<string> AllowedScopes { get; init; } = [];
    public bool RequireConsent { get; init; }
}

/// <summary>Result of OAuth scopes list orchestration.</summary>
public record OAuthScopesResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<string> Scopes { get; init; } = [];

    public static OAuthScopesResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static OAuthScopesResult Successful(List<string> scopes) =>
        new() { Success = true, Scopes = scopes };
}

// ─── Priority 4 Orchestration Result Types (QR, Magic Link, User Management) ─

/// <summary>Result of QR login generation orchestration.</summary>
public record QRLoginResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? QrCode { get; init; }
    public string? RawData { get; init; }

    public static QRLoginResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static QRLoginResult Successful(string qrCode, string rawData) =>
        new() { Success = true, QrCode = qrCode, RawData = rawData };
}

/// <summary>Result of QR login validation orchestration.</summary>
public record QRValidationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Token { get; init; }
    public UserDto? User { get; init; }

    public static QRValidationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static QRValidationResult Successful(string token, UserDto user) =>
        new() { Success = true, Token = token, User = user };
}

/// <summary>Result of magic link send orchestration.</summary>
public record MagicLinkSendResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Email { get; init; }

    public static MagicLinkSendResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static MagicLinkSendResult Successful(string email) =>
        new() { Success = true, Email = email };
}

/// <summary>Result of user retrieval orchestration.</summary>
public record UserResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public UserDetailDto? User { get; init; }

    public static UserResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static UserResult Successful(UserDetailDto user) =>
        new() { Success = true, User = user };
}

/// <summary>Result of all users retrieval orchestration.</summary>
public record UsersResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<UserDto> Users { get; init; } = [];

    public static UsersResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static UsersResult Successful(List<UserDto> users) =>
        new() { Success = true, Users = users };
}

/// <summary>Detailed user DTO with roles and permissions.</summary>
public record UserDetailDto
{
    public required uint Id { get; init; }
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsActive { get; init; }
    public ulong CreatedAt { get; init; }
    public ulong? LastLoginAt { get; init; }
    public bool? EmailConfirmed { get; init; }
    public int Role { get; init; }
    public List<RoleDto> Roles { get; init; } = [];
    public List<string> Permissions { get; init; } = [];
}

/// <summary>Role DTO.</summary>
public record RoleDto
{
    public required uint RoleId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool IsSystem { get; init; }
}

// ─── Additional Business Logic Result Types ──────────────────────────────────

/// <summary>Result of QR login status check.</summary>
public record QRLoginStatusResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Status { get; init; }
    public string? Token { get; init; }
    public UserDto? User { get; init; }

    public static QRLoginStatusResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static QRLoginStatusResult Successful(string status, string? token = null, UserDto? user = null) =>
        new() { Success = true, Status = status, Token = token, User = user };
}

/// <summary>Result of QR login cancellation.</summary>
public record QRLoginCancelResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static QRLoginCancelResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static QRLoginCancelResult Successful() =>
        new() { Success = true };
}

/// <summary>Result of QR login notification.</summary>
public record QRLoginNotifyResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static QRLoginNotifyResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static QRLoginNotifyResult Successful() =>
        new() { Success = true };
}

/// <summary>Result of WebAuthn registration options generation.</summary>
public record WebAuthnRegisterOptionsResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Fido2NetLib.CredentialCreateOptions? Options { get; init; }

    public static WebAuthnRegisterOptionsResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static WebAuthnRegisterOptionsResult Successful(Fido2NetLib.CredentialCreateOptions options) =>
        new() { Success = true, Options = options };
}

/// <summary>Result of WebAuthn login options generation.</summary>
public record WebAuthnLoginOptionsResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Fido2NetLib.AssertionOptions? Options { get; init; }

    public static WebAuthnLoginOptionsResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static WebAuthnLoginOptionsResult Successful(Fido2NetLib.AssertionOptions options) =>
        new() { Success = true, Options = options };
}

/// <summary>Result of WebAuthn login completion.</summary>
public record WebAuthnLoginResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Token { get; init; }
    public Dictionary<string, object>? Claims { get; init; }
    public UserDto? User { get; init; }

    public static WebAuthnLoginResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static WebAuthnLoginResult Successful(string token, UserDto user, Dictionary<string, object>? claims = null) =>
        new() { Success = true, Token = token, User = user, Claims = claims };
}

/// <summary>Result of OAuth client details retrieval.</summary>
public record OAuthClientDetailsResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public OAuthClientDetailDto? Client { get; init; }

    public static OAuthClientDetailsResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static OAuthClientDetailsResult Successful(OAuthClientDetailDto client) =>
        new() { Success = true, Client = client };
}

/// <summary>Detailed OAuth client DTO.</summary>
public record OAuthClientDetailDto
{
    public required string ClientId { get; init; }
    public required string DisplayName { get; init; }
    public List<string> RedirectUris { get; init; } = [];
    public List<string> PostLogoutRedirectUris { get; init; } = [];
    public List<string> AllowedScopes { get; init; } = [];
    public bool RequireConsent { get; init; }
}

/// <summary>Result of OAuth client secret regeneration.</summary>
public record OAuthClientSecretResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ClientSecret { get; init; }

    public static OAuthClientSecretResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static OAuthClientSecretResult Successful(string clientSecret) =>
        new() { Success = true, ClientSecret = clientSecret };
}

/// <summary>Result of profile update.</summary>
public record ProfileUpdateResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static ProfileUpdateResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static ProfileUpdateResult Successful() =>
        new() { Success = true };
}

/// <summary>Result of password change.</summary>
public record PasswordChangeResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static PasswordChangeResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static PasswordChangeResult Successful() =>
        new() { Success = true };
}

/// <summary>Result of logout.</summary>
public record LogoutResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static LogoutResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static LogoutResult Successful() =>
        new() { Success = true };
}

/// <summary>Result of token refresh.</summary>
public record RefreshTokenResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Token { get; init; }
    public string? RefreshToken { get; init; }
    public DateTime? ExpiresAt { get; init; }

    public static RefreshTokenResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static RefreshTokenResult Successful(string token, string refreshToken, DateTime expiresAt) =>
        new() { Success = true, Token = token, RefreshToken = refreshToken, ExpiresAt = expiresAt };
}

/// <summary>Result of user settings retrieval.</summary>
public record UserSettingsResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public UserSettingsDto? Settings { get; init; }

    public static UserSettingsResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static UserSettingsResult Successful(UserSettingsDto settings) =>
        new() { Success = true, Settings = settings };
}

/// <summary>User settings DTO for result.</summary>
public record UserSettingsDto
{
    public bool TotpEnabled { get; init; }
    public bool WebAuthnEnabled { get; init; }
    public bool EmailNotifications { get; init; }
}

/// <summary>Result of settings update.</summary>
public record SettingsUpdateResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static SettingsUpdateResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static SettingsUpdateResult Successful() =>
        new() { Success = true };
}

/// <summary>Result of auth status check.</summary>
public record AuthStatusResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsAuthenticated { get; init; }
    public string? Username { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public UserDto? User { get; init; }

    public static AuthStatusResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static AuthStatusResult Successful(bool isAuthenticated, string? username = null, DateTime? expiresAt = null, UserDto? user = null) =>
        new() { Success = true, IsAuthenticated = isAuthenticated, Username = username, ExpiresAt = expiresAt, User = user };
}

// ─── Profile Service ─────────────────────────────────────────────────────────

/// <summary>
/// Aggregates profile data from multiple data sources (SpacetimeDB tables for
/// user profile, roles, permissions, security settings, WebAuthn credentials).
/// </summary>
public interface IProfileService
{
    /// <summary>Build a complete profile view model for the authenticated user.</summary>
    Task<ProfileViewModel?> GetProfileAsync(string userId, string? token);
}

// ─── Request Detection ───────────────────────────────────────────────────────

/// <summary>
/// Determines whether the current HTTP request originates from a browser
/// (expecting HTML) or an API client (expecting JSON).
/// Extracted from the original IsBrowserRequest() method.
/// </summary>
public interface IRequestDetector
{
    bool IsBrowserRequest();
}

// Note: UserProfile, Role, Permission, and WebAuthnCredentialDto are defined in BRU_AVTOPARK.Models.Responses
// ProfileViewModel is defined in BRU_AVTOPARK.Models.ViewModels
