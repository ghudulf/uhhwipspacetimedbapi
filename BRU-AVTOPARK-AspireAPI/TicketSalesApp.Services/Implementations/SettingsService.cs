using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    /// <summary>
    /// Implementation of the user settings service for managing authentication preferences
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<SettingsService> _logger;

        public SettingsService(
            ISpacetimeDBService spacetimeService,
            ILogger<SettingsService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves user settings or creates default settings if none exist
        /// </summary>
        public async Task<UserSettings> GetOrCreateUserSettingsAsync(Identity userId)
        {
            try
            {
                _logger.LogInformation("Getting or creating user settings for user: {UserId}", userId);

                var conn = _spacetimeService.GetConnection();

                // Try to find existing settings
                var existingSettings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(userId));

                if (existingSettings != null)
                {
                    _logger.LogInformation("Found existing user settings for user: {UserId}", userId);
                    return existingSettings;
                }

                // Create default settings if none exist
                _logger.LogInformation("Creating default user settings for user: {UserId}", userId);
                conn.Reducers.CreateUserSettings(userId);

                // Wait for the reducer to complete (with retries)
                UserSettings? newSettings = null;
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(100);
                    newSettings = conn.Db.UserSettings.Iter()
                        .FirstOrDefault(s => s.UserId.Equals(userId));
                    
                    if (newSettings != null)
                    {
                        _logger.LogInformation("Successfully created default user settings for user: {UserId}", userId);
                        return newSettings;
                    }
                }

                // If still null after retries, return default settings object
                // This allows login to proceed without 2FA
                _logger.LogWarning("Failed to create user settings in database for user: {UserId}, returning default settings", userId);
                return new UserSettings
                {
                    UserId = userId,
                    TotpEnabled = false,
                    WebAuthnEnabled = false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting or creating user settings for user: {UserId}", userId);
                
                // Return default settings to allow login to proceed
                _logger.LogWarning("Returning default settings due to error for user: {UserId}", userId);
                return new UserSettings
                {
                    UserId = userId,
                    TotpEnabled = false,
                    WebAuthnEnabled = false
                };
            }
        }

        /// <summary>
        /// Marks TOTP as enabled for the user
        /// </summary>
        public async Task<bool> EnableTotpAsync(Identity userId)
        {
            try
            {
                _logger.LogInformation("Enabling TOTP for user: {UserId}", userId);

                var conn = _spacetimeService.GetConnection();

                // Verify settings exist
                var settings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(userId));

                if (settings == null)
                {
                    _logger.LogWarning("User settings not found when enabling TOTP for user: {UserId}", userId);
                    return false;
                }

                // Call the reducer to enable TOTP
                conn.Reducers.EnableTotp(userId);

                _logger.LogInformation("Successfully enabled TOTP for user: {UserId}", userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling TOTP for user: {UserId}", userId);
                return false;
            }
        }

        /// <summary>
        /// Marks TOTP as disabled for the user
        /// </summary>
        public async Task<bool> DisableTotpAsync(Identity userId)
        {
            try
            {
                _logger.LogInformation("Disabling TOTP for user: {UserId}", userId);

                var conn = _spacetimeService.GetConnection();

                // Verify settings exist
                var settings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(userId));

                if (settings == null)
                {
                    _logger.LogWarning("User settings not found when disabling TOTP for user: {UserId}", userId);
                    return false;
                }

                // Call the reducer to disable TOTP
                conn.Reducers.DisableTotp(userId);

                _logger.LogInformation("Successfully disabled TOTP for user: {UserId}", userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling TOTP for user: {UserId}", userId);
                return false;
            }
        }

        /// <summary>
        /// Marks WebAuthn as enabled for the user
        /// </summary>
        public async Task<bool> EnableWebAuthnAsync(Identity userId)
        {
            try
            {
                _logger.LogInformation("Enabling WebAuthn for user: {UserId}", userId);

                var conn = _spacetimeService.GetConnection();

                // Verify settings exist
                var settings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(userId));

                if (settings == null)
                {
                    _logger.LogWarning("User settings not found when enabling WebAuthn for user: {UserId}", userId);
                    return false;
                }

                // Call the reducer to enable WebAuthn
                conn.Reducers.EnableWebAuthn(userId);

                _logger.LogInformation("Successfully enabled WebAuthn for user: {UserId}", userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling WebAuthn for user: {UserId}", userId);
                return false;
            }
        }

        /// <summary>
        /// Marks WebAuthn as disabled for the user
        /// </summary>
        public async Task<bool> DisableWebAuthnAsync(Identity userId)
        {
            try
            {
                _logger.LogInformation("Disabling WebAuthn for user: {UserId}", userId);

                var conn = _spacetimeService.GetConnection();

                // Verify settings exist
                var settings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(userId));

                if (settings == null)
                {
                    _logger.LogWarning("User settings not found when disabling WebAuthn for user: {UserId}", userId);
                    return false;
                }

                // Call the reducer to disable WebAuthn
                conn.Reducers.DisableWebAuthn(userId);

                _logger.LogInformation("Successfully disabled WebAuthn for user: {UserId}", userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling WebAuthn for user: {UserId}", userId);
                return false;
            }
        }

        /// <summary>
        /// Updates user settings with new values
        /// NOTE: Currently not implemented - use specific methods (EnableTotpAsync, DisableTotpAsync, etc.)
        /// TODO: Add a general UpdateUserSettings reducer in the server code if needed
        /// </summary>
        public async Task<bool> UpdateSettingsAsync(Identity userId, UserSettings settings)
        {
            try
            {
                _logger.LogInformation("Updating user settings for user: {UserId}", userId);

                var conn = _spacetimeService.GetConnection();

                // Verify settings exist
                var existingSettings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(userId));

                if (existingSettings == null)
                {
                    _logger.LogWarning("User settings not found when updating for user: {UserId}", userId);
                    return false;
                }

                // TODO: Implement general settings update when reducer is available
                // For now, use specific methods: EnableTotpAsync, DisableTotpAsync, EnableWebAuthnAsync, DisableWebAuthnAsync
                _logger.LogWarning("UpdateSettingsAsync is not yet implemented. Use specific enable/disable methods instead.");
                return await Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user settings for user: {UserId}", userId);
                return false;
            }
        }
    }
}
