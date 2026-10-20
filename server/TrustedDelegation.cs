using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Table]
    public partial class DeploymentAuthority
    {
        [PrimaryKey] public uint Id;
        public Identity Owner;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class TrustedApiIdentity
    {
        [PrimaryKey] public Identity Identity;
        public ulong ProvisionedAt;
        // Constant partition enables indexed RLS subscriptions in SDK 2.1.0.
        [SpacetimeDB.Default(0u), SpacetimeDB.Index.BTree]
        public uint ApiAccessPartition;
    }

    // Existing databases require an owner-published migration build providing this method.
    // Never infer deployment authority from a role, a login, or the first remote caller.
    static partial void ConfigureLegacyDeploymentOwner(ref string? owner);

    private static void RequireDeploymentOwner(ReducerContext ctx)
    {
        var authority = ctx.Db.DeploymentAuthority.Id.Find(1);
        if (authority == null)
        {
            string? owner = null;
            ConfigureLegacyDeploymentOwner(ref owner);
            if (string.IsNullOrWhiteSpace(owner) || !string.Equals(ctx.Sender.ToString(), owner, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Deployment owner migration is required.");
            authority = ctx.Db.DeploymentAuthority.Insert(new DeploymentAuthority { Id = 1, Owner = ctx.Sender });
        }
        if (ctx.Sender != authority.Owner) throw new Exception("Deployment owner required.");
    }

    [SpacetimeDB.Reducer]
    public static void ProvisionTrustedApi(ReducerContext ctx, Identity identity)
    {
        RequireDeploymentOwner(ctx);
        if (ctx.Db.TrustedApiIdentity.Identity.Find(identity) == null)
            ctx.Db.TrustedApiIdentity.Insert(new TrustedApiIdentity
            { Identity = identity, ProvisionedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 });
        Log.Info($"API identity provisioned by deployment owner: {identity}");
    }

    [SpacetimeDB.Reducer]
    public static void RevokeTrustedApi(ReducerContext ctx, Identity identity)
    {
        RequireDeploymentOwner(ctx);
        ctx.Db.TrustedApiIdentity.Identity.Delete(identity);
        Log.Info($"API identity revoked by deployment owner: {identity}");
    }

    private static bool IsTrustedApi(ReducerContext ctx) => ctx.Db.TrustedApiIdentity.Identity.Find(ctx.Sender) != null;
    private static void RequireTrustedApi(ReducerContext ctx)
    {
        if (!IsTrustedApi(ctx)) throw new Exception("Provisioned API identity required.");
    }
    private static Identity ResolveEffectiveUser(ReducerContext ctx, Identity? actingUser)
    {
        var effective = actingUser ?? ctx.Sender;
        if (effective != ctx.Sender && !IsTrustedApi(ctx)) throw new Exception("Untrusted delegated identity.");
        Log.Debug($"Authorization sender={ctx.Sender}, effectiveUser={effective}");
        return effective;
    }
}
