using System;
using System.Linq;
using SpacetimeDB;

public static partial class Module
{
    /// <summary>
    /// Updates a feature flag at runtime.
    /// Creates a new override if it doesn't exist, or updates the existing one.
    /// Requires "feature_flags.update" permission.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="flagName">The name of the feature flag to update.</param>
    /// <param name="enabled">The new enabled state for the flag.</param>
    /// <param name="actingUserId">The identity of the user making the change.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to update feature flags.</exception>
    [SpacetimeDB.Reducer]
    public static void UpdateFeatureFlag(ReducerContext ctx, string flagName, bool enabled, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity, not the actual user
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        // Check if the caller has the necessary permission
        if (!HasPermission(ctx, effectiveUser, "feature_flags.update"))
        {
            throw new Exception("Unauthorized: You do not have permission to update feature flags.");
        }

        // Validate flag name
        if (string.IsNullOrWhiteSpace(flagName))
        {
            throw new Exception("Flag name cannot be null or empty.");
        }

        var timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;

        // Check if override already exists
        var existingOverride = ctx.Db.FeatureFlagOverride.FlagName.Find(flagName);

        if (existingOverride != null)
        {
            // Update existing override
            existingOverride.Enabled = enabled;
            existingOverride.UpdatedBy = effectiveUser;
            existingOverride.UpdatedAt = timestamp;
            ctx.Db.FeatureFlagOverride.FlagName.Update(existingOverride);

            Log.Info($"Feature flag '{flagName}' updated to {enabled} by {effectiveUser}");
        }
        else
        {
            // Create new override
            var newOverride = new FeatureFlagOverride
            {
                FlagName = flagName,
                Enabled = enabled,
                UpdatedBy = effectiveUser,
                UpdatedAt = timestamp
            };
            ctx.Db.FeatureFlagOverride.Insert(newOverride);

            Log.Info($"Feature flag '{flagName}' created with value {enabled} by {effectiveUser}");
        }
    }

    /// <summary>
    /// Clears all feature flag runtime overrides.
    /// After calling this, all flags will fall back to appsettings.json defaults.
    /// Requires "feature_flags.clear" permission.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="actingUserId">The identity of the user making the change.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to clear feature flags.</exception>
    [SpacetimeDB.Reducer]
    public static void ClearFeatureFlagOverrides(ReducerContext ctx, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity, not the actual user
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        // Check if the caller has the necessary permission
        if (!HasPermission(ctx, effectiveUser, "feature_flags.clear"))
        {
            throw new Exception("Unauthorized: You do not have permission to clear feature flag overrides.");
        }

        // Delete all overrides
        var overrides = ctx.Db.FeatureFlagOverride.Iter().ToList();
        foreach (var @override in overrides)
        {
            ctx.Db.FeatureFlagOverride.FlagName.Delete(@override.FlagName);
        }

        Log.Info($"All feature flag overrides cleared by {effectiveUser}. Count: {overrides.Count}");
    }

    /// <summary>
    /// Deletes a specific feature flag override.
    /// After calling this, the flag will fall back to appsettings.json default.
    /// Requires "feature_flags.delete" permission.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="flagName">The name of the feature flag override to delete.</param>
    /// <param name="actingUserId">The identity of the user making the change.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to delete feature flags.</exception>
    [SpacetimeDB.Reducer]
    public static void DeleteFeatureFlagOverride(ReducerContext ctx, string flagName, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity, not the actual user
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        // Check if the caller has the necessary permission
        if (!HasPermission(ctx, effectiveUser, "feature_flags.delete"))
        {
            throw new Exception("Unauthorized: You do not have permission to delete feature flag overrides.");
        }

        // Validate flag name
        if (string.IsNullOrWhiteSpace(flagName))
        {
            throw new Exception("Flag name cannot be null or empty.");
        }

        // Check if override exists
        var existingOverride = ctx.Db.FeatureFlagOverride.FlagName.Find(flagName);
        if (existingOverride == null)
        {
            throw new Exception($"Feature flag override '{flagName}' not found.");
        }

        // Delete the override
        ctx.Db.FeatureFlagOverride.FlagName.Delete(flagName);

        Log.Info($"Feature flag override '{flagName}' deleted by {effectiveUser}");
    }
}
