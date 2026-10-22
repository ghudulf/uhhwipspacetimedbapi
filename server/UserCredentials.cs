using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Table(Name = "UserCredential", Public = false)]
    public partial class UserCredential
    {
        [SpacetimeDB.PrimaryKey] public Identity UserId;
        public string PasswordHash = "";
        public ulong UpdatedAt;
    }

    private static string? GetPasswordHash(ReducerContext ctx, Identity userId) =>
        ctx.Db.UserCredential.UserId.Find(userId)?.PasswordHash ?? ctx.Db.UserProfile.UserId.Find(userId)?.PasswordHash;

    private static void SetPasswordHash(ReducerContext ctx, Identity userId, string hash)
    {
        RevokeAllRefreshTokensCore(ctx, userId);
        var credential = new UserCredential
        {
            UserId = userId, PasswordHash = hash,
            UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000
        };
        if (ctx.Db.UserCredential.UserId.Find(userId) == null) ctx.Db.UserCredential.Insert(credential);
        else ctx.Db.UserCredential.UserId.Update(credential);
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user != null && user.PasswordHash != null)
        {
            user.PasswordHash = null;
            ctx.Db.UserProfile.UserId.Update(user);
            AdvanceUserRevision(ctx, userId);
        }
    }

    [SpacetimeDB.Reducer]
    public static void MigratePasswordCredentials(ReducerContext ctx)
    {
        RequireDeploymentOwner(ctx);
        var copied = 0;
        var cleared = 0;
        foreach (var user in ctx.Db.UserProfile.Iter().ToList())
        {
            if (string.IsNullOrEmpty(user.PasswordHash)) continue;
            if (ctx.Db.UserCredential.UserId.Find(user.UserId) == null)
            {
                SetPasswordHash(ctx, user.UserId, user.PasswordHash);
                copied++;
            }
            else
            {
                user.PasswordHash = null;
                ctx.Db.UserProfile.UserId.Update(user);
                AdvanceUserRevision(ctx, user.UserId);
            }
            cleared++;
        }
        Log.Info($"Password credential migration: copied={copied}, cleared={cleared}.");
    }
}
