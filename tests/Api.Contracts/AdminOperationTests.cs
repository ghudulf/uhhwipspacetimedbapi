using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.V2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.AdminServer.Controllers;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class AdminOperationTests
{
    public static async Task RunAsync()
    {
        var actor = Identity.FromHexString(new string('3', 64));
        var allowed = true; var submissions = 0; var lookupReads = 0;
        string[]? limitedPermissions = null;
        object? lastReducer = null;
        var journal = new DatabaseChangeJournal();
        var operationsByReducer = new Dictionary<Type, string>
        {
            [typeof(Reducer.CreateTicketV2)] = "tickets.create", [typeof(Reducer.UpdateTicketV2)] = "tickets.update", [typeof(Reducer.DeleteTicketV2)] = "tickets.delete",
            [typeof(Reducer.CreateScheduleV2)] = "schedules.create", [typeof(Reducer.UpdateScheduleV2)] = "schedules.update", [typeof(Reducer.DeleteScheduleV2)] = "schedules.delete", [typeof(Reducer.SetScheduleActiveV2)] = "schedules.set-active",
            [typeof(Reducer.CreateUserV2)] = "users.create", [typeof(Reducer.UpdateUserV2)] = "users.update", [typeof(Reducer.DeleteUserV2)] = "users.delete", [typeof(Reducer.SetUserActiveV2)] = "users.set-active", [typeof(Reducer.AssignUserRoleV2)] = "users.assign-role", [typeof(Reducer.RemoveUserRoleV2)] = "users.remove-role",
            [typeof(Reducer.CreateRoleV2)] = "roles.create", [typeof(Reducer.UpdateRoleV2)] = "roles.update", [typeof(Reducer.DeleteRoleV2)] = "roles.delete", [typeof(Reducer.GrantRolePermissionV2)] = "roles.grant-permission", [typeof(Reducer.RevokeRolePermissionV2)] = "roles.revoke-permission",
            [typeof(Reducer.CreatePermissionV2)] = "permissions.create", [typeof(Reducer.UpdatePermissionV2)] = "permissions.update", [typeof(Reducer.DeletePermissionV2)] = "permissions.delete"
        };
        var permissions = new[] { "tickets", "schedules", "users", "roles", "permissions" }.SelectMany(r => new[] { "view", "create", "edit", "delete" }.Select(a => r + "." + a)).Concat(["assign_roles", "grant_permissions"]).ToArray();
        object Completed(Type type, object? value) => typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null, [value])!;
        var user = new UserProfile { UserId = actor, LegacyUserId = 7, Login = "synthetic", IsActive = true };
        var database = Stub.Create<ISpacetimeDBService>((method, args) =>
        {
            if (method.Name == "get_State") return DatabaseConnectionState.Ready;
            if (method.Name == "get_Changes") return journal;
            if (method.Name == "ExecuteReducerAsync")
            {
                submissions++;
                var reducer = args![0]!; var type = reducer.GetType();
                lastReducer = reducer;
                Assert((Identity)type.GetField("ActingUser")!.GetValue(reducer)! == actor, "administrative transport lost actor");
                var id = (string)type.GetField("OperationId")!.GetValue(reducer)!;
                var receipt = new MutationReceipt { Operation = operationsByReducer[type], EntityId = 7, Revision = 2, CommittedAt = 1000, Key = actor + ":" + id };
                return Task.FromResult((receipt, receipt));
            }
            if (method.Name == "ReadAsync")
            {
                var type = method.GetGenericArguments()[0];
                if (type == typeof(UserAccessSnapshot)) return Task.FromResult(new UserAccessSnapshot(true, false, [], allowed ? limitedPermissions ?? permissions : []));
                if (type == typeof(Ticket)) return Task.FromResult<Ticket?>(new Ticket { TicketId = 7, RouteId = 1, PaymentMethod = "Cash", PurchaseTime = 777 });
                if (type == typeof(RouteSchedule)) return Task.FromResult<RouteSchedule?>(new RouteSchedule { ScheduleId = 7, RouteId = 1, DepartureTime = 1000, ArrivalTime = 2000 });
                if (type == typeof(UserProfile)) return Task.FromResult<UserProfile?>(user);
                if (type == typeof(Role)) return Task.FromResult<Role?>(new Role { RoleId = 7, Name = "synthetic", Description = "" });
                if (type == typeof(Permission)) return Task.FromResult<Permission?>(new Permission { PermissionId = 7, Name = "synthetic", Description = "", Category = "" });
                if (type == typeof(UserRow)) return Task.FromResult(new UserRow(7, actor.ToString(), "synthetic", null, null, true, null, null, [], 0));
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CatalogPageResult<>))
                    return Completed(type, Activator.CreateInstance(type, Array.CreateInstance(type.GetGenericArguments()[0], 0), 1, 50, 0, false, false, false, null, null));
                if (type == typeof(AdminLookupSnapshot))
                { lookupReads++; return Task.FromResult(new AdminLookupSnapshot(new Dictionary<uint, TicketLookup>(), new Dictionary<uint, RouteLookup>(),
                    new Dictionary<uint, RoleLookup>(), new Dictionary<uint, PermissionLookup>(), [], [], [])); }
            }
            throw new Exception("Unexpected administrative dependency invocation: " + method.Name);
        });
        var ops = new ApplicationOperations(database, Stub.Create<ITicketSalesService>(), Stub.Create<IBusService>(), NullLogger<ApplicationOperations>.Instance);
        var valid = new (string Name, string Permission, object Payload)[]
        {
            ("tickets.create", "tickets.create", new CreateTicketCommand(1, 10, 1, "Cash")), ("tickets.update", "tickets.edit", new UpdateTicketCommand(7, TicketPrice: 20)), ("tickets.delete", "tickets.delete", new DeleteTicketCommand(7)),
            ("schedules.create", "schedules.create", new CreateScheduleCommand(1, 1000, 10, 10)), ("schedules.update", "schedules.edit", new UpdateScheduleCommand(7, Price: 20)), ("schedules.delete", "schedules.delete", new DeleteScheduleCommand(7)), ("schedules.set-active", "schedules.edit", new SetScheduleActiveCommand(7, false)),
            ("users.create", "users.create", new CreateUserCommand("synthetic", "synthetic-credential")), ("users.update", "users.edit", new UpdateUserCommand(7, Email: "test@example.invalid")), ("users.delete", "users.delete", new DeleteUserCommand(7)), ("users.set-active", "users.edit", new SetUserActiveCommand(7, false)),
            ("users.assign-role", "assign_roles", new AssignUserRoleCommand(7, 1)), ("users.remove-role", "assign_roles", new RemoveUserRoleCommand(7, 1)),
            ("roles.create", "roles.create", new CreateRoleCommand("synthetic")), ("roles.update", "roles.edit", new UpdateRoleCommand(7, Description: "edited")), ("roles.delete", "roles.delete", new DeleteRoleCommand(7)), ("roles.grant-permission", "grant_permissions", new GrantRolePermissionCommand(7, 1)), ("roles.revoke-permission", "grant_permissions", new RevokeRolePermissionCommand(7, 1)),
            ("permissions.create", "permissions.create", new CreatePermissionCommand("synthetic")), ("permissions.update", "permissions.edit", new UpdatePermissionCommand(7, Description: "edited")), ("permissions.delete", "permissions.delete", new DeletePermissionCommand(7))
        };
        foreach (var (name, permission, payload) in valid)
        {
            Assert(ops.Capabilities.Single(c => c.Name == name) is { Mutation: true } capability && capability.Permission == permission, "administrative capability descriptor incorrect");
            var id = Guid.NewGuid().ToString("N");
            var result = (MutationResult)(await ops.ExecuteAsync(name, actor, JsonSerializer.SerializeToElement(payload, ApiProtocol.Json), id, default))!;
            Assert(result.EntityId == 7 && result.Operation == name && result.OperationId == id, "administrative committed identity/receipt mapping incorrect");
            Assert(!JsonSerializer.Serialize(result, ApiProtocol.Json).Contains("synthetic-credential"), "credential echoed in receipt");
        }
        var before = submissions;
        var invalid = new (string, object)[]
        {
            ("tickets.create", new CreateTicketCommand(0, 10, 1, "Cash")), ("tickets.create", new CreateTicketCommand(1, -1, 1, "Cash")), ("tickets.create", new CreateTicketCommand(1, 1e12 + 1, 1, "Cash")), ("tickets.update", new UpdateTicketCommand(1)),
            ("schedules.create", new CreateScheduleCommand(1, 2000, 1, 10, ArrivalTime: 1000)), ("schedules.create", new CreateScheduleCommand(1, 1000, 1, 10, RouteStops: ["A"], StopDistances: [1, 2])), ("schedules.update", new UpdateScheduleCommand(1)),
            ("schedules.create", new CreateScheduleCommand(1, 1000, 1, 10, DaysOfWeek: ["Funday"])), ("schedules.update", new UpdateScheduleCommand(1, DaysOfWeek: ["Monday", "monday"])),
            ("schedules.create", new CreateScheduleCommand(1, 1000, 1, 10, DaysOfWeek: ["1"])),
            ("schedules.update", new UpdateScheduleCommand(1, EstimatedStopTimes: ["25:99"])), ("schedules.update", new UpdateScheduleCommand(1, StopDurationMinutes: 1441)),
            ("users.create", new CreateUserCommand("", "synthetic")), ("users.create", new CreateUserCommand("synthetic", "short")), ("users.update", new UpdateUserCommand(7)), ("users.assign-role", new AssignUserRoleCommand(7, 0)),
            ("roles.create", new CreateRoleCommand(" ")), ("roles.update", new UpdateRoleCommand(7)), ("roles.grant-permission", new GrantRolePermissionCommand(7, 0)), ("roles.create", new CreateRoleCommand("synthetic", PermissionIds: [1, 1])),
            ("permissions.create", new CreatePermissionCommand(" ")), ("permissions.update", new UpdatePermissionCommand(7)), ("permissions.delete", new DeletePermissionCommand(0))
        };
        foreach (var (operation, payload) in invalid)
            await Reject("validation_failed", () => ops.ExecuteAsync(operation, actor, JsonSerializer.SerializeToElement(payload, ApiProtocol.Json), Guid.NewGuid().ToString("N"), default));
        allowed = false;
        foreach (var (operation, _, payload) in valid)
            await Reject("forbidden", () => ops.ExecuteAsync(operation, actor, JsonSerializer.SerializeToElement(payload, ApiProtocol.Json), Guid.NewGuid().ToString("N"), default));
        allowed = true;
        Assert(submissions == before, "invalid/forbidden administration reached persistence");
        var contractTypes = valid.Select(x => x.Payload.GetType()).Concat([typeof(TicketRow), typeof(ScheduleRow), typeof(UserRow), typeof(RoleRow), typeof(PermissionRow)]);
        foreach (var type in contractTypes)
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance).Where(m => m is PropertyInfo or FieldInfo))
                Assert(!new[] { "Password", "Secret", "Hash", "Token" }.Any(term => member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)), "administrative contract exposes credential-shaped member");
        Console.WriteLine("PASS 21 administrative capabilities/committed-identity results, validation matrix, permission denial before persistence and credential-free contract reflection");

        using var services = new ServiceCollection().AddSingleton(database).AddSingleton(ops).AddSingleton<UserAccessService>().BuildServiceProvider();
        var controller = new TicketsController(database, NullLogger<TicketsController>.Instance, Stub.Create<ITicketService>(), Stub.Create<IAuthenticationService>(), Stub.Create<IRealtimeEventBus>())
        { ControllerContext = new() { HttpContext = new DefaultHttpContext { RequestServices = services, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("identity", actor.ToString())], "synthetic")) } } };
        var key = Guid.NewGuid().ToString("N"); controller.Request.Headers["Idempotency-Key"] = key;
        Assert(await controller.UpdateTicket(7, new UpdateTicketModel { TicketPrice = 20 }) is NoContentResult && controller.Response.Headers["Operation-Id"] == key, "ticket HTTP adapter did not use shared receipt path");
        try { await controller.UpdateTicket(7, new UpdateTicketModel { TicketPrice = -1 }); throw new Exception("invalid HTTP ticket accepted"); }
        catch (OperationException ex)
        {
            var context = new Microsoft.AspNetCore.Mvc.Filters.ExceptionContext(new ActionContext(controller.HttpContext, new(), new()), []) { Exception = ex };
            new ApplicationErrorFilterAttribute().OnException(context); Assert(context.Result is ObjectResult { StatusCode: 400 }, "administrative HTTP error filter mismatch");
        }
        var realtime = typeof(TicketsController).GetMethod("HandleUpdateCommandAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var request = new BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure.RealtimeCrudRequest("update", key, 7, JsonSerializer.SerializeToElement(new UpdateTicketModel { TicketPrice = 20 }));
        await (Task<object>)realtime.Invoke(controller, [request])!;
        Console.WriteLine("PASS ticket HTTP and per-resource v1 adapters share validation/receipt handling, echo Operation-Id and retain their response envelopes");

        var events = Stub.Create<IRealtimeEventBus>((_, _) => ValueTask.CompletedTask);
        var audit = Stub.Create<IAdminActionLogger>((_, _) => Task.CompletedTask);
        var roleService = Stub.Create<IRoleService>((method, _) => method.Name switch
        {
            "GetRoleByIdAsync" => Task.FromResult<Role?>(new Role { RoleId = 7, Name = "synthetic", Description = "" }),
            "GetAllRolesAsync" => Task.FromResult<IEnumerable<Role>>([]),
            _ => throw new Exception("Legacy role mutation reached persistence instead of shared handler.")
        });
        var userService = Stub.Create<IUserService>((method, _) => method.Name switch
        {
            "GetUserByIdAsync" => Task.FromResult<UserProfile?>(user), "GetAllUsersAsync" => Task.FromResult<IEnumerable<UserProfile>>([]),
            _ => throw new Exception("Legacy user mutation reached persistence instead of shared handler.")
        });
        var permissionService = Stub.Create<IPermissionService>((method, _) => method.Name switch
        {
            "GetPermissionByIdAsync" => Task.FromResult<Permission?>(new Permission { PermissionId = 7, Name = "synthetic", Description = "", Category = "" }),
            "GetAllPermissionsAsync" => Task.FromResult<IEnumerable<Permission>>([]),
            _ => throw new Exception("Legacy permission mutation reached persistence instead of shared handler.")
        });
        var schedules = Stub.Create<IRouteScheduleService>((method, _) => method.Name == "GetScheduleByIdAsync"
            ? Task.FromResult<RouteSchedule?>(new RouteSchedule { ScheduleId = 7, RouteId = 1 }) : throw new Exception("Legacy schedule mutation reached persistence instead of shared handler."));
        var configuration = new ConfigurationBuilder().Build();
        var adapters = new (Controller Controller, Func<Task<IActionResult>> Update, object Model)[]
        {
            Adapter(new UsersController(userService, Stub.Create<IAuthenticationService>(), roleService, configuration, database, events, NullLogger<UsersController>.Instance), c => c.UpdateUser(7, new UpdateUserModel { Email = "test@example.invalid" }), new UpdateUserModel { Email = "test@example.invalid" }),
            Adapter(new RolesController(roleService, audit, NullLogger<RolesController>.Instance, database, events), c => c.UpdateRole(7, new UpdateRoleModel { Description = "edited" }), new UpdateRoleModel { Description = "edited" }),
            Adapter(new PermissionsController(permissionService, audit, NullLogger<PermissionsController>.Instance, events), c => c.UpdatePermission(7, new UpdatePermissionModel { Description = "edited" }), new UpdatePermissionModel { Description = "edited" }),
            Adapter(new RouteSchedulesController(schedules, NullLogger<RouteSchedulesController>.Instance, events, configuration), c => c.UpdateRouteSchedule(7, new UpdateRouteScheduleModel { Price = 20 }), new UpdateRouteScheduleModel { Price = 20 })
        };
        foreach (var (adapter, update, model) in adapters)
        {
            var context = new DefaultHttpContext { RequestServices = services, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("identity", actor.ToString()), new Claim("sub", "7")], "synthetic")) };
            var operation = Guid.NewGuid().ToString("N"); context.Request.Headers["Idempotency-Key"] = operation;
            adapter.ControllerContext = new() { HttpContext = context };
            Assert(await update() is NoContentResult && context.Response.Headers["Operation-Id"] == operation, "administrative HTTP adapter failed shared path");
            var wsMethod = adapter.GetType().GetMethod("HandleUpdateCommandAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task<object>)wsMethod.Invoke(adapter, [new BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure.RealtimeCrudRequest("update", operation, 7, JsonSerializer.SerializeToElement(model))])!;
        }
        Console.WriteLine("PASS users/roles/permissions/schedules HTTP and v1 mutations call shared handlers, preserve envelopes and never invoke legacy mutation services");
        limitedPermissions = ["tickets.edit"];
        var readsBefore = lookupReads;
        var mutationOnly = await (Task<object>)realtime.Invoke(controller, [request])!;
        Assert(lookupReads == readsBefore && !JsonSerializer.Serialize(mutationOnly).Contains("snapshot", StringComparison.OrdinalIgnoreCase), "mutation-only ticket v1 command fetched an unauthorized snapshot");
        limitedPermissions = null;
        Console.WriteLine("PASS mutation-only ticket v1 command commits without reading or returning an unauthorized snapshot");
        var generic = new BRU_AVTOPARK_AspireAPI.ApiService.Controllers.RealtimeController(events,
            NullLogger<BRU_AVTOPARK_AspireAPI.ApiService.Controllers.RealtimeController>.Instance, services) { ControllerContext = controller.ControllerContext };
        var genericCreate = generic.GetType().GetMethod("ExecuteCreateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var numericSchedule = JsonSerializer.SerializeToElement(new { routeId = 1, departureTime = 1000UL, arrivalTime = 2000UL, price = 10, availableSeats = 10, routeStops = new[] { "A", "B" } });
        var createdSchedule = await (Task<object?>)genericCreate.Invoke(generic, [new object(), "route-schedules", numericSchedule, null, actor, Guid.NewGuid().ToString("N"), CancellationToken.None])!;
        Assert(createdSchedule is RouteSchedule { ScheduleId: 7 } && lastReducer is Reducer.CreateScheduleV2 { DepartureTime: 1000, ArrivalTime: 2000 }, "generic v1 schedule lost numeric times or its row-shaped create response");
        var genericUpdate = generic.GetType().GetMethod("ExecuteUpdateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task<object?>)genericUpdate.Invoke(generic, [schedules, "route-schedules", 7u, numericSchedule, null, actor, Guid.NewGuid().ToString("N"), CancellationToken.None])!;
        Assert(lastReducer is Reducer.UpdateScheduleV2 { DepartureTime: 1000, ArrivalTime: 2000 }, "generic v1 schedule update changed numeric-time payload semantics");
        var oldTicket = JsonSerializer.SerializeToElement(new { routeId = 1, seatNumber = 2, paymentMethod = "Cash", purchaseTime = 777UL });
        var createdTicket = await (Task<object?>)genericCreate.Invoke(generic, [new object(), "tickets", oldTicket, null, actor, Guid.NewGuid().ToString("N"), CancellationToken.None])!;
        var ticketShape = JsonSerializer.SerializeToElement(createdTicket);
        Assert(lastReducer is Reducer.CreateTicketV2 { Price: 0, PurchaseTime: 777 } && ticketShape.GetProperty("purchaseTime").GetUInt64() == 777,
            "generic v1 ticket lost supplied purchase time or its optional zero-price default");
        Console.WriteLine("PASS generic v1 schedule numeric timestamps/row responses and ticket purchaseTime/optional-price payload compatibility");

        var tokenValidator = Stub.Create<IAuthWebSocketTokenValidator>((_, _) => Task.FromResult<Dictionary<string, object>?>(new() { ["identity"] = actor.ToString() }));
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(database); builder.Services.AddSingleton(ops); builder.Services.AddSingleton(tokenValidator);
        builder.Services.AddSingleton(Stub.Create<IAuthOrchestrationService>()); builder.Services.AddSingleton<ConnectionAdmission>(); builder.Services.AddScoped<ApiSocketSession>();
        await using var app = builder.Build(); app.UseWebSockets();
        app.MapGet("/api/realtime/stream", async context => { using var socket = await context.WebSockets.AcceptWebSocketAsync(ApiProtocol.Subprotocol); await context.RequestServices.GetRequiredService<ApiSocketSession>().RunAsync(socket, "admin-tests", context.RequestAborted); });
        await app.StartAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            await using var client = new RealtimeApplicationClient(); await client.ConnectAsync(new Uri(origin + "/api/"), "synthetic-token", deadline.Token);
            foreach (var (operation, _, payload) in valid)
                Assert((await client.CallAsync<object, MutationResult>(operation, payload, Guid.NewGuid().ToString("N"), deadline.Token))?.Operation == operation, "v2 administrative dispatch mismatch");
            foreach (var (operation, resource) in new[] { ("tickets.list", "tickets"), ("schedules.list", "schedules"), ("users.list", "roles"), ("users.get", "users"), ("roles.list", "permissions"), ("permissions.list", "access") })
            {
                object payload = operation == "users.get" ? new EntityRequest(7) : new PageRequest();
                await using var updates = client.WatchAsync<object, JsonElement>(operation, payload, deadline.Token).GetAsyncEnumerator(deadline.Token);
                Assert(await updates.MoveNextAsync() && updates.Current.IsSynchronized, "administrative subscription did not open");
                journal.Publish(resource);
                Assert(await updates.MoveNextAsync() && !updates.Current.IsSynchronized && updates.Current.Reason == "changed", "administrative dependency/access change did not invalidate");
            }
            Console.WriteLine("PASS v2 administrative mutations and all six query subscriptions open/invalidate on resource, role, permission and access dependencies");
        }
        finally { await app.StopAsync(); }
    }
    private static (Controller, Func<Task<IActionResult>>, object) Adapter<T>(T controller, Func<T, Task<IActionResult>> update, object model) where T : Controller => (controller, () => update(controller), model);
    private static async Task Reject(string code, Func<Task<object?>> action) { try { await action(); } catch (OperationException ex) when (ex.Code == code) { return; } throw new Exception("Expected administrative rejection: " + code); }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
