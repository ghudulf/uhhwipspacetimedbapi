#pragma warning disable STDB_UNSTABLE
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Table(Public = true)]
    public partial class ExternalAccountLink
    {
        [PrimaryKey] public string Key = "";
        public string Provider = "";
        public string Issuer = "";
        public string Subject = "";
        public Identity UserId;
        public ulong LinkedAt;
        [SpacetimeDB.Default(0u), SpacetimeDB.Index.BTree] public uint ApiAccessPartition;
    }

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter ExternalAccountLinkAccess = new Filter.Sql(
        "SELECT ExternalAccountLink.* FROM ExternalAccountLink JOIN TrustedApiIdentity ON ExternalAccountLink.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    private static string ExternalLinkKey(string provider, string issuer, string subject)
    {
        foreach (var part in new[] { provider, issuer, subject })
            if (string.IsNullOrWhiteSpace(part) || part.Length > 512 || part.Contains('\n') || part.Contains('\r'))
                throw new Exception("Invalid external account identifier.");
        return provider + "\n" + issuer + "\n" + subject;
    }

    [SpacetimeDB.Reducer]
    public static void LinkExternalAccount(ReducerContext ctx, string provider, string issuer, string subject, Identity userId)
    {
        RequireDeploymentOwner(ctx);
        var key = ExternalLinkKey(provider, issuer, subject);
        if (ctx.Db.UserProfile.UserId.Find(userId)?.IsActive != true) throw new Exception("Active account required.");
        if (ctx.Db.ExternalAccountLink.Key.Find(key) != null) throw new Exception("External account already linked. Revoke explicitly before reassignment.");
        ctx.Db.ExternalAccountLink.Insert(new ExternalAccountLink { Key = key, Provider = provider, Issuer = issuer, Subject = subject,
            UserId = userId, LinkedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 });
        Log.Info("External account provisioned by deployment owner.");
    }

    // Only the trusted API can attest that both the provider principal and application session were verified.
    [SpacetimeDB.Reducer]
    public static void LinkOwnExternalAccount(ReducerContext ctx, string provider, string issuer, string subject, Identity actingUser)
    {
        RequireTrustedApi(ctx);
        var effectiveUser = ResolveEffectiveUser(ctx, actingUser);
        if (ctx.Db.UserProfile.UserId.Find(effectiveUser)?.IsActive != true) throw new Exception("Active account required.");
        var key = ExternalLinkKey(provider, issuer, subject);
        if (ctx.Db.ExternalAccountLink.Key.Find(key) != null) throw new Exception("External account is already linked.");
        ctx.Db.ExternalAccountLink.Insert(new ExternalAccountLink { Key = key, Provider = provider, Issuer = issuer,
            Subject = subject, UserId = effectiveUser, LinkedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 });
        Log.Info($"External self-link committed: sender {ctx.Sender}, effective user {effectiveUser}.");
    }

    [SpacetimeDB.Reducer]
    public static void UnlinkExternalAccount(ReducerContext ctx, string provider, string issuer, string subject)
    {
        RequireDeploymentOwner(ctx);
        if (!ctx.Db.ExternalAccountLink.Key.Delete(ExternalLinkKey(provider, issuer, subject))) throw new Exception("External account link not found.");
        Log.Info("External account revoked by deployment owner.");
    }
}
