using SpacetimeDB.Types;

namespace BRU_AVTOPARK.Services.Interfaces;

/// <summary>
/// Handles SpacetimeDB identity generation and retrieval operations.
/// Replaces identity-related helper methods from the original AuthController.
/// </summary>
public interface IIdentityService
{
    /// <summary>
    /// Generate a new SpacetimeDB identity for user registration.
    /// Returns the identity as a base64-encoded string.
    /// </summary>
    Task<string?> GenerateIdentityAsync();

    /// <summary>
    /// Extract the user's identity from the current claims principal.
    /// Returns null if not authenticated or identity claim is missing.
    /// </summary>
    SpacetimeDB.Identity? GetUserIdentity(System.Security.Claims.ClaimsPrincipal user);

    /// <summary>
    /// Retrieve a user profile by their SpacetimeDB identity.
    /// </summary>
    Task<SpacetimeDB.Types.UserProfile?> GetUserByIdentityAsync(SpacetimeDB.Identity? userId);

    /// <summary>
    /// Generate a temporary JWT token for SpacetimeDB identity generation.
    /// Used during the registration process.
    /// </summary>
    Task<string> GenerateJwtForRegistrationAsync();
}

