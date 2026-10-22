using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;
namespace TicketSalesApp.Services.Implementations;

public sealed class MaintenanceService(ISpacetimeDBService database, ILogger<MaintenanceService> logger) : IMaintenanceService
{
    private readonly CatalogMutationExecutor _mutations = new(database, logger);
    public Task<List<Maintenance>> GetAllMaintenanceRecordsAsync() => database.ReadAsync(c => c.Db.Maintenance.Iter().OrderByDescending(m => m.LastServiceDate).ThenBy(m => m.MaintenanceId).ToList());
    public Task<Maintenance?> GetMaintenanceByIdAsync(uint maintenanceId) => database.ReadAsync(c => c.Db.Maintenance.MaintenanceId.Find(maintenanceId));
    public Task<List<Maintenance>> GetMaintenanceByBusIdAsync(uint busId) => database.ReadAsync(c => c.Db.Maintenance.Iter().Where(m => m.BusId == busId).OrderByDescending(m => m.LastServiceDate).ThenBy(m => m.MaintenanceId).ToList());
    public Task<List<Maintenance>> GetBusMaintenanceHistoryAsync(uint busId) => GetMaintenanceByBusIdAsync(busId);
    public async Task<Maintenance?> CreateMaintenanceRecordAsync(uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string maintenanceType, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        return await _mutations.ExecuteResultAsync(new Reducer.CreateMaintenanceV2(id, actor, busId, lastServiceDate, serviceEngineer, foundIssues, nextServiceDate, roadworthiness, maintenanceType), actor, id, (c, receipt) => c.Db.Maintenance.MaintenanceId.Find(receipt.EntityId), cancellationToken);
    }
    public async Task<bool> CreateMaintenanceAsync(uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string maintenanceType, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default) => await CreateMaintenanceRecordAsync(busId, lastServiceDate, serviceEngineer, foundIssues, nextServiceDate, roadworthiness, maintenanceType, actingUser, operationId, cancellationToken) != null;
    public async Task<bool> UpdateMaintenanceAsync(uint maintenanceId, uint? busId = null, ulong? lastServiceDate = null, string? serviceEngineer = null, string? foundIssues = null, ulong? nextServiceDate = null, string? roadworthiness = null, string? maintenanceType = null, string? mileage = null, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        var receipt = await _mutations.ExecuteAsync(new Reducer.UpdateMaintenanceV2(id, actor, maintenanceId, expectedRevision, busId, lastServiceDate, serviceEngineer, foundIssues, nextServiceDate, roadworthiness, maintenanceType, mileage), actor, id, cancellationToken);
        return true;
    }
    public async Task<bool> DeleteMaintenanceAsync(uint maintenanceId, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        var receipt = await _mutations.ExecuteAsync(new Reducer.DeleteMaintenanceV2(id, actor, maintenanceId, expectedRevision), actor, id, cancellationToken);
        return true;
    }
}
