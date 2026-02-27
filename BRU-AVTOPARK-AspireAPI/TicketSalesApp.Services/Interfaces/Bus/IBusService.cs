using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IBusService
    {
        Task<IEnumerable<Bus>> GetAllBusesAsync();
        Task<Bus?> GetBusByIdAsync(uint busId);
        Task<Bus?> CreateBusAsync(string model, string? registrationNumber = null, Identity? actingUser = null);
        Task<bool> UpdateBusAsync(uint busId, string? model = null, string? registrationNumber = null, Identity? actingUser = null);
        Task<bool> DeleteBusAsync(uint busId, Identity? actingUser = null);
        Task<IEnumerable<Bus>> SearchBusesAsync(string? model = null, string? serviceStatus = null);
        Task<bool> ActivateBusAsync(uint busId, Identity? actingUser = null);
        Task<bool> DeactivateBusAsync(uint busId, Identity? actingUser = null);
    }
} 