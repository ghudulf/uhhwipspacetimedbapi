using System.Threading;
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
        Task<Maintenance?> CreateMaintenanceRecordAsync(uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string maintenanceType, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default);
        Task<bool> CreateMaintenanceAsync(uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string maintenanceType, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default);
        Task<bool> UpdateMaintenanceAsync(uint maintenanceId, uint? busId = null, ulong? lastServiceDate = null, string? serviceEngineer = null, string? foundIssues = null, ulong? nextServiceDate = null, string? roadworthiness = null, string? maintenanceType = null, string? mileage = null, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteMaintenanceAsync(uint maintenanceId, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default);
        Task<List<Maintenance>> GetBusMaintenanceHistoryAsync(uint busId);
    }
} 