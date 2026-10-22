using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class CommittedServiceTests
{
    public static async Task DataAndAuditAsync(ISpacetimeDBService db, Identity owner)
    {
        var data = new DataService(db, NullLogger<DataService>.Instance);
        var name = "synthetic.data." + Guid.NewGuid().ToString("N");
        var roles = new RoleService(db, NullLogger<RoleService>.Instance);
        var authentication = new AuthenticationService(db, roles, NullLogger<AuthenticationService>.Instance);
        Check(await authentication.RegisterAsync(name, "synthetic-service-password", 1, actingUser: owner), "authentication registration not committed");
        var registered = await db.ReadAsync(c => c.Db.UserProfile.Login.Find(name)!);
        Check(registered.UserId != owner && await authentication.GetUserRoleAsync(registered.UserId) == 1,
            "registration reused sender identity or role read failed");
        var noSettingsTotp = new TotpService(db, new ConfigurationBuilder().Build(), UrlEncoder.Default, NullLogger<TotpService>.Instance);
        Check(!(await noSettingsTotp.EnableTotpAsync(registered.UserId, Code(), "JBSWY3DPEHPK3PXP")).success &&
            !(await noSettingsTotp.DisableTotpAsync(registered.UserId)).success,
            "TOTP reducer rejection for an existing user without settings was reported as success");
        await db.ExecuteReducerAsync(new Reducer.DeleteUser(registered.UserId, owner), _ => true);
        var permission = await data.AddAsync(new Permission { Name = name, Description = "before", Category = "Test" })
            ?? throw new Exception("DataService add returned before commit");
        permission.Description = "after";
        Check((await data.GetAsync<Permission>(permission.PermissionId))!.Description == "before", "service result leaked a mutable cache row");
        Check((await data.UpdateAsync(permission))!.Description == "after", "DataService update readback stale");
        await Reject(() => data.AddAsync(new Permission { Name = name, Description = "duplicate", Category = "Test" }));
        Check(await data.DeleteAsync<Permission>(permission.PermissionId) && await data.GetAsync<Permission>(permission.PermissionId) == null, "DataService delete not committed");
        var audit = new AdminActionLogger(db, NullLogger<AdminActionLogger>.Instance, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        await audit.LogActionAsync(owner.ToString(), name, "synthetic committed audit", owner);
        Check((await audit.GetActionsByTypeAsync(name)).Count == 1, "admin audit returned before commit");
        var authorizations = new AuthorizationStore(db, NullLogger<AuthorizationStore>.Instance);
        var grant = await authorizations.InstantiateAsync(default);
        grant.Subject = owner.ToString(); grant.Type = "permanent"; grant.Status = "valid";
        await authorizations.CreateAsync(grant, default);
        var cold = new AuthorizationStore(db, NullLogger<AuthorizationStore>.Instance);
        Check((await cold.FindByIdAsync(grant.OpenIddictAuthorizationId, default))?.Id == grant.Id, "authorization create/cold readback");
        grant.Status = "revoked";
        await authorizations.UpdateAsync(grant, default);
        Check((await cold.FindByIdAsync(grant.OpenIddictAuthorizationId, default))?.Status == "revoked", "authorization update not committed");
        await authorizations.DeleteAsync(grant, default);
        Check(await cold.FindByIdAsync(grant.OpenIddictAuthorizationId, default) == null, "authorization delete not committed");
        Console.WriteLine("PASS generic data service committed add/update/delete and duplicate rejection; admin audit committed before readback");
        Console.WriteLine("PASS authorization store committed create/update/delete and cold reconstruction without polling, process mappings or property-value diagnostics");
    }

    public static async Task TicketsAndSchedulesAsync(ISpacetimeDBService db, Identity owner, string ownerToken)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);
        using var body = new StringContent("[]", System.Text.Encoding.UTF8, "application/json");
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var response = await http.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/prepare_committed_service_test_route", body);
        response.EnsureSuccessStatusCode();
        const uint route = 3900000000;
        var deniedActor = Identity.FromHexString(new string('d', 64));
        var tickets = new TicketService(db, NullLogger<TicketService>.Instance);
        await Reject(() => tickets.CreateTicketAsync(route, 1, 5, "Cash", 0, deniedActor));
        Check(await tickets.CreateTicketAsync(route, 1, 5, "Cash", 0, owner), "ticket creation not committed");
        var ticket = (await tickets.GetTicketsByRouteIdAsync(route)).Single();
        await Reject(() => tickets.UpdateTicketAsync(ticket.TicketId, ticketPrice: 7, actingUser: deniedActor));
        Check(await tickets.UpdateTicketAsync(ticket.TicketId, ticketPrice: 7, actingUser: owner) &&
            (await tickets.GetTicketByIdAsync(ticket.TicketId))!.TicketPrice == 7, "ticket update not committed");
        await Reject(() => tickets.DeleteTicketAsync(ticket.TicketId, deniedActor));
        Check(await tickets.DeleteTicketAsync(ticket.TicketId, owner) && await tickets.GetTicketByIdAsync(ticket.TicketId) == null, "ticket deletion not committed");
        using var schedules = new RouteScheduleService(db, NullLogger<RouteScheduleService>.Instance, new ConfigurationBuilder().Build());
        await Reject(() => schedules.CreateScheduleAsync(routeId: route, departureTime: 1000, arrivalTime: 2000, actingUser: deniedActor));
        const string notes = "literal [CORRELATION:user-text]";
        var ids = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => schedules.CreateScheduleAsync(routeId: route,
            departureTime: 1000, arrivalTime: 2000, notes: notes, actingUser: owner)));
        Check(ids.All(id => id.HasValue) && ids.Distinct().Count() == 2, "identical concurrent schedules correlated to wrong row");
        foreach (var id in ids)
        {
            var scheduleId = id!.Value;
            Check((await schedules.GetScheduleByIdAsync(scheduleId))!.Notes == notes, "schedule notes changed by correlation workaround");
            await Reject(() => schedules.UpdateScheduleAsync(scheduleId, price: 9, actingUser: deniedActor));
            Check(await schedules.UpdateScheduleAsync(scheduleId, price: 9, actingUser: owner) &&
                (await schedules.GetScheduleByIdAsync(scheduleId))!.Price == 9, "schedule update not committed");
            await Reject(() => schedules.DeleteScheduleAsync(scheduleId, deniedActor));
            Check(await schedules.DeleteScheduleAsync(scheduleId, owner) && await schedules.GetScheduleByIdAsync(scheduleId) == null, "schedule deletion not committed");
        }
        Console.WriteLine("PASS migrated ticket/schedule services: committed CRUD/rejection readback, two identical concurrent schedule creates get exact receipt IDs, notes unchanged and no callback/polling correlation");
    }

    public static async Task AdministrationAsync(ISpacetimeDBService db, Identity owner)
    {
        var roles = new RoleService(db, NullLogger<RoleService>.Instance);
        var permissions = new PermissionService(db, NullLogger<PermissionService>.Instance);
        var auth = new AuthenticationService(db, roles, NullLogger<AuthenticationService>.Instance);
        var users = new UserService(db, auth, roles, NullLogger<UserService>.Instance);
        var suffix = Guid.NewGuid().ToString("N");
        var role = await db.ExecuteReducerAsync(new Reducer.CreateRoleReducer(3900000, "synthetic-service-" + suffix, "test", false, 100, owner),
            c => c.Db.Role.Iter().Single(r => r.Name == "synthetic-service-" + suffix));
        var user = await users.CreateUserAsync("synthetic-service-" + suffix, "synthetic-service-password", (int)role.RoleId, actingUser: owner)
            ?? throw new Exception("Service user creation did not read committed state");
        Check(await users.UpdateUserAsync(user.LegacyUserId, null, null, null, "service@test.invalid", null, null, owner) &&
            (await users.GetUserByIdAsync(user.LegacyUserId))!.Email == "service@test.invalid", "user update not committed");
        Check(!await users.DeleteUserAsync(user.LegacyUserId, user.UserId), "self deletion rejection swallowed");
        Check(!await roles.AssignRoleToUserAsync(user.LegacyUserId, 1), "unprivileged Administrator assignment rejection swallowed");
        Check(!await roles.RemoveRoleFromUserAsync(user.LegacyUserId, role.RoleId), "unprivileged role removal rejection swallowed");
        var permission = await permissions.CreatePermissionAsync("synthetic.service." + suffix, "test", "Test", owner)
            ?? throw new Exception("Permission creation did not read committed state");
        Check(!await permissions.UpdatePermissionAsync(permission.PermissionId, null, "denied", null, null, user.UserId), "permission update rejection swallowed");
        Check(!await permissions.DeletePermissionAsync(permission.PermissionId, user.UserId), "permission deletion rejection swallowed");
        Check(await permissions.UpdatePermissionAsync(permission.PermissionId, null, "committed", null, null, owner) &&
            (await permissions.GetPermissionByIdAsync(permission.PermissionId))!.Description == "committed", "permission update not committed");
        Check(!await roles.UpdateRoleAsync(role.RoleId, null, "denied", null, null, actingUser: user.UserId), "role update rejection swallowed");
        Check(await roles.UpdateRoleAsync(role.RoleId, null, "committed", null, [permission.PermissionId], actingUser: owner) &&
            (await roles.GetRolePermissionsAsync(role.RoleId)).Any(p => p.PermissionId == permission.PermissionId), "role grant not committed");
        Check(!await permissions.DeletePermissionAsync(permission.PermissionId, owner), "assigned permission deletion reported success");
        Check(await roles.UpdateRoleAsync(role.RoleId, null, null, null, [], actingUser: owner), "role revoke not committed");
        Check(await permissions.DeletePermissionAsync(permission.PermissionId, owner) && await permissions.GetPermissionByIdAsync(permission.PermissionId) == null, "permission deletion not committed");
        Check(await users.DeleteUserAsync(user.LegacyUserId, owner), "user deletion not committed");
        Check(!await roles.DeleteRoleAsync(role.RoleId, user.UserId), "role deletion rejection swallowed");
        Check(await roles.DeleteRoleAsync(role.RoleId, owner) && await roles.GetRoleByIdAsync(role.RoleId) == null, "role deletion not committed");
        try
        {
            await roles.CreateRoleAsync("synthetic-service-grant-" + suffix, "test", 3900001, 100, [uint.MaxValue], actingUser: owner);
            throw new Exception("Rejected grant inside role creation was swallowed");
        }
        catch (ReducerRejectedException) { }
        Console.WriteLine("PASS migrated administrative services: user/permission/role committed CRUD and grant/revoke readback; assign/remove/grant/update/delete rejections observable instead of swallowed");
    }

    public static async Task SecurityAsync(ISpacetimeDBService db, Identity owner)
    {
        var settings = new SettingsService(db, NullLogger<SettingsService>.Instance);
        Check((await settings.GetOrCreateUserSettingsAsync(owner)).UserId == owner, "settings committed creation");
        Check(await settings.EnableTotpAsync(owner) && await db.ReadAsync(c => c.Db.UserSettings.Iter().Single(s => s.UserId == owner).TotpEnabled), "settings committed enable");
        Check(await settings.DisableTotpAsync(owner) && !await db.ReadAsync(c => c.Db.UserSettings.Iter().Single(s => s.UserId == owner).TotpEnabled), "settings committed disable");
        var missing = Identity.FromHexString(new string('d', 64));
        Check(!await settings.EnableTotpAsync(missing), "settings missing failure swallowed");
        var config = new ConfigurationBuilder().Build();
        var totp = new TotpService(db, config, UrlEncoder.Default, NullLogger<TotpService>.Instance);
        const string key = "JBSWY3DPEHPK3PXP";
        var code = Code();
        Check((await totp.EnableTotpAsync(owner, code, key)).success && await totp.IsTotpEnabledAsync(owner), "TOTP enable not committed");
        Check((await totp.DisableTotpAsync(owner)).success && !await totp.IsTotpEnabledAsync(owner) &&
            await db.ReadAsync(c => c.Db.TotpSecret.Iter().Where(s => s.UserId == owner).All(s => !s.IsActive)), "TOTP disable not committed");
        Check(!(await totp.EnableTotpAsync(missing, code, key)).success, "TOTP missing failure swallowed");
        var magic = new MagicLinkService(db, new UnusedEmail(), config, NullLogger<MagicLinkService>.Instance);
        Check(!await magic.MarkMagicLinkAsUsedAsync("missing-synthetic-proof"), "magic use rejection swallowed");
        var proof = Guid.NewGuid().ToString("N");
        await db.ExecuteReducerAsync(new Reducer.CreateMagicLinkToken(owner, proof, (ulong)DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), null, null), _ => true);
        Check(await magic.MarkMagicLinkAsUsedAsync(proof) && await db.ReadAsync(c => c.Db.MagicLinkToken.Token.Find(proof)!.IsUsed), "magic use returned before commit");
        Console.WriteLine("PASS migrated security services: committed settings create/enable/disable, TOTP enable/disable readback, missing-user failures and magic-link use rejection/committed state");
    }

    public static async Task SecurityOutsiderAsync(ISpacetimeDBService db, Identity owner)
    {
        var settings = new SettingsService(db, NullLogger<SettingsService>.Instance);
        try { await settings.GetOrCreateUserSettingsAsync(owner); throw new Exception("Untrusted settings creation reported success"); }
        catch (ReducerRejectedException) { }
        var totp = new TotpService(db, new ConfigurationBuilder().Build(), UrlEncoder.Default, NullLogger<TotpService>.Instance);
        Check(!(await totp.DisableTotpAsync(owner)).success, "untrusted TOTP mutation reported success");
        Console.WriteLine("PASS migrated settings fails closed on untrusted committed rejection instead of returning 2FA-disabled defaults");
    }

    private static string Code()
    {
        // RFC 4648 decoding of the fixed synthetic key above; no production credential.
        var bytes = Convert.FromHexString("48656c6c6f21deadbeef");
        using var hmac = new HMACSHA1(bytes);
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30).Reverse().ToArray();
        var hash = hmac.ComputeHash(counter); var offset = hash[^1] & 15;
        var value = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1000000).ToString("D6");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (ReducerRejectedException) { return; }
        throw new Exception("Service reported a rejected reducer as success");
    }
    private sealed class UnusedEmail : IEmailService
    {
        public Task SendEmailAsync(string to, string subject, string body) => throw new NotSupportedException();
    }
}
