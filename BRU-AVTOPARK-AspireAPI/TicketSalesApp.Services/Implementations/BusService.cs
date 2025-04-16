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
    public class BusService : IBusService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<BusService> _logger;

        public BusService(ISpacetimeDBService spacetimeService, ILogger<BusService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IEnumerable<Bus>> GetAllBusesAsync()
        {
            try
            {
                _logger.LogInformation("Fetching all buses");
                var conn = _spacetimeService.GetConnection();
                
                var buses = conn.Db.Bus.Iter()
                    .Where(b => true) // Get all buses
                    .ToList();
                
                _logger.LogDebug("Retrieved {BusCount} buses", buses.Count);
                
                // Log detailed information about each bus
                foreach (var bus in buses)
                {
                    _logger.LogDebug("Bus details - ID: {BusId}, Model: {Model}, Registration: {Registration}",
                        bus.BusId, bus.Model, bus.RegistrationNumber);
                }
                
                return buses;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all buses");
                throw;
            }
        }

        public async Task<Bus?> GetBusByIdAsync(uint busId)
        {
            try
            {
                _logger.LogInformation("Fetching bus with ID {BusId}", busId);
                var conn = _spacetimeService.GetConnection();
                
                var bus = conn.Db.Bus.BusId.Find(busId);
                if (bus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found", busId);
                    return null;
                }
                
                _logger.LogDebug("Successfully retrieved bus with ID {BusId}", busId);
                return bus;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching bus with ID {BusId}", busId);
                throw;
            }
        }

        public async Task<Bus?> CreateBusAsync(string model, string? registrationNumber = null, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Creating new bus with model {Model}", model);
                var conn = _spacetimeService.GetConnection();
                
                // Call the CreateBus reducer
                conn.Reducers.CreateBus(model, registrationNumber, actingUser);
                
                // Wait a moment for the reducer to complete and the subscription to update
                await Task.Delay(100);
                
                // Find the newly created bus (it will be the one with the highest ID)
                var allBuses = conn.Db.Bus.Iter().ToList();
                var newBus = allBuses.OrderByDescending(b => b.BusId).FirstOrDefault();
                
                if (newBus == null)
                {
                    _logger.LogWarning("Failed to retrieve newly created bus");
                    return null;
                }
                
                _logger.LogInformation("Successfully created bus with ID {BusId}", newBus.BusId);
                return newBus;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating bus with model {Model}", model);
                throw;
            }
        }

        public async Task<bool> UpdateBusAsync(uint busId, string? model = null, string? registrationNumber = null, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Updating bus with ID {BusId}", busId);
                var conn = _spacetimeService.GetConnection();
                
                // Check if bus exists
                var existingBus = conn.Db.Bus.BusId.Find(busId);
                if (existingBus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for update", busId);
                    return false;
                }
                
                // Call the UpdateBus reducer
                conn.Reducers.UpdateBus(busId, model, registrationNumber, actingUser);
                
                _logger.LogInformation("Successfully updated bus with ID {BusId}", busId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating bus with ID {BusId}", busId);
                throw;
            }
        }

        public async Task<bool> DeleteBusAsync(uint busId, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Deleting bus with ID {BusId}", busId);
                var conn = _spacetimeService.GetConnection();
                
                // Check if bus exists
                var existingBus = conn.Db.Bus.BusId.Find(busId);
                if (existingBus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for deletion", busId);
                    return false;
                }
                
                // Check if bus is used in any active routes
                var activeRoutes = conn.Db.Route.Iter()
                    .Where(r => r.BusId == busId && r.IsActive)
                    .ToList();
                
                if (activeRoutes.Any())
                {
                    _logger.LogWarning("Cannot delete bus with ID {BusId} as it is used in {RouteCount} active routes", 
                        busId, activeRoutes.Count);
                    return false;
                }
                
                // Call the DeleteBus reducer
                conn.Reducers.DeleteBus(busId, actingUser);
                
                _logger.LogInformation("Successfully deleted bus with ID {BusId}", busId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting bus with ID {BusId}", busId);
                throw;
            }
        }

        public async Task<IEnumerable<Bus>> SearchBusesAsync(string? model = null, string? serviceStatus = null)
        {
            try
            {
                _logger.LogInformation("Searching buses with model: {Model}, service status: {ServiceStatus}", 
                    model ?? "any", serviceStatus ?? "any");
                
                var conn = _spacetimeService.GetConnection();
                
                var buses = conn.Db.Bus.Iter();
                
                // Filter by model if provided
                if (!string.IsNullOrEmpty(model))
                {
                    _logger.LogDebug("Filtering by model containing: {Model}", model);
                    buses = buses.Where(b => b.Model.Contains(model, StringComparison.OrdinalIgnoreCase));
                }
                
                // Filter by service status if provided
                if (!string.IsNullOrEmpty(serviceStatus))
                {
                    _logger.LogDebug("Filtering by service status: {ServiceStatus}", serviceStatus);
                    buses = buses.Where(b => {
                        var maintenance = conn.Db.Maintenance.Iter()
                            .Where(m => m.BusId == b.BusId)
                            .OrderByDescending(m => m.LastServiceDate)
                            .FirstOrDefault();
                        return maintenance != null && maintenance.Roadworthiness == serviceStatus;
                    });
                }
                
                var results = buses.ToList();
                _logger.LogDebug("Found {ResultCount} buses matching search criteria", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching buses");
                throw;
            }
        }

        public async Task<bool> ActivateBusAsync(uint busId, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Activating bus with ID {BusId}", busId);
                var conn = _spacetimeService.GetConnection();
                
                // Check if bus exists
                var existingBus = conn.Db.Bus.BusId.Find(busId);
                if (existingBus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for activation", busId);
                    return false;
                }
                
                // Call the ActivateBus reducer
                conn.Reducers.ActivateBus(busId, actingUser);
                
                _logger.LogInformation("Successfully activated bus with ID {BusId}", busId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating bus with ID {BusId}", busId);
                throw;
            }
        }

        public async Task<bool> DeactivateBusAsync(uint busId, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Deactivating bus with ID {BusId}", busId);
                var conn = _spacetimeService.GetConnection();
                
                // Check if bus exists
                var existingBus = conn.Db.Bus.BusId.Find(busId);
                if (existingBus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for deactivation", busId);
                    return false;
                }
                
                // Check if bus is used in any active routes
                var activeRoutes = conn.Db.Route.Iter()
                    .Where(r => r.BusId == busId && r.IsActive)
                    .ToList();
                
                if (activeRoutes.Any())
                {
                    _logger.LogWarning("Cannot deactivate bus with ID {BusId} as it is used in {RouteCount} active routes", 
                        busId, activeRoutes.Count);
                    return false;
                }
                
                // Call the DeactivateBus reducer
                conn.Reducers.DeactivateBus(busId, actingUser);
                
                _logger.LogInformation("Successfully deactivated bus with ID {BusId}", busId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating bus with ID {BusId}", busId);
                throw;
            }
        }
    }
} 