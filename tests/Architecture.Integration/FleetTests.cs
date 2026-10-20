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

internal static class FleetTests
{
    public static async Task RunAsync(SpacetimeDBService database, Identity owner, string ownerToken)
    {
        using var fixture = new HttpClient();
        fixture.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        async Task Call(string reducer, params object[] args)
        {
            using var content = new StringContent(JsonSerializer.Serialize(args), Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await fixture.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/" + reducer, content);
            response.EnsureSuccessStatusCode();
        }
        await Call("prepare_fleet_test");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!await database.ReadAsync(c => c.Db.Permission.Iter().Any(p => p.Name == "buses.create"), deadline.Token))
            await Task.Delay(10, deadline.Token);
        var service = new BusService(database, NullLogger<BusService>.Instance);
        var firstId = Guid.NewGuid().ToString("N");
        var secondId = Guid.NewGuid().ToString("N");
        var concurrent = await Task.WhenAll(service.CreateBusAsync("synthetic-A", "A", owner, firstId, details: new(40, "Urban", 2024, "VIN-SYNTHETIC", "PLATE", "Ready", false)), service.CreateBusAsync("synthetic-B", "B", owner, secondId));
        var first = concurrent[0]!; var second = concurrent[1]!;
        Assert(first.Capacity == 40 && first.Year == 2024 && first.Vin == "VIN-SYNTHETIC" && first.LicensePlate == "PLATE" && !first.IsActive,
            "existing page details were silently discarded");
        var operations = new ApplicationOperations(database, new TicketSalesService(database, NullLogger<TicketSalesService>.Instance), service,
            NullLogger<ApplicationOperations>.Instance);
        var page = (BusPageResult)(await operations.ExecuteAsync("buses.list", owner,
            JsonSerializer.SerializeToElement(new BusQuery(1, 1, "VIN-SYNTHETIC", false), ApiProtocol.Json), null, default))!;
        Assert(page.Items.Length == 1 && page.Items[0].BusId == first.BusId && page.Items[0].Capacity == 40 && page.TotalCount == 1
            && page.CanCreate && page.CanEdit && page.CanDelete, "fleet projection lost filters, fields or permissions");
        Assert(first.BusId != second.BusId && first.Model == "synthetic-A" && second.Model == "synthetic-B", "concurrent creation mixed entity identities");
        Assert((await service.CreateBusAsync("synthetic-A", "A", owner, firstId, details: new(40, "Urban", 2024, "VIN-SYNTHETIC", "PLATE", "Ready", false)))!.BusId == first.BusId, "creation replay duplicated bus");
        await Reject(() => service.CreateBusAsync("changed-payload", "A", owner, firstId));
        await service.UpdateBusAsync(first.BusId, "synthetic-updated", "A", owner, expectedRevision: 1, details: new(Capacity: 45, IsActive: true));
        Assert((await service.GetBusByIdAsync(first.BusId)) is { Capacity: 45, IsActive: true, Vin: "VIN-SYNTHETIC" }, "bus detail update lost preserved fields");
        await Reject(() => service.UpdateBusAsync(first.BusId, "stale", "A", owner, expectedRevision: 1));
        // A legacy writer must also invalidate a v2 draft.
        await database.ExecuteReducerAsync(new Reducer.UpdateBus(first.BusId, "legacy-updated", null, owner), _ => true);
        await Reject(() => service.UpdateBusAsync(first.BusId, "stale-v2", null, owner, expectedRevision: 2));
        await service.SetActiveAsync(first.BusId, false, owner, expectedRevision: 3);
        Assert((await service.GetBusByIdAsync(first.BusId))?.IsActive == false, "deactivation acknowledged before commit");
        await service.ActivateBusAsync(first.BusId, owner);
        await Call("set_fleet_test_reference", first.BusId, true);
        await Reject(() => service.DeleteBusAsync(first.BusId, owner));
        Assert(await service.GetBusByIdAsync(first.BusId) != null, "referenced bus was deleted");
        await Call("set_fleet_test_reference", first.BusId, false);
        var deletion = Guid.NewGuid().ToString("N");
        await service.DeleteBusAsync(first.BusId, owner, deletion);
        await service.DeleteBusAsync(first.BusId, owner, deletion);
        Assert(await service.GetBusByIdAsync(first.BusId) == null, "deletion acknowledged before commit");
        await service.DeleteBusAsync(second.BusId, owner);
        await Reject(() => service.CreateBusAsync(" ", null, owner));
        await Reject(() => service.CreateBusAsync("invalid-capacity", null, owner, details: new(Capacity: 1001)));
        Assert(!(await service.GetAllBusesAsync()).Any(b => b.Model == "invalid-capacity"), "invalid details partially committed bus creation");
        Console.WriteLine("PASS concurrent fleet identities, replay/payload binding, legacy/v2 revision conflicts, committed state, inactive reference protection and deletion replay");
    }
    private static async Task Reject(Func<Task> operation)
    {
        try { await operation(); throw new Exception("Expected fleet reducer rejection"); }
        catch (ReducerRejectedException) { }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
