using System;
using System.Text;
using SpacetimeDB;

public static partial class Module
{
    /// <summary>
    /// Creates a new role in the system.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="legacyRoleId">The legacy identifier for the role.</param>
    /// <param name="name">The name of the role to be created.</param>
    /// <param name="description">A description of the role.</param>
    /// <param name="isSystem">Indicates if the role is a system role.</param>
    /// <param name="priority">The priority level of the role.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to create roles.</exception>
    /// <remarks>
    /// This method requires the "roles.create" permission to execute successfully.
    /// </remarks>
    [SpacetimeDB.Reducer]
    public static void CreateRoleReducer(ReducerContext ctx, int legacyRoleId, string name, string description, bool isSystem, uint priority, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity, not the actual user
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        // Check if the caller has the necessary permission (e.g., "roles.create")
        if (!HasPermission(ctx, effectiveUser, "roles.create")) // CreateRoleReducer PERM CHECK
        {
            throw new Exception("Unauthorized: You do not have permission to create roles.");
        }

        // Check if a role with the same name already exists
        if (ctx.Db.Role.Iter().Any(r => r.Name == name))
        {
            throw new Exception($"A role with the name '{name}' already exists.");
        }

        uint roleId = GetNextId(ctx, "roleId");

        var role = new Role
        {
            RoleId = roleId,
            LegacyRoleId = legacyRoleId,
            Name = name,
            Description = description,
            IsSystem = isSystem,
            Priority = priority,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            CreatedBy = effectiveUser.ToString(),
            UpdatedBy = effectiveUser.ToString(),
            NormalizedName = name.ToUpperInvariant()
        };
        ctx.Db.Role.Insert(role);
        Log.Info($"Created new role: {role.Name} ({role.RoleId}) by {effectiveUser}");
    }

