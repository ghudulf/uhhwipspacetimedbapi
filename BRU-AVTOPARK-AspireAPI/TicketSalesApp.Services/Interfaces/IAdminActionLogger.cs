using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IAdminActionLogger
    {
        Task LogActionAsync(string userId, string action, string details);
        Task<List<AdminActionLog>> GetUserActionsAsync(string userId, DateTime? startDate = null, DateTime? endDate = null);
        Task<List<AdminActionLog>> GetActionsByTypeAsync(string actionType, DateTime? startDate = null, DateTime? endDate = null);
    }
}