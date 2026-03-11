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

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allows both custom JWT (manual parsing) and ASP.NET Core auth (OpenIddict)
    public class EmployeesController : BaseController
    {
        private readonly IEmployeeService _employeeService;
        private readonly ILogger<EmployeesController> _logger;
        private readonly IAdminActionLogger _adminLogger;
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IRealtimeEventBus _realtimeEventBus;

        public EmployeesController(
            IEmployeeService employeeService,
            ILogger<EmployeesController> logger,
            IAdminActionLogger adminLogger,
            ISpacetimeDBService spacetimeService,
            IRealtimeEventBus realtimeEventBus)
        {
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _adminLogger = adminLogger ?? throw new ArgumentNullException(nameof(adminLogger));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
        }

        [HttpGet("realtime/ws")]
        public async Task StreamRealtimeEvents(CancellationToken cancellationToken)
        {
            if (!IsAuthenticated())
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!IsAdmin() && !HasPermission("employees.view"))
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await WebSocketEventStreamWriter.StreamCrudSessionAsync(
                HttpContext,
                _realtimeEventBus.SubscribeAsync("employees", cancellationToken),
                HandleRealtimeCrudAsync,
                _logger,
                cancellationToken);
        }

        private async Task<object> HandleRealtimeCrudAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
        {
            var command = (request.Command ?? string.Empty).Trim().ToLowerInvariant();

            return command switch
            {
                "read_all" => await HandleReadAllCommandAsync(),
                "read" => await HandleReadCommandAsync(request),
                "create" => await HandleCreateCommandAsync(request),
                "update" => await HandleUpdateCommandAsync(request),
                "delete" => await HandleDeleteCommandAsync(request),
                _ => throw new InvalidOperationException($"Unsupported command '{request.Command}'")
            };
        }

        private async Task<object> HandleReadAllCommandAsync()
        {
            if (!IsAdmin() && !HasPermission("employees.view"))
            {
                throw new UnauthorizedAccessException("Not authorized for employees.view");
            }

            return new { employees = await _employeeService.GetAllEmployeesAsync() };
        }

        private async Task<object> HandleReadCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("employees.view"))
            {
                throw new UnauthorizedAccessException("Not authorized for employees.view");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for read");
            return new { employee = await _employeeService.GetEmployeeByIdAsync(id) };
        }

        private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("employees.create"))
            {
                throw new UnauthorizedAccessException("Not authorized for employees.create");
            }

            var model = request.Payload?.Deserialize<CreateEmployeeModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for create");

            var newEmployee = await _employeeService.CreateEmployeeAsync(model.Name, model.Surname, model.Patronym ?? string.Empty, model.JobId);
            var success = newEmployee != null;

            if (success)
            {
                // Publish domain event for websocket-originated changes
                var metadata = new Dictionary<string, string>
                {
                    ["name"] = model.Name,
                    ["surname"] = model.Surname,
                    ["jobId"] = model.JobId.ToString()
                };
                if (newEmployee != null)
                {
                    metadata["employeeId"] = newEmployee.EmployeeId.ToString();
                }

                var domainEvent = new ApiDomainEvent(
                    EventName: "employee.created",
                    Resource: "employees",
                    HttpMethod: "WS_CREATE",
                    StatusCode: 200,
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: request.RequestId ?? Guid.NewGuid().ToString(),
                    UserId: GetUserId(),
                    UserName: GetUserName(),
                    Tenant: null,
                    SourceIp: GetClientIp(),
                    Metadata: metadata
                );
                await _realtimeEventBus.PublishAsync(domainEvent);

                // Fire-and-forget: enrich with snapshot and log action
                var userId = GetUserId();
                var employeeId = newEmployee?.EmployeeId;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var snapshot = await _employeeService.GetAllEmployeesAsync();
                        if (userId != null && employeeId.HasValue)
                        {
                            await _adminLogger.LogActionAsync(
                                userId,
                                "employees.create",
                                $"Created employee: {model.Name} {model.Surname}, JobId: {model.JobId}, EmployeeId: {employeeId.Value}"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background enrichment/logging for create");
                    }
                });
            }

            return new { operation = "create", success, employeeId = newEmployee?.EmployeeId };
        }

        private async Task<object> HandleUpdateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("employees.update"))
            {
                throw new UnauthorizedAccessException("Not authorized for employees.update");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for update");
            var model = request.Payload?.Deserialize<UpdateEmployeeModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for update");

            var success = await _employeeService.UpdateEmployeeAsync(id, model.Name, model.Surname, model.Patronym, model.JobId);

            if (success)
            {
                // Publish domain event for websocket-originated changes
                var domainEvent = new ApiDomainEvent(
                    EventName: "employee.updated",
                    Resource: "employees",
                    HttpMethod: "WS_UPDATE",
                    StatusCode: 200,
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: request.RequestId ?? Guid.NewGuid().ToString(),
                    UserId: GetUserId(),
                    UserName: GetUserName(),
                    Tenant: null,
                    SourceIp: GetClientIp(),
                    Metadata: new Dictionary<string, string>
                    {
                        ["employeeId"] = id.ToString(),
                        ["name"] = model.Name ?? "",
                        ["surname"] = model.Surname ?? "",
                        ["jobId"] = model.JobId?.ToString() ?? ""
                    }
                );
                await _realtimeEventBus.PublishAsync(domainEvent);

                // Fire-and-forget: enrich with entity/snapshot and log action
                var userId = GetUserId();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var entity = await _employeeService.GetEmployeeByIdAsync(id);
                        var snapshot = await _employeeService.GetAllEmployeesAsync();
                        if (userId != null && entity != null)
                        {
                            await _adminLogger.LogActionAsync(
                                userId,
                                "employees.update",
                                $"Updated employee: {entity.Name} {entity.Surname}, JobId: {entity.JobId}, EmployeeId: {id}"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background enrichment/logging for update");
                    }
                });
            }

            return new { operation = "update", success, employeeId = id };
        }

        private async Task<object> HandleDeleteCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("employees.delete"))
            {
                throw new UnauthorizedAccessException("Not authorized for employees.delete");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for delete");
            var employeeBeforeDelete = await _employeeService.GetEmployeeByIdAsync(id);
            var success = await _employeeService.DeleteEmployeeAsync(id);

            if (success)
            {
                // Publish domain event for websocket-originated changes
                var metadata = new Dictionary<string, string>
                {
                    ["employeeId"] = id.ToString()
                };
                if (employeeBeforeDelete != null)
                {
                    metadata["name"] = employeeBeforeDelete.Name;
                    metadata["surname"] = employeeBeforeDelete.Surname;
                    metadata["jobId"] = employeeBeforeDelete.JobId.ToString();
                }

                var domainEvent = new ApiDomainEvent(
                    EventName: "employee.deleted",
                    Resource: "employees",
                    HttpMethod: "WS_DELETE",
                    StatusCode: 200,
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: request.RequestId ?? Guid.NewGuid().ToString(),
                    UserId: GetUserId(),
                    UserName: GetUserName(),
                    Tenant: null,
                    SourceIp: GetClientIp(),
                    Metadata: metadata
                );
                await _realtimeEventBus.PublishAsync(domainEvent);

                // Fire-and-forget: enrich with snapshot and log action
                var userId = GetUserId();
                var deletedEmployee = employeeBeforeDelete;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var snapshot = await _employeeService.GetAllEmployeesAsync();
                        if (userId != null)
                        {
                            var details = deletedEmployee != null
                                ? $"Deleted employee: {deletedEmployee.Name} {deletedEmployee.Surname}, JobId: {deletedEmployee.JobId}, EmployeeId: {id}"
                                : $"Deleted employee with EmployeeId: {id}";

                            await _adminLogger.LogActionAsync(
                                userId,
                                "employees.delete",
                                details
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background enrichment/logging for delete");
                    }
                });
            }

            return new { operation = "delete", success, deletedId = id };
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

                var employee = await _employeeService.CreateEmployeeAsync(
                    model.Name,
                    model.Surname,
                    model.Patronym ?? string.Empty,
                    model.JobId
                );

                if (employee == null)
                {
                    _logger.LogWarning("DATABASE RESULT: Failed to create employee");
                    return BadRequest("Failed to create employee");
                }

                _logger.LogInformation("DATABASE RESULT: Successfully created employee with ID {EmployeeId}", employee.EmployeeId);
                _logger.LogInformation("FULL EMPLOYEE DATA: {EmployeeData}", JsonSerializer.Serialize(employee));

                // Fire-and-forget: Log the admin action
                var userId = GetUserId();
                var createdEmployee = employee;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (userId != null)
                        {
                            await _adminLogger.LogActionAsync(
                                userId,
                                "CreateEmployee",
                                $"Created employee with ID {createdEmployee.EmployeeId}, Name: {createdEmployee.Name} {createdEmployee.Surname}"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error logging admin action for create");
                    }
                });

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

                _logger.LogInformation("DATABASE RESULT: Successfully updated employee {EmployeeId}", id);

                // Fire-and-forget: Get employee after update and log admin action
                var userId = GetUserId();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var employeeAfterUpdate = await _employeeService.GetEmployeeByIdAsync(id);
                        if (employeeAfterUpdate != null)
                        {
                            _logger.LogInformation("EMPLOYEE AFTER UPDATE: {EmployeeData}", JsonSerializer.Serialize(employeeAfterUpdate));
                        }

                        if (userId != null)
                        {
                            await _adminLogger.LogActionAsync(
                                userId,
                                "UpdateEmployee",
                                $"Updated employee with ID {id}"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background enrichment/logging for update");
                    }
                });

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

                _logger.LogInformation("DATABASE RESULT: Successfully deleted employee {EmployeeId}", id);

                // Fire-and-forget: Log the admin action
                var userId = GetUserId();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (userId != null)
                        {
                            await _adminLogger.LogActionAsync(
                                userId,
                                "DeleteEmployee",
                                $"Deleted employee with ID {id}"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error logging admin action for delete");
                    }
                });

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