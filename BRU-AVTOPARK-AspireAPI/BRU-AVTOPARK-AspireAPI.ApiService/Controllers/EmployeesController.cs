using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmployeesController : BaseController
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<EmployeesController> _logger;

        public EmployeesController(
            ISpacetimeDBService spacetimeService,
            ILogger<EmployeesController> logger)
        {
            _spacetimeService = spacetimeService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmployeeWithJob>>> GetEmployees()
        {
            try
            {
                _logger.LogInformation("Fetching all employees");
                
                var conn = _spacetimeService.GetConnection();
                
                // Get all employees
                var employees = conn.Db.Employee.Iter().ToList();
                
                // Get all jobs for joining
                var jobs = conn.Db.Job.Iter().ToList();
                
                // Create result with job details
                var result = employees.Select(e => new EmployeeWithJob
                {
                    EmployeeId = e.EmployeeId,
                    Name = e.Name,
                    Surname = e.Surname,
                    Patronym = e.Patronym,
                    EmployedSince = e.EmployedSince,
                    JobId = e.JobId,
                    Job = jobs.FirstOrDefault(j => j.JobId == e.JobId)
                }).ToList();
                
                _logger.LogDebug("Retrieved {EmployeeCount} employees", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees");
                return StatusCode(500, "An error occurred while retrieving employees");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<EmployeeWithJob>> GetEmployee(uint id)
        {
            try
            {
                _logger.LogInformation("Fetching employee with ID {EmployeeId}", id);
                
                var conn = _spacetimeService.GetConnection();
                
                // Get employee by ID
                var employee = conn.Db.Employee.EmployeeId.Find(id);
                
                if (employee == null)
                {
                    _logger.LogWarning("Employee with ID {EmployeeId} not found", id);
                    return NotFound();
                }
                
                // Get job details
                var job = conn.Db.Job.JobId.Find(employee.JobId);
                
                var result = new EmployeeWithJob
                {
                    EmployeeId = employee.EmployeeId,
                    Name = employee.Name,
                    Surname = employee.Surname,
                    Patronym = employee.Patronym,
                    EmployedSince = employee.EmployedSince,
                    JobId = employee.JobId,
                    Job = job
                };
                
                _logger.LogDebug("Successfully retrieved employee with ID {EmployeeId}", id);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employee with ID {EmployeeId}", id);
                return StatusCode(500, $"An error occurred while retrieving employee with ID {id}");
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<Employee>> CreateEmployee([FromBody] CreateEmployeeModel model)
        {
            try
            {
                _logger.LogInformation("Creating new employee {Name} {Surname}", model.Name, model.Surname);
                
                var conn = _spacetimeService.GetConnection();
                
                // Check if job exists
                var job = conn.Db.Job.JobId.Find(model.JobId);
                if (job == null)
                {
                    _logger.LogWarning("Invalid job ID {JobId} provided for employee creation", model.JobId);
                    return BadRequest("Invalid job ID");
                }
                
                // Create employee using reducer
                conn.Reducers.CreateEmployee(
                    model.Name,
                    model.Surname,
                    model.Patronym ?? string.Empty,
                    model.JobId
                );
                
                // Wait a moment for the reducer to complete
                await Task.Delay(100);
                
                // Get the newly created employee
                var employee = conn.Db.Employee.Iter()
                    .OrderByDescending(e => e.EmployeeId)
                    .FirstOrDefault();
                
                if (employee == null)
                {
                    _logger.LogError("Failed to create employee");
                    return StatusCode(500, "Failed to create employee");
                }
                
                _logger.LogInformation("Successfully created employee with ID {EmployeeId}", employee.EmployeeId);
                return CreatedAtAction(nameof(GetEmployee), new { id = employee.EmployeeId }, employee);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating employee");
                return StatusCode(500, $"An error occurred while creating employee: {ex.Message}");
            }
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateEmployee(uint id, [FromBody] UpdateEmployeeModel model)
        {
            try
            {
                _logger.LogInformation("Updating employee with ID {EmployeeId}", id);
                
                var conn = _spacetimeService.GetConnection();
                
                // Check if employee exists
                var employee = conn.Db.Employee.EmployeeId.Find(id);
                if (employee == null)
                {
                    _logger.LogWarning("Employee with ID {EmployeeId} not found for update", id);
                    return NotFound();
                }
                
                // Check if job exists if JobId is provided
                if (model.JobId.HasValue)
                {
                    var job = conn.Db.Job.JobId.Find(model.JobId.Value);
                    if (job == null)
                    {
                        _logger.LogWarning("Invalid job ID {JobId} provided for employee update", model.JobId.Value);
                        return BadRequest("Invalid job ID");
                    }
                }
                
                // Update employee using reducer
                conn.Reducers.UpdateEmployee(
                    id,
                    model.Name,
                    model.Surname,
                    model.Patronym,
                    model.JobId
                );
                
                _logger.LogInformation("Successfully updated employee with ID {EmployeeId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating employee with ID {EmployeeId}", id);
                return StatusCode(500, $"An error occurred while updating employee: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteEmployee(uint id)
        {
            try
            {
                _logger.LogInformation("Deleting employee with ID {EmployeeId}", id);
                
                var conn = _spacetimeService.GetConnection();
                
                // Check if employee exists
                var employee = conn.Db.Employee.EmployeeId.Find(id);
                if (employee == null)
                {
                    _logger.LogWarning("Employee with ID {EmployeeId} not found for deletion", id);
                    return NotFound();
                }
                
                // Check if there are any routes where this employee is a driver
                var hasRoutes = conn.Db.Route.Iter().Any(r => r.DriverId == id && r.IsActive);
                if (hasRoutes)
                {
                    _logger.LogWarning("Cannot delete employee with ID {EmployeeId} because they are assigned as a driver to active routes", id);
                    return BadRequest("Cannot delete employee because they are assigned as a driver to active routes");
                }
                
                // Check if there are any maintenance records where this employee is a technician
                var hasMaintenance = conn.Db.Maintenance.Iter().Any(m => m.ServiceEngineer == $"{employee.Surname} {employee.Name.Substring(0, 1)}.{employee.Patronym?.Substring(0, 1) ?? ""}.");
                if (hasMaintenance)
                {
                    _logger.LogWarning("Cannot delete employee with ID {EmployeeId} because they have maintenance records", id);
                    return BadRequest("Cannot delete employee because they have maintenance records");
                }
                
                // Delete employee using reducer
                conn.Reducers.DeleteEmployee(id);
                
                _logger.LogInformation("Successfully deleted employee with ID {EmployeeId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting employee with ID {EmployeeId}", id);
                return StatusCode(500, $"An error occurred while deleting employee: {ex.Message}");
            }
        }

        [HttpGet("by-job/{jobId}")]
        public async Task<ActionResult<IEnumerable<EmployeeWithJob>>> GetEmployeesByJob(uint jobId)
        {
            try
            {
                _logger.LogInformation("Fetching employees for job with ID {JobId}", jobId);
                
                var conn = _spacetimeService.GetConnection();
                
                // Check if job exists
                var job = conn.Db.Job.JobId.Find(jobId);
                if (job == null)
                {
                    _logger.LogWarning("Job with ID {JobId} not found", jobId);
                    return NotFound();
                }
                
                // Get employees for the job
                var employees = conn.Db.Employee.Iter()
                    .Where(e => e.JobId == jobId)
                    .ToList();
                
                // Create result with job details
                var result = employees.Select(e => new EmployeeWithJob
                {
                    EmployeeId = e.EmployeeId,
                    Name = e.Name,
                    Surname = e.Surname,
                    Patronym = e.Patronym,
                    EmployedSince = e.EmployedSince,
                    JobId = e.JobId,
                    Job = job
                }).ToList();
                
                _logger.LogDebug("Found {EmployeeCount} employees for job with ID {JobId}", 
                    result.Count, jobId);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees for job with ID {JobId}", jobId);
                return StatusCode(500, $"An error occurred while retrieving employees for job with ID {jobId}");
            }
        }

        [HttpGet("drivers")]
        public async Task<ActionResult<IEnumerable<EmployeeWithJob>>> GetDrivers()
        {
            try
            {
                _logger.LogInformation("Fetching all drivers");
                
                var conn = _spacetimeService.GetConnection();
                
                // Get driver job
                var driverJob = conn.Db.Job.Iter()
                    .FirstOrDefault(j => j.JobTitle.Contains("Водитель", StringComparison.OrdinalIgnoreCase));
                
                if (driverJob == null)
                {
                    _logger.LogWarning("Driver job not found");
                    return new List<EmployeeWithJob>();
                }
                
                // Get all drivers
                var drivers = conn.Db.Employee.Iter()
                    .Where(e => e.JobId == driverJob.JobId)
                    .ToList();
                
                // Create result with job details
                var result = drivers.Select(e => new EmployeeWithJob
                {
                    EmployeeId = e.EmployeeId,
                    Name = e.Name,
                    Surname = e.Surname,
                    Patronym = e.Patronym,
                    EmployedSince = e.EmployedSince,
                    JobId = e.JobId,
                    Job = driverJob
                }).ToList();
                
                _logger.LogDebug("Retrieved {DriverCount} drivers", result.Count);
                return result;
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

    public class EmployeeWithJob
    {
        public uint EmployeeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string? Patronym { get; set; }
        public ulong EmployedSince { get; set; }
        public uint JobId { get; set; }
        public Job? Job { get; set; }
    }
}