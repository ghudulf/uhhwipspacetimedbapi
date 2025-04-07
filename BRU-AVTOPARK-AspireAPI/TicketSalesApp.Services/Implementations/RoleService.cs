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
    public class RoleService : IRoleService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<RoleService> _logger;

        public RoleService(
            ISpacetimeDBService spacetimeService,
            ILogger<RoleService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IEnumerable<Role>> GetAllRolesAsync()
        {
            try
            {
                _logger.LogInformation("Getting all roles");
                
                var conn = _spacetimeService.GetConnection();
                
                // Get all roles from SpacetimeDB
                var roles = conn.Db.Role.Iter()
                    .Where(r => r.IsActive)
                    .ToList();
                
                _logger.LogInformation("Retrieved {Count} roles", roles.Count);
                return roles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all roles");
                throw;
            }
        }

        public async Task<Role?> GetRoleByIdAsync(uint roleId)
        {
            try
            {
                _logger.LogInformation("Getting role by ID: {RoleId}", roleId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Find role by ID
                var role = conn.Db.Role.RoleId.Find(roleId);
                
                if (role == null)
                {
                    _logger.LogWarning("Role not found with ID: {RoleId}", roleId);
                    return null;
                }
                
                if (!role.IsActive)
                {
                    _logger.LogWarning("Role with ID {RoleId} is not active", roleId);
                    return null;
                }
                
                return role;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting role by ID: {RoleId}", roleId);
                throw;
            }
        }

        public async Task<Role?> GetRoleByLegacyIdAsync(int legacyRoleId)
        {
            try
            {
                _logger.LogInformation("Getting role by legacy ID: {LegacyRoleId}", legacyRoleId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Find role by legacy ID
                var role = conn.Db.Role.Iter()
                    .FirstOrDefault(r => r.LegacyRoleId == legacyRoleId && r.IsActive);
                
                if (role == null)
                {
                    _logger.LogWarning("Role not found with legacy ID: {LegacyRoleId}", legacyRoleId);
                    return null;
                }
                
                return role;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting role by legacy ID: {LegacyRoleId}", legacyRoleId);
                throw;
            }
        }

        public async Task<IEnumerable<Permission>> GetRolePermissionsAsync(uint roleId)
        {
            try
            {
                _logger.LogInformation("Getting permissions for role: {RoleId}", roleId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Find role by ID
                var role = conn.Db.Role.RoleId.Find(roleId);
                
                if (role == null)
                {
                    _logger.LogWarning("Role not found with ID: {RoleId}", roleId);
                    return Enumerable.Empty<Permission>();
                }
                
                // Get role permissions
                var rolePermissions = conn.Db.RolePermission.Iter()
                    .Where(rp => rp.RoleId == roleId)
                    .ToList();
                
                // Get permission details
                var permissions = new List<Permission>();
                foreach (var rp in rolePermissions)
                {
                    var permission = conn.Db.Permission.PermissionId.Find(rp.PermissionId);
                    if (permission != null && permission.IsActive)
                    {
                        permissions.Add(permission);
                    }
                }
                
                _logger.LogInformation("Retrieved {Count} permissions for role {RoleId}", permissions.Count, roleId);
                return permissions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting permissions for role: {RoleId}", roleId);
                throw;
            }
        }

        public async Task<bool> AssignRoleToUserAsync(uint userId, uint roleId)
        {
            try
            {
                _logger.LogInformation("Assigning role {RoleId} to user {UserId}", roleId, userId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Find user by legacy ID
                var userProfile = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.LegacyUserId == userId);
                
                if (userProfile == null)
                {
                    _logger.LogWarning("User not found with ID: {UserId}", userId);
                    return false;
                }
                
                // Find role by ID
                var role = conn.Db.Role.RoleId.Find(roleId);
                
                if (role == null)
                {
                    _logger.LogWarning("Role not found with ID: {RoleId}", roleId);
                    return false;
                }
                
                // Check if user already has this role
                var existingUserRole = conn.Db.UserRole.Iter()
                    .FirstOrDefault(ur => ur.UserId.Equals(userProfile.UserId) && ur.RoleId == roleId);
                
                if (existingUserRole != null)
                {
                    _logger.LogInformation("User {UserId} already has role {RoleId}", userId, roleId);
                    return true;
                }
                
                // Call the AssignRole reducer
                conn.Reducers.AssignRole(userProfile.UserId, roleId);
                
                _logger.LogInformation("Successfully assigned role {RoleId} to user {UserId}", roleId, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning role {RoleId} to user {UserId}", roleId, userId);
                return false;
            }
        }

        public async Task<bool> RemoveRoleFromUserAsync(uint userId, uint roleId)
        {
            try
            {
                _logger.LogInformation("Removing role {RoleId} from user {UserId}", roleId, userId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Find user by legacy ID
                var userProfile = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.LegacyUserId == userId);
                
                if (userProfile == null)
                {
                    _logger.LogWarning("User not found with ID: {UserId}", userId);
                    return false;
                }
                
                // Find role by ID
                var role = conn.Db.Role.RoleId.Find(roleId);
                
                if (role == null)
                {
                    _logger.LogWarning("Role not found with ID: {RoleId}", roleId);
                    return false;
                }
                
                // Check if user has this role
                var existingUserRole = conn.Db.UserRole.Iter()
                    .FirstOrDefault(ur => ur.UserId.Equals(userProfile.UserId) && ur.RoleId == roleId);
                
                if (existingUserRole == null)
                {
                    _logger.LogInformation("User {UserId} does not have role {RoleId}", userId, roleId);
                    return true;
                }
                
                // Call the RemoveRole reducer
                conn.Reducers.RemoveRole(userProfile.UserId, roleId);
                
                _logger.LogInformation("Successfully removed role {RoleId} from user {UserId}", roleId, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing role {RoleId} from user {UserId}", roleId, userId);
                return false;
            }
        }

        public async Task<Role?> CreateRoleAsync(string name, string description, int legacyRoleId, uint priority, List<uint>? permissionIds, string? createdBy = null)
        {
            try
            {
                _logger.LogInformation("Creating new role: {Name}", name);
                
                var conn = _spacetimeService.GetConnection();
                
                // Check if a role with this name already exists
                var existingRole = conn.Db.Role.Iter()
                    .FirstOrDefault(r => r.Name == name && r.IsActive);
                
                if (existingRole != null)
                {
                    _logger.LogWarning("Role with name {Name} already exists", name);
                    return null;
                }
                
                // Check if a role with this legacy ID already exists
                existingRole = conn.Db.Role.Iter()
                    .FirstOrDefault(r => r.LegacyRoleId == legacyRoleId && r.IsActive);
                
                if (existingRole != null)
                {
                    _logger.LogWarning("Role with legacy ID {LegacyRoleId} already exists", legacyRoleId);
                    return null;
                }
                
                // Call the CreateRole reducer
                conn.Reducers.CreateRoleReducer(legacyRoleId, name, description, true, priority);
                
                // Wait a moment for the reducer to complete and the subscription to update
                await Task.Delay(100);
                
                // Get the newly created role
                var newRole = conn.Db.Role.Iter()
                    .FirstOrDefault(r => r.Name == name && r.LegacyRoleId == legacyRoleId);
                
                if (newRole == null)
                {
                    _logger.LogError("Role was not created properly");
                    return null;
                }
                
                // Assign permissions if needed
                if (permissionIds != null && permissionIds.Any())
                {
                    foreach (var permissionId in permissionIds)
                    {
                        try
                        {
                            // Check if permission exists
                            var permission = conn.Db.Permission.PermissionId.Find(permissionId);
                            if (permission != null && permission.IsActive)
                            {
                                // Call the AssignPermissionToRole reducer
                                conn.Reducers.GrantPermissionToRole(newRole.RoleId, permissionId);
                            }
                            else
                            {
                                _logger.LogWarning("Permission with ID {PermissionId} not found or is inactive", permissionId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error assigning permission {PermissionId} to role {RoleId}", permissionId, newRole.RoleId);
                        }
                    }
                }
                
                _logger.LogInformation("Successfully created role {Name} with ID {RoleId}", name, newRole.RoleId);
                return newRole;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating role {Name}", name);
                throw;
            }
        }

        public async Task<bool> UpdateRoleAsync(uint roleId, string? name, string? description, uint? priority, List<uint>? permissionIds, string? updatedBy = null)
        {
            try
            {
                _logger.LogInformation("Updating role {RoleId}", roleId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Find role by ID
                var role = conn.Db.Role.RoleId.Find(roleId);
                
                if (role == null)
                {
                    _logger.LogWarning("Role not found with ID: {RoleId}", roleId);
                    return false;
                }
                
                if (role.IsSystem)
                {
                    _logger.LogWarning("Cannot update system role {RoleId}", roleId);
                    return false;
                }
                
                // Check if name is unique if provided
                if (name != null && name != role.Name)
                {
                    var existingRole = conn.Db.Role.Iter()
                        .FirstOrDefault(r => r.Name == name && r.RoleId != roleId);
                    
                    if (existingRole != null)
                    {
                        _logger.LogWarning("Role with name {Name} already exists", name);
                        return false;
                    }
                }
                
                // Call the UpdateRole reducer
                conn.Reducers.UpdateRoleReducer(roleId, name, description, null, priority);
                
                // Update permissions if needed
                if (permissionIds != null)
                {
                    // First, remove any permissions that are no longer assigned
                    var existingPermissions = conn.Db.RolePermission.Iter()
                        .Where(rp => rp.RoleId == roleId)
                        .ToList();
                    
                    foreach (var rp in existingPermissions)
                    {
                        if (!permissionIds.Contains(rp.PermissionId))
                        {
                            try
                            {
                                // Check if permission exists
                                var permission = conn.Db.Permission.PermissionId.Find(rp.PermissionId);
                                if (permission != null && permission.IsActive)
                                {
                                    // Call the RemovePermissionFromRole reducer
                                    conn.Reducers.RevokePermissionFromRole(roleId, rp.PermissionId);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error removing permission {PermissionId} from role {RoleId}", rp.PermissionId, roleId);
                            }
                        }
                    }
                    
                    // Then, add any new permissions
                    foreach (var permissionId in permissionIds)
                    {
                        if (!existingPermissions.Any(rp => rp.PermissionId == permissionId))
                        {
                            try
                            {
                                // Check if permission exists
                                var permission = conn.Db.Permission.PermissionId.Find(permissionId);
                                if (permission != null && permission.IsActive)
                                {
                                    // Call the AssignPermissionToRole reducer
                                    conn.Reducers.GrantPermissionToRole(roleId, permissionId);
                                }
                                else
                                {
                                    _logger.LogWarning("Permission with ID {PermissionId} not found or is inactive", permissionId);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error assigning permission {PermissionId} to role {RoleId}", permissionId, roleId);
                            }
                        }
                    }
                }
                
                _logger.LogInformation("Successfully updated role {RoleId}", roleId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating role {RoleId}", roleId);
                return false;
            }
        }

        public async Task<bool> DeleteRoleAsync(uint roleId)
        {
            try
            {
                _logger.LogInformation("Deleting role {RoleId}", roleId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Find role by ID
                var role = conn.Db.Role.RoleId.Find(roleId);
                
                if (role == null)
                {
                    _logger.LogWarning("Role not found with ID: {RoleId}", roleId);
                    return false;
                }
                
                // Check if the role is a system role
                if (role.IsSystem)
                {
                    _logger.LogWarning("Cannot delete system role {RoleId}", roleId);
                    return false;
                }
                
                // Check if the role is assigned to any users
                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.RoleId == roleId)
                    .ToList();
                
                if (userRoles.Any())
                {
                    _logger.LogWarning("Cannot delete role {RoleId} as it is assigned to {UserCount} users", roleId, userRoles.Count);
                    return false;
                }
                
                // Call the DeleteRole reducer
                conn.Reducers.DeleteRoleReducer(roleId);
                
                _logger.LogInformation("Successfully deleted role {RoleId}", roleId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting role {RoleId}", roleId);
                return false;
            }
        }
    }
}