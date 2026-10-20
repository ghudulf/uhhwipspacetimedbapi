using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public sealed record BusDetailsInput(uint? Capacity = null, string? BusType = null, uint? Year = null, string? Vin = null,
        string? LicensePlate = null, string? CurrentStatus = null, bool? IsActive = null);

    public interface IBusService
    {
        Task<IEnumerable<Bus>> GetAllBusesAsync();
        Task<Bus?> GetBusByIdAsync(uint busId);
        Task<Bus?> CreateBusAsync(string model, string? registrationNumber = null, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default, BusDetailsInput? details = null);
        Task<bool> UpdateBusAsync(uint busId, string? model = null, string? registrationNumber = null, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default, BusDetailsInput? details = null);
        Task<bool> DeleteBusAsync(uint busId, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default);
        Task<IEnumerable<Bus>> SearchBusesAsync(string? model = null, string? serviceStatus = null);
        Task<bool> SetActiveAsync(uint busId, bool active, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default);
        Task<bool> ActivateBusAsync(uint busId, Identity? actingUser = null);
        Task<bool> DeactivateBusAsync(uint busId, Identity? actingUser = null);
    }
} 