using System;
using System.Text;
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void CreateRoleReducer(ReducerContext ctx, int legacyRoleId, string name, string description, bool isSystem, uint priority)
    {
        // Check if the caller has the necessary permission (e.g., "roles.create")
        if (!HasPermission(ctx, ctx.Sender, "roles.create"))
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
            CreatedBy = ctx.Sender.ToString(),
            UpdatedBy = ctx.Sender.ToString(),
            NormalizedName = name.ToUpperInvariant()
        };
        ctx.Db.Role.Insert(role);
        Log.Info($"Created new role: {role.Name} ({role.RoleId})");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateRoleReducer(ReducerContext ctx, uint roleId, string? name, string? description, int? legacyRoleId, uint? priority)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.edit"))
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
        role.UpdatedBy = ctx.Sender.ToString(); // Set the updater
        ctx.Db.Role.RoleId.Update(role);
        Log.Info($"Role {roleId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteRoleReducer(ReducerContext ctx, uint roleId)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.delete"))
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
        Log.Info($"Role {roleId} has been deleted");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateRole(ReducerContext ctx, uint roleId, string? name, string? description, int? legacyRoleId, uint? priority)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.edit"))
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
        Log.Info($"Role {roleId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteRole(ReducerContext ctx, uint roleId)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.delete"))
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
        Log.Info($"Role {roleId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void UpdatePermission(ReducerContext ctx, uint permissionId, string? name, string? description, string? category, bool? isActive)
    {
        if (!HasPermission(ctx, ctx.Sender, "permissions.edit")) // Assuming you have a permission for this
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
        Log.Info($"Updated permission {permissionId}");
    }

    [SpacetimeDB.Reducer]
    public static void DeletePermission(ReducerContext ctx, uint permissionId)
    {
        if (!HasPermission(ctx, ctx.Sender, "permissions.delete"))
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
        Log.Info($"Permission {permissionId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void AssignRole(ReducerContext ctx, Identity userId, uint roleId)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "assign_roles"))
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
            AssignedBy = ctx.Sender.ToString() // Track who assigned the role
        };
        // Insert the new assignment into the database
        ctx.Db.UserRole.Insert(userRole);
    }

    [SpacetimeDB.Reducer]
    public static void GrantPermissionToRole(ReducerContext ctx, uint roleId, uint permissionId)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "grant_permissions"))
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
            GrantedBy = ctx.Sender.ToString()  // Track who granted the permission
        };
        // Insert the new assignment into the database
        ctx.Db.RolePermission.Insert(rolePermission);
    }

    [SpacetimeDB.Reducer]
    public static void RevokePermissionFromRole(ReducerContext ctx, uint roleId, uint permissionId)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "grant_permissions"))
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
        Log.Info($"Permission {permissionId} revoked from role {roleId} by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void RemoveRole(ReducerContext ctx, Identity userId, uint roleId)
    {
        // Authorization check - verify the sender has the required permission
        if (!HasPermission(ctx, ctx.Sender, "assign_roles"))
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
        Log.Info($"Role {roleId} removed from user {userId} by {ctx.Sender}");
    }

    // Helper method to check if a user has a specific permission
    private static bool HasPermission(ReducerContext ctx, Identity userId, string permissionName)
    {
        // Get all roles for the user
        var roleIds = ctx.Db.UserRole.Iter()
                           .Where(ur => ur.UserId == userId)  // Find all role assignments for this user
                           .Select(ur => ur.RoleId)           // Get the role IDs
                           .ToList();                         // Convert to list

        // Check if any of the user's roles have the specified permission
        var permissionIds = ctx.Db.RolePermission.Iter()
                                .Where(rp => roleIds.Contains(rp.RoleId))  // Find permissions for user's roles
                                .Select(rp => rp.PermissionId)             // Get the permission IDs
                                .ToList();                                 // Convert to list

        // Final permission check - look for the specific permission name among the user's permissions
        return ctx.Db.Permission.Iter()
                    .Where(p => permissionIds.Contains(p.PermissionId))  // Filter to user's permissions
                    .Any(p => p.Name == permissionName && p.IsActive);   // Check for matching name and active status
    }

    [SpacetimeDB.Reducer]
    public static void AddNewPermission(ReducerContext ctx, string name, string description, string category)
    {
        // Check if the caller has the necessary permission (e.g., "permissions.create")
        if (!HasPermission(ctx, ctx.Sender, "permissions.create"))
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
        Log.Info($"Created new permission: {permission.Name} ({permission.PermissionId})");
    }
}