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
        var route = (await routes.CreateRouteRecordAsync("A", "B", employee.EmployeeId, bus.BusId, "00:10", true, owner, createId, routeNumber: "R-14", stopCount: 12, routeLength: 25.5, routeDescription: "Описание", routeType: "Городской"))!;
        Assert(route.RouteNumber == "R-14" && route.StopCount == 12 && route.RouteLength == 25.5 && route.RouteDescription == "Описание" && route.RouteType == "Городской", "route create lost metadata");
        Assert((await routes.CreateRouteRecordAsync("A", "B", employee.EmployeeId, bus.BusId, "00:10", true, owner, createId, routeNumber: "R-14", stopCount: 12, routeLength: 25.5, routeDescription: "Описание", routeType: "Городской"))?.RouteId == route.RouteId, "route replay duplicated entity");
        await Reject(() => routes.CreateRouteAsync("A", "B", employee.EmployeeId, bus.BusId, "00:10", true, owner, createId, routeNumber: "changed"));
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
        async Task<CatalogPageResult<T>> Page<T>(string resource, string search) => (CatalogPageResult<T>)(await operations.ExecuteAsync(resource + ".list", owner,
            JsonSerializer.SerializeToElement(new PageRequest(1, 100, search), ApiProtocol.Json), null, deadline.Token))!;
        Assert((await Page<JobRow>("jobs", job.JobTitle)).Items.Single().JobId == job.JobId, "typed job page lost filter/identity");
        var jobPage = await Page<JobRow>("jobs", job.JobTitle);
        Assert(jobPage.CanCreate && jobPage.CanEdit && jobPage.CanDelete && jobPage.Items.Single().EmployeeCount == 2, "job permissions/count projection incorrect");
        var currentJob = (JobRow)(await operations.ExecuteAsync("jobs.get", owner,
            JsonSerializer.SerializeToElement(new EntityRequest(job.JobId), ApiProtocol.Json), null, deadline.Token))!;
        Assert(currentJob.JobId == job.JobId && currentJob.Revision == 1, "conflict reload did not return authoritative identity/revision");
        Assert((await Page<RouteRow>("routes", "Updated")).Items.Any(r => r.RouteId == route.RouteId && r.Revision == 3), "typed route page lost legacy revision");
        Assert((await Page<EmployeeRow>("employees", "Synthetic Shared")).Items.Any(e => e.EmployeeId == employee.EmployeeId && e.EmployedSince?.Year >= 2026), "typed employee timestamp/identity lost");
        Assert((await Page<MaintenanceRow>("maintenance", "Synthetic")).Items.Any(m => m.MaintenanceId == record.MaintenanceId && m.NextServiceDate == DateTimeOffset.FromUnixTimeMilliseconds(2000)), "typed maintenance timestamp/identity lost");
        await CatalogReadAssertions.MatchesListAsync(operations, owner, "routes", route.RouteId, 3, deadline.Token);
        await CatalogReadAssertions.MatchesListAsync(operations, owner, "employees", employee.EmployeeId, 1, deadline.Token);
        await CatalogReadAssertions.MatchesListAsync(operations, owner, "maintenance", record.MaintenanceId, 1, deadline.Token);
        await routes.UpdateRouteAsync(route.RouteId, travelTime: "00:20", isActive: false, actingUser: owner, expectedRevision: 3, routeNumber: "R-15", stopCount: 0, routeLength: 0, routeDescription: "", routeType: "Междугородний");
        var details = (await routes.GetRouteByIdAsync(route.RouteId))!;
        Assert(details.RouteNumber == "R-15" && details.StopCount == 0 && details.RouteLength == 0 && details.RouteDescription == "" && details.RouteType == "Междугородний" && !details.IsActive, "route atomic update/activation lost metadata or empty/zero values");
        foreach (var invalid in new Func<Task>[] {
            () => routes.UpdateRouteAsync(route.RouteId, actingUser: owner, routeNumber: new string('x', 51)),
            () => routes.UpdateRouteAsync(route.RouteId, actingUser: owner, routeDescription: new string('x', 2001)),
            () => routes.UpdateRouteAsync(route.RouteId, actingUser: owner, routeType: new string('x', 101)),
            () => routes.UpdateRouteAsync(route.RouteId, actingUser: owner, stopCount: 10001),
            () => routes.UpdateRouteAsync(route.RouteId, actingUser: owner, routeLength: -1),
            () => routes.UpdateRouteAsync(route.RouteId, actingUser: owner, routeLength: double.NaN),
            () => routes.UpdateRouteAsync(route.RouteId, actingUser: owner, routeLength: double.PositiveInfinity),
            () => routes.UpdateRouteAsync(route.RouteId, actingUser: owner, routeLength: 1000001) }) await Reject(invalid);
        await employees.UpdateEmployeeAsync(employee.EmployeeId, employeePatronym: "Updated", actingUser: owner, expectedRevision: 1);
        await maintenance.UpdateMaintenanceAsync(record.MaintenanceId, serviceEngineer: "Updated", actingUser: owner, expectedRevision: 1);
        await CatalogReadAssertions.MatchesListAsync(operations, owner, "routes", route.RouteId, 4, deadline.Token);
        var accessService = new TicketSalesApp.Services.Authorization.UserAccessService(database);
        var originallyVisible = (await accessService.GetAsync(owner)).Allows("tickets.view");
        async Task SeedTickets(bool present, bool visible)
        {
            using var seed = new StringContent(JsonSerializer.Serialize(new object[] { route.RouteId, present, visible }), Encoding.UTF8);
            seed.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await http.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/set_catalog_route_tickets", seed);
            response.EnsureSuccessStatusCode();
            while ((await accessService.GetAsync(owner)).Allows("tickets.view") != visible) await Task.Delay(10, deadline.Token);
        }
        await SeedTickets(true, true);
        Assert((await Page<RouteRow>("routes", "R-15")).Items.Single(r => r.RouteId == route.RouteId).TicketCount == 1, "route counted inactive tickets or lost active ticket count");
        await CatalogReadAssertions.MatchesListAsync(operations, owner, "routes", route.RouteId, 4, deadline.Token);
        await SeedTickets(true, false);
        Assert((await Page<RouteRow>("routes", "R-15")).Items.Single(r => r.RouteId == route.RouteId).TicketCount == null, "route fabricated/exposed count without tickets.view");
        await CatalogReadAssertions.MatchesListAsync(operations, owner, "routes", route.RouteId, 4, deadline.Token);
        await SeedTickets(false, originallyVisible);
        await CatalogReadAssertions.MatchesListAsync(operations, owner, "employees", employee.EmployeeId, 2, deadline.Token);
        await CatalogReadAssertions.MatchesListAsync(operations, owner, "maintenance", record.MaintenanceId, 2, deadline.Token);
        await Reject(() => maintenance.UpdateMaintenanceAsync(record.MaintenanceId, nextServiceDate: 500, actingUser: owner));
        await Reject(() => maintenance.UpdateMaintenanceAsync(record.MaintenanceId, busId: uint.MaxValue, actingUser: owner));
        Assert((await maintenance.GetMaintenanceByIdAsync(record.MaintenanceId))?.NextServiceDate == 2000, "invalid maintenance edit partially committed");
        var remove = Guid.NewGuid().ToString("N");
        await maintenance.DeleteMaintenanceAsync(record.MaintenanceId, owner, remove, 2);
        await maintenance.DeleteMaintenanceAsync(record.MaintenanceId, owner, remove, 2);
        await CatalogReadAssertions.MissingAsync(operations, owner, "maintenance", record.MaintenanceId, deadline.Token);
        await routes.DeleteRouteAsync(route.RouteId, owner);
        await CatalogReadAssertions.MissingAsync(operations, owner, "routes", route.RouteId, deadline.Token);
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
        await CatalogReadAssertions.MissingAsync(operations, owner, "employees", employee.EmployeeId, deadline.Token);
        await employees.DeleteJobAsync(job.JobId, owner);
        // Legacy creation reducers must no longer accept an anonymous direct caller.
        using var anonymous = new HttpClient();
        using var untrustedBody = new StringContent(JsonSerializer.Serialize(new[] { "untrusted-job", "" }), Encoding.UTF8);
        untrustedBody.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var rejected = await anonymous.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/create_job", untrustedBody);
        Assert(!rejected.IsSuccessStatusCode, "legacy job creation accepted unauthenticated caller");
        Console.WriteLine("PASS catalog exact concurrent identities, actor-bound replay, duplicate title, reference integrity, legacy/v2 conflicts, maintenance rollback and unauthorized legacy creation");
        Console.WriteLine("PASS real employees/routes/maintenance get: typed list-row equality, authoritative revisions advance after update and deleted entities return not_found");
    }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); throw new Exception("Expected catalog rejection"); }
        catch (ReducerRejectedException) { }
    }
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
}
