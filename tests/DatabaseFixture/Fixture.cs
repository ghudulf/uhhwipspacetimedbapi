using SpacetimeDB;

// Exact production reducers and tables, with a bounded synthetic seed for integration tests.
public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void PrepareCommittedServiceTestRoute(ReducerContext ctx)
    {
        RequireDeploymentOwner(ctx);
        ctx.Db.Route.Insert(new Route { RouteId = 3900000000, RouteNumber = "synthetic-service", StartPoint = "Service A", EndPoint = "Service B", IsActive = true });
    }
    [SpacetimeDB.Reducer]
    public static void PrepareRefreshTokenTest(ReducerContext ctx, string identityHex)
    {
        RequireDeploymentOwner(ctx);
        var identity = Identity.FromHexString(identityHex);
        if (ctx.Db.UserProfile.UserId.Find(identity) != null) throw new Exception("Fresh synthetic fixture required.");
        ctx.Db.UserProfile.Insert(new UserProfile { UserId = identity, LegacyUserId = 3999999999,
            Login = "synthetic-refresh-user", IsActive = true });
    }
    [SpacetimeDB.Reducer]
    public static void ExpireRefreshTokenForTest(ReducerContext ctx, string hash)
    {
        RequireDeploymentOwner(ctx);
        var row = ctx.Db.RefreshTokenRecord.TokenHash.Find(hash) ?? throw new Exception("Synthetic row missing.");
        row.ExpiresAt = RefreshNow(ctx) - 1;
        ctx.Db.RefreshTokenRecord.TokenHash.Update(row);
    }
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
    public static void SetCatalogRouteTickets(ReducerContext ctx, uint routeId, bool present, bool visible)
    {
        RequireDeploymentOwner(ctx);
        const uint first = 3999999980;
        ctx.Db.Ticket.TicketId.Delete(first); ctx.Db.Ticket.TicketId.Delete(first + 1);
        if (present)
        {
            ctx.Db.Ticket.Insert(new Ticket { TicketId = first, RouteId = routeId, SeatNumber = 1, IsActive = true, PaymentMethod = "Cash" });
            ctx.Db.Ticket.Insert(new Ticket { TicketId = first + 1, RouteId = routeId, SeatNumber = 2, IsActive = false, PaymentMethod = "Cash" });
        }
        var permission = ctx.Db.Permission.Iter().FirstOrDefault(p => p.Name == "tickets.view");
        if (permission == null)
            permission = ctx.Db.Permission.Insert(new Permission { PermissionId = ctx.Db.Permission.Iter().Max(p => p.PermissionId) + 1, Name = "tickets.view", Description = "Synthetic ticket visibility", Category = "Test", IsActive = true });
        var grant = ctx.Db.RolePermission.Iter().FirstOrDefault(p => p.RoleId == 1 && p.PermissionId == permission.PermissionId);
        if (visible && grant == null) ctx.Db.RolePermission.Insert(new RolePermission { Id = ctx.Db.RolePermission.Iter().Max(p => p.Id) + 1, RoleId = 1, PermissionId = permission.PermissionId });
        if (!visible && grant != null) ctx.Db.RolePermission.Id.Delete(grant.Id);
    }
    [SpacetimeDB.Reducer]
    public static void PrepareQrDeviceTest(ReducerContext ctx, string identityHex, bool active)
    {
        RequireDeploymentOwner(ctx);
        var identity = Identity.FromHexString(identityHex);
        var user = ctx.Db.UserProfile.UserId.Find(identity);
        if (user == null) ctx.Db.UserProfile.Insert(new UserProfile { UserId = identity, LegacyUserId = 4000000000, Login = "synthetic-qr-device-user", IsActive = active });
        else
        {
            if (user.Login != "synthetic-qr-device-user") throw new Exception("Synthetic fixture only.");
            user.IsActive = active;
            ctx.Db.UserProfile.UserId.Update(user);
        }
    }

    [SpacetimeDB.Reducer]
    public static void PrepareBootstrapTest(ReducerContext ctx)
    {
        RequireDeploymentOwner(ctx);
        var user = ctx.Db.UserProfile.UserId.Find(ctx.Sender)!;
        if (user.Login != "synthetic-owner") throw new Exception("Synthetic fixture only.");
        user.PasswordHash = null;
        ctx.Db.UserCredential.UserId.Delete(user.UserId);
        ctx.Db.UserProfile.UserId.Update(user);
    }

    [SpacetimeDB.Reducer]
    public static void PrepareAdminOperationsTest(ReducerContext ctx)
    {
        RequireDeploymentOwner(ctx);
        const uint routeId = 3000000000;
        if (ctx.Db.Route.RouteId.Find(routeId) == null)
            ctx.Db.Route.Insert(new Route { RouteId = routeId, RouteNumber = "synthetic-admin", StartPoint = "Synthetic admin start", EndPoint = "Synthetic admin end", IsActive = true });
        if (ctx.Db.RoleIdCounter.Key.Find("roleId") == null)
            ctx.Db.RoleIdCounter.Insert(new RoleIdCounter { Key = "roleId", NextId = ctx.Db.Role.Iter().Select(r => r.RoleId).DefaultIfEmpty(1u).Max() });
        if (ctx.Db.UserIdCounter.Key.Find("userId") == null)
            ctx.Db.UserIdCounter.Insert(new UserIdCounter { Key = "userId", NextId = 100 });
        if (ctx.Db.UserRoleIdCounter.Key.Find("userRoleId") == null)
            ctx.Db.UserRoleIdCounter.Insert(new UserRoleIdCounter { Key = "userRoleId", NextId = ctx.Db.UserRole.Iter().Select(r => r.Id).DefaultIfEmpty(0u).Max() });
        if (ctx.Db.TicketIdCounter.Key.Find("ticketId") == null)
            ctx.Db.TicketIdCounter.Insert(new TicketIdCounter { Key = "ticketId", NextId = ctx.Db.Ticket.Iter().Select(t => t.TicketId).DefaultIfEmpty(0u).Max() });
    }

    [SpacetimeDB.Reducer]
    public static void PrepareCredentialMigrationTest(ReducerContext ctx, string identityHex, string login, string password, bool privateCredential, bool conflictingLegacy, bool active)
    {
        RequireDeploymentOwner(ctx);
        if (!login.StartsWith("synthetic-credential-")) throw new Exception("Synthetic fixture only.");
        if (ctx.Db.UserIdCounter.Key.Find("userId") == null)
            ctx.Db.UserIdCounter.Insert(new UserIdCounter { Key = "userId", NextId = 100 });
        var identity = Identity.FromHexString(identityHex);
        ctx.Db.UserCredential.UserId.Delete(identity);
        ctx.Db.UserProfile.UserId.Delete(identity);
        var user = new UserProfile { UserId = identity, Login = login, IsActive = active,
            LegacyUserId = GetNextId(ctx, "userId"), PasswordHash = privateCredential ? null : HashPassword(ctx, password) };
        ctx.Db.UserProfile.Insert(user);
        if (privateCredential) SetPasswordHash(ctx, identity, HashPassword(ctx, password));
        if (conflictingLegacy)
        {
            user.PasswordHash = HashPassword(ctx, password + "-stale");
            ctx.Db.UserProfile.UserId.Update(user);
        }
    }

    [SpacetimeDB.Reducer]
    public static void AssertCredentialTestState(ReducerContext ctx, string identityHex, bool hasCredential, bool legacyEmpty, string password)
    {
        RequireDeploymentOwner(ctx);
        var identity = Identity.FromHexString(identityHex);
        var credential = ctx.Db.UserCredential.UserId.Find(identity);
        var profile = ctx.Db.UserProfile.UserId.Find(identity);
        if ((credential != null) != hasCredential || (string.IsNullOrEmpty(profile?.PasswordHash)) != legacyEmpty ||
            (!string.IsNullOrEmpty(password) && (credential == null || !VerifyPassword(password, credential.PasswordHash))))
            throw new Exception("Credential fixture state assertion failed; values omitted.");
    }
}
