using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IRouteService
    {
        Task<List<Route>> GetAllRoutesAsync();
        Task<Route?> GetRouteByIdAsync(uint routeId);
        Task<List<Route>> GetRoutesByBusIdAsync(uint busId);
        Task<List<Route>> GetRoutesByDriverIdAsync(uint driverId);
        Task<bool> CreateRouteAsync(string startPoint, string endPoint, uint driverId, uint busId, string travelTime, bool isActive, Identity? actingUser = null);
        Task<bool> UpdateRouteAsync(uint routeId, string? startPoint = null, string? endPoint = null, uint? driverId = null, uint? busId = null, string? travelTime = null, bool? isActive = null, Identity? actingUser = null);
        Task<bool> DeleteRouteAsync(uint routeId, Identity? actingUser = null);
        Task<bool> ActivateRouteAsync(uint routeId);
        Task<bool> DeactivateRouteAsync(uint routeId);
    }
} 