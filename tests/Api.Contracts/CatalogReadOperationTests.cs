using System.Reflection;
using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.V2;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB;
using TicketSalesApp.AdminServer.Controllers;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

internal static class CatalogReadOperationTests
{
    public static async Task RunAsync()
    {
        var actor = Identity.FromHexString(new string('7', 64));
        var rows = new (string Resource, object Row)[]
        {
            ("routes", new RouteRow(7, "A", "B", 1, 2, null, true, 3)),
            ("employees", new EmployeeRow(7, "Имя", "Фамилия", null, 1, null, 3)),
            ("maintenance", new MaintenanceRow(7, 2, null, null, "Инженер", null, null, null, null, 3)),
            ("tickets", new TicketRow(7, 1, null, 2, 10m, "Cash", null, true, 3)),
            ("schedules", new ScheduleRow(7, 1, null, null, null, null, null, 10m, 20, true, 3)),
            ("roles", new RoleRow(7, 1, "Role", "", false, 1, true, [], [], 0, 3)),
            ("permissions", new PermissionRow(7, "permission", "", "", true, 3))
        };
        var permissions = Array.Empty<string>(); var missing = false; var projectionReads = 0;
        var database = Stub.Create<ISpacetimeDBService>((method, _) =>
        {
            if (method.Name != "ReadAsync") throw new Exception("Read operation attempted a non-read database call");
            var type = method.GetGenericArguments()[0];
            if (type == typeof(UserAccessSnapshot)) return Task.FromResult(new UserAccessSnapshot(true, false, [], permissions));
            projectionReads++;
            if (missing) throw new OperationException("not_found");
            var value = rows.Single(r => r.Row.GetType() == type).Row;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null, [value])!;
        });
        var operations = new ApplicationOperations(database, Stub.Create<ITicketSalesService>(), Stub.Create<IBusService>(), NullLogger<ApplicationOperations>.Instance);
        var tokens = Stub.Create<IAuthWebSocketTokenValidator>((_, _) => Task.FromResult<Dictionary<string, object>?>(new() { ["identity"] = actor.ToString() }));
        var controller = new ApplicationOperationsController(operations, tokens) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Headers.Authorization = "Bearer synthetic-catalog-read";
        var subscribable = (System.Collections.IDictionary)typeof(ApiSocketSession).GetField("QueryResources", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (var (resource, row) in rows)
        {
            var operation = resource + ".get";
            var capability = operations.Capabilities.Single(c => c.Name == operation);
            Assert(!capability.Mutation && capability.Permission == resource + ".view" && !subscribable.Contains(operation), "single-row capability mutates/subscribes or uses wrong permission");
            var request = JsonSerializer.SerializeToElement(new EntityRequest(7), ApiProtocol.Json);
            permissions = [];
            var before = projectionReads;
            await Reject("forbidden", () => operations.ExecuteAsync(operation, actor, request, null, default));
            Assert(await controller.Execute(operation, request, default) is ObjectResult { StatusCode: 403 } && projectionReads == before, "denied read reached entity projection or HTTP permission mapping failed");
            permissions = [resource + ".view"];
            var result = await operations.ExecuteAsync(operation, actor, request, null, default);
            Assert(result?.GetType() == row.GetType() && JsonSerializer.Serialize(result, ApiProtocol.Json) == JsonSerializer.Serialize(row, ApiProtocol.Json), "single-row typed contract/revision shape changed");
            missing = true;
            await Reject("not_found", () => operations.ExecuteAsync(operation, actor, request, null, default));
            Assert(await controller.Execute(operation, request, default) is ObjectResult { StatusCode: 404 }, "missing entity did not map to HTTP 404");
            missing = false;
            permissions = [];
            await Reject("forbidden", () => operations.ExecuteAsync(operation, actor, request, null, default));
            permissions = [resource + ".view"];
            await Reject("validation_failed", () => operations.ExecuteAsync(operation, actor, JsonSerializer.SerializeToElement(new EntityRequest(0)), null, default));
        }
        Assert(operations.Capabilities.Count(c => c.Name == "jobs.get") == 1 && operations.Capabilities.Count(c => c.Name == "users.get") == 1, "existing single-row operations duplicated");
        Console.WriteLine("PASS catalog single-row reads: seven typed capabilities/permissions, per-call revocation, missing entity/HTTP 404, zero-ID validation, nonmutating and nonsubscribable; existing jobs/users reads not duplicated");
    }
    private static async Task Reject(string code, Func<Task<object?>> action)
    {
        try { await action(); throw new Exception("Expected " + code); } catch (OperationException ex) when (ex.Code == code) { }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
