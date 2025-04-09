using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IEmployeeService
    {
        Task<List<Employee>> GetAllEmployeesAsync();
        Task<Employee?> GetEmployeeByIdAsync(uint employeeId);
        Task<List<Employee>> GetEmployeesByJobIdAsync(uint jobId);
        Task<bool> CreateEmployeeAsync(string employeeName, string employeeSurname, string employeePatronym, uint jobId, Identity? actingUser = null);
        Task<bool> UpdateEmployeeAsync(uint employeeId, string? employeeName = null, string? employeeSurname = null, string? employeePatronym = null, uint? jobId = null, Identity? actingUser = null);
        Task<bool> DeleteEmployeeAsync(uint employeeId, Identity? actingUser = null);
        Task<List<Job>> GetAllJobsAsync();
        Task<Job?> GetJobByIdAsync(uint jobId);
        Task<bool> CreateJobAsync(string jobTitle, string jobInternship, Identity? actingUser = null);
        Task<bool> UpdateJobAsync(uint jobId, string? jobTitle = null, string? jobInternship = null, Identity? actingUser = null);
        Task<bool> DeleteJobAsync(uint jobId, Identity? actingUser = null);
    }
} 