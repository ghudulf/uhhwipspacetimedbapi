using SpacetimeDB.Types;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Services.Auth
{
    /// <summary>
    /// Abstracts all JWT token generation, validation, and utility operations.
    /// Extracted from the monolithic AuthController to enable reuse and testability.
    /// </summary>
    public interface ITokenService
    {
        /// <summary>
        /// Generates a signed JWT access token for an authenticated user,
        /// embedding roles, permissions, and SpacetimeDB identity claims.
        /// </summary>
        string GenerateJwtToken(UserProfile userProfile);

        /// <summary>
        /// Extracts all claims from a JWT token string and returns them
        /// as a dictionary (multi-valued claims become List&lt;string&gt;).
        /// </summary>
        Dictionary<string, object> ExtractTokenClaims(string token);

        /// <summary>
        /// Generates a short-lived JWT suitable for the registration flow
        /// where a temporary identity is required.
        /// </summary>
        string GenerateTemporaryRegistrationToken();

        /// <summary>
        /// Generates a cryptographically random base-64 token
        /// (used for 2FA temp tokens, CSRF, etc.).
        /// </summary>
        string GenerateRandomToken();

        /// <summary>
        /// Validates a JWT token and returns the claims principal.
        /// Returns null when validation fails.
        /// </summary>
        System.Security.Claims.ClaimsPrincipal? ValidateToken(string token);
    }
}
