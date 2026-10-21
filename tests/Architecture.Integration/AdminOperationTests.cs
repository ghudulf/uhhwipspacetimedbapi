using System.Net.Http.Headers;
using System.Text;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

internal static class AdminOperationTests
{
    public static async Task RunAsync(SpacetimeDBService db, Identity actor, string ownerToken)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        using var content = new StringContent("[]", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await http.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/prepare_admin_operations_test", content);
        response.EnsureSuccessStatusCode();
        const uint route = 3000000000;
        string Id() => Guid.NewGuid().ToString("N");
        async Task<MutationReceipt> Send<T>(T reducer, string id) where T : Reducer, IReducerArgs, new() =>
            await db.ExecuteReducerAsync(reducer, c => c.Db.MutationReceipt.Key.Find(actor + ":" + id) ?? throw new Exception("Receipt missing."));
        async Task Reject(Func<Task> action)
        {
            try { await action(); } catch (ReducerRejectedException) { return; }
            throw new Exception("Invalid or unauthorized administrative mutation committed.");
        }
        async Task Crud<TCreate, TUpdate, TDelete>(string resource, Func<string, string, TCreate> create,
            Func<string, uint, ulong?, TUpdate> update, Func<string, uint, ulong?, TDelete> delete, Func<uint, Task> legacy)
            where TCreate : Reducer, IReducerArgs, new() where TUpdate : Reducer, IReducerArgs, new() where TDelete : Reducer, IReducerArgs, new()
        {
            var operation = Id(); var label = "synthetic-" + Id();
            var concurrent = await Task.WhenAll(Send(create(operation, label), operation), Send(create(operation, label), operation));
            Check(concurrent[0].EntityId == concurrent[1].EntityId, resource + " concurrent replay returned another identity");
            var first = concurrent[0];
            await Reject(() => Send(create(operation, label + "-different"), operation));
            var edit = Id();
            var changed = await Send(update(edit, first.EntityId, first.Revision), edit);
            Check(changed.Revision > first.Revision, resource + " update did not bump revision");
            await Send(update(edit, first.EntityId, first.Revision), edit);
            await Reject(() => Send(update(Id(), first.EntityId, first.Revision), Id()));
            await legacy(first.EntityId);
            await Reject(() => Send(update(Id(), first.EntityId, changed.Revision), Id()));
            var removal = Id(); var removed = await Send(delete(removal, first.EntityId, null), removal);
            var replay = await Send(delete(removal, first.EntityId, null), removal);
            Check(removed.EntityId == replay.EntityId && removed.Revision > changed.Revision, resource + " delete/replay receipt invalid");
            Console.WriteLine("PASS " + resource + " create/update/delete actor receipts, concurrent replay, mismatch, stale revision and legacy-writer conflicts");
        }
        await Crud<Reducer.CreateTicketV2, Reducer.UpdateTicketV2, Reducer.DeleteTicketV2>("tickets",
            (id, label) => new(id, actor, route, 12.5, 123, label, null),
            (id, entity, version) => new(id, actor, entity, version, null, null, 20, null, null),
            (id, entity, version) => new(id, actor, entity, version),
            entity => db.ExecuteReducerAsync(new Reducer.UpdateTicket(entity, null, null, 21, null, null, actor), _ => true));
        await Crud<Reducer.CreateScheduleV2, Reducer.UpdateScheduleV2, Reducer.DeleteScheduleV2>("schedules",
            (id, label) => new(id, actor, route, 1000, 12.5, 10, null, null, null, null, 2000, null, null, null, null, label, null),
            (id, entity, version) => new(id, actor, entity, version, null, null, null, null, null, null, 20, null, null, null, null, null, null, null, "edited"),
            (id, entity, version) => new(id, actor, entity, version),
            entity => db.ExecuteReducerAsync(new Reducer.UpdateRouteSchedule(entity, null, null, null, null, null, null, 21, null, null, null, null, null, null, null, "legacy", actor), _ => true));
        await Crud<Reducer.CreateUserV2, Reducer.UpdateUserV2, Reducer.DeleteUserV2>("users",
            (id, label) => new(id, actor, label, "synthetic-initial-credential", "", "", null),
            (id, entity, version) => new(id, actor, entity, version, "updated@example.invalid", null),
            (id, entity, version) => new(id, actor, entity, version),
            async entity => { var identity = await db.ReadAsync(c => c.Db.UserProfile.LegacyUserId.Find(entity)!.UserId); await db.ExecuteReducerAsync(new Reducer.UpdateUser(identity, null, null, null, null, "legacy@example.invalid", null, actor), _ => true); });
        await Crud<Reducer.CreateRoleV2, Reducer.UpdateRoleV2, Reducer.DeleteRoleV2>("roles",
            (id, label) => new(id, actor, 0, label, "", false, 0, null),
            (id, entity, version) => new(id, actor, entity, version, null, "updated", null, null, null),
            (id, entity, version) => new(id, actor, entity, version),
            entity => db.ExecuteReducerAsync(new Reducer.UpdateRole(entity, null, "legacy", null, null, actor), _ => true));
        await Crud<Reducer.CreatePermissionV2, Reducer.UpdatePermissionV2, Reducer.DeletePermissionV2>("permissions",
            (id, label) => new(id, actor, label, "", "Synthetic"),
            (id, entity, version) => new(id, actor, entity, version, null, "updated", null, null),
            (id, entity, version) => new(id, actor, entity, version),
            entity => db.ExecuteReducerAsync(new Reducer.UpdatePermission(entity, null, "legacy", null, null, actor), _ => true));

        var userOp = Id(); var user = await Send(new Reducer.CreateUserV2(userOp, actor, "synthetic-limited-" + Id(), "synthetic-initial-credential", "", "", null), userOp);
        var userIdentity = await db.ReadAsync(c => c.Db.UserProfile.LegacyUserId.Find(user.EntityId)!.UserId);
        var roleOp = Id(); var role = await Send(new Reducer.CreateRoleV2(roleOp, actor, 0, "synthetic-limited-" + Id(), "", false, 0, null), roleOp);
        var accessBefore = db.Changes.Current;
        foreach (var permission in new[] { "assign_roles", "grant_permissions", "roles.create" })
        {
            var permissionId = await db.ReadAsync(c => c.Db.Permission.Iter().Single(p => p.Name == permission).PermissionId);
            var id = Id(); await Send(new Reducer.GrantRolePermissionV2(id, actor, role.EntityId, null, permissionId), id);
        }
        var assign = Id(); await Send(new Reducer.AssignUserRoleV2(assign, actor, user.EntityId, null, role.EntityId), assign);
        var accessChanges = db.Changes.Read(accessBefore).Changes.Select(c => c.Resource).ToHashSet();
        Check(new[] { "users", "roles", "permissions", "access" }.All(accessChanges.Contains), "role/user/permission/access change journal mapping incomplete");
        await Reject(() => db.ExecuteReducerAsync(new Reducer.DeleteRoleV2(Id(), actor, role.EntityId, null), _ => true));
        var adminId = await db.ReadAsync(c => c.Db.Role.Iter().Single(r => r.Name == "Administrator").RoleId);
        var elevated = await db.ReadAsync(c => c.Db.Permission.Iter().Single(p => p.Name == "users.delete").PermissionId);
        await Reject(() => db.ExecuteReducerAsync(new Reducer.AssignUserRoleV2(Id(), userIdentity, user.EntityId, null, adminId), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.GrantRolePermissionV2(Id(), userIdentity, role.EntityId, null, elevated), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.UpdateTicketV2(Id(), userIdentity, 1, null, null, null, 1, null, null), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.UpdateScheduleV2(Id(), userIdentity, 1, null, null, null, null, null, null, null, 1, null, null, null, null, null, null, null, null), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.UpdateUserV2(Id(), userIdentity, user.EntityId, null, "x@example.invalid", null), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.UpdateRoleV2(Id(), userIdentity, role.EntityId, null, null, "x", null, null, null), _ => true));
        var compoundOp = Id();
        await Reject(() => db.ExecuteReducerAsync(new Reducer.CreateRoleV2(compoundOp, userIdentity, 0, "synthetic-compound-denied-" + compoundOp, "", false, 0, new() { elevated }), _ => true));
        Check(!await db.ReadAsync(c => c.Db.Role.Iter().Any(r => r.Name == "synthetic-compound-denied-" + compoundOp)), "composite privilege denial left a partial role");
        var scopedId = Id();
        var ownerScoped = await Send(new Reducer.CreateRoleV2(scopedId, actor, 0, "synthetic-owner-scoped-" + scopedId, "", false, 0, null), scopedId);
        var userScoped = await db.ExecuteReducerAsync(new Reducer.CreateRoleV2(scopedId, userIdentity, 0, "synthetic-user-scoped-" + scopedId, "", false, 0, null), c => c.Db.MutationReceipt.Key.Find(userIdentity + ":" + scopedId)!);
        Check(userScoped.Actor == userIdentity && ownerScoped.Actor == actor && userScoped.EntityId != ownerScoped.EntityId, "receipt key was not actor-scoped");
        foreach (var scopedRole in new[] { ownerScoped.EntityId, userScoped.EntityId })
        { var cleanup = Id(); await Send(new Reducer.DeleteRoleV2(cleanup, actor, scopedRole, null), cleanup); }
        await Reject(() => db.ExecuteReducerAsync(new Reducer.CreatePermissionV2(Id(), userIdentity, "synthetic-denied-" + Id(), "", ""), _ => true));
        var ownerLegacy = await db.ReadAsync(c => c.Db.UserProfile.UserId.Find(actor)!.LegacyUserId);
        await Reject(() => db.ExecuteReducerAsync(new Reducer.SetUserActiveV2(Id(), actor, ownerLegacy, null, false), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.DeleteUserV2(Id(), actor, ownerLegacy, null), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RemoveUserRoleV2(Id(), actor, ownerLegacy, null, adminId), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RevokeRolePermissionV2(Id(), actor, adminId, null, elevated), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.UpdateRoleV2(Id(), actor, adminId, null, "renamed-admin", null, null, null, null), _ => true));
        var systemOp = Id(); var system = await Send(new Reducer.CreateRoleV2(systemOp, actor, 0, "synthetic-system-" + Id(), "", true, 0, null), systemOp);
        await Reject(() => db.ExecuteReducerAsync(new Reducer.DeleteRoleV2(Id(), actor, system.EntityId, null), _ => true));
        var scheduleOp = Id(); var schedule = await Send(new Reducer.CreateScheduleV2(scheduleOp, actor, route, 1000, 0, 10, null, null, null, null, 2000, null, null, null, null, null, null), scheduleOp);
        await Reject(() => db.ExecuteReducerAsync(new Reducer.DeleteRoute(route, actor), _ => true));
        var ticketOp = Id(); var ticket = await Send(new Reducer.CreateTicketV2(ticketOp, actor, route, 10, (uint)Random.Shared.Next(1000, int.MaxValue), "Cash", null), ticketOp);
        var saleOp = Id(); var sale = await Send(new Reducer.CreateSaleV2(saleOp, actor, ticket.EntityId, "Synthetic buyer", "", null, null), saleOp);
        await Reject(() => db.ExecuteReducerAsync(new Reducer.DeleteTicketV2(Id(), actor, ticket.EntityId, null), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.SetScheduleActiveV2(Id(), actor, schedule.EntityId, null, false), _ => true));
        Check(await db.ReadAsync(c => c.Db.UserProfile.Iter().All(u => string.IsNullOrEmpty(u.PasswordHash))), "administration exposed credentials in profiles");
        var operations = new ApplicationOperations(db, new TicketSalesService(db, NullLogger<TicketSalesService>.Instance),
            new BusService(db, NullLogger<BusService>.Instance), NullLogger<ApplicationOperations>.Instance);
        foreach (var query in new[] { "tickets.list", "schedules.list", "users.list", "roles.list", "permissions.list" })
        {
            var projected = await operations.ExecuteAsync(query, actor, JsonSerializer.SerializeToElement(new PageRequest()), null, default);
            var serialized = JsonSerializer.Serialize(projected, ApiProtocol.Json);
            Check(!new[] { "PasswordHash", "Password", "Secret", "Token" }.Any(term => serialized.Contains('"' + term + '"', StringComparison.OrdinalIgnoreCase)), "administrative query exposed a credential member");
        }
        var detail = (UserRow)(await operations.ExecuteAsync("users.get", actor, JsonSerializer.SerializeToElement(new EntityRequest(user.EntityId)), null, default))!;
        Check(detail.Identity == userIdentity.ToString() && detail.Roles.Length > 0, "real administrative user projection lost identity/roles");
        Console.WriteLine("PASS real five-resource paged projections, credential-free users.get and role/permission/user changes invalidate access resources");
        var remove = Id(); await Send(new Reducer.RemoveUserRoleV2(remove, actor, user.EntityId, null, role.EntityId), remove);
        var deleteSale = Id(); await Send(new Reducer.DeleteSaleV2(deleteSale, actor, sale.EntityId, null), deleteSale);
        var deleteTicket = Id(); await Send(new Reducer.DeleteTicketV2(deleteTicket, actor, ticket.EntityId, null), deleteTicket);
        var deleteSchedule = Id(); await Send(new Reducer.DeleteScheduleV2(deleteSchedule, actor, schedule.EntityId, null), deleteSchedule);
        var deleteRole = Id(); await Send(new Reducer.DeleteRoleV2(deleteRole, actor, role.EntityId, null), deleteRole);
        var deleteUser = Id(); await Send(new Reducer.DeleteUserV2(deleteUser, actor, user.EntityId, null), deleteUser);
        Console.WriteLine("PASS admin integrity: assigned/system roles, Administrator invariants, self-deactivation/deletion, route/sale references, recent-sale schedule semantics, permission denials and privilege escalation refusals");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
