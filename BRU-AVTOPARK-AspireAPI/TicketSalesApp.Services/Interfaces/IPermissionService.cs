using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IPermissionService
    {
        Task<IEnumerable<Permission>> GetAllPermissionsAsync();
        Task<Permission?> GetPermissionByIdAsync(uint permissionId);
        Task<IEnumerable<Permission>> GetPermissionsByCategoryAsync(string category);
        Task<IEnumerable<string>> GetAllCategoriesAsync();
        Task<Permission?> CreatePermissionAsync(string name, string description, string category, Identity? actingUser = null);
        Task<bool> UpdatePermissionAsync(uint permissionId, string? name = null, string? description = null, string? category = null, bool? isActive = null, Identity? actingUser = null);
        Task<bool> DeletePermissionAsync(uint permissionId, Identity? actingUser = null);
        Task<bool> IsPermissionInUseAsync(uint permissionId);
    }
} 