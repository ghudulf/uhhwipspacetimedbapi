#pragma warning disable STDB_UNSTABLE
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Table(Public = true)]
    public partial class RefreshTokenRecord
    {
        [PrimaryKey] public string TokenHash = "";
        [SpacetimeDB.Index.BTree] public Identity UserId;
        public string FamilyId = "";
        public ulong IssuedAt;
        public ulong ExpiresAt;
        public ulong? UsedAt;
        public ulong? RevokedAt;
        public string DeviceLabel = "";
        [SpacetimeDB.Default(0u), SpacetimeDB.Index.BTree] public uint ApiAccessPartition;
    }
    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter RefreshTokenRecordAccess = new Filter.Sql(
        "SELECT RefreshTokenRecord.* FROM RefreshTokenRecord JOIN TrustedApiIdentity ON RefreshTokenRecord.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");
    private static ulong RefreshNow(ReducerContext ctx) => (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
    private static void ValidRefreshHash(string value)
    {
        if (value.Length != 43 || value.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw new Exception("invalid_refresh_token");
    }
    private static void PruneRefreshTokens(ReducerContext ctx)
    {
        var now = RefreshNow(ctx);
        // Preserve used, unexpired, unrevoked records: they are needed for family-reuse detection.
        foreach (var row in ctx.Db.RefreshTokenRecord.Iter().Where(row => row.ExpiresAt <= now || row.RevokedAt != null).Take(128).ToArray())
            ctx.Db.RefreshTokenRecord.TokenHash.Delete(row.TokenHash);
    }
    private static void RevokeRefreshFamilyCore(ReducerContext ctx, Identity actor, string family)
    {
        foreach (var row in ctx.Db.RefreshTokenRecord.UserId.Filter(actor).Where(row => row.FamilyId == family && row.RevokedAt == null).ToArray())
        { row.RevokedAt = RefreshNow(ctx); ctx.Db.RefreshTokenRecord.TokenHash.Update(row); }
    }
    private static void RevokeAllRefreshTokensCore(ReducerContext ctx, Identity actor)
    {
        foreach (var row in ctx.Db.RefreshTokenRecord.UserId.Filter(actor).Where(row => row.RevokedAt == null).ToArray())
        { row.RevokedAt = RefreshNow(ctx); ctx.Db.RefreshTokenRecord.TokenHash.Update(row); }
    }
    [SpacetimeDB.Reducer]
    public static void IssueRefreshToken(ReducerContext ctx, string tokenHash, Identity actingUser, string deviceLabel, uint lifetimeSeconds)
    {
        RequireTrustedApi(ctx);
        var actor = ResolveEffectiveUser(ctx, actingUser);
        ValidRefreshHash(tokenHash);
        if (ctx.Db.UserProfile.UserId.Find(actor)?.IsActive != true || deviceLabel.Length > 64 || deviceLabel.Any(char.IsControl) ||
            ctx.Db.RefreshTokenRecord.TokenHash.Find(tokenHash) != null) throw new Exception("invalid_refresh_token");
        PruneRefreshTokens(ctx);
        var now = RefreshNow(ctx);
        var families = ctx.Db.RefreshTokenRecord.UserId.Filter(actor).Where(row => row.RevokedAt == null && row.ExpiresAt > now)
            .GroupBy(row => row.FamilyId).OrderBy(group => group.Min(row => row.IssuedAt)).ThenBy(group => group.Key).ToArray();
        foreach (var family in families.Take(Math.Max(0, families.Length - 9))) RevokeRefreshFamilyCore(ctx, actor, family.Key);
        // Hard per-user retention cap bounds even unexpired high-frequency rotations without deleting reuse proofs.
        if (ctx.Db.RefreshTokenRecord.UserId.Filter(actor).Count() >= 4096) throw new Exception("invalid_refresh_token");
        ctx.Db.RefreshTokenRecord.Insert(new RefreshTokenRecord { TokenHash = tokenHash, UserId = actor, FamilyId = tokenHash,
            DeviceLabel = deviceLabel, IssuedAt = now, ExpiresAt = now + (ulong)Math.Clamp(lifetimeSeconds, 3600u, 90u * 86400u) * 1000 });
    }
    [SpacetimeDB.Reducer]
    public static void RotateRefreshToken(ReducerContext ctx, string presentedHash, string newHash)
    {
        RequireTrustedApi(ctx);
        ValidRefreshHash(presentedHash); ValidRefreshHash(newHash);
        var row = ctx.Db.RefreshTokenRecord.TokenHash.Find(presentedHash);
        var now = RefreshNow(ctx);
        if (row == null || row.RevokedAt != null || row.ExpiresAt <= now || ctx.Db.UserProfile.UserId.Find(row.UserId)?.IsActive != true)
            throw new Exception("invalid_refresh_token");
        if (row.UsedAt != null)
        {
            // Committed denial: throwing here would roll back theft-detection revocation.
            RevokeRefreshFamilyCore(ctx, row.UserId, row.FamilyId);
            return;
        }
        if (newHash == presentedHash || ctx.Db.RefreshTokenRecord.TokenHash.Find(newHash) != null ||
            ctx.Db.RefreshTokenRecord.UserId.Filter(row.UserId).Count() >= 4096) throw new Exception("invalid_refresh_token");
        PruneRefreshTokens(ctx);
        row.UsedAt = now; ctx.Db.RefreshTokenRecord.TokenHash.Update(row);
        ctx.Db.RefreshTokenRecord.Insert(new RefreshTokenRecord { TokenHash = newHash, UserId = row.UserId, FamilyId = row.FamilyId,
            IssuedAt = now, ExpiresAt = row.ExpiresAt, DeviceLabel = row.DeviceLabel });
    }
    [SpacetimeDB.Reducer]
    public static void RevokeRefreshFamily(ReducerContext ctx, string presentedHash)
    {
        RequireTrustedApi(ctx); ValidRefreshHash(presentedHash);
        var row = ctx.Db.RefreshTokenRecord.TokenHash.Find(presentedHash);
        if (row == null) throw new Exception("invalid_refresh_token");
        RevokeRefreshFamilyCore(ctx, row.UserId, row.FamilyId);
    }
    [SpacetimeDB.Reducer]
    public static void RevokeAllRefreshTokens(ReducerContext ctx, Identity actingUser)
    {
        RequireTrustedApi(ctx);
        var actor = ResolveEffectiveUser(ctx, actingUser);
        if (ctx.Db.UserProfile.UserId.Find(actor)?.IsActive != true) throw new Exception("invalid_refresh_token");
        RevokeAllRefreshTokensCore(ctx, actor);
    }
}
