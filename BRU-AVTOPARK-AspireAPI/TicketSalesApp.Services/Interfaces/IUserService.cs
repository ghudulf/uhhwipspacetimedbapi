using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IUserService
    {
        Task<IEnumerable<UserProfile>> GetAllUsersAsync();
        Task<UserProfile?> GetUserByIdAsync(uint userId);
        Task<UserProfile?> GetUserByLoginAsync(string login);
        Task<bool> UpdateUserAsync(uint userId, string? login = null, string? password = null, int? role = null, string? email = null, string? phoneNumber = null, bool? isActive = null);
        Task<bool> DeleteUserAsync(uint userId);
        Task<IEnumerable<Role>> GetUserRolesAsync(uint userId);
        Task<IEnumerable<Permission>> GetUserPermissionsAsync(uint userId);
        Task<UserProfile?> GetCurrentUserAsync(string login);
        Task<UserProfile?> CreateUserAsync(string login, string password, int role, string? email = null, string? phoneNumber = null);
    }
} 