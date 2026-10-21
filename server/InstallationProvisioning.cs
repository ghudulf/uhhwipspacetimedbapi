using SpacetimeDB;

public static partial class Module
{
    // The deployment owner's authenticated connection is the authority. An application
    // administrator or a newly connected API cannot grant itself database trust.
    [SpacetimeDB.Reducer]
    public static void BootstrapInstallation(ReducerContext ctx, Identity apiIdentity, string? adminLogin, string? adminPassword)
    {
        RequireDeploymentOwner(ctx);
        if ((adminLogin == null) != (adminPassword == null))
            throw new Exception("Provide both administrator login and password, or neither for trust-only provisioning.");
        if (adminPassword != null)
        {
            if (adminPassword.Length < 12 || adminPassword.Length > 1024 || string.IsNullOrWhiteSpace(adminPassword))
                throw new Exception("Initial administrator password must contain 12 to 1024 characters.");
            var admin = ctx.Db.UserProfile.Login.Find(adminLogin!);
            if (admin == null || !admin.IsActive)
                throw new Exception("An active existing administrator account is required.");
            var administrator = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator" && r.IsActive);
            if (administrator == null || !ctx.Db.UserRole.UserId.Filter(admin.UserId).Any(r => r.RoleId == administrator.RoleId))
                throw new Exception("Account must already hold the active Administrator role.");
            if (!string.IsNullOrEmpty(GetPasswordHash(ctx, admin.UserId)))
                throw new Exception("Administrator already has a password. Use trust-only provisioning; change passwords through account management.");
            SetPasswordHash(ctx, admin.UserId, HashPassword(ctx, adminPassword));
            Log.Info($"Initial administrator credential provisioned by deployment owner for account {admin.UserId}; credential omitted.");
        }
        // Same transaction: a rejected password must not partially grant API trust.
        ProvisionTrustedApi(ctx, apiIdentity);
    }

    [SpacetimeDB.Reducer]
    public static void AuthenticateUserChecked(ReducerContext ctx, string login, string password)
    {
        RequireTrustedApi(ctx);
        var user = ctx.Db.UserProfile.Login.Find(login);
        if (user == null || !user.IsActive || string.IsNullOrEmpty(GetPasswordHash(ctx, user.UserId)) || !VerifyPassword(password, GetPasswordHash(ctx, user.UserId)))
            throw new Exception("Invalid credentials.");
        AuthenticateUser(ctx, login, password);
    }

    [SpacetimeDB.Reducer]
    public static void RecordAuthenticationFailure(ReducerContext ctx, string login, string password)
    {
        RequireTrustedApi(ctx);
        var user = ctx.Db.UserProfile.Login.Find(login);
        if (user != null && user.IsActive && !string.IsNullOrEmpty(GetPasswordHash(ctx, user.UserId)) && VerifyPassword(password, GetPasswordHash(ctx, user.UserId)))
            throw new Exception("Valid credentials cannot record an authentication failure.");
        InsertAuthenticationFailure(ctx, user);
    }

    private static void InsertAuthenticationFailure(ReducerContext ctx, UserProfile? user)
    {
        ctx.Db.AuthenticationEvent.Insert(new AuthenticationEvent
        {
            UserId = user is { IsActive: true } ? user.UserId : new Identity(),
            EventType = "Failed",
            Timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            Details = user == null ? "User not found" : !user.IsActive ? "User is inactive" : "Invalid password",
            IpAddress = null
        });
    }
}
