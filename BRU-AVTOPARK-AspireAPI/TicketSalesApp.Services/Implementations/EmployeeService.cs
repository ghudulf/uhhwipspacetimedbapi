using Microsoft.Extensions.Logging;
using SpacetimeDB;
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

        /// <summary>
        /// Retrieves all employees assigned to the specified job ID.
        /// </summary>
        /// <param name="jobId">The identifier of the job used to filter employees.</param>
        /// <returns>A list of employees whose JobId equals the provided jobId; an empty list if no matches are found.</returns>
        public async Task<List<Employee>> GetEmployeesByJobIdAsync(uint jobId)
        {
            try
            {
                _logger.LogInformation("Retrieving employees by job ID: {JobId}", jobId);
                var connection = _spacetimeDBService.GetConnection();
                
                // Add detailed logging before ToList()
                var filteredEmployees = connection.Db.Employee.Iter()
                    .Where(e => e.JobId == jobId);
                
                _logger.LogDebug("Employees matching JobId {JobId} before ToList():", jobId);
                foreach (var emp in filteredEmployees) // Iterate before ToList to log IDs
                {
                    _logger.LogDebug("- EmployeeId: {EmployeeId}, Name: {Surname}, JobId: {JobId}", emp.EmployeeId, emp.Surname, emp.JobId);
                    if (emp.EmployeeId == 0)
                    {
                         _logger.LogWarning("Found employee with EmployeeId = 0 and JobId = {JobId} before ToList()!", jobId);
                    }
                }
                
                var resultList = filteredEmployees.ToList();
                _logger.LogDebug("Final list count for JobId {JobId}: {Count}", jobId, resultList.Count);
                
                return resultList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees by job ID: {JobId}", jobId);
                throw;
            }
        }

        /// <summary>
        /// Creates a new employee with the specified name, surname, patronym, and job association.
        /// </summary>
        /// <param name="employeeName">The first name of the employee.</param>
        /// <param name="employeeSurname">The surname (last name) of the employee.</param>
        /// <param name="employeePatronym">The patronym (middle name) of the employee.</param>
        /// <param name="jobId">Identifier of the job to associate with the new employee.</param>
        /// <param name="actingUser">Optional identity of the user performing the operation.</param>
        /// <returns>The created <see cref="Employee"/> if the job exists and the new employee is confirmed or found; otherwise <c>null</c>.</returns>
        public async Task<Employee?> CreateEmployeeAsync(string employeeName, string employeeSurname, string employeePatronym, uint jobId, Identity? actingUser = null)
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
                    return null;
                }

                // NOTE: Correlation token flow would require modifying the SpacetimeDB reducer signature
                // to accept a correlationToken parameter and the Employee table schema to include a
                // CorrelationToken field. This would allow matching by token instead of field combination.
                // For now, we use the existing approach of matching by unique field combination.
                // TODO: Update when reducer and schema support correlation tokens:
                // var correlationToken = Guid.NewGuid().ToString();
                // connection.Reducers.CreateEmployee(employeeName, employeeSurname, employeePatronym, jobId, correlationToken);

                // Use a TaskCompletionSource to wait for the employee insert event
                var tcs = new TaskCompletionSource<Employee>();
                var timeout = TimeSpan.FromSeconds(5);

                // Subscribe to Employee table insert events to capture the newly created employee
                EventHandler<Employee>? insertHandler = null;
                insertHandler = (sender, employee) =>
                {
                    // Match the employee by the unique combination of fields we're creating
                    // TODO: When correlation token support is added, match by: employee.CorrelationToken == correlationToken
                    if (employee.Name == employeeName &&
                        employee.Surname == employeeSurname &&
                        employee.Patronym == employeePatronym &&
                        employee.JobId == jobId)
                    {
                        _logger.LogInformation("Captured newly created employee with ID: {EmployeeId}", employee.EmployeeId);
                        connection.Db.Employee.OnInsert -= insertHandler;
                        tcs.TrySetResult(employee);
                    }
                };

                connection.Db.Employee.OnInsert += insertHandler;

                try
                {
                    // Call the CreateEmployee reducer
                    connection.Reducers.CreateEmployee(employeeName, employeeSurname, employeePatronym, jobId);

                    // Wait for the insert event with timeout
                    using var cts = new CancellationTokenSource(timeout);
                    cts.Token.Register(() => tcs.TrySetCanceled());

                    var newEmployee = await tcs.Task;
                    _logger.LogInformation("Successfully created employee with ID: {EmployeeId}", newEmployee.EmployeeId);
                    return newEmployee;
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("Timeout waiting for employee creation event. Attempting fallback retrieval.");

                    // Fallback: try to find the employee by unique fields
                    var employee = connection.Db.Employee.Iter()
                        .Where(e => e.Name == employeeName &&
                                   e.Surname == employeeSurname &&
                                   e.Patronym == employeePatronym &&
                                   e.JobId == jobId)
                        .OrderByDescending(e => e.EmployeeId)
                        .FirstOrDefault();

                    if (employee != null)
                    {
                        _logger.LogInformation("Found employee via fallback with ID: {EmployeeId}", employee.EmployeeId);
                    }
                    else
                    {
                        _logger.LogWarning("Employee reducer called but could not retrieve created employee");
                    }

                    return employee;
                }
                finally
                {
                    // Always unsubscribe to prevent memory leaks
                    connection.Db.Employee.OnInsert -= insertHandler;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating employee: {Name} {Surname}", employeeName, employeeSurname);
                throw;
            }
        }

        public async Task<bool> UpdateEmployeeAsync(uint employeeId, string? employeeName = null, string? employeeSurname = null, string? employeePatronym = null, uint? jobId = null, Identity? actingUser = null)
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
                    jobId ?? employee.JobId,
                    actingUser
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating employee: {EmployeeId}", employeeId);
                throw;
            }
        }

        public async Task<bool> DeleteEmployeeAsync(uint employeeId, Identity? actingUser = null)
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
                connection.Reducers.DeleteEmployee(employeeId, actingUser);

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

        public async Task<bool> CreateJobAsync(string jobTitle, string jobInternship, Identity? actingUser = null)
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
                connection.Reducers.CreateJob(jobTitle, jobInternship); // does not need active user

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating job: {Title}", jobTitle);
                throw;
            }
        }

        public async Task<bool> UpdateJobAsync(uint jobId, string? jobTitle = null, string? jobInternship = null, Identity? actingUser = null)
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
                    jobInternship ?? job.Internship,
                    actingUser
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating job: {JobId}", jobId);
                throw;
            }
        }

        public async Task<bool> DeleteJobAsync(uint jobId, Identity? actingUser = null)
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
                connection.Reducers.DeleteJob(jobId, actingUser);

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