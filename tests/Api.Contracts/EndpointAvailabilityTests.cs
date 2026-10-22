using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpacetimeDB.Types;
using TicketSalesApp.AdminServer.Configuration;
using TicketSalesApp.AdminServer.Experimental.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class EndpointAvailabilityTests
{
    public static async Task RunAsync()
    {
        var options = new FeatureFlagOptions { EnableLoginRefactoring = false };
        var overrides = new Dictionary<string, bool>();
        bool Resolve() => EndpointAvailability.Resolve(options, nameof(FeatureFlagOptions.LoginEndpointEnabled),
            key => overrides.TryGetValue(key, out var value) ? value : null);
        Assert(Resolve(), "legacy false selection disabled endpoint");
        overrides[nameof(FeatureFlagOptions.EnableLoginRefactoring)] = false;
        Assert(Resolve(), "legacy persisted selection disabled endpoint without opt-in");
        options.LegacyAvailabilityCompatibility = true;
        Assert(!Resolve(), "compatibility mode lost persisted kill switch");
        options.LoginEndpointEnabled = true;
        Assert(Resolve(), "explicit new configuration did not supersede compatibility fallback");
        overrides[nameof(FeatureFlagOptions.LoginEndpointEnabled)] = false;
        Assert(!Resolve(), "committed canonical disable did not supersede configuration");
        overrides[nameof(FeatureFlagOptions.LoginEndpointEnabled)] = true;
        options.LoginEndpointEnabled = false;
        Assert(Resolve(), "committed canonical enable did not supersede configuration");
        Assert(!EndpointAvailability.Resolve(options, "UnregisteredEndpointEnabled", _ => true), "unknown availability exposed");
        Assert(EndpointAvailability.CanonicalName(nameof(FeatureFlagOptions.EnableLoginRefactoring)) == nameof(FeatureFlagOptions.LoginEndpointEnabled), "legacy action mapping missing");

        // Exercise production service selection against an empty SDK cache, without network or synthetic overrides.
        var connection = new DbConnection();
        try
        {
        var database = Stub.Create<ISpacetimeDBService>((method, args) =>
        {
            if (method.Name == "ReadAsync" && method.GetGenericArguments()[0] == typeof(bool))
                return Task.FromResult(((Func<DbConnection, bool>)args![0]!)(connection));
            if (method.Name == "ReadAsync" && method.GetGenericArguments()[0] == typeof(Dictionary<string, bool>))
                return Task.FromResult(((Func<DbConnection, Dictionary<string, bool>>)args![0]!)(connection));
            throw new Exception("Unexpected flag service operation " + method.Name);
        });
        options = new FeatureFlagOptions { EnableLoginRefactoring = false };
        var service = new FeatureFlagService(new Monitor(options), database, NullLogger<FeatureFlagService>.Instance);
        Assert(await service.GetFlagAsync(nameof(FeatureFlagOptions.LoginEndpointEnabled)), "service interpreted legacy selector as availability");
        options.LoginEndpointEnabled = false;
        Assert(!await service.GetFlagAsync(nameof(FeatureFlagOptions.LoginEndpointEnabled)), "service lost explicit availability disable");
        var flags = await service.GetAllFlagsAsync();
        Assert(flags.ContainsKey(nameof(FeatureFlagOptions.EnableLoginRefactoring)) && flags.ContainsKey(nameof(FeatureFlagOptions.LoginEndpointEnabled)), "HTTP flag inventory removed compatibility keys");
        Assert(!flags.ContainsKey(nameof(FeatureFlagOptions.LegacyAvailabilityCompatibility)), "runtime flags exposed migration configuration switch");
        Console.WriteLine("PASS explicit endpoint availability, legacy selection preservation, opt-in kill-switch compatibility and canonical configuration/override precedence");
        }
        finally { connection.Disconnect(); }
    }
    private sealed class Monitor(FeatureFlagOptions value) : IOptionsMonitor<FeatureFlagOptions>
    {
        public FeatureFlagOptions CurrentValue => value;
        public FeatureFlagOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<FeatureFlagOptions, string?> listener) => null;
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
