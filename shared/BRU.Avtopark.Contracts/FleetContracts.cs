namespace BRU.Avtopark.Contracts;

public sealed record BusRow(uint BusId, string Model, string? RegistrationNumber, bool IsActive, string BusType,
    uint Capacity, uint Year, string? Vin, string? LicensePlate, string? CurrentStatus, int? ActiveRouteCount, int? MaintenanceCount, ulong Revision);
public sealed record BusQuery(int Page = 1, int PageSize = 100, string? Search = null, bool? Active = null);
public sealed record CreateBusCommand(string Model, string? RegistrationNumber = null, BusDetails? Details = null);
public sealed record UpdateBusCommand(uint Id, ulong? ExpectedRevision, string? Model = null, string? RegistrationNumber = null, BusDetails? Details = null);
public sealed record DeleteBusCommand(uint Id, ulong? ExpectedRevision = null);
public sealed record SetBusActiveCommand(uint Id, bool Active, ulong? ExpectedRevision = null);

public sealed record BusDetails(uint? Capacity = null, string? BusType = null, uint? Year = null, string? Vin = null,
    string? LicensePlate = null, string? CurrentStatus = null, bool? IsActive = null);

public sealed record BusPageResult(BusRow[] Items, int Page, int PageSize, int TotalCount, bool CanCreate, bool CanEdit, bool CanDelete);
