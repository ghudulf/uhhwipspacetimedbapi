using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;
namespace TicketSalesApp.Services.Implementations;

public sealed class RouteService(ISpacetimeDBService database, ILogger<RouteService> logger) : IRouteService
{
    private readonly CatalogMutationExecutor _mutations = new(database, logger);
    public Task<List<Route>> GetAllRoutesAsync() => database.ReadAsync(c => c.Db.Route.Iter().OrderBy(r => r.RouteId).ToList());
    public Task<Route?> GetRouteByIdAsync(uint routeId) => database.ReadAsync(c => c.Db.Route.RouteId.Find(routeId));
    public Task<List<Route>> GetRoutesByBusIdAsync(uint busId) => database.ReadAsync(c => c.Db.Route.Iter().Where(r => r.BusId == busId).OrderBy(r => r.RouteId).ToList());
    public Task<List<Route>> GetRoutesByDriverIdAsync(uint driverId) => database.ReadAsync(c => c.Db.Route.Iter().Where(r => r.DriverId == driverId).OrderBy(r => r.RouteId).ToList());
    public Task<List<Route>> SearchRoutesAsync(string searchTerm) => database.ReadAsync(c => c.Db.Route.Iter().Where(r => r.StartPoint.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) || r.EndPoint.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.RouteId).ToList());
    public Task<List<Route>> GetActiveRoutesAsync() => database.ReadAsync(c => c.Db.Route.Iter().Where(r => r.IsActive).OrderBy(r => r.RouteId).ToList());
    public Task<bool> ActivateRouteAsync(uint routeId, Identity? actingUser = null) => SetActiveAsync(routeId, true, actingUser);
    public Task<bool> DeactivateRouteAsync(uint routeId, Identity? actingUser = null) => SetActiveAsync(routeId, false, actingUser);
    public async Task<bool> SetActiveAsync(uint routeId, bool active, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        await _mutations.ExecuteAsync(new Reducer.SetRouteActiveV2(id, actor, routeId, expectedRevision, active), actor, id, cancellationToken);
        return true;
    }
    public async Task<Route?> CreateRouteRecordAsync(string startPoint, string endPoint, uint driverId, uint busId, string travelTime, bool isActive, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default, string routeNumber = "", uint stopCount = 0, double routeLength = 0, string? routeDescription = null, string? routeType = null)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        return await _mutations.ExecuteResultAsync(new Reducer.CreateRouteV2(id, actor, startPoint, endPoint, driverId, busId, travelTime, isActive, routeNumber, stopCount, routeLength, routeDescription, routeType), actor, id, (c, receipt) => c.Db.Route.RouteId.Find(receipt.EntityId), cancellationToken);
    }
    public async Task<bool> CreateRouteAsync(string startPoint, string endPoint, uint driverId, uint busId, string travelTime, bool isActive, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default, string routeNumber = "", uint stopCount = 0, double routeLength = 0, string? routeDescription = null, string? routeType = null) => await CreateRouteRecordAsync(startPoint, endPoint, driverId, busId, travelTime, isActive, actingUser, operationId, cancellationToken, routeNumber, stopCount, routeLength, routeDescription, routeType) != null;
    public async Task<bool> UpdateRouteAsync(uint routeId, string? startPoint = null, string? endPoint = null, uint? driverId = null, uint? busId = null, string? travelTime = null, bool? isActive = null, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default, string? routeNumber = null, uint? stopCount = null, double? routeLength = null, string? routeDescription = null, string? routeType = null)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        var receipt = await _mutations.ExecuteAsync(new Reducer.UpdateRouteV2(id, actor, routeId, expectedRevision, startPoint, endPoint, driverId, busId, travelTime, isActive, routeNumber, stopCount, routeLength, routeDescription, routeType), actor, id, cancellationToken);
        return true;
    }
    public async Task<bool> DeleteRouteAsync(uint routeId, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        var receipt = await _mutations.ExecuteAsync(new Reducer.DeleteRouteV2(id, actor, routeId, expectedRevision), actor, id, cancellationToken);
        return true;
    }
}
