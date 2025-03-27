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

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmployeesController : BaseController
    {
        private readonly IEmployeeService _employeeService;
        private readonly ILogger<EmployeesController> _logger;

        public EmployeesController(
            IEmployeeService employeeService,
            ILogger<EmployeesController> logger)
        {
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Employee>>> GetEmployees()
        {
            try
            {
                _logger.LogInformation("Fetching all employees");
                var employees = await _employeeService.GetAllEmployeesAsync();
                _logger.LogDebug("Retrieved {Count} employees", employees.Count);
                return Ok(employees);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees");
                return StatusCode(500, "An error occurred while retrieving employees");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Employee>> GetEmployee(uint id)
        {
            try
            {
                _logger.LogInformation("Fetching employee {EmployeeId}", id);
                var employee = await _employeeService.GetEmployeeByIdAsync(id);

                if (employee == null)
                {
                    _logger.LogWarning("Employee {EmployeeId} not found", id);
                    return NotFound();
                }

                return Ok(employee);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employee {EmployeeId}", id);
                return StatusCode(500, "An error occurred while retrieving the employee");
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<Employee>> CreateEmployee([FromBody] CreateEmployeeModel model)
        {
            try
            {
                _logger.LogInformation("Creating new employee: {Name} {Surname}", model.Name, model.Surname);

                var success = await _employeeService.CreateEmployeeAsync(
                    model.Name,
                    model.Surname,
                    model.Patronym ?? string.Empty,
                    model.JobId
                );

                if (!success)
                {
                    _logger.LogWarning("Failed to create employee");
                    return BadRequest("Failed to create employee");
                }

                // Get the newly created employee
                var employees = await _employeeService.GetAllEmployeesAsync();
                var employee = employees.LastOrDefault();

                if (employee == null)
                {
                    _logger.LogError("Employee was created but could not be retrieved");
                    return StatusCode(500, "Employee was created but could not be retrieved");
                }

                _logger.LogInformation("Successfully created employee with ID {EmployeeId}", employee.EmployeeId);
                return CreatedAtAction(nameof(GetEmployee), new { id = employee.EmployeeId }, employee);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating employee");
                return StatusCode(500, "An error occurred while creating the employee");
            }
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateEmployee(uint id, [FromBody] UpdateEmployeeModel model)
        {
            try
            {
                _logger.LogInformation("Updating employee {EmployeeId}", id);

                var success = await _employeeService.UpdateEmployeeAsync(
                    id,
                    model.Name,
                    model.Surname,
                    model.Patronym,
                    model.JobId
                );

                if (!success)
                {
                    _logger.LogWarning("Employee {EmployeeId} not found", id);
                    return NotFound();
                }

                _logger.LogInformation("Successfully updated employee {EmployeeId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating employee {EmployeeId}", id);
                return StatusCode(500, "An error occurred while updating the employee");
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteEmployee(uint id)
        {
            try
            {
                _logger.LogInformation("Deleting employee {EmployeeId}", id);

                var success = await _employeeService.DeleteEmployeeAsync(id);
                if (!success)
                {
                    _logger.LogWarning("Employee {EmployeeId} not found", id);
                    return NotFound();
                }

                _logger.LogInformation("Successfully deleted employee {EmployeeId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting employee {EmployeeId}", id);
                return StatusCode(500, "An error occurred while deleting the employee");
            }
        }

        [HttpGet("by-job/{jobId}")]
        public async Task<ActionResult<IEnumerable<Employee>>> GetEmployeesByJob(uint jobId)
        {
            try
            {
                _logger.LogInformation("Fetching employees for job {JobId}", jobId);
                var employees = await _employeeService.GetEmployeesByJobIdAsync(jobId);
                _logger.LogDebug("Retrieved {Count} employees for job {JobId}", employees.Count, jobId);
                return Ok(employees);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees for job {JobId}", jobId);
                return StatusCode(500, "An error occurred while retrieving employees");
            }
        }

        [HttpGet("drivers")]
        public async Task<ActionResult<IEnumerable<Employee>>> GetDrivers()
        {
            try
            {
                _logger.LogInformation("Fetching all drivers");
                var jobs = await _employeeService.GetAllJobsAsync();
                var driverJob = jobs.FirstOrDefault(j => j.JobTitle.Contains("Driver", StringComparison.OrdinalIgnoreCase));

                if (driverJob == null)
                {
                    _logger.LogWarning("Driver job not found");
                    return Ok(Array.Empty<Employee>());
                }

                var drivers = await _employeeService.GetEmployeesByJobIdAsync(driverJob.JobId);
                _logger.LogDebug("Retrieved {Count} drivers", drivers.Count);
                return Ok(drivers);
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