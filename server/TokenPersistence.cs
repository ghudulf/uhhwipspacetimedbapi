#pragma warning disable STDB_UNSTABLE // Pinned SDK 2.1.0 RLS API, verified during publish.
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Table(Public = true)]
    public partial class OidcTokenRevision
    {
        [PrimaryKey] public uint TokenId;
        public ulong Revision;
        // Constant partition enables indexed RLS subscriptions in SDK 2.1.0.
        [SpacetimeDB.Default(0u), SpacetimeDB.Index.BTree]
        public uint ApiAccessPartition;
    }

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter TokenRevisionApiAccess = new Filter.Sql(
        "SELECT OidcTokenRevision.* FROM OidcTokenRevision JOIN TrustedApiIdentity ON OidcTokenRevision.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    private static void AdvanceTokenRevision(ReducerContext ctx, uint tokenId)
    {
        var revision = ctx.Db.OidcTokenRevision.TokenId.Find(tokenId);
        if (revision == null) ctx.Db.OidcTokenRevision.Insert(new OidcTokenRevision { TokenId = tokenId, Revision = 1 });
        else { revision.Revision = checked(revision.Revision + 1); ctx.Db.OidcTokenRevision.TokenId.Update(revision); }
    }

    [SpacetimeDB.Reducer]
    public static void ReplaceOidcToken(ReducerContext ctx, uint internalId, ulong expectedRevision,
        uint? authorizationId, string? applicationClientId, ulong? creationDate, ulong? expirationDate,
        string? payload, string? propertiesJson, ulong? redemptionDate, string? referenceId,
        string? status, string? subject, string? type)
    {
        RequireTrustedApi(ctx);
        var token = ctx.Db.OpenIddictSpacetimeToken.Id.Find(internalId) ?? throw new Exception("Token not found.");
        var revision = ctx.Db.OidcTokenRevision.TokenId.Find(internalId)?.Revision ?? 0;
        if (revision != expectedRevision) throw new Exception("Token concurrency conflict.");
        if (referenceId != null && ctx.Db.OpenIddictSpacetimeToken.Iter().Any(t => t.Id != internalId && t.ReferenceId == referenceId))
            throw new Exception("Token reference already exists.");
        // Full replacement permits clearing nullable fields. Stable IDs are immutable.
        token.AuthorizationId = authorizationId;
        token.ApplicationClientId = applicationClientId;
        token.CreationDate = creationDate;
        token.ExpirationDate = expirationDate;
        token.Payload = payload;
        token.Properties = propertiesJson;
        token.RedemptionDate = redemptionDate;
        token.ReferenceId = referenceId;
        token.Status = status;
        token.Subject = subject;
        token.Type = type;
        ctx.Db.OpenIddictSpacetimeToken.Id.Update(token);
        AdvanceTokenRevision(ctx, internalId);
        Log.Info($"OIDC token committed: databaseId={internalId}, revision={revision + 1}, status={status}");
    }
}
