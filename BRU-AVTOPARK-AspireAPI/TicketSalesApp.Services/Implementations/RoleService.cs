using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

public class RoleService(ISpacetimeDBService spacetimeService, ILogger<RoleService> logger) : IRoleService
{
    public async Task<IEnumerable<Role>> GetAllRolesAsync() =>
        await spacetimeService.ReadAsync(c => c.Db.Role.Iter().Where(r => r.IsActive).ToArray());

    public Task<Role?> GetRoleByIdAsync(uint roleId) => spacetimeService.ReadAsync(c =>
        c.Db.Role.RoleId.Find(roleId) is { IsActive: true } role ? role : null);

    public Task<Role?> GetRoleByLegacyIdAsync(int legacyRoleId) => spacetimeService.ReadAsync(c =>
        c.Db.Role.Iter().FirstOrDefault(r => r.LegacyRoleId == legacyRoleId && r.IsActive));

    public async Task<IEnumerable<Permission>> GetRolePermissionsAsync(uint roleId) => await spacetimeService.ReadAsync(c =>
    {
        if (c.Db.Role.RoleId.Find(roleId) == null) return Array.Empty<Permission>();
        return c.Db.RolePermission.RoleId.Filter(roleId).Select(rp => c.Db.Permission.PermissionId.Find(rp.PermissionId))
            .OfType<Permission>().Where(p => p.IsActive).ToArray();
    });

    public async Task<bool> AssignRoleToUserAsync(uint userId, uint roleId)
    {
        try
        {
            var user = await spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter().FirstOrDefault(u => u.LegacyUserId == userId));
            if (user == null || await GetRoleByIdAsync(roleId) == null) return false;
            if (await spacetimeService.ReadAsync(c => c.Db.UserRole.UserId.Filter(user.UserId).Any(r => r.RoleId == roleId))) return true;
            await spacetimeService.ExecuteReducerAsync(new Reducer.AssignRole(user.UserId, roleId, user.UserId), _ => true);
            return true;
        }
        catch (Exception ex) { logger.LogWarning("Role assignment failed: {ErrorType}", ex.GetType().Name); return false; }
    }

    public async Task<bool> RemoveRoleFromUserAsync(uint userId, uint roleId)
    {
        try
        {
            var user = await spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter().FirstOrDefault(u => u.LegacyUserId == userId));
            if (user == null || await GetRoleByIdAsync(roleId) == null) return false;
            if (!await spacetimeService.ReadAsync(c => c.Db.UserRole.UserId.Filter(user.UserId).Any(r => r.RoleId == roleId))) return true;
            await spacetimeService.ExecuteReducerAsync(new Reducer.RemoveRole(user.UserId, roleId, user.UserId), _ => true);
            return true;
        }
        catch (Exception ex) { logger.LogWarning("Role removal failed: {ErrorType}", ex.GetType().Name); return false; }
    }

    public async Task<Role?> CreateRoleAsync(string name, string description, int legacyRoleId, uint priority, List<uint>? permissionIds, string? createdBy = null, Identity? actingUser = null)
    {
        if (await spacetimeService.ReadAsync(c => c.Db.Role.Iter().Any(r => r.IsActive && (r.Name == name || r.LegacyRoleId == legacyRoleId)))) return null;
        var actor = actingUser ?? spacetimeService.GetLocalIdentity() ?? throw new DatabaseUnavailableException("Database identity unavailable.");
        // Existing v2 reducer commits role metadata and its permission set atomically.
        return await spacetimeService.ExecuteReducerAsync(new Reducer.CreateRoleV2(Guid.NewGuid().ToString("N"), actor,
            legacyRoleId, name, description, true, priority, permissionIds),
            c => c.Db.Role.Iter().Single(r => r.Name == name && r.LegacyRoleId == legacyRoleId));
    }

    public async Task<bool> UpdateRoleAsync(uint roleId, string? name, string? description, uint? priority, List<uint>? permissionIds, string? updatedBy = null, Identity? actingUser = null)
    {
        try
        {
            var role = await GetRoleByIdAsync(roleId);
            if (role == null || role.IsSystem) return false;
            if (name != null && await spacetimeService.ReadAsync(c => c.Db.Role.Iter().Any(r => r.Name == name && r.RoleId != roleId))) return false;
            var actor = actingUser ?? spacetimeService.GetLocalIdentity() ?? throw new DatabaseUnavailableException("Database identity unavailable.");
            await spacetimeService.ExecuteReducerAsync(new Reducer.UpdateRoleV2(Guid.NewGuid().ToString("N"), actor,
                roleId, null, name, description, null, priority, permissionIds), _ => true);
            return true;
        }
        catch (Exception ex) { logger.LogWarning("Role update failed: {ErrorType}", ex.GetType().Name); return false; }
    }

    public async Task<bool> DeleteRoleAsync(uint roleId, Identity? actingUser = null)
    {
        try
        {
            var role = await GetRoleByIdAsync(roleId);
            if (role == null || role.IsSystem || await spacetimeService.ReadAsync(c => c.Db.UserRole.Iter().Any(ur => ur.RoleId == roleId))) return false;
            await spacetimeService.ExecuteReducerAsync(new Reducer.DeleteRoleReducer(roleId, actingUser), _ => true);
            return true;
        }
        catch (Exception ex) { logger.LogWarning("Role deletion failed: {ErrorType}", ex.GetType().Name); return false; }
    }
}
