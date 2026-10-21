#pragma warning disable STDB_UNSTABLE
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Table(Public = true)]
    public partial class WebAuthnUserHandle
    {
        [PrimaryKey] public Identity UserId;
        public byte[] Handle = [];
        public ulong CreatedAt;
        [SpacetimeDB.Default(0u), SpacetimeDB.Index.BTree] public uint ApiAccessPartition;
    }

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter WebAuthnUserHandleAccess = new Filter.Sql(
        "SELECT WebAuthnUserHandle.* FROM WebAuthnUserHandle JOIN TrustedApiIdentity ON WebAuthnUserHandle.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.Reducer]
    public static void EnsureWebAuthnUserHandle(ReducerContext ctx, Identity actingUser)
    {
        RequireTrustedApi(ctx);
        var actor = ResolveEffectiveUser(ctx, actingUser);
        if (ctx.Db.UserProfile.UserId.Find(actor)?.IsActive != true) throw new Exception("Active account required.");
        if (ctx.Db.WebAuthnUserHandle.UserId.Find(actor) != null) return;
        // The deterministic module runtime exposes transaction-seeded System.Random, not an OS CSPRNG.
        // Handles are opaque identifiers, never authentication secrets. Never derive them from identity/name/key material.
        var handle = new byte[32];
        ctx.Rng.NextBytes(handle);
        ctx.Db.WebAuthnUserHandle.Insert(new WebAuthnUserHandle { UserId = actor, Handle = handle,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 });
    }

    private static void AdvanceWebAuthnCounter(ReducerContext ctx, WebAuthnCredential credential, uint counter)
    {
        if (!credential.IsActive || ctx.Db.UserProfile.UserId.Find(credential.UserId)?.IsActive != true)
            throw new Exception("Active credential and account required.");
        if ((credential.Counter != 0 || counter != 0) && counter <= credential.Counter)
            throw new Exception("WebAuthn counter replay rejected.");
        credential.Counter = counter;
        ctx.Db.WebAuthnCredential.Id.Update(credential);
    }

    [SpacetimeDB.Reducer]
    public static void RemoveOwnWebAuthnCredential(ReducerContext ctx, uint id, Identity actingUser)
    {
        RequireTrustedApi(ctx);
        var actor = ResolveEffectiveUser(ctx, actingUser);
        if (ctx.Db.UserProfile.UserId.Find(actor)?.IsActive != true) throw new Exception("Active account required.");
        var credential = ctx.Db.WebAuthnCredential.Id.Find(id);
        if (credential == null || !credential.IsActive || credential.UserId != actor) throw new Exception("Own active credential required.");
        credential.IsActive = false;
        ctx.Db.WebAuthnCredential.Id.Update(credential);
        if (!ctx.Db.WebAuthnCredential.Iter().Any(c => c.UserId == actor && c.IsActive))
        {
            var settings = ctx.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId == actor);
            if (settings != null) { settings.WebAuthnEnabled = false; ctx.Db.UserSettings.UserSettingId.Update(settings); }
        }
    }
}
