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
        // Tickets in the synthetic baseline deliberately refer to route 1. Keep catalog test IDs separate.
        var routeCounter = ctx.Db.RouteIdCounter.Key.Find("routeId");
        if (routeCounter == null) ctx.Db.RouteIdCounter.Insert(new RouteIdCounter { Key = "routeId", NextId = 100 });
        else if (routeCounter.NextId < 100) { routeCounter.NextId = 100; ctx.Db.RouteIdCounter.Key.Update(routeCounter); }
        var next = ctx.Db.Permission.Iter().Select(p => p.PermissionId).DefaultIfEmpty(0u).Max();
        foreach (var name in new[] { "buses.create", "buses.edit", "buses.delete", "buses.view", "routes.view", "routes.create", "routes.edit", "routes.delete", "maintenance.view", "maintenance.create", "maintenance.edit", "maintenance.delete", "employees.view", "employees.create", "employees.edit", "employees.delete", "jobs.view", "jobs.create", "jobs.edit", "jobs.delete" })
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
    [SpacetimeDB.Reducer]
    public static void SetCatalogEmployeeReference(ReducerContext ctx, uint employeeId, bool present)
    {
        RequireDeploymentOwner(ctx);
        const uint id = 4000000000;
        ctx.Db.Maintenance.MaintenanceId.Delete(id);
        if (present) ctx.Db.Maintenance.Insert(new Maintenance { MaintenanceId = id, BusId = 0, ScheduledByEmployeeId = employeeId });
    }
    [SpacetimeDB.Reducer]
    public static void PrepareBootstrapTest(ReducerContext ctx)
    {
        RequireDeploymentOwner(ctx);
        var user = ctx.Db.UserProfile.UserId.Find(ctx.Sender)!;
        if (user.Login != "synthetic-owner") throw new Exception("Synthetic fixture only.");
        user.PasswordHash = null;
        ctx.Db.UserProfile.UserId.Update(user);
    }
}
