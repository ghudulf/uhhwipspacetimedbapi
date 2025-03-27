using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Implementations
{
    public class PermissionService : IPermissionService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<PermissionService> _logger;

        public PermissionService(ISpacetimeDBService spacetimeService, ILogger<PermissionService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IEnumerable<Permission>> GetAllPermissionsAsync()
        {
            try
            {
                _logger.LogInformation("Getting all permissions");
                
                var conn = _spacetimeService.GetConnection();
                
                // Get all permissions from SpacetimeDB
                var permissions = conn.Db.Permission.Iter()
                    .Where(p => p.IsActive)
                    .ToList();
                
                _logger.LogInformation("Retrieved {Count} permissions", permissions.Count);
                return permissions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all permissions");
                throw;
            }
        }

        public async Task<Permission?> GetPermissionByIdAsync(uint permissionId)
        {
            try
            {
                _logger.LogInformation("Getting permission by ID: {PermissionId}", permissionId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Find permission by ID
                var permission = conn.Db.Permission.PermissionId.Find(permissionId);
                
                if (permission == null)
                {
                    _logger.LogWarning("Permission not found with ID: {PermissionId}", permissionId);
                    return null;
                }
                
                if (!permission.IsActive)
                {
                    _logger.LogWarning("Permission with ID {PermissionId} is not active", permissionId);
                    return null;
                }
                
                return permission;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting permission by ID: {PermissionId}", permissionId);
                throw;
            }
        }

        public async Task<IEnumerable<Permission>> GetPermissionsByCategoryAsync(string category)
        {
            try
            {
                _logger.LogInformation("Getting permissions by category: {Category}", category);
                
                var conn = _spacetimeService.GetConnection();
                
                // Get permissions by category
                var permissions = conn.Db.Permission.Iter()
                    .Where(p => p.Category == category && p.IsActive)
                    .ToList();
                
                _logger.LogInformation("Retrieved {Count} permissions for category {Category}", permissions.Count, category);
                return permissions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting permissions by category: {Category}", category);
                throw;
            }
        }

        public async Task<IEnumerable<string>> GetAllCategoriesAsync()
        {
            try
            {
                _logger.LogInformation("Getting all permission categories");
                
                var conn = _spacetimeService.GetConnection();
                
                // Get all distinct categories
                var categories = conn.Db.Permission.Iter()
                    .Where(p => p.IsActive)
                    .Select(p => p.Category)
                    .Distinct()
                    .ToList();
                
                _logger.LogInformation("Retrieved {Count} permission categories", categories.Count);
                return categories;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all permission categories");
                throw;
            }
        }

        public async Task<Permission?> CreatePermissionAsync(string name, string description, string category)
        {
            try
            {
                _logger.LogInformation("Creating new permission: {Name}", name);
                
                var conn = _spacetimeService.GetConnection();
                
                // Check if a permission with this name already exists
                var existingPermission = conn.Db.Permission.Iter()
                    .FirstOrDefault(p => p.Name == name && p.IsActive);
                
                if (existingPermission != null)
                {
                    _logger.LogWarning("Permission with name {Name} already exists", name);
                    return null;
                }
                
                // Call the AddNewPermission reducer
                conn.Reducers.AddNewPermission(name, description, category);
                
                // Wait a moment for the reducer to complete and the subscription to update
                await Task.Delay(100);
                
                // Get the newly created permission
                var newPermission = conn.Db.Permission.Iter()
                    .FirstOrDefault(p => p.Name == name);
                
                if (newPermission == null)
                {
                    _logger.LogError("Permission was not created properly");
                    return null;
                }
                
                _logger.LogInformation("Successfully created permission {Name} with ID {PermissionId}", name, newPermission.PermissionId);
                return newPermission;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating permission {Name}", name);
                throw;
            }
        }

        public async Task<bool> UpdatePermissionAsync(uint permissionId, string? name, string? description, string? category, bool? isActive)
        {
            try
            {
                _logger.LogInformation("Updating permission {PermissionId}", permissionId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Find permission by ID
                var permission = conn.Db.Permission.PermissionId.Find(permissionId);
                
                if (permission == null)
                {
                    _logger.LogWarning("Permission not found with ID: {PermissionId}", permissionId);
                    return false;
                }
                
                // Check if name is unique if provided
                if (name != null && name != permission.Name)
                {
                    var existingPermission = conn.Db.Permission.Iter()
                        .FirstOrDefault(p => p.Name == name && p.PermissionId != permissionId);
                    
                    if (existingPermission != null)
                    {
                        _logger.LogWarning("Permission with name {Name} already exists", name);
                        return false;
                    }
                }
                
                // Call the UpdatePermission reducer
                conn.Reducers.UpdatePermission(permissionId, name, description, category, isActive);
                
                _logger.LogInformation("Successfully updated permission {PermissionId}", permissionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating permission {PermissionId}", permissionId);
                return false;
            }
        }

        public async Task<bool> DeletePermissionAsync(uint permissionId)
        {
            try
            {
                _logger.LogInformation("Deleting permission {PermissionId}", permissionId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Find permission by ID
                var permission = conn.Db.Permission.PermissionId.Find(permissionId);
                
                if (permission == null)
                {
                    _logger.LogWarning("Permission not found with ID: {PermissionId}", permissionId);
                    return false;
                }
                
                // Check if the permission is in use
                var isInUse = await IsPermissionInUseAsync(permissionId);
                if (isInUse)
                {
                    _logger.LogWarning("Cannot delete permission {PermissionId} as it is in use", permissionId);
                    return false;
                }
                
                // Call the DeletePermission reducer
                conn.Reducers.DeletePermission(permissionId);
                
                _logger.LogInformation("Successfully deleted permission {PermissionId}", permissionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting permission {PermissionId}", permissionId);
                return false;
            }
        }

        public async Task<bool> IsPermissionInUseAsync(uint permissionId)
        {
            try
            {
                _logger.LogInformation("Checking if permission {PermissionId} is in use", permissionId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Check if the permission is assigned to any role
                var isInUse = conn.Db.RolePermission.Iter()
                    .Any(rp => rp.PermissionId == permissionId);
                
                _logger.LogInformation("Permission {PermissionId} is {Status}", permissionId, isInUse ? "in use" : "not in use");
                return isInUse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if permission {PermissionId} is in use", permissionId);
                throw;
            }
        }
    }
} 