using SpacetimeDB;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

public sealed record TicketLookup(uint TicketId, uint RouteId, uint SeatNumber, double TicketPrice, string PaymentMethod, ulong PurchaseTime, bool IsActive);
public sealed record RouteLookup(uint RouteId, string StartPoint, string EndPoint, string? TravelTime, bool IsActive);
public sealed record RoleLookup(uint RoleId, int LegacyRoleId, string Name, string? Description, bool IsActive, uint Priority, bool IsSystem);
public sealed record PermissionLookup(uint PermissionId, string Name, string? Description, string? Category, bool IsActive);
public sealed record UserRoleLookup(Identity UserId, uint RoleId);
public sealed record RolePermissionLookup(uint RoleId, uint PermissionId);
public sealed record SaleLookup(uint TicketId);
public sealed record AdminLookupSnapshot(IReadOnlyDictionary<uint, TicketLookup> Tickets, IReadOnlyDictionary<uint, RouteLookup> Routes,
    IReadOnlyDictionary<uint, RoleLookup> Roles, IReadOnlyDictionary<uint, PermissionLookup> Permissions,
    UserRoleLookup[] UserRoles, RolePermissionLookup[] RolePermissions, SaleLookup[] Sales);

/// <summary>Detached display-only snapshots for legacy controller projections, acquired on the coordinator thread.</summary>
public sealed class AdminLookupService(ISpacetimeDBService database)
{
    public Task<AdminLookupSnapshot> ReadAsync(bool tickets, CancellationToken ct = default) => database.ReadAsync(c => new AdminLookupSnapshot(
        !tickets ? new Dictionary<uint, TicketLookup>() : c.Db.Ticket.Iter().ToDictionary(t => t.TicketId, t => new TicketLookup(t.TicketId, t.RouteId, t.SeatNumber, t.TicketPrice, t.PaymentMethod, t.PurchaseTime, t.IsActive)),
        !tickets ? new Dictionary<uint, RouteLookup>() : c.Db.Route.Iter().ToDictionary(r => r.RouteId, r => new RouteLookup(r.RouteId, r.StartPoint, r.EndPoint, r.TravelTime, r.IsActive)),
        tickets ? new Dictionary<uint, RoleLookup>() : c.Db.Role.Iter().ToDictionary(r => r.RoleId, r => new RoleLookup(r.RoleId, r.LegacyRoleId, r.Name, r.Description, r.IsActive, r.Priority, r.IsSystem)),
        tickets ? new Dictionary<uint, PermissionLookup>() : c.Db.Permission.Iter().ToDictionary(p => p.PermissionId, p => new PermissionLookup(p.PermissionId, p.Name, p.Description, p.Category, p.IsActive)),
        tickets ? [] : c.Db.UserRole.Iter().Select(r => new UserRoleLookup(r.UserId, r.RoleId)).ToArray(),
        tickets ? [] : c.Db.RolePermission.Iter().Select(r => new RolePermissionLookup(r.RoleId, r.PermissionId)).ToArray(),
        !tickets ? [] : c.Db.Sale.Iter().Select(s => new SaleLookup(s.TicketId)).ToArray()), ct);
}
