using System.Security.Claims;
using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.V2;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB;
using TicketSalesApp.AdminServer.Controllers;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class DriverLookupOperationTests
{
    public static async Task RunAsync()
    {
        var actor = Identity.FromHexString(new string('5', 64)); var allowed = true; var reads = 0;
        var row = new DriverRow(7, "Имя", "Фамилия", null, 2, "Driver", "Стаж", 3);
        var database = Stub.Create<ISpacetimeDBService>((method, _) =>
        {
            if (method.Name == "ReadAsync" && method.GetGenericArguments()[0] == typeof(UserAccessSnapshot))
                return Task.FromResult(new UserAccessSnapshot(true, false, [], allowed ? ["employees.view"] : []));
            if (method.Name == "ReadAsync" && method.GetGenericArguments()[0] == typeof(DriverLookupResult)) { reads++; return Task.FromResult(new DriverLookupResult([row], false)); }
            throw new Exception("Unexpected driver lookup dependency");
        });
        var operations = new ApplicationOperations(database, Stub.Create<ITicketSalesService>(), Stub.Create<IBusService>(), NullLogger<ApplicationOperations>.Instance);
        var capability = operations.Capabilities.Single(c => c.Name == "employees.drivers");
        Check(!capability.Mutation && capability.Permission == "employees.view", "driver capability requires wrong permission or mutates");
        var resources = (Dictionary<string, string[]>)typeof(ApiSocketSession).GetField("QueryResources", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        Check(resources["employees.drivers"].ToHashSet().SetEquals(["employees", "jobs", "access"]), "driver subscription resources incomplete");
        Check(resources["routes.list"].Contains("tickets"), "active-ticket count changes do not invalidate route snapshots");
        var payload = JsonSerializer.SerializeToElement(new DriverLookupRequest(), ApiProtocol.Json);
        Check((await operations.ExecuteAsync("employees.drivers", actor, payload, null, default)) is DriverLookupResult { Items.Length: 1 }, "employees.view-only driver lookup failed");
        using var provider = new ServiceCollection().AddSingleton(database).AddSingleton(operations).AddSingleton<UserAccessService>().BuildServiceProvider();
        var controller = new EmployeesController(Stub.Create<IEmployeeService>(), NullLogger<EmployeesController>.Instance, Stub.Create<IAdminActionLogger>(), database, Stub.Create<IRealtimeEventBus>(), new CatalogLookupService(database))
        { ControllerContext = new() { HttpContext = new DefaultHttpContext { RequestServices = provider, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("identity", actor.ToString())], "synthetic")) } } };
        var legacy = (OkObjectResult)(await controller.GetDrivers()).Result!;
        var legacyRows = JsonSerializer.SerializeToElement(legacy.Value, ApiProtocol.Json);
        Check(legacyRows[0].GetProperty("employeeId").GetUInt32() == row.EmployeeId && legacyRows[0].GetProperty("job").GetProperty("jobTitle").GetString() == row.JobTitle && legacyRows[0].GetProperty("job").GetProperty("internship").GetString() == row.Internship, "legacy driver adapter differs from shared lookup");
        var before = reads; allowed = false;
        await Reject("forbidden", payload); Check(reads == before, "forbidden driver lookup reached projection");
        allowed = true;
        foreach (var limit in new[] { 0, 1001 }) await Reject("validation_failed", JsonSerializer.SerializeToElement(new DriverLookupRequest(limit), ApiProtocol.Json));
        async Task Reject(string code, JsonElement request)
        { try { await operations.ExecuteAsync("employees.drivers", actor, request, null, default); throw new Exception("Expected " + code); } catch (OperationException ex) when (ex.Code == code) { } }
        Console.WriteLine("PASS typed driver lookup capability, employees.view-only permission, denial/limit validation, employee/job/access subscription resources and shared legacy endpoint envelope parity");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
