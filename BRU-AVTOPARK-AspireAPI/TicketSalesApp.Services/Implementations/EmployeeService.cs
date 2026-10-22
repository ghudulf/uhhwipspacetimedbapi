using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;
namespace TicketSalesApp.Services.Implementations;

public sealed class EmployeeService(ISpacetimeDBService database, ILogger<EmployeeService> logger) : IEmployeeService
{
    private readonly CatalogMutationExecutor _mutations = new(database, logger);
    public Task<List<Employee>> GetAllEmployeesAsync() => database.ReadAsync(c => c.Db.Employee.Iter().OrderBy(e => e.EmployeeId).ToList());
    public Task<Employee?> GetEmployeeByIdAsync(uint employeeId) => database.ReadAsync(c => c.Db.Employee.EmployeeId.Find(employeeId));
    public Task<List<Employee>> GetEmployeesByJobIdAsync(uint jobId) => database.ReadAsync(c => c.Db.Employee.Iter().Where(e => e.JobId == jobId).OrderBy(e => e.EmployeeId).ToList());
    public Task<List<Job>> GetAllJobsAsync() => database.ReadAsync(c => c.Db.Job.Iter().OrderBy(j => j.JobId).ToList());
    public Task<Job?> GetJobByIdAsync(uint jobId) => database.ReadAsync(c => c.Db.Job.JobId.Find(jobId));
    public async Task<Employee?> CreateEmployeeAsync(string employeeName, string employeeSurname, string employeePatronym, uint jobId, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        return await _mutations.ExecuteResultAsync(new Reducer.CreateEmployeeV2(id, actor, employeeName, employeeSurname, employeePatronym, jobId), actor, id, (c, receipt) => c.Db.Employee.EmployeeId.Find(receipt.EntityId), cancellationToken);
    }
    public async Task<bool> UpdateEmployeeAsync(uint employeeId, string? employeeName = null, string? employeeSurname = null, string? employeePatronym = null, uint? jobId = null, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        var receipt = await _mutations.ExecuteAsync(new Reducer.UpdateEmployeeV2(id, actor, employeeId, expectedRevision, employeeName, employeeSurname, employeePatronym, jobId), actor, id, cancellationToken);
        return true;
    }
    public async Task<bool> DeleteEmployeeAsync(uint employeeId, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        var receipt = await _mutations.ExecuteAsync(new Reducer.DeleteEmployeeV2(id, actor, employeeId, expectedRevision), actor, id, cancellationToken);
        return true;
    }
    public async Task<Job?> CreateJobRecordAsync(string jobTitle, string jobInternship, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        return await _mutations.ExecuteResultAsync(new Reducer.CreateJobV2(id, actor, jobTitle, jobInternship), actor, id, (c, receipt) => c.Db.Job.JobId.Find(receipt.EntityId), cancellationToken);
    }
    public async Task<bool> CreateJobAsync(string jobTitle, string jobInternship, Identity? actingUser = null, string? operationId = null, CancellationToken cancellationToken = default) => await CreateJobRecordAsync(jobTitle, jobInternship, actingUser, operationId, cancellationToken) != null;
    public async Task<bool> UpdateJobAsync(uint jobId, string? jobTitle = null, string? jobInternship = null, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        var receipt = await _mutations.ExecuteAsync(new Reducer.UpdateJobV2(id, actor, jobId, expectedRevision, jobTitle, jobInternship), actor, id, cancellationToken);
        return true;
    }
    public async Task<bool> DeleteJobAsync(uint jobId, Identity? actingUser = null, string? operationId = null, ulong? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var actor = CatalogMutationExecutor.RequireActor(actingUser);
        var id = operationId ?? Guid.NewGuid().ToString("N");
        var receipt = await _mutations.ExecuteAsync(new Reducer.DeleteJobV2(id, actor, jobId, expectedRevision), actor, id, cancellationToken);
        return true;
    }
}
