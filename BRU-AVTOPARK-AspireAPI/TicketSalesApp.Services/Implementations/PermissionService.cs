using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

public class PermissionService(ISpacetimeDBService spacetimeService, ILogger<PermissionService> logger) : IPermissionService
{
    public async Task<IEnumerable<Permission>> GetAllPermissionsAsync() =>
        await spacetimeService.ReadAsync(c => c.Db.Permission.Iter().Where(p => p.IsActive).ToArray());

    public Task<Permission?> GetPermissionByIdAsync(uint permissionId) => spacetimeService.ReadAsync(c =>
        c.Db.Permission.PermissionId.Find(permissionId) is { IsActive: true } permission ? permission : null);

    public async Task<IEnumerable<Permission>> GetPermissionsByCategoryAsync(string category) =>
        await spacetimeService.ReadAsync(c => c.Db.Permission.Iter().Where(p => p.IsActive && p.Category == category).ToArray());

    public async Task<IEnumerable<string>> GetAllCategoriesAsync() =>
        await spacetimeService.ReadAsync(c => c.Db.Permission.Iter().Where(p => p.IsActive).Select(p => p.Category).Distinct().ToArray());

    public async Task<Permission?> CreatePermissionAsync(string name, string description, string category, Identity? actingUser = null)
    {
        if (await spacetimeService.ReadAsync(c => c.Db.Permission.Iter().Any(p => p.Name == name && p.IsActive))) return null;
        return await spacetimeService.ExecuteReducerAsync(new Reducer.AddNewPermission(name, description, category, actingUser),
            c => c.Db.Permission.Iter().Single(p => p.Name == name));
    }

    public async Task<bool> UpdatePermissionAsync(uint permissionId, string? name, string? description, string? category, bool? isActive, Identity? actingUser = null)
    {
        try
        {
            if (await spacetimeService.ReadAsync(c => c.Db.Permission.PermissionId.Find(permissionId)) == null) return false;
            if (name != null && await spacetimeService.ReadAsync(c => c.Db.Permission.Iter().Any(p => p.Name == name && p.PermissionId != permissionId))) return false;
            await spacetimeService.ExecuteReducerAsync(new Reducer.UpdatePermission(permissionId, name, description, category, isActive, actingUser), _ => true);
            return true;
        }
        catch (Exception ex) { logger.LogWarning("Permission update failed: {ErrorType}", ex.GetType().Name); return false; }
    }

    public async Task<bool> DeletePermissionAsync(uint permissionId, Identity? actingUser = null)
    {
        try
        {
            if (await spacetimeService.ReadAsync(c => c.Db.Permission.PermissionId.Find(permissionId)) == null || await IsPermissionInUseAsync(permissionId)) return false;
            await spacetimeService.ExecuteReducerAsync(new Reducer.DeletePermission(permissionId, actingUser), _ => true);
            return true;
        }
        catch (Exception ex) { logger.LogWarning("Permission deletion failed: {ErrorType}", ex.GetType().Name); return false; }
    }

    public Task<bool> IsPermissionInUseAsync(uint permissionId) =>
        spacetimeService.ReadAsync(c => c.Db.RolePermission.Iter().Any(rp => rp.PermissionId == permissionId));
}
