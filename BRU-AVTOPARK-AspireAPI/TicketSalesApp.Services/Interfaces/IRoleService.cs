using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IRoleService
    {
        Task<IEnumerable<Role>> GetAllRolesAsync();
        Task<Role?> GetRoleByIdAsync(uint roleId);
        Task<Role?> GetRoleByLegacyIdAsync(int legacyRoleId);
        Task<IEnumerable<Permission>> GetRolePermissionsAsync(uint roleId);
        Task<bool> AssignRoleToUserAsync(uint userId, uint roleId);
        Task<bool> RemoveRoleFromUserAsync(uint userId, uint roleId);
        
        // Role management methods
        Task<Role?> CreateRoleAsync(string name, string description, int legacyRoleId, uint priority, List<uint>? permissionIds = null, string? createdBy = null);
        Task<bool> UpdateRoleAsync(uint roleId, string? name = null, string? description = null, uint? priority = null, List<uint>? permissionIds = null, string? updatedBy = null);
        Task<bool> DeleteRoleAsync(uint roleId);
    }
}