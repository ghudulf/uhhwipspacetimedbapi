using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.AdminServer.Configuration;
using TicketSalesApp.AdminServer.Experimental.Services.Interfaces;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.AdminServer.Experimental.Services.Implementations
{
    /// <summary>
    /// Service for managing feature flags with runtime configuration support.
    /// Implements priority: runtime overrides (database) > appsettings.json > default (false)
    /// Uses in-memory cache for hot reload (immediate effect without restart).
    /// </summary>
    public class FeatureFlagService : IFeatureFlagService
    {
        private readonly IOptionsMonitor<FeatureFlagOptions> _options;
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<FeatureFlagService> _logger;

        // In-memory cache for runtime overrides (hot reload)
        // Key: flag name, Value: enabled state
        private static readonly ConcurrentDictionary<string, bool> _runtimeOverrides = new();

        public FeatureFlagService(
            IOptionsMonitor<FeatureFlagOptions> options,
            ISpacetimeDBService spacetimeService,
            ILogger<FeatureFlagService> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Load runtime overrides from database on startup
            _ = LoadRuntimeOverridesAsync();
        }

        /// <summary>
        /// Load runtime overrides from SpacetimeDB into in-memory cache.
        /// Called on service initialization.
        /// </summary>
        private async Task LoadRuntimeOverridesAsync()
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                var overrides = conn.Db.FeatureFlagOverride.Iter().ToList();

                foreach (var @override in overrides)
                {
                    _runtimeOverrides[@override.FlagName] = @override.Enabled;
                }

                _logger.LogInformation("Loaded {Count} feature flag runtime overrides from database", overrides.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load feature flag runtime overrides from database. Using appsettings.json defaults.");
            }
        }

        public async Task<Dictionary<string, bool>> GetAllFlagsAsync()
        {
            var flags = new Dictionary<string, bool>();

            // Get all properties from FeatureFlagOptions
            var properties = typeof(FeatureFlagOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(bool));

            foreach (var property in properties)
            {
                var flagName = property.Name;
                var flagValue = await GetFlagAsync(flagName);
                flags[flagName] = flagValue;
            }

            return flags;
        }

        public async Task<bool> GetFlagAsync(string flagName)
        {
            if (string.IsNullOrWhiteSpace(flagName))
            {
                _logger.LogWarning("GetFlagAsync called with null or empty flag name");
                return false;
            }

            // Priority 1: Check runtime override (database cache)
            if (_runtimeOverrides.TryGetValue(flagName, out bool cachedValue))
            {
                _logger.LogDebug("Feature flag {FlagName} found in runtime overrides: {Enabled}", flagName, cachedValue);
                return cachedValue;
            }

            // Priority 2: Fall back to appsettings.json configuration
            var property = typeof(FeatureFlagOptions).GetProperty(flagName);
            if (property != null && property.PropertyType == typeof(bool))
            {
                var value = (bool)property.GetValue(_options.CurrentValue);
                _logger.LogDebug("Feature flag {FlagName} found in appsettings.json: {Enabled}", flagName, value);
                return value;
            }

            // Priority 3: Default to false (disabled)
            _logger.LogDebug("Feature flag {FlagName} not found, defaulting to false", flagName);
            return false;
        }

        public async Task UpdateFlagAsync(string flagName, bool enabled, Identity updatedBy)
        {
            if (string.IsNullOrWhiteSpace(flagName))
            {
                throw new ArgumentException("Flag name cannot be null or empty", nameof(flagName));
            }

            // Validate flag name exists in FeatureFlagOptions
            var property = typeof(FeatureFlagOptions).GetProperty(flagName);
            if (property == null || property.PropertyType != typeof(bool))
            {
                throw new ArgumentException($"Invalid feature flag name: {flagName}", nameof(flagName));
            }

            try
            {
                var conn = _spacetimeService.GetConnection();

                // Update in database for persistence
                conn.Reducers.UpdateFeatureFlag(flagName, enabled, updatedBy);

                // Update in-memory cache for immediate effect (hot reload)
                _runtimeOverrides[flagName] = enabled;

                _logger.LogInformation(
                    "Feature flag {FlagName} updated to {Enabled} by {UpdatedBy}",
                    flagName,
                    enabled,
                    updatedBy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update feature flag {FlagName}", flagName);
                throw;
            }
        }

        public async Task BulkUpdateFlagsAsync(Dictionary<string, bool> flags, Identity updatedBy)
        {
            if (flags == null || flags.Count == 0)
            {
                throw new ArgumentException("Flags dictionary cannot be null or empty", nameof(flags));
            }

            var successCount = 0;
            var failureCount = 0;

            foreach (var kvp in flags)
            {
                try
                {
                    await UpdateFlagAsync(kvp.Key, kvp.Value, updatedBy);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update feature flag {FlagName} during bulk update", kvp.Key);
                    failureCount++;
                }
            }

            _logger.LogInformation(
                "Bulk feature flag update completed: {SuccessCount} succeeded, {FailureCount} failed",
                successCount,
                failureCount);

            if (failureCount > 0)
            {
                throw new InvalidOperationException($"Bulk update partially failed: {failureCount} flags failed to update");
            }
        }

        public async Task ResetToDefaultsAsync(Identity actingUserId)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();

                // Clear all runtime overrides from database
                conn.Reducers.ClearFeatureFlagOverrides(actingUserId);

                // Clear in-memory cache
                _runtimeOverrides.Clear();

                _logger.LogInformation("All feature flag runtime overrides cleared by {UserId}. Using appsettings.json defaults.", actingUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset feature flags to defaults");
                throw;
            }
        }

        public async Task<List<FeatureFlagAuditLog>> GetAuditLogAsync(int limit = 100)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();

                var auditLogs = conn.Db.FeatureFlagOverride.Iter()
                    .OrderByDescending(f => f.UpdatedAt)
                    .Take(limit)
                    .Select(f => new FeatureFlagAuditLog
                    {
                        FlagName = f.FlagName,
                        Enabled = f.Enabled,
                        UpdatedBy = f.UpdatedBy,
                        UpdatedByUsername = GetUsernameForIdentity(conn, f.UpdatedBy),
                        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)f.UpdatedAt).DateTime
                    })
                    .ToList();

                return auditLogs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve feature flag audit log");
                return new List<FeatureFlagAuditLog>();
            }
        }

        /// <summary>
        /// Helper method to get username for an identity (for audit log display).
        /// </summary>
        private string GetUsernameForIdentity(DbConnection conn, Identity identity)
        {
            try
            {
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(identity));

                return user?.Login ?? identity.ToString();
            }
            catch
            {
                return identity.ToString();
            }
        }
    }
}
