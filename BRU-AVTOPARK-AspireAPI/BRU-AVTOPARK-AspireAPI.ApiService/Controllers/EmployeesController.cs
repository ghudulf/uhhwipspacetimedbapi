using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Serilog;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;
using System.Text.Json;
using SpacetimeDB;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class EmployeesController : BaseController
    {
        private readonly IEmployeeService _employeeService;
        private readonly ILogger<EmployeesController> _logger;
        private readonly IAdminActionLogger _adminLogger;
        private readonly ISpacetimeDBService _spacetimeService;

        public EmployeesController(
            IEmployeeService employeeService,
            ILogger<EmployeesController> logger,
            IAdminActionLogger adminLogger,
            ISpacetimeDBService spacetimeService)
        {
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _adminLogger = adminLogger ?? throw new ArgumentNullException(nameof(adminLogger));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        }

        [HttpGet]
        //all view operations are fine to not need admin - this applies to all get type
        public async Task<ActionResult<IEnumerable<dynamic>>> GetEmployees()
        {
            _logger.LogInformation("REQUEST RECEIVED: GetEmployees");
            
            try
            {
                if (!IsAdmin() && !HasPermission("employees.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view employees");
                    return Forbid();
                }

                _logger.LogInformation("DATABASE OPERATION: GetAllEmployees");
                var employees = await _employeeService.GetAllEmployeesAsync();
                var conn = _spacetimeService.GetConnection();
                
                _logger.LogInformation("DATABASE RESULT: GetAllEmployees - Retrieved {EmployeeCount} employees", employees.Count());
                
                // Map to anonymous type with Job details
                var result = employees.Select(e => {
                    var job = conn.Db.Job.JobId.Find(e.JobId);
                    return new {
                        e.EmployeeId,
                        e.Name,
                        e.Surname,
                        e.Patronym,
                        e.JobId,
                        Job = job != null ? new { job.JobId, job.JobTitle, job.Internship } : null
                    };
                }).ToList();

                _logger.LogInformation("FULL EMPLOYEE DATA: {EmployeeData}", JsonSerializer.Serialize(result));
                
                foreach (var employee in result)
                {
                    _logger.LogDebug("Employee ID: {EmployeeId}, Name: {Name}, Surname: {Surname}, Job Title: {JobTitle}", 
                        employee.EmployeeId, employee.Name, employee.Surname, employee.Job?.JobTitle);
                }
                
                _logger.LogInformation("RESPONSE SENT: Returning {EmployeeCount} employees to client", result.Count());
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees");
                return StatusCode(500, "An error occurred while retrieving employees");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetEmployee(uint id)
        {
            _logger.LogInformation("REQUEST RECEIVED: GetEmployee with ID {EmployeeId}", id);
            
            try
            {
                if (!IsAdmin() && !HasPermission("employees.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view employee {EmployeeId}", id);
                    return Forbid();
                }

                _logger.LogInformation("DATABASE OPERATION: Fetching employee with ID {EmployeeId}", id);
                var employee = await _employeeService.GetEmployeeByIdAsync(id);
                
                if (employee == null)
                {
                    _logger.LogWarning("DATABASE RESULT: Employee with ID {EmployeeId} not found", id);
                    return NotFound();
                }
                
                var conn = _spacetimeService.GetConnection();
                var job = conn.Db.Job.JobId.Find(employee.JobId);

                // Map to anonymous type with Job details
                var result = new {
                    employee.EmployeeId,
                    employee.Name,
                    employee.Surname,
                    employee.Patronym,
                    employee.JobId,
                    Job = job != null ? new { job.JobId, job.JobTitle, job.Internship } : null
                };

                _logger.LogInformation("DATABASE RESULT: Successfully retrieved employee with ID {EmployeeId}", id);
                _logger.LogInformation("FULL EMPLOYEE DATA: {EmployeeData}", JsonSerializer.Serialize(result));
                _logger.LogInformation("RESPONSE SENT: Employee details for ID {EmployeeId}, Name: {Name}, Surname: {Surname}, Job Title: {JobTitle}", 
                    result.EmployeeId, result.Name, result.Surname, result.Job?.JobTitle);
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employee {EmployeeId}", id);
                return StatusCode(500, "An error occurred while retrieving the employee");
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult<Employee>> CreateEmployee([FromBody] CreateEmployeeModel model)
        {
            _logger.LogInformation("REQUEST RECEIVED: CreateEmployee with data: {RequestData}", JsonSerializer.Serialize(model));
            
            try
            {
                if (!IsAdmin() && !HasPermission("employees.create"))
                {
                    _logger.LogWarning("Unauthorized attempt to create employee");
                    return Forbid();
                }

                _logger.LogInformation("DATABASE OPERATION: Creating new employee: {Name} {Surname}", model.Name, model.Surname);

                var success = await _employeeService.CreateEmployeeAsync(
                    model.Name,
                    model.Surname,
                    model.Patronym ?? string.Empty,
                    model.JobId
                );

                if (!success)
                {
                    _logger.LogWarning("DATABASE RESULT: Failed to create employee");
                    return BadRequest("Failed to create employee");
                }

                // Get the newly created employee
                _logger.LogInformation("DATABASE OPERATION: Retrieving newly created employee");
                var employees = await _employeeService.GetAllEmployeesAsync();
                var employee = employees.LastOrDefault();

                if (employee == null)
                {
                    _logger.LogError("DATABASE RESULT: Employee was created but could not be retrieved");
                    return StatusCode(500, "Employee was created but could not be retrieved");
                }

                _logger.LogInformation("DATABASE RESULT: Successfully created employee with ID {EmployeeId}", employee.EmployeeId);
                _logger.LogInformation("FULL EMPLOYEE DATA: {EmployeeData}", JsonSerializer.Serialize(employee));
                
                // Get the current user ID from token
                var userId = GetUserId();
                if (userId != null)
                {
                    // Log the admin action
                    await _adminLogger.LogActionAsync(
                        userId,
                        "CreateEmployee",
                        $"Created employee with ID {employee.EmployeeId}, Name: {employee.Name} {employee.Surname}"
                    );
                }
                
                _logger.LogInformation("RESPONSE SENT: Created employee with ID {EmployeeId}", employee.EmployeeId);
                return CreatedAtAction(nameof(GetEmployee), new { id = employee.EmployeeId }, employee);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating employee");
                return StatusCode(500, "An error occurred while creating the employee");
            }
        }

        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateEmployee(uint id, [FromBody] UpdateEmployeeModel model)
        {
            _logger.LogInformation("REQUEST RECEIVED: UpdateEmployee ID {EmployeeId} with data: {RequestData}", 
                id, JsonSerializer.Serialize(model));
            
            try
            {
                if (!IsAdmin() && !HasPermission("employees.update"))
                {
                    _logger.LogWarning("Unauthorized attempt to update employee {EmployeeId}", id);
                    return Forbid();
                }

                // Get employee data before update for logging
                _logger.LogInformation("DATABASE OPERATION: Fetching employee with ID {EmployeeId} before update", id);
                var employeeBeforeUpdate = await _employeeService.GetEmployeeByIdAsync(id);
                if (employeeBeforeUpdate != null)
                {
                    _logger.LogInformation("EMPLOYEE BEFORE UPDATE: {EmployeeData}", JsonSerializer.Serialize(employeeBeforeUpdate));
                }

                _logger.LogInformation("DATABASE OPERATION: Updating employee {EmployeeId}", id);
                var success = await _employeeService.UpdateEmployeeAsync(
                    id,
                    model.Name,
                    model.Surname,
                    model.Patronym,
                    model.JobId
                );

                if (!success)
                {
                    _logger.LogWarning("DATABASE RESULT: Employee {EmployeeId} not found for update", id);
                    return NotFound();
                }

                // Get employee data after update for logging
                _logger.LogInformation("DATABASE OPERATION: Fetching employee with ID {EmployeeId} after update", id);
                var employeeAfterUpdate = await _employeeService.GetEmployeeByIdAsync(id);
                if (employeeAfterUpdate != null)
                {
                    _logger.LogInformation("EMPLOYEE AFTER UPDATE: {EmployeeData}", JsonSerializer.Serialize(employeeAfterUpdate));
                }

                // Get the current user ID from token
                var userId = GetUserId();
                if (userId != null)
                {
                    // Log the admin action
                    await _adminLogger.LogActionAsync(
                        userId,
                        "UpdateEmployee",
                        $"Updated employee with ID {id}"
                    );
                }

                _logger.LogInformation("DATABASE RESULT: Successfully updated employee {EmployeeId}", id);
                _logger.LogInformation("RESPONSE SENT: Updated employee with ID {EmployeeId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating employee {EmployeeId}", id);
                return StatusCode(500, "An error occurred while updating the employee");
            }
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteEmployee(uint id)
        {
            _logger.LogInformation("REQUEST RECEIVED: DeleteEmployee ID {EmployeeId}", id);
            
            try
            {
                if (!IsAdmin() && !HasPermission("employees.delete"))
                {
                    _logger.LogWarning("Unauthorized attempt to delete employee {EmployeeId}", id);
                    return Forbid();
                }

                // Get employee data before deletion for logging
                _logger.LogInformation("DATABASE OPERATION: Fetching employee with ID {EmployeeId} before deletion", id);
                var employeeBeforeDeletion = await _employeeService.GetEmployeeByIdAsync(id);
                if (employeeBeforeDeletion != null)
                {
                    _logger.LogInformation("EMPLOYEE BEFORE DELETION: {EmployeeData}", JsonSerializer.Serialize(employeeBeforeDeletion));
                }

                _logger.LogInformation("DATABASE OPERATION: Deleting employee {EmployeeId}", id);
                var success = await _employeeService.DeleteEmployeeAsync(id);
                if (!success)
                {
                    _logger.LogWarning("DATABASE RESULT: Employee {EmployeeId} not found for deletion", id);
                    return NotFound();
                }

                // Get the current user ID from token
                var userId = GetUserId();
                if (userId != null)
                {
                    // Log the admin action
                    await _adminLogger.LogActionAsync(
                        userId,
                        "DeleteEmployee",
                        $"Deleted employee with ID {id}"
                    );
                }

                _logger.LogInformation("DATABASE RESULT: Successfully deleted employee {EmployeeId}", id);
                _logger.LogInformation("RESPONSE SENT: Deleted employee with ID {EmployeeId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting employee {EmployeeId}", id);
                return StatusCode(500, "An error occurred while deleting the employee");
            }
        }

        [HttpGet("by-job/{jobId}")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetEmployeesByJob(uint jobId)
        {
            _logger.LogInformation("REQUEST RECEIVED: GetEmployeesByJob with JobID {JobId}", jobId);
            
            try
            {
                if (!IsAdmin() && !HasPermission("employees.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view employees by job {JobId}", jobId);
                    return Forbid();
                }

                _logger.LogInformation("DATABASE OPERATION: Fetching employees for job {JobId}", jobId);
                var employees = await _employeeService.GetEmployeesByJobIdAsync(jobId);
                var conn = _spacetimeService.GetConnection();
                var job = conn.Db.Job.JobId.Find(jobId);
                
                // Map to anonymous type
                var result = employees.Select(e => new {
                    e.EmployeeId,
                    e.Name,
                    e.Surname,
                    e.Patronym,
                    e.JobId,
                    Job = job != null ? new { job.JobId, job.JobTitle, job.Internship } : null
                }).ToList();
                
                _logger.LogInformation("DATABASE RESULT: Retrieved {Count} employees for job {JobId}", result.Count, jobId);
                _logger.LogInformation("FULL EMPLOYEE DATA FOR JOB {JobId}: {EmployeeData}", jobId, JsonSerializer.Serialize(result));
                
                foreach (var employee in result)
                {
                    _logger.LogDebug("Employee ID: {EmployeeId}, Name: {Name}, Surname: {Surname}, JobId: {JobId}", 
                        employee.EmployeeId, employee.Name, employee.Surname, employee.JobId);
                }
                
                _logger.LogInformation("RESPONSE SENT: Returning {Count} employees for job {JobId}", result.Count, jobId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees for job {JobId}", jobId);
                return StatusCode(500, "An error occurred while retrieving employees");
            }
        }

        [HttpGet("drivers")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetDrivers()
        {
            _logger.LogInformation("REQUEST RECEIVED: GetDrivers");
            
            try
            {
                if (!IsAdmin() && !HasPermission("employees.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view drivers");
                    return Forbid();
                }

                _logger.LogInformation("DATABASE OPERATION: Fetching all jobs to identify driver job");
                var jobs = await _employeeService.GetAllJobsAsync();
                _logger.LogInformation("FULL JOBS DATA: {JobsData}", JsonSerializer.Serialize(jobs));
                
                var driverJob = jobs.FirstOrDefault(j => j.JobTitle.Contains("Driver", StringComparison.OrdinalIgnoreCase));

                if (driverJob == null)
                {
                    _logger.LogWarning("DATABASE RESULT: Driver job not found in jobs list");
                    _logger.LogInformation("RESPONSE SENT: Empty array - no driver job found");
                    return Ok(Array.Empty<dynamic>());
                }

                _logger.LogInformation("DATABASE OPERATION: Fetching employees with driver job ID {JobId}", driverJob.JobId);
                var drivers = await _employeeService.GetEmployeesByJobIdAsync(driverJob.JobId);
                
                // Map to anonymous type
                var result = drivers.Select(d => new {
                    d.EmployeeId,
                    d.Name,
                    d.Surname,
                    d.Patronym,
                    d.JobId,
                    Job = new { driverJob.JobId, driverJob.JobTitle, driverJob.Internship }
                }).ToList();
                
                _logger.LogInformation("DATABASE RESULT: Retrieved {Count} drivers", result.Count);
                _logger.LogInformation("FULL DRIVERS DATA: {DriversData}", JsonSerializer.Serialize(result));
                
                foreach (var driver in result)
                {
                    _logger.LogDebug("Driver ID: {EmployeeId}, Name: {Name}, Surname: {Surname}", 
                        driver.EmployeeId, driver.Name, driver.Surname);
                }
                
                _logger.LogInformation("RESPONSE SENT: Returning {Count} drivers to client", result.Count);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving drivers");
                return StatusCode(500, "An error occurred while retrieving drivers");
            }
        }
    }

    public class CreateEmployeeModel
    {
        public required string Name { get; set; }
        public required string Surname { get; set; }
        public string? Patronym { get; set; }
        public required uint JobId { get; set; }
    }

    public class UpdateEmployeeModel
    {
        public string? Name { get; set; }
        public string? Surname { get; set; }
        public string? Patronym { get; set; }
        public uint? JobId { get; set; }
    }
}