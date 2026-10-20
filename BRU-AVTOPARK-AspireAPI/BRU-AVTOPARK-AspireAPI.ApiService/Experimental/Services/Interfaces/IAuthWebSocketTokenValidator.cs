namespace BRU_AVTOPARK.Services.Interfaces;

/// <summary>
/// Validates bearer tokens for the WebSocket auth service.
/// Abstracts the JWE/JWT routing logic from BaseController so the service
/// has no dependency on HttpContext or controller infrastructure.
/// </summary>
public interface IAuthWebSocketTokenValidator
{
    /// <summary>
    /// Validates a bearer token string directly (no HttpContext required).
    /// Routes JWE (5-segment) tokens to the tokeninfo endpoint and validates
    /// plain JWT (3-segment) tokens locally via signature verification.
    /// Returns null if the token is invalid or expired.
    /// </summary>
    Task<Dictionary<string, object>?> ValidateTokenDirectAsync(string token, CancellationToken cancellationToken = default);
}
