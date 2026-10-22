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

                // Try to find existing settings
                var existingSettings = await _spacetimeService.ReadAsync(c => c.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId == userId));

                if (existingSettings != null)
                {
                    _logger.LogInformation("Found existing user settings for user: {UserId}", userId);
                    return existingSettings;
                }

                // Create default settings if none exist
                _logger.LogInformation("Creating default user settings for user: {UserId}", userId);
                return await _spacetimeService.ExecuteReducerAsync(new Reducer.CreateUserSettings(userId),
                    c => c.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId == userId) ?? throw new InvalidOperationException("Committed settings missing."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting or creating user settings for user: {UserId}", userId);
                
                // Settings failure must not silently disable required second factors.
                throw;
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

                // Verify settings exist
                var settings = await _spacetimeService.ReadAsync(c => c.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId == userId));

                if (settings == null)
                {
                    _logger.LogWarning("User settings not found when enabling TOTP for user: {UserId}", userId);
                    return false;
                }

                // Call the reducer to enable TOTP
                await _spacetimeService.ExecuteReducerAsync(new Reducer.EnableTotp(userId), _ => true);

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

                // Verify settings exist
                var settings = await _spacetimeService.ReadAsync(c => c.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId == userId));

                if (settings == null)
                {
                    _logger.LogWarning("User settings not found when disabling TOTP for user: {UserId}", userId);
                    return false;
                }

                // Call the reducer to disable TOTP
                await _spacetimeService.ExecuteReducerAsync(new Reducer.DisableTotp(userId), _ => true);

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

                // Verify settings exist
                var settings = await _spacetimeService.ReadAsync(c => c.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId == userId));

                if (settings == null)
                {
                    _logger.LogWarning("User settings not found when enabling WebAuthn for user: {UserId}", userId);
                    return false;
                }

                // Call the reducer to enable WebAuthn
                await _spacetimeService.ExecuteReducerAsync(new Reducer.EnableWebAuthn(userId), _ => true);

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

                // Verify settings exist
                var settings = await _spacetimeService.ReadAsync(c => c.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId == userId));

                if (settings == null)
                {
                    _logger.LogWarning("User settings not found when disabling WebAuthn for user: {UserId}", userId);
                    return false;
                }

                // Call the reducer to disable WebAuthn
                await _spacetimeService.ExecuteReducerAsync(new Reducer.DisableWebAuthn(userId), _ => true);

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

                // Verify settings exist
                var existingSettings = await _spacetimeService.ReadAsync(c => c.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId == userId));

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
