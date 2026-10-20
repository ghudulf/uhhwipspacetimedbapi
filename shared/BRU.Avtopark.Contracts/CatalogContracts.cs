namespace BRU.Avtopark.Contracts;

public sealed record CreateRouteCommand(string StartPoint, string EndPoint, uint DriverId, uint BusId, string? TravelTime, bool IsActive);
public sealed record UpdateRouteCommand(uint Id, ulong? ExpectedRevision = null, string? StartPoint = null, string? EndPoint = null, uint? DriverId = null, uint? BusId = null, string? TravelTime = null, bool? IsActive = null);
public sealed record DeleteRouteCommand(uint Id, ulong? ExpectedRevision);
public sealed record CreateEmployeeCommand(string EmployeeName, string EmployeeSurname, string EmployeePatronym, uint JobId);
public sealed record UpdateEmployeeCommand(uint Id, ulong? ExpectedRevision = null, string? EmployeeName = null, string? EmployeeSurname = null, string? EmployeePatronym = null, uint? JobId = null);
public sealed record DeleteEmployeeCommand(uint Id, ulong? ExpectedRevision);
public sealed record CreateJobCommand(string JobTitle, string JobInternship);
public sealed record UpdateJobCommand(uint Id, ulong? ExpectedRevision = null, string? JobTitle = null, string? JobInternship = null);
public sealed record DeleteJobCommand(uint Id, ulong? ExpectedRevision);
public sealed record CreateMaintenanceCommand(uint BusId, ulong LastServiceDate, string ServiceEngineer, string FoundIssues, ulong NextServiceDate, string Roadworthiness, string MaintenanceType);
public sealed record UpdateMaintenanceCommand(uint Id, ulong? ExpectedRevision = null, uint? BusId = null, ulong? LastServiceDate = null, string? ServiceEngineer = null, string? FoundIssues = null, ulong? NextServiceDate = null, string? Roadworthiness = null, string? MaintenanceType = null, string? Mileage = null);
public sealed record DeleteMaintenanceCommand(uint Id, ulong? ExpectedRevision);
public sealed record SetRouteActiveCommand(uint Id, bool Active, ulong? ExpectedRevision = null);
public sealed record RouteRow(uint RouteId, string StartPoint, string EndPoint, uint DriverId, uint BusId, string? TravelTime, bool IsActive, ulong Revision);
public sealed record EmployeeRow(uint EmployeeId, string Name, string Surname, string? Patronym, uint JobId, DateTimeOffset EmployedSince, ulong Revision);
public sealed record JobRow(uint JobId, string JobTitle, string? Internship, ulong Revision);
public sealed record MaintenanceRow(uint MaintenanceId, uint BusId, DateTimeOffset LastServiceDate, DateTimeOffset NextServiceDate,
    string ServiceEngineer, string FoundIssues, string Roadworthiness, string MaintenanceType, string? MileageThreshold, ulong Revision);
