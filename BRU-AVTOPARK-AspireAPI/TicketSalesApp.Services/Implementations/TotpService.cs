    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Threading.Tasks;
    using TicketSalesApp.Services.Interfaces;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using SpacetimeDB;
    using SpacetimeDB.Types;

    namespace TicketSalesApp.Services.Implementations
    {
        /// <summary>
        /// Implementation of the TOTP service
        /// </summary>
        public class TotpService : ITotpService
        {
            private readonly ISpacetimeDBService _spacetimeService;
            private readonly IConfiguration _configuration;
            private readonly UrlEncoder _urlEncoder;
            private readonly ILogger<TotpService> _logger;

            public TotpService(
                ISpacetimeDBService spacetimeService,
                IConfiguration configuration,
                UrlEncoder urlEncoder,
                ILogger<TotpService> logger)
            {
                _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
                _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
                _urlEncoder = urlEncoder ?? throw new ArgumentNullException(nameof(urlEncoder));
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            /// <summary>
            /// Sets up TOTP for a user
            /// </summary>
            public async Task<(bool success, string? secretKey, string? qrCodeUri, string? errorMessage)> SetupTotpAsync(Identity userId, string username)
            {
                try
                {
                    _logger.LogInformation("Setting up TOTP for user: {Username}", username);

                    var conn = _spacetimeService.GetConnection();
                    
                    // Check if user exists
                    var user = conn.Db.UserProfile.Iter()
                        .FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);
                    
                    if (user == null)
                    {
                        _logger.LogWarning("User not found for TOTP setup: {UserId}", userId);
                        return (false, null, null, "User not found");
                    }

                    // Generate a new secret key
                    var secretKey = await GenerateTotpSecretKeyAsync();
                    
                    // Generate QR code URI
                    var qrCodeUri = GenerateTotpQrCodeUri(username, secretKey);

                    return (true, secretKey, qrCodeUri, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error setting up TOTP for user: {Username}", username);
                    return (false, null, null, "An error occurred while setting up TOTP");
                }
            }

            /// <summary>
            /// Enables TOTP for a user
            /// </summary>
            public async Task<(bool success, string? errorMessage)> EnableTotpAsync(Identity userId, string verificationCode, string secretKey)
            {
                try
                {
                    _logger.LogInformation("Enabling TOTP for user ID: {UserId}", userId);

                    // Verify the code
                    if (!VerifyTotpCode(secretKey, verificationCode))
                    {
                        _logger.LogWarning("Invalid TOTP code for user ID: {UserId}", userId);
                        return (false, "Invalid verification code");
                    }

                    var conn = _spacetimeService.GetConnection();
                    
                    // Check if user exists
                    var user = conn.Db.UserProfile.Iter()
                        .FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);
                    
                    if (user == null)
                    {
                        _logger.LogWarning("User not found for TOTP enable: {UserId}", userId);
                        return (false, "User not found");
                    }

                    // Store the secret key
                    conn.Reducers.StoreTotpSecret(userId, secretKey);
                    
                    // Enable TOTP for the user
                    conn.Reducers.EnableTotp(userId);

                    _logger.LogInformation("TOTP enabled successfully for user ID: {UserId}", userId);
                    return (true, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enabling TOTP for user ID: {UserId}", userId);
                    return (false, "An error occurred while enabling TOTP");
                }
            }

            /// <summary>
            /// Disables TOTP for a user
            /// </summary>
            public async Task<(bool success, string? errorMessage)> DisableTotpAsync(Identity userId)
            {
                try
                {
                    _logger.LogInformation("Disabling TOTP for user ID: {UserId}", userId);

                    var conn = _spacetimeService.GetConnection();
                    
                    // Check if user exists
                    var user = conn.Db.UserProfile.Iter()
                        .FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);
                    
                    if (user == null)
                    {
                        _logger.LogWarning("User not found for TOTP disable: {UserId}", userId);
                        return (false, "User not found");
                    }

                    // Disable TOTP for the user
                    conn.Reducers.DisableTotp(userId);
                    
                    // Deactivate the TOTP secret
                    conn.Reducers.DeactivateTotpSecret(userId);

                    _logger.LogInformation("TOTP disabled successfully for user ID: {UserId}", userId);
                    return (true, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disabling TOTP for user ID: {UserId}", userId);
                    return (false, "An error occurred while disabling TOTP");
                }
            }

            /// <summary>
            /// Validates a TOTP code for a user
            /// </summary>
            public async Task<(bool success, string? errorMessage)> ValidateTotpAsync(Identity userId, string code)
            {
                try
                {
                    _logger.LogInformation("Validating TOTP code for user ID: {UserId}", userId);

                    var conn = _spacetimeService.GetConnection();
                    
                    // Check if user exists
                    var user = conn.Db.UserProfile.Iter()
                        .FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);
                    
                    if (user == null)
                    {
                        _logger.LogWarning("User not found for TOTP validation: {UserId}", userId);
                        return (false, "User not found");
                    }

                    // Get the TOTP secret
                    var totpSecret = conn.Db.TotpSecret.Iter()
                        .FirstOrDefault(s => s.UserId.Equals(userId) && s.IsActive);
                    
                    if (totpSecret == null)
                    {
                        _logger.LogWarning("TOTP not set up for user ID: {UserId}", userId);
                        return (false, "TOTP not set up for this user");
                    }

                    // Verify the code
                    if (!VerifyTotpCode(totpSecret.Secret, code))
                    {
                        _logger.LogWarning("Invalid TOTP code for user ID: {UserId}", userId);
                        return (false, "Invalid verification code");
                    }

                    _logger.LogInformation("TOTP code validated successfully for user ID: {UserId}", userId);
                    return (true, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error validating TOTP code for user ID: {UserId}", userId);
                    return (false, "An error occurred while validating TOTP code");
                }
            }

            /// <summary>
            /// Validates a TOTP code with a temporary token
            /// </summary>
            public async Task<(bool success, string? errorMessage)> ValidateTotpWithTokenAsync(string tempToken, string code)
            {
                try
                {
                    _logger.LogInformation("Validating TOTP code with token: {Token}", tempToken);

                    var conn = _spacetimeService.GetConnection();
                    
                    // Find the token
                    var twoFactorToken = conn.Db.TwoFactorToken.Iter()
                        .FirstOrDefault(t => t.Token == tempToken && t.ExpiresAt > (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    
                    if (twoFactorToken == null)
                    {
                        _logger.LogWarning("Invalid or expired two-factor token: {Token}", tempToken);
                        return (false, "Invalid or expired token");
                    }

                    // Get the user
                    var user = conn.Db.UserProfile.Iter()
                        .FirstOrDefault(u => u.UserId.Equals(twoFactorToken.UserId));
                    
                    if (user == null)
                    {
                        _logger.LogWarning("User not found for two-factor token: {Token}", tempToken);
                        return (false, "User not found");
                    }

                    // Get the TOTP secret
                    var totpSecret = conn.Db.TotpSecret.Iter()
                        .FirstOrDefault(s => s.UserId.Equals(user.UserId) && s.IsActive);
                    
                    if (totpSecret == null)
                    {
                        _logger.LogWarning("TOTP not set up for user ID: {UserId}", user.UserId);
                        return (false, "TOTP not set up for this user");
                    }

                    // Verify the code
                    if (!VerifyTotpCode(totpSecret.Secret, code))
                    {
                        _logger.LogWarning("Invalid TOTP code for user ID: {UserId}", user.UserId);
                        return (false, "Invalid verification code");
                    }

                    // Delete the token
                    conn.Reducers.DeleteTwoFactorToken(twoFactorToken.Id);

                    _logger.LogInformation("TOTP code validated successfully with token for user ID: {UserId}", user.UserId);
                    return (true, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error validating TOTP code with token: {Token}", tempToken);
                    return (false, "An error occurred while validating TOTP code");
                }
            }

            /// <summary>
            /// Checks if TOTP is enabled for a user
            /// </summary>
            public async Task<bool> IsTotpEnabledAsync(Identity userId)
            {
                try
                {
                    var conn = _spacetimeService.GetConnection();
                    
                    // Get user settings
                    var userSettings = conn.Db.UserSettings.Iter()
                        .FirstOrDefault(s => s.UserId.Equals(userId));
                    
                    return userSettings?.TotpEnabled ?? false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking if TOTP is enabled for user ID: {UserId}", userId);
                    return false;
                }
            }

            /// <summary>
            /// Generates a TOTP secret key
            /// </summary>
            public async Task<string> GenerateTotpSecretKeyAsync()
            {
                var randomBytes = new byte[20]; // 160 bits
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(randomBytes);
                }
                return Base32Encode(randomBytes);
            }

            /// <summary>
            /// Generates a TOTP QR code URI
            /// </summary>
            public string GenerateTotpQrCodeUri(string username, string secretKey)
            {
                var appName = _configuration["AppName"] ?? "BRU-AVTOPARK";
                var encodedAppName = _urlEncoder.Encode(appName);
                var encodedUsername = _urlEncoder.Encode(username);
            
                return $"otpauth://totp/{encodedAppName}:{encodedUsername}?secret={secretKey}&issuer={encodedAppName}";
            }

            /// <summary>
            /// Verifies a TOTP code
            /// </summary>
            public bool VerifyTotpCode(string secretKey, string code)
            {
                try
                {
                    var timeStep = 30; // 30-second time step
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var counter = timestamp / timeStep;

                    // Try current and previous time steps
                    for (var i = -1; i <= 1; i++)
                    {
                        var hash = new HMACSHA1(Base32Decode(secretKey));
                        var counterBytes = BitConverter.GetBytes(counter + i).Reverse().ToArray();
                        var hashBytes = hash.ComputeHash(counterBytes);
                        var offset = hashBytes[hashBytes.Length - 1] & 0xf;
                        var truncatedHash = ((hashBytes[offset] & 0x7f) << 24) |
                                        ((hashBytes[offset + 1] & 0xff) << 16) |
                                        ((hashBytes[offset + 2] & 0xff) << 8) |
                                        (hashBytes[offset + 3] & 0xff);
                        var totpCode = (truncatedHash % 1000000).ToString("D6");

                        if (totpCode == code)
                        {
                            return true;
                        }
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error verifying TOTP code");
                    return false;
                }
            }

            /// <summary>
            /// Base32 encodes a byte array
            /// </summary>
            private string Base32Encode(byte[] data)
            {
                const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
                var result = new StringBuilder();
                var bits = 0;
                var buffer = 0;

                foreach (var b in data)
                {
                    buffer = (buffer << 8) | b;
                    bits += 8;

                    while (bits >= 5)
                    {
                        bits -= 5;
                        result.Append(alphabet[(buffer >> bits) & 31]);
                    }
                }

                if (bits > 0)
                {
                    buffer <<= (5 - bits);
                    result.Append(alphabet[buffer & 31]);
                }

                return result.ToString();
            }

            /// <summary>
            /// Base32 decodes a string
            /// </summary>
            private byte[] Base32Decode(string input)
            {
                const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
                var bits = 0;
                var value = 0;
                var output = new List<byte>();

                foreach (var c in input.ToUpper())
                {
                    value = (value << 5) | alphabet.IndexOf(c);
                    bits += 5;

                    if (bits >= 8)
                    {
                        output.Add((byte)(value >> (bits - 8)));
                        bits -= 8;
                    }
                }

                return output.ToArray();
            }
        }
    }


