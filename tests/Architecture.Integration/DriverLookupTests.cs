using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB;
using TicketSalesApp.Services.Implementations;

internal static class DriverLookupTests
{
    internal static async Task RunAsync(SpacetimeDBService database, Identity owner, string ownerToken)
    {
        var employees = new EmployeeService(database, NullLogger<EmployeeService>.Instance);
        var operations = new ApplicationOperations(database, new TicketSalesService(database, NullLogger<TicketSalesService>.Instance), new BusService(database, NullLogger<BusService>.Instance), NullLogger<ApplicationOperations>.Instance);
        async Task<DriverLookupResult> Read(int limit = 1000) => (DriverLookupResult)(await operations.ExecuteAsync("employees.drivers", owner, JsonSerializer.SerializeToElement(new DriverLookupRequest(limit), ApiProtocol.Json), null, default))!;
        Check((await Read()).Items.Length == 0, "fresh fixture unexpectedly has driver jobs");
        var first = (await employees.CreateJobRecordAsync("DRIVER-primary-" + Guid.NewGuid().ToString("N"), "Стаж", owner))!;
        var second = (await employees.CreateJobRecordAsync("Other driver-" + Guid.NewGuid().ToString("N"), "", owner))!;
        var zulu = (await employees.CreateEmployeeAsync("Zulu", "A", "", first.JobId, owner))!;
        var alpha = (await employees.CreateEmployeeAsync("Alpha", "A", "", first.JobId, owner))!;
        var ignored = (await employees.CreateEmployeeAsync("Ignored", "A", "", second.JobId, owner))!;
        var result = await Read();
        Check(result.Items.Select(e => e.EmployeeId).SequenceEqual([alpha.EmployeeId, zulu.EmployeeId]) && result.Items.All(e => e.JobId == first.JobId && e.Revision == 1) && !result.Truncated,
            "driver query broadened first-job rule or lost surname/name ordering/revisions");
        using var http = new HttpClient(); http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        async Task Seed(bool present)
        {
            using var content = new StringContent(JsonSerializer.Serialize(new object[] { first.JobId, present }), Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await http.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/seed_driver_lookup_rows", content); response.EnsureSuccessStatusCode();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (await database.ReadAsync(c => c.Db.Employee.EmployeeId.Find(3999998000) != null) != present) await Task.Delay(10, deadline.Token);
        }
        await Seed(true);
        result = await Read(); Check(result.Items.Length == 1000 && result.Truncated, "driver lookup exceeded its bound or hid truncation");
        result = await Read(1); Check(result.Items.Length == 1 && result.Truncated && result.Items[0].EmployeeId == alpha.EmployeeId, "driver lookup ignored caller limit/order");
        await Seed(false);
        foreach (var employee in new[] { zulu, alpha, ignored }) await employees.DeleteEmployeeAsync(employee.EmployeeId, owner);
        await employees.DeleteJobAsync(first.JobId, owner); await employees.DeleteJobAsync(second.JobId, owner);
        Check((await Read()).Items.Length == 0, "no-driver lookup was not empty");
        Console.WriteLine("PASS real driver lookup: no matching job, multiple driver-like jobs choose lowest JobId, surname/name ordering, revisions and explicit bounded/truncated results");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
