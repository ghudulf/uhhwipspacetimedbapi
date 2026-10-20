using SpacetimeDB;

// Exact production reducers and tables, with a bounded synthetic seed for integration tests.
public static partial class Module
{
    [SpacetimeDB.Reducer(SpacetimeDB.ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        ctx.Db.DeploymentAuthority.Insert(new DeploymentAuthority { Id = 1, Owner = ctx.Sender });
        ctx.Db.UserProfile.Insert(new UserProfile { UserId = ctx.Sender, LegacyUserId = 1, Login = "synthetic-owner", IsActive = true });
        ctx.Db.Role.Insert(new Role { RoleId = 1, LegacyRoleId = 1, Name = "Administrator", Description = "Synthetic test role", IsActive = true });
        ctx.Db.UserRole.Insert(new UserRole { UserId = ctx.Sender, RoleId = 1 });
        uint id = 1;
        foreach (var permission in new[] { "sales.create", "sales.edit", "sales.delete", "sales.view", "users.edit" })
        {
            ctx.Db.Permission.Insert(new Permission { PermissionId = id, Name = permission, Description = "Synthetic test permission", Category = "Test", IsActive = true });
            ctx.Db.RolePermission.Insert(new RolePermission { RoleId = 1, PermissionId = id++ });
        }
        for (uint ticketId = 1; ticketId <= 4; ticketId++)
            ctx.Db.Ticket.Insert(new Ticket { TicketId = ticketId, RouteId = 1, TicketPrice = 12.50, IsActive = true, SeatNumber = ticketId, PaymentMethod = "Cash" });
    }
    [SpacetimeDB.Reducer]
    public static void SetTestSalesPermissionSpelling(ReducerContext ctx, bool legacy)
    {
        RequireDeploymentOwner(ctx);
        var permission = ctx.Db.Permission.Iter().First(p => p.Name == "sales.edit" || p.Name == "sales.update");
        permission.Name = legacy ? "sales.update" : "sales.edit";
        ctx.Db.Permission.PermissionId.Update(permission);
    }
    [SpacetimeDB.Reducer]
    public static void PrepareFleetTest(ReducerContext ctx)
    {
        RequireDeploymentOwner(ctx);
        var next = ctx.Db.Permission.Iter().Select(p => p.PermissionId).DefaultIfEmpty(0u).Max();
        foreach (var name in new[] { "buses.create", "buses.edit", "buses.delete", "buses.view", "routes.view", "maintenance.view" })
        {
            if (ctx.Db.Permission.Iter().Any(p => p.Name == name)) continue;
            var id = checked(++next);
            ctx.Db.Permission.Insert(new Permission { PermissionId = id, Name = name, Description = "Synthetic fleet test permission", Category = "Test", IsActive = true });
            ctx.Db.RolePermission.Insert(new RolePermission { RoleId = 1, PermissionId = id });
        }
    }

    [SpacetimeDB.Reducer]
    public static void SetFleetTestReference(ReducerContext ctx, uint busId, bool present)
    {
        RequireDeploymentOwner(ctx);
        // A deliberately inactive route must still prevent dangling references on delete.
        const uint routeId = 4000000000;
        ctx.Db.Route.RouteId.Delete(routeId);
        if (present) ctx.Db.Route.Insert(new Route { RouteId = routeId, BusId = busId, DriverId = 0,
            RouteNumber = "synthetic-reference", StartPoint = "A", EndPoint = "B", TravelTime = "00:10", IsActive = false });
    }
}
