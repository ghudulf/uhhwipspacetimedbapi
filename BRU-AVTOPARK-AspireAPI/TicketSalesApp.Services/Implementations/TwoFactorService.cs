using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    /// <summary>
    /// Implementation of the two-factor authentication token service
    /// </summary>
    public class TwoFactorService : ITwoFactorService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<TwoFactorService> _logger;

        public TwoFactorService(
            ISpacetimeDBService spacetimeService,
            ILogger<TwoFactorService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a temporary token for 2FA validation (TOTP, WebAuthn, Magic Link)
        /// </summary>
        public async Task<string> CreateTempTokenAsync(Identity userId, string tokenType, string? deviceInfo = null, string? ipAddress = null)
        {
            try
            {
                _logger.LogInformation("Creating temporary 2FA token for user: {UserId}, type: {TokenType}", userId, tokenType);

                // Generate a secure random token
                var token = GenerateSecureToken();

                // Set expiration to 10 minutes from now
                var expiresAt = (ulong)DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();

                var conn = _spacetimeService.GetConnection();

                // Create the token in the database
                conn.Reducers.CreateTwoFactorToken(
                    userId,
                    token,
                    isUsed: false,
                    expiresAt,
                    deviceInfo,
                    ipAddress
                );

                _logger.LogInformation("Successfully created temporary 2FA token for user: {UserId}", userId);

                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating temporary 2FA token for user: {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Validates a temporary token and returns the associated user ID if valid
        /// </summary>
        public async Task<(bool isValid, Identity? userId)> ValidateTempTokenAsync(string token)
        {
            try
            {
                _logger.LogInformation("Validating temporary 2FA token: {Token}", token);

                var conn = _spacetimeService.GetConnection();

                // Find the token
                var twoFactorToken = conn.Db.TwoFactorToken.Iter()
                    .FirstOrDefault(t => t.Token == token);

                if (twoFactorToken == null)
                {
                    _logger.LogWarning("Two-factor token not found: {Token}", token);
                    return (false, null);
                }

                // Check if token is expired
                var currentTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (twoFactorToken.ExpiresAt < currentTime)
                {
                    _logger.LogWarning("Two-factor token expired: {Token}", token);
                    return (false, null);
                }

                // Check if token has already been used
                if (twoFactorToken.IsUsed)
                {
                    _logger.LogWarning("Two-factor token already used: {Token}", token);
                    return (false, null);
                }

                _logger.LogInformation("Successfully validated temporary 2FA token for user: {UserId}", twoFactorToken.UserId);

                return (true, twoFactorToken.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating temporary 2FA token: {Token}", token);
                return (false, null);
            }
        }

        /// <summary>
        /// Marks a token as used to prevent replay attacks
        /// </summary>
        public async Task<bool> MarkTokenAsUsedAsync(string token)
        {
            try
            {
                _logger.LogInformation("Marking temporary 2FA token as used: {Token}", token);

                var conn = _spacetimeService.GetConnection();

                // Find the token
                var twoFactorToken = conn.Db.TwoFactorToken.Iter()
                    .FirstOrDefault(t => t.Token == token);

                if (twoFactorToken == null)
                {
                    _logger.LogWarning("Two-factor token not found when marking as used: {Token}", token);
                    return false;
                }

                // Update the token to mark it as used
                conn.Reducers.UpdateTwoFactorToken(
                    twoFactorToken.Id,
                    twoFactorToken.UserId,
                    twoFactorToken.Token,
                    isUsed: true,
                    twoFactorToken.ExpiresAt
                );

                _logger.LogInformation("Successfully marked temporary 2FA token as used: {Token}", token);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking temporary 2FA token as used: {Token}", token);
                return false;
            }
        }

        /// <summary>
        /// Removes expired tokens from the database (background cleanup job)
        /// </summary>
        public async Task<int> CleanupExpiredTokensAsync()
        {
            try
            {
                _logger.LogInformation("Starting cleanup of expired 2FA tokens");

                var conn = _spacetimeService.GetConnection();
                var currentTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // Find all expired tokens
                var expiredTokens = conn.Db.TwoFactorToken.Iter()
                    .Where(t => t.ExpiresAt < currentTime)
                    .ToList();

                // Delete each expired token
                foreach (var token in expiredTokens)
                {
                    conn.Reducers.DeleteTwoFactorToken(token.Id);
                }

                _logger.LogInformation("Successfully cleaned up {Count} expired 2FA tokens", expiredTokens.Count);

                return expiredTokens.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired 2FA tokens");
                return 0;
            }
        }

        /// <summary>
        /// Generates a secure random token
        /// </summary>
        private string GenerateSecureToken()
        {
            var randomBytes = new byte[32]; // 256 bits
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return Convert.ToBase64String(randomBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }
    }
}