    /// <summary>
    /// Updates an existing role in the system.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="roleId">The identifier of the role to be updated.</param>
    /// <param name="name">The new name of the role.</param>
    /// <param name="description">The new description of the role.</param>
    /// <param name="legacyRoleId">The new legacy identifier for the role.</param>
    /// <param name="priority">The new priority level of the role.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to edit roles.</exception>
    [SpacetimeDB.Reducer]
    public static void UpdateRoleReducer(ReducerContext ctx, uint roleId, string? name, string? description, int? legacyRoleId, uint? priority, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "roles.edit")) // UpdateRoleReducer PERM CHECK
        {
            throw new Exception("Unauthorized: You do not have permission to edit roles.");
        }
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null)
        {
            throw new Exception("Role not found");
        }
        // prevent from editing system roles
        if (role.IsSystem)
        {
            throw new Exception("Cannot modify a system role");
        }

        if (name != null)
        {   // Check if name is being changed and if it conflicts
            if (ctx.Db.Role.Iter().Any(r => r.Name == name && r.RoleId != roleId))
            {
                throw new Exception("Another role with this name already exists");
            }
            role.Name = name;
            role.NormalizedName = name.ToUpperInvariant();
        }
        if (description != null)
        {
            role.Description = description;
        }
        if (legacyRoleId.HasValue)
        {
            role.LegacyRoleId = legacyRoleId.Value;
        }
        if (priority.HasValue)
        {
            role.Priority = priority.Value;
        }
        role.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        role.UpdatedBy = effectiveUser.ToString(); // Set the updater
        ctx.Db.Role.RoleId.Update(role);
        Log.Info($"Role {roleId} updated by {effectiveUser}");
    }

    /// <summary>
    /// Deletes a role from the system.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="roleId">The identifier of the role to be deleted.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to delete roles.</exception>
    [SpacetimeDB.Reducer]
    public static void DeleteRoleReducer(ReducerContext ctx, uint roleId, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "roles.delete")) // DeleteRoleReducer PERM CHECK
        {
            throw new Exception("Unauthorized: Missing roles.delete permission");
        }
        // Prevent deleting system roles
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null)
        {
            throw new Exception("Role not found");
        }
        if (role.IsSystem)
        {
            throw new Exception("Cannot delete a system role");
        }
        // Remove role assignments (UserRole entries)
        var userRoles = ctx.Db.UserRole.Iter().Where(ur => ur.RoleId == roleId).ToList();
        foreach (var userRole in userRoles)
        {
            ctx.Db.UserRole.Id.Delete(userRole.Id); // Delete by unique ID
        }

        // Remove role permissions (RolePermission entries)
        var rolePermissions = ctx.Db.RolePermission.Iter().Where(rp => rp.RoleId == roleId).ToList();
        foreach (var rolePermission in rolePermissions)
        {
            ctx.Db.RolePermission.Id.Delete(rolePermission.Id); // Delete by unique ID
        }
        ctx.Db.Role.RoleId.Delete(roleId);
        Log.Info($"Role {roleId} has been deleted by {effectiveUser}");
    }

    /// <summary>
    /// Updates an existing role in the system.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="roleId">The identifier of the role to be updated.</param>
    /// <param name="name">The new name of the role.</param>
    /// <param name="description">The new description of the role.</param>
    /// <param name="legacyRoleId">The new legacy identifier for the role.</param>
    /// <param name="priority">The new priority level of the role.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to edit roles.</exception>
    [SpacetimeDB.Reducer]
    public static void UpdateRole(ReducerContext ctx, uint roleId, string? name, string? description, int? legacyRoleId, uint? priority, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "roles.edit")) // UpdateRole PERM CHECK
        {
            throw new Exception("Unauthorized: You do not have permission to edit roles.");
        }
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null)
        {
            throw new Exception("Role not found");
        }

        if (name != null)
        {
            role.Name = name;
        }
        if (description != null)
        {
            role.Description = description;
        }
        if (legacyRoleId.HasValue)
        {
            role.LegacyRoleId = legacyRoleId.Value;
        }
        if (priority.HasValue)
        {
            role.Priority = priority.Value;
        }
        role.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ctx.Db.Role.RoleId.Update(role);
        Log.Info($"Role {roleId} updated by {effectiveUser}");
    }

    /// <summary>
    /// Deletes a role from the system.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="roleId">The identifier of the role to be deleted.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to delete roles.</exception>
    [SpacetimeDB.Reducer]
    public static void DeleteRole(ReducerContext ctx, uint roleId, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "roles.delete")) // DeleteRole PERM CHECK
        {
            throw new Exception("Unauthorized: Missing roles.delete permission");
        }
        // Prevent deleting system roles
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null)
        {
            throw new Exception("Role not found");
        }
        if (role.IsSystem)
        {
            throw new Exception("Cannot delete a system role");
        }
        // Remove role assignments (UserRole entries)
        var userRoles = ctx.Db.UserRole.Iter().Where(ur => ur.RoleId == roleId).ToList();
        foreach (var userRole in userRoles)
        {
            ctx.Db.UserRole.Id.Delete(userRole.Id); // Delete by unique ID
        }

        // Remove role permissions (RolePermission entries)
        var rolePermissions = ctx.Db.RolePermission.Iter().Where(rp => rp.RoleId == roleId).ToList();
        foreach (var rolePermission in rolePermissions)
        {
            ctx.Db.RolePermission.Id.Delete(rolePermission.Id); // Delete by unique ID
        }
        ctx.Db.Role.RoleId.Delete(roleId);
        Log.Info($"Role {roleId} has been deleted by {effectiveUser}");
    }

    /// <summary>
    /// Updates an existing permission in the system.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="permissionId">The identifier of the permission to be updated.</param>
    /// <param name="name">The new name of the permission.</param>
    /// <param name="description">The new description of the permission.</param>
    /// <param name="category">The new category of the permission.</param>
    /// <param name="isActive">Indicates if the permission is active.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to edit permissions.</exception>
    [SpacetimeDB.Reducer]
    public static void UpdatePermission(ReducerContext ctx, uint permissionId, string? name, string? description, string? category, bool? isActive, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "permissions.edit")) // UpdatePermission PERM CHECK Assuming you have a permission for this
        {
            throw new Exception("Unauthorized: You do not have permission to edit permissions.");
        }

        var permission = ctx.Db.Permission.PermissionId.Find(permissionId);
        if (permission == null)
        {
            throw new Exception("Permission not found.");
        }

        // Update properties if provided (not null)
        if (name != null)
        {
            // Check for name uniqueness
            if (ctx.Db.Permission.Iter().Any(p => p.Name == name && p.PermissionId != permissionId))
            {
                throw new Exception("A permission with this name already exists.");
            }
            permission.Name = name;
        }
        if (description != null) permission.Description = description;
        if (category != null) permission.Category = category;
        if (isActive.HasValue) permission.IsActive = isActive.Value;

        ctx.Db.Permission.PermissionId.Update(permission); // Use the generated Update method.
        Log.Info($"Updated permission {permissionId} by {effectiveUser}");
    }

    /// <summary>
    /// Deletes a permission from the system.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="permissionId">The identifier of the permission to be deleted.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to delete permissions.</exception>
    [SpacetimeDB.Reducer]
    public static void DeletePermission(ReducerContext ctx, uint permissionId, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "permissions.delete")) // DeletePermission PERM CHECK
        {
            throw new Exception("Unauthorized: Missing permissions.delete permission");
        }
        // Prevent deleting if it's still assigned to any role
        if (ctx.Db.RolePermission.Iter().Any(rp => rp.PermissionId == permissionId))
        {
            throw new Exception("Cannot delete permission: it is still assigned to one or more roles.");
        }

        if (ctx.Db.Permission.PermissionId.Find(permissionId) == null)
        {
            throw new Exception("Permission not found");
        }
        ctx.Db.Permission.PermissionId.Delete(permissionId);
        Log.Info($"Permission {permissionId} has been deleted by {effectiveUser}");
    }

    /// <summary>
    /// Assigns a role to a user.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="userId">The identifier of the user to whom the role will be assigned.</param>
    /// <param name="roleId">The identifier of the role to be assigned.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to assign roles.</exception>
    [SpacetimeDB.Reducer]
    public static void AssignRole(ReducerContext ctx, Identity userId, uint roleId, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, effectiveUser, "assign_roles")) // AssignRole PERM CHECK
            throw new Exception("Unauthorized");

        // Validate that the user exists
        if (!ctx.Db.UserProfile.Iter().Any(u => u.UserId == userId))
            throw new Exception("User not found");
        // Validate that the role exists
        if (!ctx.Db.Role.Iter().Any(r => r.RoleId == roleId))
            throw new Exception("Role not found");

        // Prevent duplicate role assignments
        if (ctx.Db.UserRole.Iter().Any(ur => ur.UserId == userId && ur.RoleId == roleId))
            throw new Exception("Role already assigned");

        // Create a new user-role assignment
        var userRole = new UserRole
        {
            Id = 0, // Auto-increment will assign this
            UserId = userId,  // Set the user ID
            RoleId = roleId,  // Set the role ID
            AssignedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,  // Set assignment time
            AssignedBy = effectiveUser.ToString() // Track who assigned the role
        };
        // Insert the new assignment into the database
        ctx.Db.UserRole.Insert(userRole);
        Log.Info($"Role {roleId} assigned to user {userId} by {effectiveUser}");
    }

    /// <summary>
    /// Grants a permission to a role.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="roleId">The identifier of the role to which the permission will be granted.</param>
    /// <param name="permissionId">The identifier of the permission to be granted.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to grant permissions.</exception>
    [SpacetimeDB.Reducer]
    public static void GrantPermissionToRole(ReducerContext ctx, uint roleId, uint permissionId, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, effectiveUser, "grant_permissions")) // GrantPermissionToRole PERM CHECK
        {
            throw new Exception("Unauthorized: You do not have permission to grant permissions.");
        }

        // Validate that the role exists
        if (!ctx.Db.Role.Iter().Any(r => r.RoleId == roleId))
            throw new Exception("Role not found.");
        // Validate that the permission exists
        if (!ctx.Db.Permission.Iter().Any(p => p.PermissionId == permissionId))
            throw new Exception("Permission not found.");

        // Check for existing assignment to prevent duplicates
        if (ctx.Db.RolePermission.Iter().Any(rp => rp.RoleId == roleId && rp.PermissionId == permissionId))
            throw new Exception("Permission already granted to this role.");

        // Create a new role-permission assignment
        var rolePermission = new RolePermission
        {
            Id = 0, // Auto-increment will assign this
            RoleId = roleId,  // Set the role ID
            PermissionId = permissionId,  // Set the permission ID
            GrantedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,  // Set grant time
            GrantedBy = effectiveUser.ToString()  // Track who granted the permission
        };
        // Insert the new assignment into the database
        ctx.Db.RolePermission.Insert(rolePermission);
        Log.Info($"Permission {permissionId} granted to role {roleId} by {effectiveUser}");
    }

    /// <summary>
    /// Revokes a permission from a role.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="roleId">The identifier of the role from which the permission will be revoked.</param>
    /// <param name="permissionId">The identifier of the permission to be revoked.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to revoke permissions.</exception>
    [SpacetimeDB.Reducer]
    public static void RevokePermissionFromRole(ReducerContext ctx, uint roleId, uint permissionId, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, effectiveUser, "grant_permissions")) // RevokePermissionFromRole PERM CHECK
        {
            throw new Exception("Unauthorized: You do not have permission to revoke permissions.");
        }

        // Validate that the role exists
        if (!ctx.Db.Role.Iter().Any(r => r.RoleId == roleId))
            throw new Exception("Role not found.");
        // Validate that the permission exists
        if (!ctx.Db.Permission.Iter().Any(p => p.PermissionId == permissionId))
            throw new Exception("Permission not found.");

        // Find the role-permission assignment
        var rolePermission = ctx.Db.RolePermission.Iter()
            .FirstOrDefault(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

        // Check if the assignment exists
        if (rolePermission == null)
            throw new Exception("Permission is not granted to this role.");

        // Delete the role-permission assignment
        ctx.Db.RolePermission.Id.Delete(rolePermission.Id);
        // Log the revocation
        Log.Info($"Permission {permissionId} revoked from role {roleId} by {effectiveUser}");
    }

    /// <summary>
    /// Removes a role from a user.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="userId">The identifier of the user from whom the role will be removed.</param>
    /// <param name="roleId">The identifier of the role to be removed.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to remove roles.</exception>
    [SpacetimeDB.Reducer]
    public static void RemoveRole(ReducerContext ctx, Identity userId, uint roleId, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, effectiveUser, "assign_roles")) // RemoveRole PERM CHECK
            throw new Exception("Unauthorized: You do not have permission to remove roles.");

        // Validate that the user exists
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null)
            throw new Exception("User not found.");

        // Validate that the role exists
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null)
            throw new Exception("Role not found.");

        // Prevent removing the last admin role
        if (role.Name == "Administrator")
        {
            // Find the Administrator role
            var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
            if (adminRole != null)
            {
                // Count how many users have the admin role
                var adminCount = ctx.Db.UserRole.Iter()
                    .Where(ur => ur.RoleId == adminRole.RoleId)
                    .Select(ur => ur.UserId)
                    .Distinct()
                    .Count();

                // If this is the last admin and it's the main admin user, prevent removal
                if (adminCount <= 1 && user.Login == "admin")
                {
                    throw new Exception("Cannot remove the last administrator role.");
                }
            }
        }

        // Find the user-role assignment
        var userRole = ctx.Db.UserRole.Iter()
            .FirstOrDefault(ur => ur.UserId == userId && ur.RoleId == roleId);

        // Check if the assignment exists
        if (userRole == null)
            throw new Exception("User does not have this role.");

        // Delete the user-role assignment
        ctx.Db.UserRole.Id.Delete(userRole.Id);
        // Log the removal
        Log.Info($"Role {roleId} removed from user {userId} by {effectiveUser}");
    }

    // Helper method to check if a user has a specific permission
    /// <summary>
    /// Checks if a user has a specific permission based on their assigned roles.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="userId">The identity of the user whose permissions are being checked.</param>
    /// <param name="permissionName">The name of the permission to check for.</param>
    /// <returns>True if the user has the specified permission; otherwise, false.</returns>
    private static bool HasPermission(ReducerContext ctx, Identity userId, string permissionName)
    {
        Log.Debug($"Checking permission '{permissionName}' for user {userId}");
        
        // Get all roles for the user
        var roleIds = ctx.Db.UserRole.Iter()
                           .Where(ur => ur.UserId == userId)  // Find all role assignments for this user
                           .Select(ur => ur.RoleId)           // Get the role IDs
                           .ToList();                         // Convert to list
        
        Log.Debug($"User has {roleIds.Count} roles: {string.Join(", ", roleIds)}");
        
        // Check if any of the user's roles have the specified permission
        var permissionIds = ctx.Db.RolePermission.Iter()
                                .Where(rp => roleIds.Contains(rp.RoleId))  // Find permissions for user's roles
                                .Select(rp => rp.PermissionId)             // Get the permission IDs
                                .ToList();                                 // Convert to list
        
        Log.Debug($"User's roles have {permissionIds.Count} permissions");
        
        // Get the actual permission names for debugging
        var permissionNames = ctx.Db.Permission.Iter()
                                .Where(p => permissionIds.Contains(p.PermissionId))
                                .Select(p => $"{p.Name}({p.IsActive})")
                                .ToList();
        
        Log.Debug($"Available permissions: {string.Join(", ", permissionNames)}");
        
        // Final permission check - look for the specific permission name among the user's permissions
        bool hasPermission = ctx.Db.Permission.Iter()
                    .Where(p => permissionIds.Contains(p.PermissionId))  // Filter to user's permissions
                    .Any(p => p.Name == permissionName && p.IsActive);   // Check for matching name and active status
        
        Log.Debug($"Permission check result for '{permissionName}': {hasPermission}");
        return hasPermission;
    }

    /// <summary>
    /// Adds a new permission to the system.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="name">The name of the permission to be created.</param>
    /// <param name="description">A description of the permission.</param>
    /// <param name="category">The category of the permission.</param>
    /// <param name="actingUserId">The identity of the user acting on behalf of the request.</param>
    /// <exception cref="Exception">Thrown when the user does not have permission to create permissions.</exception>
    [SpacetimeDB.Reducer]
    public static void AddNewPermission(ReducerContext ctx, string name, string description, string category, Identity? actingUserId = null)
    {
        // Use the provided actingUserId if available, otherwise fall back to ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity, not the actual user
        Identity effectiveUser = actingUserId ?? ctx.Sender;
        
        // Check if the caller has the necessary permission (e.g., "permissions.create")
        if (!HasPermission(ctx, effectiveUser, "permissions.create")) // AddNewPermission PERM CHECK
        {
            throw new Exception("Unauthorized: You do not have permission to create permissions.");
        }

        // Check if a permission with the same name already exists
        if (ctx.Db.Permission.Iter().Any(p => p.Name == name))
        {
            throw new Exception($"A permission with the name '{name}' already exists.");
        }

        uint permissionId = GetNextId(ctx, "permissionId");
        var permission = new Permission
        {
            PermissionId = permissionId,
            Name = name,
            Description = description,
            Category = category,
            IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000
        };
        ctx.Db.Permission.Insert(permission);
        Log.Info($"Created new permission: {permission.Name} ({permission.PermissionId}) by {effectiveUser}");
    }
}