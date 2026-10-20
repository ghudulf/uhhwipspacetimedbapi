using SpacetimeDB;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Authorization;

/// <summary>Detached, current database authorization state. Never derives access from stale token roles.</summary>
public sealed record UserAccessSnapshot(bool IsActive, bool IsAdministrator, string[] Roles, string[] Permissions)
{
    public static UserAccessSnapshot Denied { get; } = new(false, false, [], []);
    public bool Allows(string permission) => IsActive && Permissions.Contains(CanonicalPermission(permission), StringComparer.Ordinal);
    public static string CanonicalPermission(string permission) => BRU.Avtopark.Security.PermissionNames.Canonical(permission);
}

public sealed class UserAccessService(ISpacetimeDBService database)
{
    public Task<UserAccessSnapshot> GetAsync(Identity actor, CancellationToken cancellationToken = default) =>
        database.ReadAsync(connection =>
        {
            if (connection.Db.UserProfile.UserId.Find(actor)?.IsActive != true) return UserAccessSnapshot.Denied;
            var roleIds = connection.Db.UserRole.Iter().Where(r => r.UserId == actor).Select(r => r.RoleId).ToHashSet();
            var roles = connection.Db.Role.Iter().Where(r => r.IsActive && roleIds.Contains(r.RoleId)).ToArray();
            roleIds = roles.Select(r => r.RoleId).ToHashSet();
            var permissionIds = connection.Db.RolePermission.Iter().Where(p => roleIds.Contains(p.RoleId)).Select(p => p.PermissionId).ToHashSet();
            var permissions = connection.Db.Permission.Iter().Where(p => p.IsActive && permissionIds.Contains(p.PermissionId))
                .Select(p => UserAccessSnapshot.CanonicalPermission(p.Name)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return new UserAccessSnapshot(true, roles.Any(r => r.Name == "Administrator"),
                roles.Select(r => r.Name).Order(StringComparer.Ordinal).ToArray(), permissions);
        }, cancellationToken);
}
