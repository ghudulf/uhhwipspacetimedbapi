using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Table(Public = true)]
    public partial class OidcApplicationMetadata
    {
        [PrimaryKey] public string ClientId = "";
        public string Permissions = "[]";
        public string Requirements = "[]";
        public string DisplayNames = "{}";
        public string Properties = "{}";
        public ulong Revision;
        [SpacetimeDB.Index.BTree] public uint ApiAccessPartition;
    }

#pragma warning disable STDB_UNSTABLE
    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter ApplicationMetadataApiAccess = new Filter.Sql(
        "SELECT OidcApplicationMetadata.* FROM OidcApplicationMetadata JOIN TrustedApiIdentity ON OidcApplicationMetadata.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");
#pragma warning restore STDB_UNSTABLE

    private static void AdvanceLegacyApplicationRevision(ReducerContext ctx, string clientId)
    {
        var metadata = ctx.Db.OidcApplicationMetadata.ClientId.Find(clientId);
        if (metadata == null) return;
        metadata.Revision = checked(metadata.Revision + 1);
        // Legacy adapters only carry scope names, so use the legacy endpoint/grant projection.
        metadata.Permissions = "";
        ctx.Db.OidcApplicationMetadata.ClientId.Update(metadata);
    }

    private static void AdvanceLegacyScopeRevision(ReducerContext ctx, uint scopeId)
    {
        var revision = ctx.Db.OidcScopeRevision.ScopeId.Find(scopeId);
        if (revision == null) ctx.Db.OidcScopeRevision.Insert(new OidcScopeRevision { ScopeId = scopeId, Revision = 1 });
        else { revision.Revision = checked(revision.Revision + 1); ctx.Db.OidcScopeRevision.ScopeId.Update(revision); }
    }

    [SpacetimeDB.Reducer]
    public static void ReplaceOidcApplication(ReducerContext ctx, string clientId, string clientSecret,
        string displayName, string[] redirectUris, string[] postLogoutRedirectUris, string[] allowedScopes,
        string consentType, string clientType, string permissions, string requirements, string displayNames,
        string properties, ulong expectedRevision, bool create)
    {
        RequireTrustedApi(ctx);
        var existing = ctx.Db.OpenIdConnect.ClientId.Find(clientId);
        var metadata = ctx.Db.OidcApplicationMetadata.ClientId.Find(clientId);
        if ((create && existing != null) || (!create && (existing == null || !existing.IsActive)) ||
            (metadata?.Revision ?? 0) != expectedRevision)
            throw new Exception("Application concurrency conflict.");
        RegisterOpenIdClient(ctx, clientId, clientSecret, displayName, redirectUris, postLogoutRedirectUris, allowedScopes, consentType, clientType);
        var replacement = new OidcApplicationMetadata
        {
            ClientId = clientId, Permissions = permissions, Requirements = requirements,
            DisplayNames = displayNames, Properties = properties, Revision = checked(expectedRevision + 1)
        };
        if (metadata == null) ctx.Db.OidcApplicationMetadata.Insert(replacement);
        else ctx.Db.OidcApplicationMetadata.ClientId.Update(replacement);
        Log.Info($"OIDC application metadata committed: revision={replacement.Revision}");
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class OidcScopeRevision
    {
        [PrimaryKey] public uint ScopeId;
        public ulong Revision;
    }

    [SpacetimeDB.Reducer]
    public static void ReplaceOidcScope(ReducerContext ctx, uint internalId, ulong expectedRevision,
        string name, string? description, string descriptionsJson, string? displayName,
        string displayNamesJson, string propertiesJson, string resourcesJson)
    {
        RequireTrustedApi(ctx);
        var scope = ctx.Db.OpenIddictSpacetimeScope.Id.Find(internalId) ?? throw new Exception("Scope not found.");
        var revision = ctx.Db.OidcScopeRevision.ScopeId.Find(internalId);
        if ((revision?.Revision ?? 0) != expectedRevision) throw new Exception("Scope concurrency conflict.");
        if (string.IsNullOrWhiteSpace(name) || ctx.Db.OpenIddictSpacetimeScope.Iter().Any(x => x.Id != internalId && x.Name == name))
            throw new Exception("Invalid or duplicate scope name.");
        scope.Name = name;
        scope.Description = description;
        scope.Descriptions = descriptionsJson;
        scope.DisplayName = displayName;
        scope.DisplayNames = displayNamesJson;
        scope.Properties = propertiesJson;
        scope.Resources = resourcesJson;
        ctx.Db.OpenIddictSpacetimeScope.Id.Update(scope);
        if (revision == null) ctx.Db.OidcScopeRevision.Insert(new OidcScopeRevision { ScopeId = internalId, Revision = 1 });
        else { revision.Revision = checked(revision.Revision + 1); ctx.Db.OidcScopeRevision.ScopeId.Update(revision); }
        Log.Info($"OIDC scope replacement committed: databaseId={internalId}");
    }
}
