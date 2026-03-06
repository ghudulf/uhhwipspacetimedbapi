using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;

namespace TicketSalesApp.AdminServer.Experimental.Services.Interfaces
{
    /// <summary>
    /// Service for managing feature flags with runtime configuration support.
    /// Supports both file-based (appsettings.json) and runtime (database) configuration.
    /// </summary>
    public interface IFeatureFlagService
    {
        /// <summary>
        /// Get all feature flags and their current state.
        /// Priority: runtime overrides (database) > appsettings.json > default (false)
        /// </summary>
        /// <returns>Dictionary of flag names and their enabled state</returns>
        Task<Dictionary<string, bool>> GetAllFlagsAsync();

        /// <summary>
        /// Get a specific feature flag value.
        /// Priority: runtime overrides (database) > appsettings.json > default (false)
        /// </summary>
        /// <param name="flagName">Name of the feature flag</param>
        /// <returns>True if enabled, false if disabled</returns>
        Task<bool> GetFlagAsync(string flagName);

        /// <summary>
        /// Update a specific feature flag at runtime.
        /// Changes take effect immediately (hot reload) without application restart.
        /// Persists to SpacetimeDB for durability across restarts.
        /// </summary>
        /// <param name="flagName">Name of the feature flag</param>
        /// <param name="enabled">New enabled state</param>
        /// <param name="updatedBy">Identity of the user making the change (for audit logging)</param>
        Task UpdateFlagAsync(string flagName, bool enabled, Identity updatedBy);

        /// <summary>
        /// Update multiple feature flags at once.
        /// Changes take effect immediately (hot reload) without application restart.
        /// </summary>
        /// <param name="flags">Dictionary of flag names and their new enabled state</param>
        /// <param name="updatedBy">Identity of the user making the changes (for audit logging)</param>
        Task BulkUpdateFlagsAsync(Dictionary<string, bool> flags, Identity updatedBy);

        /// <summary>
        /// Reset all runtime overrides to appsettings.json defaults.
        /// Clears all database-stored overrides.
        /// </summary>
        /// <param name="actingUserId">Identity of the user making the change (for audit logging)</param>
        Task ResetToDefaultsAsync(Identity actingUserId);

        /// <summary>
        /// Get audit log of feature flag changes.
        /// </summary>
        /// <param name="limit">Maximum number of log entries to return</param>
        /// <returns>List of audit log entries</returns>
        Task<List<FeatureFlagAuditLog>> GetAuditLogAsync(int limit = 100);
    }

    /// <summary>
    /// Audit log entry for feature flag changes.
    /// </summary>
    public class FeatureFlagAuditLog
    {
        public string FlagName { get; set; }
        public bool Enabled { get; set; }
        public Identity UpdatedBy { get; set; }
        public string UpdatedByUsername { get; set; }
        public System.DateTime UpdatedAt { get; set; }
    }
}
