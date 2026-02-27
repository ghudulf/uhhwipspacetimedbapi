using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IBusService
    {
        Task<IEnumerable<Bus>> GetAllBusesAsync();
        Task<Bus?> GetBusByIdAsync(uint busId);
        Task<Bus?> CreateBusAsync(string model, string? registrationNumber = null);
        Task<bool> UpdateBusAsync(uint busId, string? model = null, string? registrationNumber = null);
        Task<bool> DeleteBusAsync(uint busId);
        Task<IEnumerable<Bus>> SearchBusesAsync(string? model = null, string? serviceStatus = null);
        Task<bool> ActivateBusAsync(uint busId);
        Task<bool> DeactivateBusAsync(uint busId);
    }
} 