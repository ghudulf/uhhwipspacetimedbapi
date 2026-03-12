using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IEmployeeService
    {
        /// <summary>
/// Retrieves all employees.
/// </summary>
/// <returns>A list of all Employee objects; an empty list if no employees exist.</returns>
Task<List<Employee>> GetAllEmployeesAsync();
        /// <summary>
/// Retrieves the employee with the specified identifier.
/// </summary>
/// <param name="employeeId">Identifier of the employee to retrieve.</param>
/// <returns>The employee with the specified ID, or null if no matching employee exists.</returns>
Task<Employee?> GetEmployeeByIdAsync(uint employeeId);
        /// <summary>
/// Retrieves all employees assigned to the specified job.
/// </summary>
/// <param name="jobId">The identifier of the job whose employees should be returned.</param>
/// <returns>A list of employees that have the given job ID; an empty list if none are found.</returns>
Task<List<Employee>> GetEmployeesByJobIdAsync(uint jobId);
        /// <summary>
/// Creates a new Employee with the specified name components and job assignment.
/// </summary>
/// <param name="employeeName">The employee's given name.</param>
/// <param name="employeeSurname">The employee's family name.</param>
/// <param name="employeePatronym">The employee's patronymic (middle name), if any.</param>
/// <param name="jobId">The identifier of the Job to assign to the new employee.</param>
/// <param name="actingUser">Optional identity of the user performing the operation for auditing.</param>
/// <returns>The created <see cref="Employee"/> instance, or `null` if creation failed.</returns>
Task<Employee?> CreateEmployeeAsync(string employeeName, string employeeSurname, string employeePatronym, uint jobId, Identity? actingUser = null);
        /// <summary>
/// Updates one or more fields of an existing employee identified by <paramref name="employeeId"/>.
/// </summary>
/// <param name="employeeId">The identifier of the employee to update.</param>
/// <param name="employeeName">New given name to set; if null, the given name is not changed.</param>
/// <param name="employeeSurname">New surname to set; if null, the surname is not changed.</param>
/// <param name="employeePatronym">New patronymic to set; if null, the patronymic is not changed.</param>
/// <param name="jobId">New job identifier to assign; if null, the job is not changed.</param>
/// <param name="actingUser">Optional identity of the user performing the update for auditing purposes.</param>
/// <returns>`true` if the employee was found and the update was applied, `false` otherwise.</returns>
Task<bool> UpdateEmployeeAsync(uint employeeId, string? employeeName = null, string? employeeSurname = null, string? employeePatronym = null, uint? jobId = null, Identity? actingUser = null);
        /// <summary>
/// Deletes the employee identified by the given ID.
/// </summary>
/// <param name="employeeId">The ID of the employee to delete.</param>
/// <param name="actingUser">Optional identity of the user performing the deletion, used for auditing.</param>
/// <returns>`true` if the employee was deleted, `false` otherwise.</returns>
Task<bool> DeleteEmployeeAsync(uint employeeId, Identity? actingUser = null);
        /// <summary>
/// Retrieves all Job records.
/// </summary>
/// <returns>A list of all Job objects.</returns>
Task<List<Job>> GetAllJobsAsync();
        Task<Job?> GetJobByIdAsync(uint jobId);
        Task<bool> CreateJobAsync(string jobTitle, string jobInternship, Identity? actingUser = null);
        Task<bool> UpdateJobAsync(uint jobId, string? jobTitle = null, string? jobInternship = null, Identity? actingUser = null);
        Task<bool> DeleteJobAsync(uint jobId, Identity? actingUser = null);
    }
} 