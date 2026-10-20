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
            if (administrator == null || !ctx.Db.UserRole.Iter().Any(r => r.UserId == admin.UserId && r.RoleId == administrator.RoleId))
                throw new Exception("Account must already hold the active Administrator role.");
            if (!string.IsNullOrEmpty(admin.PasswordHash))
                throw new Exception("Administrator already has a password. Use trust-only provisioning; change passwords through account management.");
            admin.PasswordHash = HashPassword(ctx, adminPassword);
            ctx.Db.UserProfile.UserId.Update(admin);
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
        if (user == null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash) || !VerifyPassword(password, user.PasswordHash))
            throw new Exception("Invalid credentials.");
        AuthenticateUser(ctx, login, password);
    }
}
