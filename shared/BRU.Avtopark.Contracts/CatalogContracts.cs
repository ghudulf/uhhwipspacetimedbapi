namespace BRU.Avtopark.Contracts;

public sealed record CreateRouteCommand(string StartPoint, string EndPoint, uint DriverId, uint BusId, string? TravelTime, bool IsActive,
    string RouteNumber = "", uint StopCount = 0, double RouteLength = 0, string? RouteDescription = null, string? RouteType = null);
public sealed record UpdateRouteCommand(uint Id, ulong? ExpectedRevision = null, string? StartPoint = null, string? EndPoint = null, uint? DriverId = null, uint? BusId = null, string? TravelTime = null, bool? IsActive = null,
    string? RouteNumber = null, uint? StopCount = null, double? RouteLength = null, string? RouteDescription = null, string? RouteType = null);
public sealed record DeleteRouteCommand(uint Id, ulong? ExpectedRevision);
public sealed record CreateEmployeeCommand(string EmployeeName, string EmployeeSurname, string EmployeePatronym, uint JobId);
public sealed record UpdateEmployeeCommand(uint Id, ulong? ExpectedRevision = null, string? EmployeeName = null, string? EmployeeSurname = null, string? EmployeePatronym = null, uint? JobId = null);
public sealed record DeleteEmployeeCommand(uint Id, ulong? ExpectedRevision);
public sealed record DriverLookupRequest(int Limit = 1000);
public sealed record DriverRow(uint EmployeeId, string Name, string Surname, string? Patronym, uint JobId, string JobTitle, string? Internship, ulong Revision);
public sealed record DriverLookupResult(DriverRow[] Items, bool Truncated);
public sealed record CreateJobCommand(string JobTitle, string JobInternship);
public sealed record UpdateJobCommand(uint Id, ulong? ExpectedRevision = null, string? JobTitle = null, string? JobInternship = null);
public sealed record DeleteJobCommand(uint Id, ulong? ExpectedRevision);
public sealed record CreateMaintenanceCommand(uint BusId, ulong LastServiceDate, string ServiceEngineer, string FoundIssues, ulong NextServiceDate, string Roadworthiness, string MaintenanceType);
public sealed record UpdateMaintenanceCommand(uint Id, ulong? ExpectedRevision = null, uint? BusId = null, ulong? LastServiceDate = null, string? ServiceEngineer = null, string? FoundIssues = null, ulong? NextServiceDate = null, string? Roadworthiness = null, string? MaintenanceType = null, string? Mileage = null);
public sealed record DeleteMaintenanceCommand(uint Id, ulong? ExpectedRevision);
public sealed record SetRouteActiveCommand(uint Id, bool Active, ulong? ExpectedRevision = null);
public sealed record CatalogPageResult<T>(T[] Items, int Page, int PageSize, int TotalCount, bool CanCreate, bool CanEdit, bool CanDelete);
public sealed record RouteRow(uint RouteId, string StartPoint, string EndPoint, uint DriverId, uint BusId, string? TravelTime, bool IsActive, ulong Revision, string? BusModel = null, string? RegistrationNumber = null, string? DriverName = null,
    string RouteNumber = "", uint StopCount = 0, double RouteLength = 0, string? RouteDescription = null, string? RouteType = null, int? TicketCount = null);
public sealed record EmployeeRow(uint EmployeeId, string Name, string Surname, string? Patronym, uint JobId, DateTimeOffset? EmployedSince, ulong Revision, string? JobTitle = null);
public sealed record JobRow(uint JobId, string JobTitle, string? Internship, ulong Revision, int? EmployeeCount = null, string? Department = null);
public sealed record MaintenanceRow(uint MaintenanceId, uint BusId, DateTimeOffset? LastServiceDate, DateTimeOffset? NextServiceDate,
    string? ServiceEngineer, string? FoundIssues, string? Roadworthiness, string? MaintenanceType, string? MileageThreshold, ulong Revision,
    string? BusModel = null, string? RegistrationNumber = null, decimal? MaintenanceCost = null, decimal? PartsCost = null, decimal? LaborCost = null, string? MaintenanceStatus = null);
