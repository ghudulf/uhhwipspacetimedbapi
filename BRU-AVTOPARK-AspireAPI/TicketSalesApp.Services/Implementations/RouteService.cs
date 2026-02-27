using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class RouteService : IRouteService
    {
        private readonly ISpacetimeDBService _spacetimeDBService;
        private readonly ILogger<RouteService> _logger;

        public RouteService(ISpacetimeDBService spacetimeDBService, ILogger<RouteService> logger)
        {
            _spacetimeDBService = spacetimeDBService ?? throw new ArgumentNullException(nameof(spacetimeDBService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<Route>> GetAllRoutesAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all routes");
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Route.Iter().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all routes");
                throw;
            }
        }

        public async Task<Route?> GetRouteByIdAsync(uint routeId)
        {
            try
            {
                _logger.LogInformation("Retrieving route by ID: {RouteId}", routeId);
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Route.Iter()
                    .FirstOrDefault(r => r.RouteId == routeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving route by ID: {RouteId}", routeId);
                throw;
            }
        }

        public async Task<List<Route>> GetRoutesByBusIdAsync(uint busId)
        {
            try
            {
                _logger.LogInformation("Retrieving routes for bus: {BusId}", busId);
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Route.Iter()
                    .Where(r => r.BusId == busId)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving routes for bus: {BusId}", busId);
                throw;
            }
        }

        public async Task<List<Route>> GetRoutesByDriverIdAsync(uint driverId)
        {
            try
            {
                _logger.LogInformation("Retrieving routes for driver: {DriverId}", driverId);
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Route.Iter()
                    .Where(r => r.DriverId == driverId)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving routes for driver: {DriverId}", driverId);
                throw;
            }
        }

        public async Task<bool> CreateRouteAsync(string startPoint, string endPoint, uint driverId, uint busId, string travelTime, bool isActive, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Creating route from {StartPoint} to {EndPoint}", startPoint, endPoint);
                var connection = _spacetimeDBService.GetConnection();

                // Call the CreateRoute reducer
                connection.Reducers.CreateRoute(
                    startPoint,
                    endPoint,
                    driverId,
                    busId,
                    travelTime,
                    isActive
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating route from {StartPoint} to {EndPoint}", startPoint, endPoint);
                throw;
            }
        }

        public async Task<bool> UpdateRouteAsync(uint routeId, string? startPoint = null, string? endPoint = null, uint? driverId = null, uint? busId = null, string? travelTime = null, bool? isActive = null, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Updating route: {RouteId}", routeId);
                var connection = _spacetimeDBService.GetConnection();
                
                var route = connection.Db.Route.Iter()
                    .FirstOrDefault(r => r.RouteId == routeId);
                if (route == null)
                {
                    _logger.LogWarning("Route not found: {RouteId}", routeId);
                    return false;
                }

                // Call the UpdateRoute reducer
                connection.Reducers.UpdateRoute(
                    routeId,
                    startPoint ?? route.StartPoint,
                    endPoint ?? route.EndPoint,
                    driverId, // Pass driverId as is
                    busId, // Pass busId as is
                    travelTime, // Pass travelTime as is
                    isActive ?? route.IsActive, // Pass isActive as is
                    actingUser
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating route: {RouteId}", routeId);
                throw;
            }
        }

        public async Task<bool> DeleteRouteAsync(uint routeId, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Deleting route: {RouteId}", routeId);
                var connection = _spacetimeDBService.GetConnection();
                
                var route = connection.Db.Route.Iter()
                    .FirstOrDefault(r => r.RouteId == routeId);
                if (route == null)
                {
                    _logger.LogWarning("Route not found: {RouteId}", routeId);
                    return false;
                }

                // Check if route has schedules
                var schedules = connection.Db.RouteSchedule.Iter()
                    .Where(s => s.RouteId == routeId)
                    .ToList();
                if (schedules.Any())
                {
                    _logger.LogWarning("Cannot delete route {RouteId} as it has schedules", routeId);
                    return false;
                }

                // Call the DeleteRoute reducer
                connection.Reducers.DeleteRoute(routeId, actingUser);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting route: {RouteId}", routeId);
                throw;
            }
        }

        public async Task<bool> ActivateRouteAsync(uint routeId)
        {
            try
            {
                _logger.LogInformation("Activating route: {RouteId}", routeId);
                var connection = _spacetimeDBService.GetConnection();
                
                var route = connection.Db.Route.Iter()
                    .FirstOrDefault(r => r.RouteId == routeId);
                if (route == null)
                {
                    _logger.LogWarning("Route not found: {RouteId}", routeId);
                    return false;
                }

                _spacetimeDBService.EnqueueCommand("ActivateRoute", new Dictionary<string, object>
                {
                    { "routeId", routeId }
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating route: {RouteId}", routeId);
                throw;
            }
        }

        public async Task<bool> DeactivateRouteAsync(uint routeId)
        {
            try
            {
                _logger.LogInformation("Deactivating route: {RouteId}", routeId);
                var connection = _spacetimeDBService.GetConnection();
                
                var route = connection.Db.Route.Iter()
                    .FirstOrDefault(r => r.RouteId == routeId);
                if (route == null)
                {
                    _logger.LogWarning("Route not found: {RouteId}", routeId);
                    return false;
                }

                _spacetimeDBService.EnqueueCommand("DeactivateRoute", new Dictionary<string, object>
                {
                    { "routeId", routeId }
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating route: {RouteId}", routeId);
                throw;
            }
        }

        public async Task<List<Route>> SearchRoutesAsync(string searchTerm)
        {
            try
            {
                _logger.LogInformation("Searching routes with term: {SearchTerm}", searchTerm);
                var connection = _spacetimeDBService.GetConnection();
                
                searchTerm = searchTerm.ToLower();
                return connection.Db.Route.Iter()
                    .Where(r => r.StartPoint.ToLower().Contains(searchTerm) ||
                               r.EndPoint.ToLower().Contains(searchTerm))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching routes with term: {SearchTerm}", searchTerm);
                throw;
            }
        }

        public async Task<List<Route>> GetActiveRoutesAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving active routes");
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Route.Iter()
                    .Where(r => r.IsActive)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving active routes");
                throw;
            }
        }
    }
} 