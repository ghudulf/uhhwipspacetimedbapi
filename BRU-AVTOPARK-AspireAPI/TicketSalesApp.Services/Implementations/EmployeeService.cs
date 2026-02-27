using Microsoft.Extensions.Logging;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class EmployeeService : IEmployeeService
    {
        private readonly ISpacetimeDBService _spacetimeDBService;
        private readonly ILogger<EmployeeService> _logger;

        public EmployeeService(ISpacetimeDBService spacetimeDBService, ILogger<EmployeeService> logger)
        {
            _spacetimeDBService = spacetimeDBService ?? throw new ArgumentNullException(nameof(spacetimeDBService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<Employee>> GetAllEmployeesAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all employees");
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Employee.Iter().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all employees");
                throw;
            }
        }

        public async Task<Employee?> GetEmployeeByIdAsync(uint employeeId)
        {
            try
            {
                _logger.LogInformation("Retrieving employee by ID: {EmployeeId}", employeeId);
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Employee.Iter()
                    .FirstOrDefault(e => e.EmployeeId == employeeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employee by ID: {EmployeeId}", employeeId);
                throw;
            }
        }

        public async Task<List<Employee>> GetEmployeesByJobIdAsync(uint jobId)
        {
            try
            {
                _logger.LogInformation("Retrieving employees by job ID: {JobId}", jobId);
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Employee.Iter()
                    .Where(e => e.JobId == jobId)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees by job ID: {JobId}", jobId);
                throw;
            }
        }

        public async Task<bool> CreateEmployeeAsync(string employeeName, string employeeSurname, string employeePatronym, uint jobId)
        {
            try
            {
                _logger.LogInformation("Creating new employee: {Name} {Surname}", employeeName, employeeSurname);
                var connection = _spacetimeDBService.GetConnection();
                
                var job = connection.Db.Job.Iter()
                    .FirstOrDefault(j => j.JobId == jobId);
                if (job == null)
                {
                    _logger.LogWarning("Job not found: {JobId}", jobId);
                    return false;
                }

                // Call the CreateEmployee reducer
                connection.Reducers.CreateEmployee(employeeName, employeeSurname, employeePatronym, jobId);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating employee: {Name} {Surname}", employeeName, employeeSurname);
                throw;
            }
        }

        public async Task<bool> UpdateEmployeeAsync(uint employeeId, string? employeeName = null, string? employeeSurname = null, string? employeePatronym = null, uint? jobId = null)
        {
            try
            {
                _logger.LogInformation("Updating employee: {EmployeeId}", employeeId);
                var connection = _spacetimeDBService.GetConnection();
                
                var employee = connection.Db.Employee.Iter()
                    .FirstOrDefault(e => e.EmployeeId == employeeId);
                if (employee == null)
                {
                    _logger.LogWarning("Employee not found: {EmployeeId}", employeeId);
                    return false;
                }

                if (jobId.HasValue)
                {
                    var job = connection.Db.Job.Iter()
                        .FirstOrDefault(j => j.JobId == jobId);
                    if (job == null)
                    {
                        _logger.LogWarning("Job not found: {JobId}", jobId);
                        return false;
                    }
                }

                // Call the UpdateEmployee reducer
                connection.Reducers.UpdateEmployee(
                    employeeId,
                    employeeName ?? employee.Name,
                    employeeSurname ?? employee.Surname,
                    employeePatronym ?? employee.Patronym,
                    jobId ?? employee.JobId
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating employee: {EmployeeId}", employeeId);
                throw;
            }
        }

        public async Task<bool> DeleteEmployeeAsync(uint employeeId)
        {
            try
            {
                _logger.LogInformation("Deleting employee: {EmployeeId}", employeeId);
                var connection = _spacetimeDBService.GetConnection();
                
                var employee = connection.Db.Employee.Iter()
                    .FirstOrDefault(e => e.EmployeeId == employeeId);
                if (employee == null)
                {
                    _logger.LogWarning("Employee not found: {EmployeeId}", employeeId);
                    return false;
                }

                // Check if employee is assigned to routes
                var routes = connection.Db.Route.Iter()
                    .Where(r => r.DriverId == employeeId)
                    .ToList();
                if (routes.Any())
                {
                    _logger.LogWarning("Cannot delete employee {EmployeeId} as they are assigned to routes", employeeId);
                    return false;
                }

                // Call the DeleteEmployee reducer
                connection.Reducers.DeleteEmployee(employeeId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting employee: {EmployeeId}", employeeId);
                throw;
            }
        }

        public async Task<List<Job>> GetAllJobsAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all jobs");
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Job.Iter().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all jobs");
                throw;
            }
        }

        public async Task<Job?> GetJobByIdAsync(uint jobId)
        {
            try
            {
                _logger.LogInformation("Retrieving job by ID: {JobId}", jobId);
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Job.Iter()
                    .FirstOrDefault(j => j.JobId == jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving job by ID: {JobId}", jobId);
                throw;
            }
        }

        public async Task<bool> CreateJobAsync(string jobTitle, string jobInternship)
        {
            try
            {
                _logger.LogInformation("Creating new job: {Title}", jobTitle);
                var connection = _spacetimeDBService.GetConnection();
                
                var existingJob = connection.Db.Job.Iter()
                    .FirstOrDefault(j => j.JobTitle == jobTitle);
                if (existingJob != null)
                {
                    _logger.LogWarning("Job already exists with title: {Title}", jobTitle);
                    return false;
                }

                // Call the CreateJob reducer
                connection.Reducers.CreateJob(jobTitle, jobInternship);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating job: {Title}", jobTitle);
                throw;
            }
        }

        public async Task<bool> UpdateJobAsync(uint jobId, string? jobTitle = null, string? jobInternship = null)
        {
            try
            {
                _logger.LogInformation("Updating job: {JobId}", jobId);
                var connection = _spacetimeDBService.GetConnection();
                
                var job = connection.Db.Job.Iter()
                    .FirstOrDefault(j => j.JobId == jobId);
                if (job == null)
                {
                    _logger.LogWarning("Job not found: {JobId}", jobId);
                    return false;
                }

                if (jobTitle != null)
                {
                    var existingJob = connection.Db.Job.Iter()
                        .FirstOrDefault(j => j.JobTitle == jobTitle && j.JobId != jobId);
                    if (existingJob != null)
                    {
                        _logger.LogWarning("Job already exists with title: {Title}", jobTitle);
                        return false;
                    }
                }

                // Call the UpdateJob reducer
                connection.Reducers.UpdateJob(
                    jobId,
                    jobTitle ?? job.JobTitle,
                    jobInternship ?? job.Internship
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating job: {JobId}", jobId);
                throw;
            }
        }

        public async Task<bool> DeleteJobAsync(uint jobId)
        {
            try
            {
                _logger.LogInformation("Deleting job: {JobId}", jobId);
                var connection = _spacetimeDBService.GetConnection();
                
                var job = connection.Db.Job.Iter()
                    .FirstOrDefault(j => j.JobId == jobId);
                if (job == null)
                {
                    _logger.LogWarning("Job not found: {JobId}", jobId);
                    return false;
                }

                // Check if job has employees
                var employees = connection.Db.Employee.Iter()
                    .Where(e => e.JobId == jobId)
                    .ToList();
                if (employees.Any())
                {
                    _logger.LogWarning("Cannot delete job {JobId} as it has employees assigned", jobId);
                    return false;
                }

                // Call the DeleteJob reducer
                connection.Reducers.DeleteJob(jobId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting job: {JobId}", jobId);
                throw;
            }
        }
    }
} 