using System.Threading;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IRouteService
    {
        Task<bool> SetActiveAsync(uint routeId, bool active, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default);
        Task<List<Route>> GetAllRoutesAsync();
        Task<Route?> GetRouteByIdAsync(uint routeId);
        Task<List<Route>> GetRoutesByBusIdAsync(uint busId);
        Task<List<Route>> GetRoutesByDriverIdAsync(uint driverId);
        Task<Route?> CreateRouteRecordAsync(string startPoint, string endPoint, uint driverId, uint busId, string travelTime, bool isActive, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default, string routeNumber = "", uint stopCount = 0, double routeLength = 0, string? routeDescription = null, string? routeType = null);
        Task<bool> CreateRouteAsync(string startPoint, string endPoint, uint driverId, uint busId, string travelTime, bool isActive, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default, string routeNumber = "", uint stopCount = 0, double routeLength = 0, string? routeDescription = null, string? routeType = null);
        Task<bool> UpdateRouteAsync(uint routeId, string? startPoint = null, string? endPoint = null, uint? driverId = null, uint? busId = null, string? travelTime = null, bool? isActive = null, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default, string? routeNumber = null, uint? stopCount = null, double? routeLength = null, string? routeDescription = null, string? routeType = null);
        Task<bool> DeleteRouteAsync(uint routeId, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default);
        Task<bool> ActivateRouteAsync(uint routeId, Identity? actingUser = null);
        Task<bool> DeactivateRouteAsync(uint routeId, Identity? actingUser = null);
    }
}
