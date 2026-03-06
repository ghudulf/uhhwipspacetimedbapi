using System.Threading.Tasks;
using SpacetimeDB;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for managing temporary two-factor authentication tokens
    /// </summary>
    public interface ITwoFactorService
    {
        /// <summary>
        /// Creates a temporary token for 2FA validation (TOTP, WebAuthn, Magic Link)
        /// </summary>
        /// <param name="userId">The user identity</param>
        /// <param name="tokenType">The type of token (e.g., "login", "totp", "webauthn")</param>
        /// <param name="deviceInfo">Optional device information</param>
        /// <param name="ipAddress">Optional IP address</param>
        /// <returns>The generated token string</returns>
        Task<string> CreateTempTokenAsync(Identity userId, string tokenType, string? deviceInfo = null, string? ipAddress = null);

        /// <summary>
        /// Validates a temporary token and returns the associated user ID if valid
        /// </summary>
        /// <param name="token">The token to validate</param>
        /// <returns>A tuple containing validity status and the associated user ID if valid</returns>
        Task<(bool isValid, Identity? userId)> ValidateTempTokenAsync(string token);

        /// <summary>
        /// Marks a token as used to prevent replay attacks
        /// </summary>
        /// <param name="token">The token to mark as used</param>
        /// <returns>True if the token was successfully marked as used</returns>
        Task<bool> MarkTokenAsUsedAsync(string token);

        /// <summary>
        /// Removes expired tokens from the database (background cleanup job)
        /// </summary>
        /// <returns>The number of tokens cleaned up</returns>
        Task<int> CleanupExpiredTokensAsync();
    }
}
