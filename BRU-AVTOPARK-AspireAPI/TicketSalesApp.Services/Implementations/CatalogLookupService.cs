using TicketSalesApp.Services.Interfaces;
namespace TicketSalesApp.Services.Implementations;

// Legacy HTTP projection inputs. Only public display fields cross this boundary; cache enumeration
// and lookup construction occur together on the database coordinator's frame thread.
public sealed record BusLookup(uint BusId, string Model, string? RegistrationNumber);
public sealed record EmployeeLookup(uint EmployeeId, string Name, string Surname);
public sealed record JobLookup(uint JobId, string JobTitle, string? Internship);
public sealed record CatalogLookupSnapshot(IReadOnlyDictionary<uint, BusLookup> Buses,
    IReadOnlyDictionary<uint, EmployeeLookup> Employees, IReadOnlyDictionary<uint, JobLookup> Jobs);
[Flags]
public enum CatalogLookups { Buses = 1, Employees = 2, Jobs = 4 }
public sealed class CatalogLookupService(ISpacetimeDBService database)
{
    public Task<CatalogLookupSnapshot> ReadAsync(CatalogLookups requested, CancellationToken ct = default) => database.ReadAsync(c => new CatalogLookupSnapshot(
        !requested.HasFlag(CatalogLookups.Buses) ? new Dictionary<uint, BusLookup>() : c.Db.Bus.Iter().ToDictionary(b => b.BusId, b => new BusLookup(b.BusId, b.Model, b.RegistrationNumber)),
        !requested.HasFlag(CatalogLookups.Employees) ? new Dictionary<uint, EmployeeLookup>() : c.Db.Employee.Iter().ToDictionary(e => e.EmployeeId, e => new EmployeeLookup(e.EmployeeId, e.Name, e.Surname)),
        !requested.HasFlag(CatalogLookups.Jobs) ? new Dictionary<uint, JobLookup>() : c.Db.Job.Iter().ToDictionary(j => j.JobId, j => new JobLookup(j.JobId, j.JobTitle, j.Internship))), ct);
}
