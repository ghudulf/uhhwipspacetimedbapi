using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IMaintenanceService
    {
        Task<List<Maintenance>> GetAllMaintenanceRecordsAsync();
        Task<Maintenance?> GetMaintenanceByIdAsync(uint maintenanceId);
        Task<List<Maintenance>> GetMaintenanceByBusIdAsync(uint busId);
        Task<bool> CreateMaintenanceAsync(uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string maintenanceType, Identity? actingUser = null);
        Task<bool> UpdateMaintenanceAsync(uint maintenanceId, uint? busId = null, ulong? lastServiceDate = null, string? serviceEngineer = null, string? foundIssues = null, ulong? nextServiceDate = null, string? roadworthiness = null, string? maintenanceType = null, string? mileage = null, Identity? actingUser = null);
        Task<bool> DeleteMaintenanceAsync(uint maintenanceId, Identity? actingUser = null);
        Task<List<Maintenance>> GetBusMaintenanceHistoryAsync(uint busId);
    }
} 