using System.Security.Claims;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Models.ViewModels;
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
    /// Orchestrates OAuth authorization flow.
    /// NOTE: This is a placeholder - actual OAuth flow is handled by OpenIddict middleware.
    /// Controllers should NOT call this method directly - use OpenIddict's built-in flow.
    /// Coordinates OpenIdConnectService authorization flow.
    /// </summary>
    Task<OAuthAuthorizeResult> AuthorizeOAuthAsync(string clientId, string redirectUri, string scope, SpacetimeDB.Identity userId);

    /// <summary>
    /// Orchestrates OAuth token exchange.
    /// NOTE: This validates the client but does NOT perform the actual token exchange.
    /// The token exchange MUST be handled by OpenIddict middleware in the controller.
    /// Coordinates OpenIdConnectService token exchange.
    /// </summary>
    Task<OAuthTokenResult> ExchangeTokenAsync(string code, string clientId, string clientSecret);

    /// <summary>
    /// Builds a fresh ClaimsIdentity for OAuth token exchange with all user claims, roles, and permissions.
    /// This method contains the business logic from AuthController.Exchange() for building the token identity.
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

/// <summary>Result of OAuth authorization orchestration.</summary>
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
