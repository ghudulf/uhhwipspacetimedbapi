using System.Reflection;
using Microsoft.Extensions.Options;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.AdminServer.Configuration;
using TicketSalesApp.AdminServer.Experimental.Services.Interfaces;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.AdminServer.Experimental.Services.Implementations;

/// <summary>Committed database overrides take precedence over current configuration. No process-only override cache.</summary>
public sealed class FeatureFlagService(IOptionsMonitor<FeatureFlagOptions> options, ISpacetimeDBService database,
    ILogger<FeatureFlagService> logger) : IFeatureFlagService
{
    // Only declared boolean configuration properties are eligible; no arbitrary property/service invocation.
    private static readonly IReadOnlyDictionary<string, PropertyInfo> Flags = typeof(FeatureFlagOptions)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType == typeof(bool))
        .ToDictionary(p => p.Name, StringComparer.Ordinal);

    public Task<Dictionary<string, bool>> GetAllFlagsAsync() => database.ReadAsync(c =>
    {
        var configuration = options.CurrentValue;
        var result = Flags.ToDictionary(p => p.Key, p => (bool)p.Value.GetValue(configuration)!, StringComparer.Ordinal);
        foreach (var row in c.Db.FeatureFlagOverride.Iter()) if (result.ContainsKey(row.FlagName)) result[row.FlagName] = row.Enabled;
        return result;
    });

    public async Task<bool> GetFlagAsync(string flagName)
    {
        if (string.IsNullOrEmpty(flagName) || !Flags.TryGetValue(flagName, out var property)) return false;
        var configured = (bool)property.GetValue(options.CurrentValue)!;
        var result = await database.ReadAsync(c => c.Db.FeatureFlagOverride.FlagName.Find(flagName)?.Enabled ?? configured);
        logger.LogDebug("Endpoint flag {FlagName}: {Enabled}", flagName, result);
        return result;
    }

    public async Task UpdateFlagAsync(string flagName, bool enabled, Identity updatedBy)
    {
        Validate(flagName);
        await database.ExecuteReducerAsync(new Reducer.UpdateFeatureFlag(flagName, enabled, updatedBy), _ => true);
        logger.LogInformation("Endpoint flag {FlagName} committed: {Enabled}", flagName, enabled);
    }

    public async Task BulkUpdateFlagsAsync(Dictionary<string, bool> flags, Identity updatedBy)
    {
        if (flags == null || flags.Count == 0 || flags.Count > Flags.Count) throw new ArgumentException("Invalid flag count.");
        foreach (var name in flags.Keys) Validate(name);
        // Existing reducer contract is per-flag: stop and report failure, never report an uncommitted override as active.
        foreach (var flag in flags.OrderBy(p => p.Key, StringComparer.Ordinal)) await UpdateFlagAsync(flag.Key, flag.Value, updatedBy);
    }

    public async Task ResetToDefaultsAsync(Identity actingUserId)
    {
        await database.ExecuteReducerAsync(new Reducer.ClearFeatureFlagOverrides(actingUserId), _ => true);
        logger.LogInformation("Endpoint flag overrides cleared after commit");
    }

    public Task<List<FeatureFlagAuditLog>> GetAuditLogAsync(int limit = 100)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        // Preserves the existing endpoint's latest-change projection; durable historical audit events are separate.
        return database.ReadAsync(c => c.Db.FeatureFlagOverride.Iter().OrderByDescending(f => f.UpdatedAt).ThenBy(f => f.FlagName)
            .Take(limit).Select(f => new FeatureFlagAuditLog
            {
                FlagName = f.FlagName, Enabled = f.Enabled, UpdatedBy = f.UpdatedBy,
                UpdatedByUsername = c.Db.UserProfile.UserId.Find(f.UpdatedBy)?.Login ?? f.UpdatedBy.ToString(),
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(checked((long)f.UpdatedAt)).UtcDateTime
            }).ToList());
    }
    private static void Validate(string flagName)
    {
        if (string.IsNullOrEmpty(flagName) || !Flags.ContainsKey(flagName)) throw new ArgumentException("Unknown endpoint flag.", nameof(flagName));
    }
}
