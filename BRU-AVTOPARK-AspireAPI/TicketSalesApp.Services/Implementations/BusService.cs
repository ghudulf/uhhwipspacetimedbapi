using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

/// <summary>Fleet reads run on the coordinator; every mutation awaits its actor-scoped committed receipt.</summary>
public sealed class BusService(ISpacetimeDBService database, ILogger<BusService> logger) : IBusService
{
    public async Task<IEnumerable<Bus>> GetAllBusesAsync() =>
        await database.ReadAsync(c => c.Db.Bus.Iter().OrderBy(b => b.BusId).ToArray());

    public Task<Bus?> GetBusByIdAsync(uint busId) => database.ReadAsync(c => c.Db.Bus.BusId.Find(busId));

    public async Task<Bus?> CreateBusAsync(string model, string? registrationNumber = null, Identity? actingUser = null,
        string? operationId = null, CancellationToken cancellationToken = default, BusDetailsInput? details = null)
    {
        var actor = RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        logger.LogInformation("Submitting bus creation {OperationId}, actor {Actor}", id, actor);
        return await database.ExecuteReducerAsync(new Reducer.CreateBusV2(id, actor, model, registrationNumber, details?.Capacity, details?.BusType, details?.Year, details?.Vin, details?.LicensePlate, details?.CurrentStatus, details?.IsActive), c =>
        {
            var receipt = RequireReceipt(c, actor, id);
            return c.Db.Bus.BusId.Find(receipt.EntityId) ?? throw new InvalidOperationException("Committed bus no longer exists.");
        }, cancellationToken);
    }

    public Task<bool> UpdateBusAsync(uint busId, string? model = null, string? registrationNumber = null, Identity? actingUser = null,
        string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default, BusDetailsInput? details = null)
    {
        var actor = RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        return CommitAsync(new Reducer.UpdateBusV2(id, actor, busId, expectedRevision, model, registrationNumber, details?.Capacity, details?.BusType, details?.Year, details?.Vin, details?.LicensePlate, details?.CurrentStatus, details?.IsActive), actor, id, cancellationToken);
    }

    public Task<bool> DeleteBusAsync(uint busId, Identity? actingUser = null,
        string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var actor = RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        return CommitAsync(new Reducer.DeleteBusV2(id, actor, busId, expectedRevision), actor, id, cancellationToken);
    }

    public async Task<IEnumerable<Bus>> SearchBusesAsync(string? model = null, string? serviceStatus = null) => await database.ReadAsync(c =>
    {
        // Materialize one latest-maintenance lookup instead of scanning maintenance for every bus.
        var latest = string.IsNullOrEmpty(serviceStatus) ? null : c.Db.Maintenance.Iter().GroupBy(m => m.BusId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.LastServiceDate).ThenByDescending(m => m.MaintenanceId).First().Roadworthiness);
        return c.Db.Bus.Iter().Where(b => string.IsNullOrEmpty(model) || b.Model.Contains(model, StringComparison.OrdinalIgnoreCase))
            .Where(b => latest == null || (latest.TryGetValue(b.BusId, out var status) && status == serviceStatus))
            .OrderBy(b => b.BusId).ToArray();
    });

    public Task<bool> ActivateBusAsync(uint busId, Identity? actingUser = null) => SetActiveAsync(busId, true, actingUser);
    public Task<bool> DeactivateBusAsync(uint busId, Identity? actingUser = null) => SetActiveAsync(busId, false, actingUser);

    public Task<bool> SetActiveAsync(uint busId, bool active, Identity? actingUser = null, string? operationId = null,
        ulong? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var actor = RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        return CommitAsync(new Reducer.SetBusActiveV2(id, actor, busId, expectedRevision, active), actor, id, cancellationToken);
    }

    private async Task<bool> CommitAsync<T>(T reducer, Identity actor, string id, CancellationToken cancellationToken) where T : Reducer, IReducerArgs, new()
    {
        logger.LogInformation("Submitting fleet mutation {OperationId}, actor {Actor}", id, actor);
        await database.ExecuteReducerAsync(reducer, c => RequireReceipt(c, actor, id), cancellationToken);
        logger.LogInformation("Fleet mutation {OperationId} confirmed committed", id);
        return true;
    }
    private static MutationReceipt RequireReceipt(DbConnection connection, Identity actor, string id) =>
        connection.Db.MutationReceipt.Key.Find(actor + ":" + id) ?? throw new InvalidOperationException("Committed fleet receipt missing.");
    private static Identity RequireActor(Identity? actor) => actor ?? throw new UnauthorizedAccessException("Authenticated actor required.");
}
