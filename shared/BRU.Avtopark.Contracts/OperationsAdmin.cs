namespace BRU.Avtopark.Contracts;

public sealed record TicketRow(uint Id, uint RouteId, string? RouteLabel, uint SeatNumber, decimal? TicketPrice, string PaymentMethod, DateTimeOffset? PurchaseTime, bool IsActive, ulong Revision);
public sealed record ScheduleRow(uint Id, uint RouteId, string? RouteLabel, string? BusLabel, string? DriverLabel, DateTimeOffset? DepartureTime, DateTimeOffset? ArrivalTime, decimal? Price, uint AvailableSeats, bool IsActive, ulong Revision);
public sealed record UserRow(uint Id, string Identity, string Login, string? Email, string? PhoneNumber, bool IsActive, DateTimeOffset? CreatedAt, DateTimeOffset? LastLoginAt, string[] Roles, ulong Revision);
public sealed record RoleRow(uint Id, int LegacyRoleId, string Name, string? Description, bool IsSystem, uint Priority, bool IsActive, uint[] PermissionIds, string[] Permissions, int UserCount, ulong Revision);
public sealed record PermissionRow(uint Id, string Name, string? Description, string? Category, bool IsActive, ulong Revision);

public sealed record CreateTicketCommand(uint RouteId, double TicketPrice, uint SeatNumber, string PaymentMethod, ulong? PurchaseTime = null);
public sealed record UpdateTicketCommand(uint Id, uint? RouteId = null, double? TicketPrice = null, uint? SeatNumber = null, string? PaymentMethod = null, bool? IsActive = null, ulong? ExpectedRevision = null);
public sealed record DeleteTicketCommand(uint Id, ulong? ExpectedRevision = null);

public sealed record CreateScheduleCommand(uint RouteId, ulong DepartureTime, double Price, uint AvailableSeats, string[]? DaysOfWeek = null, string? StartPoint = null, string? EndPoint = null,
    string[]? RouteStops = null, ulong? ArrivalTime = null, uint? StopDurationMinutes = null, bool? IsRecurring = null, string[]? EstimatedStopTimes = null, double[]? StopDistances = null, string? Notes = null, string[]? BusTypes = null);
public sealed record UpdateScheduleCommand(uint Id, uint? RouteId = null, ulong? DepartureTime = null, double? Price = null, uint? AvailableSeats = null, string[]? DaysOfWeek = null,
    string? StartPoint = null, string? EndPoint = null, string[]? RouteStops = null, ulong? ArrivalTime = null, uint? StopDurationMinutes = null, bool? IsRecurring = null,
    string[]? EstimatedStopTimes = null, double[]? StopDistances = null, string? Notes = null, string[]? BusTypes = null, ulong? ExpectedRevision = null);
public sealed record DeleteScheduleCommand(uint Id, ulong? ExpectedRevision = null);
public sealed record SetScheduleActiveCommand(uint Id, bool Active, ulong? ExpectedRevision = null);

// Input only. Never include this command in receipts, diagnostics, traces or response projections.
public sealed record CreateUserCommand(string Login, string InitialCredential, string? Email = null, string? PhoneNumber = null, uint? RoleId = null);
public sealed record UpdateUserCommand(uint Id, string? Email = null, string? PhoneNumber = null, ulong? ExpectedRevision = null);
public sealed record SetUserActiveCommand(uint Id, bool Active, ulong? ExpectedRevision = null);
public sealed record DeleteUserCommand(uint Id, ulong? ExpectedRevision = null);
public sealed record AssignUserRoleCommand(uint Id, uint RoleId, ulong? ExpectedRevision = null);
public sealed record RemoveUserRoleCommand(uint Id, uint RoleId, ulong? ExpectedRevision = null);

public sealed record CreateRoleCommand(string Name, string? Description = null, int LegacyRoleId = 0, bool IsSystem = false, uint Priority = 0, uint[]? PermissionIds = null);
public sealed record UpdateRoleCommand(uint Id, string? Name = null, string? Description = null, int? LegacyRoleId = null, uint? Priority = null, ulong? ExpectedRevision = null, uint[]? PermissionIds = null);
public sealed record DeleteRoleCommand(uint Id, ulong? ExpectedRevision = null);
public sealed record GrantRolePermissionCommand(uint Id, uint PermissionId, ulong? ExpectedRevision = null);
public sealed record RevokeRolePermissionCommand(uint Id, uint PermissionId, ulong? ExpectedRevision = null);

public sealed record CreatePermissionCommand(string Name, string? Description = null, string? Category = null);
public sealed record UpdatePermissionCommand(uint Id, string? Name = null, string? Description = null, string? Category = null, bool? IsActive = null, ulong? ExpectedRevision = null);
public sealed record DeletePermissionCommand(uint Id, ulong? ExpectedRevision = null);
