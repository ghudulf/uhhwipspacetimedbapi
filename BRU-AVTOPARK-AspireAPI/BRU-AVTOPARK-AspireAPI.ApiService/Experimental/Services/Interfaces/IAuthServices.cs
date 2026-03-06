using System.Security.Claims;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Models.ViewModels;

namespace BRU_AVTOPARK.Services.Interfaces;

// ─── Token Service ───────────────────────────────────────────────────────────

/// <summary>
/// Encapsulates all JWT token operations: generation, validation, and claim extraction.
/// Replaces the inline GenerateJwtToken / token-parsing logic scattered across the original controller.
/// </summary>
public interface ITokenService
{
    /// <summary>Generate a signed JWT for an authenticated user.</summary>
    string GenerateToken(UserTokenPayload payload);

    /// <summary>Validate a JWT and return the claims principal, or null if invalid.</summary>
    ClaimsPrincipal? ValidateToken(string token);

    /// <summary>Extract specific claims without full validation (for display/debugging).</summary>
    UserTokenPayload? ReadTokenPayload(string token);

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
