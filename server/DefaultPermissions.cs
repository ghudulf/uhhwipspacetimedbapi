using SpacetimeDB;

public static partial class Module
{
    // Shared by fresh initialization and the additive, owner-authorized migration.
    private static readonly string[] DefaultPermissionNames = ("assign_roles grant_permissions " +
        "users.view users.create users.edit users.delete users.assign.roles users.remove.roles users.view.permissions users.view.roles " +
        "roles.view roles.create roles.edit roles.delete roles.view.permissions " +
        "permissions.view permissions.create permissions.edit permissions.delete permissions.view.categories " +
        "employees.view employees.create employees.edit employees.delete employees.view.self employees.edit.self " +
        "jobs.view jobs.create jobs.edit jobs.delete buses.view buses.create buses.edit buses.delete " +
        "routes.view routes.create routes.edit routes.delete schedules.view schedules.create schedules.edit schedules.delete " +
        "tickets.view tickets.create tickets.edit tickets.delete tickets.buy tickets.reserve " +
        "sales.view sales.create sales.edit sales.delete maintenance.view maintenance.create maintenance.edit maintenance.delete " +
        "reports.view reports.create reports.export system.view " +
        "openid.connect.view openid.connect.create openid.connect.edit openid.connect.delete openid.connect.grant openid.connect.revoke openid.connect.list openid.connect.refresh " +
        "magiclink.view magiclink.create magiclink.edit magiclink.delete magiclink.grant magiclink.revoke magiclink.list magiclink.refresh " +
        "feature_flags.update feature_flags.clear feature_flags.delete").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [SpacetimeDB.Reducer]
    public static void EnsureDefaultPermissions(ReducerContext ctx)
    {
        RequireDeploymentOwner(ctx);
        var administrator = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator")
            ?? throw new Exception("Administrator role must exist before default permissions are provisioned.");
        var nextPermission = ctx.Db.Permission.Iter().Select(p => p.PermissionId).DefaultIfEmpty(0u).Max();
        var nextGrant = ctx.Db.RolePermission.Iter().Select(p => p.Id).DefaultIfEmpty(0u).Max();
        foreach (var name in DefaultPermissionNames)
        {
            var permission = ctx.Db.Permission.Iter().FirstOrDefault(p => p.Name == name);
            if (permission == null)
                permission = ctx.Db.Permission.Insert(new Permission { PermissionId = checked(++nextPermission), Name = name,
                    Description = name, Category = "Default", IsActive = true, CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 });
            if (!ctx.Db.RolePermission.RoleId.Filter(administrator.RoleId).Any(p => p.PermissionId == permission.PermissionId))
                ctx.Db.RolePermission.Insert(new RolePermission { Id = checked(++nextGrant), RoleId = administrator.RoleId,
                    PermissionId = permission.PermissionId, GrantedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, GrantedBy = "Deployment owner" });
        }
        var permissionCounter = ctx.Db.PermissionIdCounter.Key.Find("permissionId");
        if (permissionCounter == null) ctx.Db.PermissionIdCounter.Insert(new PermissionIdCounter { Key = "permissionId", NextId = nextPermission });
        else if (permissionCounter.NextId < nextPermission) { permissionCounter.NextId = nextPermission; ctx.Db.PermissionIdCounter.Key.Update(permissionCounter); }
        var grantCounter = ctx.Db.RolePermissionIdCounter.Key.Find("rolePermissionId");
        if (grantCounter == null) ctx.Db.RolePermissionIdCounter.Insert(new RolePermissionIdCounter { Key = "rolePermissionId", NextId = nextGrant });
        else if (grantCounter.NextId < nextGrant) { grantCounter.NextId = nextGrant; ctx.Db.RolePermissionIdCounter.Key.Update(grantCounter); }
    }
}
