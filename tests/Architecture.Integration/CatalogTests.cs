using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class CatalogTests
{
    public static async Task RunAsync(SpacetimeDBService database, Identity owner, string ownerToken)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        using var body = new StringContent("[]", Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var prepared = await http.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/prepare_fleet_test", body);
        prepared.EnsureSuccessStatusCode();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await database.ReadAsync(c => c.Db.Permission.Iter().Any(p => p.Name == "jobs.create"), deadline.Token)) await Task.Delay(10, deadline.Token);
        var employees = new EmployeeService(database, NullLogger<EmployeeService>.Instance);
        var routes = new RouteService(database, NullLogger<RouteService>.Instance);
        var maintenance = new MaintenanceService(database, NullLogger<MaintenanceService>.Instance);
        var buses = new BusService(database, NullLogger<BusService>.Instance);
        var jobId = Guid.NewGuid().ToString("N");
        var job = (await employees.CreateJobRecordAsync("synthetic-" + jobId, "", owner, jobId, deadline.Token))!;
        Assert((await employees.CreateJobRecordAsync(job.JobTitle, "", owner, jobId, deadline.Token))?.JobId == job.JobId, "job replay duplicated entity");
        await Reject(() => employees.CreateJobAsync(job.JobTitle, "", owner));
        var sameName = await Task.WhenAll(employees.CreateEmployeeAsync("Synthetic", "Shared", "", job.JobId, owner), employees.CreateEmployeeAsync("Synthetic", "Shared", "", job.JobId, owner));
        Assert(sameName[0]!.EmployeeId != sameName[1]!.EmployeeId, "same-name creation confused committed identities");
        var employee = sameName[0]!;
        var bus = (await buses.CreateBusAsync("catalog-synthetic", actingUser: owner))!;
        var createId = Guid.NewGuid().ToString("N");
        var route = (await routes.CreateRouteRecordAsync("A", "B", employee.EmployeeId, bus.BusId, "00:10", true, owner, createId))!;
        Assert((await routes.CreateRouteRecordAsync("A", "B", employee.EmployeeId, bus.BusId, "00:10", true, owner, createId))?.RouteId == route.RouteId, "route replay duplicated entity");
        await Reject(() => routes.CreateRouteAsync("different", "B", employee.EmployeeId, bus.BusId, "00:10", true, owner, createId));
        await Reject(() => employees.DeleteEmployeeAsync(employee.EmployeeId, owner));
        await Reject(() => employees.DeleteJobAsync(job.JobId, owner));
        await routes.UpdateRouteAsync(route.RouteId, startPoint: "Updated", actingUser: owner, expectedRevision: 1);
        await Reject(() => routes.UpdateRouteAsync(route.RouteId, endPoint: "stale", actingUser: owner, expectedRevision: 1));
        await database.ExecuteReducerAsync(new Reducer.UpdateRoute(route.RouteId, null, "Legacy", null, null, null, null, owner), _ => true);
        await Reject(() => routes.UpdateRouteAsync(route.RouteId, endPoint: "stale", actingUser: owner, expectedRevision: 2));
        var record = (await maintenance.CreateMaintenanceRecordAsync(bus.BusId, 1000, "Synthetic", "", 2000, "Ready", "Regular", owner))!;
        var operations = new ApplicationOperations(database, new TicketSalesService(database, NullLogger<TicketSalesService>.Instance), buses,
            NullLogger<ApplicationOperations>.Instance, routes: routes, employees: employees, maintenance: maintenance);
        async Task<PageResult<T>> Page<T>(string resource, string search) => (PageResult<T>)(await operations.ExecuteAsync(resource + ".list", owner,
            JsonSerializer.SerializeToElement(new PageRequest(1, 100, search), ApiProtocol.Json), null, deadline.Token))!;
        Assert((await Page<JobRow>("jobs", job.JobTitle)).Items.Single().JobId == job.JobId, "typed job page lost filter/identity");
        Assert((await Page<RouteRow>("routes", "Updated")).Items.Any(r => r.RouteId == route.RouteId && r.Revision == 3), "typed route page lost legacy revision");
        Assert((await Page<EmployeeRow>("employees", "Synthetic Shared")).Items.Any(e => e.EmployeeId == employee.EmployeeId && e.EmployedSince.Year >= 2026), "typed employee timestamp/identity lost");
        Assert((await Page<MaintenanceRow>("maintenance", "Synthetic")).Items.Any(m => m.MaintenanceId == record.MaintenanceId && m.NextServiceDate == DateTimeOffset.FromUnixTimeMilliseconds(2000)), "typed maintenance timestamp/identity lost");
        await Reject(() => maintenance.UpdateMaintenanceAsync(record.MaintenanceId, nextServiceDate: 500, actingUser: owner));
        await Reject(() => maintenance.UpdateMaintenanceAsync(record.MaintenanceId, busId: uint.MaxValue, actingUser: owner));
        Assert((await maintenance.GetMaintenanceByIdAsync(record.MaintenanceId))?.NextServiceDate == 2000, "invalid maintenance edit partially committed");
        var remove = Guid.NewGuid().ToString("N");
        await maintenance.DeleteMaintenanceAsync(record.MaintenanceId, owner, remove, 1);
        await maintenance.DeleteMaintenanceAsync(record.MaintenanceId, owner, remove, 1);
        await routes.DeleteRouteAsync(route.RouteId, owner);
        await buses.DeleteBusAsync(bus.BusId, owner);
        async Task SetHistoricalReference(bool present)
        {
            using var referenceBody = new StringContent(JsonSerializer.Serialize(new object[] { employee.EmployeeId, present }), Encoding.UTF8);
            referenceBody.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await http.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/set_catalog_employee_reference", referenceBody);
            response.EnsureSuccessStatusCode();
        }
        await SetHistoricalReference(true);
        await Reject(() => employees.DeleteEmployeeAsync(employee.EmployeeId, owner));
        await SetHistoricalReference(false);
        foreach (var row in sameName) await employees.DeleteEmployeeAsync(row!.EmployeeId, owner);
        await employees.DeleteJobAsync(job.JobId, owner);
        // Legacy creation reducers must no longer accept an anonymous direct caller.
        using var anonymous = new HttpClient();
        using var untrustedBody = new StringContent(JsonSerializer.Serialize(new[] { "untrusted-job", "" }), Encoding.UTF8);
        untrustedBody.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var rejected = await anonymous.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/create_job", untrustedBody);
        Assert(!rejected.IsSuccessStatusCode, "legacy job creation accepted unauthenticated caller");
        Console.WriteLine("PASS catalog exact concurrent identities, actor-bound replay, duplicate title, reference integrity, legacy/v2 conflicts, maintenance rollback and unauthorized legacy creation");
    }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); throw new Exception("Expected catalog rejection"); }
        catch (ReducerRejectedException) { }
    }
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
}
