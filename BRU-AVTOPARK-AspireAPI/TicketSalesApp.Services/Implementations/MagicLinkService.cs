using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Implementations
{
    /// <summary>
    /// Implementation of the Magic Link service
    /// </summary>
    public class MagicLinkService : IMagicLinkService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MagicLinkService> _logger;
        private const int MAGIC_LINK_EXPIRY_MINUTES = 15;

        public MagicLinkService(
            ISpacetimeDBService spacetimeService,
            IEmailService emailService,
            IConfiguration configuration,
            ILogger<MagicLinkService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Sends a magic link to a user's email
        /// </summary>
        public async Task<(bool success, string? errorMessage)> SendMagicLinkAsync(string email, string? userAgent, string? ipAddress)
        {
            try
            {
                _logger.LogInformation("Sending magic link to email: {Email}", email);

                var conn = _spacetimeService.GetConnection();
                
                // Find user by email
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Email == email && u.IsActive);
                
                if (user == null)
                {
                    // Don't reveal that the user doesn't exist
                    _logger.LogWarning("User not found for magic link: {Email}", email);
                    return (true, null);
                }

                if (!user.EmailConfirmed)
                {
                    // Don't reveal that the email is not confirmed
                    _logger.LogWarning("Email not confirmed for magic link: {Email}", email);
                    return (true, null);
                }

                // Generate a unique token
                var token = GenerateRandomToken();
                
                // Store the token in SpacetimeDB
                var expiryTime = DateTime.UtcNow.AddMinutes(MAGIC_LINK_EXPIRY_MINUTES);
                await conn.Reducers.CreateMagicLinkTokenAsync(
                    user.UserId,
                    token,
                    (ulong)new DateTimeOffset(expiryTime).ToUnixTimeMilliseconds(),
                    userAgent ?? "Unknown",
                    ipAddress ?? "Unknown"
                );
                
                // Generate magic link URL
                var appUrl = _configuration["AppUrl"];
                var magicLinkUrl = $"{appUrl}/api/auth/validate-magic-link?token={token}";
                
                // Send the magic link email
                await _emailService.SendEmailAsync(
                    user.Email,
                    "Your Magic Link for Login",
                    $"Click the link below to log in:<br><a href='{magicLinkUrl}'>{magicLinkUrl}</a><br>This link will expire in {MAGIC_LINK_EXPIRY_MINUTES} minutes."
                );
                
                _logger.LogInformation("Magic link sent to: {Email}", email);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending magic link to email: {Email}", email);
                return (false, "An error occurred while sending the magic link");
            }
        }

        /// <summary>
        /// Validates a magic link token
        /// </summary>
        public async Task<(bool success, UserProfile? user, string? errorMessage)> ValidateMagicLinkAsync(string token)
        {
            try
            {
                _logger.LogInformation("Validating magic link token: {Token}", token);

                if (string.IsNullOrEmpty(token))
                {
                    return (false, null, "Token is required");
                }
                
                var conn = _spacetimeService.GetConnection();
                
                // Find the token
                var magicLinkToken = conn.Db.MagicLinkToken.Iter()
                    .FirstOrDefault(t => t.Token == token && 
                                        t.ExpiresAt > (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() && 
                                        !t.IsUsed);
                
                if (magicLinkToken == null)
                {
                    _logger.LogWarning("Invalid or expired magic link token: {Token}", token);
                    return (false, null, "Invalid or expired token");
                }
                
                // Get the user
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(magicLinkToken.UserId));
                
                if (user == null)
                {
                    _logger.LogWarning("User not found for magic link token: {Token}", token);
                    return (false, null, "User not found");
                }
                
                if (!user.IsActive)
                {
                    _logger.LogWarning("Account is disabled for magic link token: {Token}", token);
                    return (false, null, "Account is disabled");
                }

                _logger.LogInformation("Magic link token validated successfully for user: {Login}", user.Login);
                return (true, user, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating magic link token: {Token}", token);
                return (false, null, "An error occurred while validating the magic link");
            }
        }

        /// <summary>
        /// Marks a magic link token as used
        /// </summary>
        public async Task<bool> MarkMagicLinkAsUsedAsync(string token)
        {
            try
            {
                _logger.LogInformation("Marking magic link token as used: {Token}", token);

                var conn = _spacetimeService.GetConnection();
                await conn.Reducers.UseMagicLinkTokenAsync(token);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking magic link token as used: {Token}", token);
                return false;
            }
        }

        /// <summary>
        /// Generates a random token
        /// </summary>
        private string GenerateRandomToken()
        {
            var randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return Convert.ToBase64String(randomBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }
    }
}

