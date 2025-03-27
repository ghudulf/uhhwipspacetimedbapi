using Microsoft.Extensions.Logging;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class MaintenanceService : IMaintenanceService
    {
        private readonly ISpacetimeDBService _spacetimeDBService;
        private readonly ILogger<MaintenanceService> _logger;

        public MaintenanceService(ISpacetimeDBService spacetimeDBService, ILogger<MaintenanceService> logger)
        {
            _spacetimeDBService = spacetimeDBService ?? throw new ArgumentNullException(nameof(spacetimeDBService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<Maintenance>> GetAllMaintenanceRecordsAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all maintenance records");
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Maintenance.Iter().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all maintenance records");
                throw;
            }
        }

        public async Task<Maintenance?> GetMaintenanceByIdAsync(uint maintenanceId)
        {
            try
            {
                _logger.LogInformation("Retrieving maintenance record by ID: {MaintenanceId}", maintenanceId);
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Maintenance.Iter()
                    .FirstOrDefault(m => m.MaintenanceId == maintenanceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving maintenance record by ID: {MaintenanceId}", maintenanceId);
                throw;
            }
        }

        public async Task<List<Maintenance>> GetMaintenanceByBusIdAsync(uint busId)
        {
            try
            {
                _logger.LogInformation("Retrieving maintenance records for bus: {BusId}", busId);
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Maintenance.Iter()
                    .Where(m => m.BusId == busId)
                    .OrderByDescending(m => m.LastServiceDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving maintenance records for bus: {BusId}", busId);
                throw;
            }
        }

        public async Task<bool> CreateMaintenanceAsync(uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string maintenanceType)
        {
            try
            {
                _logger.LogInformation("Creating maintenance record for bus: {BusId}", busId);
                var connection = _spacetimeDBService.GetConnection();
                
                var bus = connection.Db.Bus.Iter()
                    .FirstOrDefault(b => b.BusId == busId);
                if (bus == null)
                {
                    _logger.LogWarning("Bus not found: {BusId}", busId);
                    return false;
                }

                // Call the CreateMaintenance reducer
                connection.Reducers.CreateMaintenance(
                    busId,
                    lastServiceDate,
                    serviceEngineer,
                    foundIssues,
                    nextServiceDate,
                    roadworthiness,
                    maintenanceType
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating maintenance record for bus: {BusId}", busId);
                throw;
            }
        }

        public async Task<bool> UpdateMaintenanceAsync(uint maintenanceId, uint? busId = null, ulong? lastServiceDate = null, string? serviceEngineer = null, string? foundIssues = null, ulong? nextServiceDate = null, string? roadworthiness = null, string? maintenanceType = null, string? mileage = null)
        {
            try
            {
                _logger.LogInformation("Updating maintenance record: {MaintenanceId}", maintenanceId);
                var connection = _spacetimeDBService.GetConnection();
                
                var maintenance = connection.Db.Maintenance.Iter()
                    .FirstOrDefault(m => m.MaintenanceId == maintenanceId);
                if (maintenance == null)
                {
                    _logger.LogWarning("Maintenance record not found: {MaintenanceId}", maintenanceId);
                    return false;
                }

                if (busId.HasValue)
                {
                    var bus = connection.Db.Bus.Iter()
                        .FirstOrDefault(b => b.BusId == busId);
                    if (bus == null)
                    {
                        _logger.LogWarning("Bus not found: {BusId}", busId);
                        return false;
                    }
                }

                // Call the UpdateMaintenance reducer
                connection.Reducers.UpdateMaintenance(
                    maintenanceId,
                    busId ?? maintenance.BusId,
                    lastServiceDate ?? maintenance.LastServiceDate,
                    serviceEngineer ?? maintenance.ServiceEngineer,
                    foundIssues ?? maintenance.FoundIssues,
                    nextServiceDate ?? maintenance.NextServiceDate,
                    roadworthiness ?? maintenance.Roadworthiness,
                    maintenanceType ?? maintenance.MaintenanceType,
                    mileage ?? maintenance.MileageThreshold
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating maintenance record: {MaintenanceId}", maintenanceId);
                throw;
            }
        }

        public async Task<bool> DeleteMaintenanceAsync(uint maintenanceId)
        {
            try
            {
                _logger.LogInformation("Deleting maintenance record: {MaintenanceId}", maintenanceId);
                var connection = _spacetimeDBService.GetConnection();
                
                var maintenance = connection.Db.Maintenance.Iter()
                    .FirstOrDefault(m => m.MaintenanceId == maintenanceId);
                if (maintenance == null)
                {
                    _logger.LogWarning("Maintenance record not found: {MaintenanceId}", maintenanceId);
                    return false;
                }

                // Call the DeleteMaintenance reducer
                connection.Reducers.DeleteMaintenance(maintenanceId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting maintenance record: {MaintenanceId}", maintenanceId);
                throw;
            }
        }

        public async Task<List<Maintenance>> GetBusMaintenanceHistoryAsync(uint busId)
        {
            try
            {
                _logger.LogInformation("Retrieving maintenance history for bus: {BusId}", busId);
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Maintenance.Iter()
                    .Where(m => m.BusId == busId)
                    .OrderByDescending(m => m.LastServiceDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving maintenance history for bus: {BusId}", busId);
                throw;
            }
        }
    }
} 