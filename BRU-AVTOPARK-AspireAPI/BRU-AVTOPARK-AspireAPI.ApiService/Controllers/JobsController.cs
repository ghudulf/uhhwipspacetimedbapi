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

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allows both custom JWT (manual parsing) and ASP.NET Core auth (OpenIddict)
    public class JobsController : BaseController
    {
        private readonly IEmployeeService _employeeService;
        private readonly ILogger<JobsController> _logger;

        private readonly IRealtimeEventBus _realtimeEventBus;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobsController"/> with required services.
        /// </summary>
        /// <param name="employeeService">Service for managing job and employee data.</param>
        /// <param name="logger">Logger for recording controller activity and diagnostics.</param>
        /// <param name="realtimeEventBus">Realtime event bus used to subscribe and publish job-related CRUD events.</param>
        /// <exception cref="ArgumentNullException">Thrown if any required dependency is null.</exception>
        public JobsController(
            IEmployeeService employeeService,
            ILogger<JobsController> logger,
            IRealtimeEventBus realtimeEventBus)
        {
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
        }


        /// <summary>
        /// Streams realtime CRUD events for jobs over a WebSocket to the authenticated caller.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the streaming session and underlying subscription.</param>
        /// <returns>A task that completes when the streaming session ends.</returns>
        [HttpGet("realtime/ws")]
        public async Task StreamRealtimeEvents(CancellationToken cancellationToken)
        {
            // Use async token validation instead of weak IsAuthenticated check
            var claims = await ValidateOAuthTokenAsync();
            if (claims == null)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await WebSocketEventStreamWriter.StreamCrudSessionAsync(
                HttpContext,
                _realtimeEventBus.SubscribeAsync("jobs", cancellationToken),
                HandleRealtimeCrudAsync,
                _logger,
                cancellationToken);
        }

        /// <summary>
        /// Dispatches realtime CRUD commands for jobs and returns an operation-specific result object.
        /// </summary>
        /// <param name="request">The realtime request containing a Command (read_all, read, create, update, delete), optional Id, and optional Payload.</param>
        /// <param name="cancellationToken">Token to observe for request cancellation.</param>
        /// <returns>
        /// An object representing the command result:
        /// - For "read_all": an anonymous object with a `jobs` collection.
        /// - For "read": an anonymous object with a `job` entity.
        /// - For "create": an anonymous result produced by the create handler (operation, success, snapshot).
        /// - For "update": an anonymous result produced by the update handler (operation, success, entity, snapshot).
        /// - For "delete": an anonymous result produced by the delete handler (operation, success, deletedId, snapshot).
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a required Id is missing for "read", or when the Command is unsupported.
        /// </exception>
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
            var jobs = await _employeeService.GetAllJobsAsync();
            // Map to same DTO shape as REST GetJobs endpoint
            var mappedJobs = jobs.Select(j => new {
                j.JobId,
                j.JobTitle,
                j.Internship,
                j.BaseSalary,
                j.Department,
                j.JobDescription,
                j.RequiredExperience,
                j.RequiredSkills,
                j.RequiredCertifications,
                j.EducationRequirements,
                j.WorkSchedule,
                j.IsFullTime,
                j.IsPartTime,
                j.IsShiftWork,
                j.Benefits,
                j.ReportingTo,
                j.VacationDays,
                j.SickDays,
                j.PerformanceMetrics
            }).ToList();
            return new { jobs = mappedJobs };
        }

        private async Task<object> HandleReadCommandAsync(RealtimeCrudRequest request)
        {
            var id = request.Id ?? throw new InvalidOperationException("id is required for read");
            var job = await _employeeService.GetJobByIdAsync(id);
            // Map to same DTO shape as REST GetJob endpoint
            var mappedJob = job != null ? new {
                job.JobId,
                job.JobTitle,
                job.Internship
            } : null;
            return new { job = mappedJob };
        }

        /// <summary>
        /// Handle the "create" realtime CRUD command by creating a new job and returning the operation result with a current snapshot of all jobs.
        /// </summary>
        /// <param name="request">Realtime CRUD request whose Payload must contain a serialized CreateJobModel (case-insensitive).</param>
        /// <returns>An anonymous object with `operation = "create"`, `success` indicating whether creation succeeded, and `snapshot` containing the list of all jobs.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller does not have admin privileges.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request Payload is missing or cannot be deserialized into a CreateJobModel.</exception>
        private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
        {
            if (!await IsAdminAsync()) throw new UnauthorizedAccessException("Admin role required");
            var model = request.Payload?.Deserialize<CreateJobModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for create");
            var success = await _employeeService.CreateJobAsync(model.JobTitle, model.JobInternship);
            var result = new { operation = "create", success };

            if (success)
            {
                try
                {
                    var userId = GetUserId();
                    var userName = await GetUserNameAsync();
                    var tenant = User?.FindFirst("tenant")?.Value;
                    var sourceIp = GetClientIp();

                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "job.created",
                        Resource: "jobs",
                        HttpMethod: "POST",
                        StatusCode: 201,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: userId,
                        UserName: userName,
                        Tenant: tenant,
                        SourceIp: sourceIp,
                        Metadata: new Dictionary<string, string> { 
                            ["operation"] = "create", 
                            ["success"] = success.ToString(),
                            ["jobTitle"] = model.JobTitle ?? "",
                            ["internship"] = model.JobInternship?.ToString() ?? ""
                        }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for job.created (Resource: jobs, EventName: job.created)");
                }
            }

            return result;
        }

        /// <summary>
        /// Handle an incoming realtime "update" CRUD command for jobs.
        /// </summary>
        /// <param name="request">Realtime CRUD request. Must include <c>Id</c> and a <c>Payload</c> deserializable to <see cref="UpdateJobModel"/>.</param>
        /// <returns>An anonymous object with properties: <c>operation</c> (string), <c>success</c> (bool), <c>entity</c> (the updated job), and <c>snapshot</c> (all jobs).</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an administrator.</exception>
        /// <exception cref="InvalidOperationException">Thrown when <c>Id</c> or <c>Payload</c> is missing or invalid for the update operation.</exception>
        private async Task<object> HandleUpdateCommandAsync(RealtimeCrudRequest request)
        {
            if (!await IsAdminAsync()) throw new UnauthorizedAccessException("Admin role required");
            var id = request.Id ?? throw new InvalidOperationException("id is required for update");
            var model = request.Payload?.Deserialize<UpdateJobModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for update");
            var success = await _employeeService.UpdateJobAsync(id, model.JobTitle, model.JobInternship);
            var entity = await _employeeService.GetJobByIdAsync(id);
            var result = new { operation = "update", success, entity };

            if (success)
            {
                try
                {
                    var userId = GetUserId();
                    var userName = await GetUserNameAsync();
                    var tenant = User?.FindFirst("tenant")?.Value;
                    var sourceIp = GetClientIp();

                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "job.updated",
                        Resource: "jobs",
                        HttpMethod: "PUT",
                        StatusCode: 200,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: userId,
                        UserName: userName,
                        Tenant: tenant,
                        SourceIp: sourceIp,
                        Metadata: new Dictionary<string, string> { 
                            ["operation"] = "update", 
                            ["success"] = success.ToString(),
                            ["id"] = id.ToString()
                        }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for job.updated (Resource: jobs, EventName: job.updated, JobId: {JobId})", id);
                }
            }

            return result;
        }

        /// <summary>
        /// Handles a realtime "delete" command by removing the specified job and returning an operation snapshot.
        /// </summary>
        /// <param name="request">The realtime CRUD request; its <c>Id</c> must be provided to identify the job to delete.</param>
        /// <returns>An object containing: <c>operation</c> ("delete"), <c>success</c> (whether deletion succeeded), <c>deletedId</c> (the id that was deleted), and <c>snapshot</c> (the current list of all jobs).</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an administrator.</exception>
        /// <exception cref="InvalidOperationException">Thrown when <c>request.Id</c> is null.</exception>
        private async Task<object> HandleDeleteCommandAsync(RealtimeCrudRequest request)
        {
            if (!await IsAdminAsync()) throw new UnauthorizedAccessException("Admin role required");
            var id = request.Id ?? throw new InvalidOperationException("id is required for delete");
            var success = await _employeeService.DeleteJobAsync(id);
            var result = new { operation = "delete", success, deletedId = id };

            if (success)
            {
                try
                {
                    var userId = GetUserId();
                    var userName = await GetUserNameAsync();
                    var tenant = User?.FindFirst("tenant")?.Value;
                    var sourceIp = GetClientIp();

                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "job.deleted",
                        Resource: "jobs",
                        HttpMethod: "DELETE",
                        StatusCode: 200,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: userId,
                        UserName: userName,
                        Tenant: tenant,
                        SourceIp: sourceIp,
                        Metadata: new Dictionary<string, string> { 
                            ["operation"] = "delete", 
                            ["success"] = success.ToString(),
                            ["deletedId"] = id.ToString()
                        }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for job.deleted (Resource: jobs, EventName: job.deleted, JobId: {JobId})", id);
                }
            }

            return result;
        }

        /// <summary>
        /// Retrieves all jobs from the data store and returns them mapped to client-facing fields.
        /// </summary>
        /// <returns>An HTTP 200 response containing a list of job objects with fields: JobId, JobTitle, Internship, BaseSalary, Department, JobDescription, RequiredExperience, RequiredSkills, RequiredCertifications, EducationRequirements, WorkSchedule, IsFullTime, IsPartTime, IsShiftWork, Benefits, ReportingTo, VacationDays, SickDays, PerformanceMetrics; returns an HTTP 500 status with an error message if retrieval fails.</returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetJobs()
        {
            _logger.LogInformation("REQUEST RECEIVED: GetJobs - Fetching all jobs");
            
            try
            {
                _logger.LogInformation("DATABASE OPERATION: GetAllJobsAsync");
                var jobs = await _employeeService.GetAllJobsAsync();
                
                // Map to anonymous type - CRITICAL: This converts SpacetimeDB structure to valid JSON
                // Include ALL fields that the client needs
                var result = jobs.Select(j => new {
                    j.JobId,
                    j.JobTitle,
                    j.Internship,
                    j.BaseSalary,
                    j.Department,
                    j.JobDescription,
                    j.RequiredExperience,
                    j.RequiredSkills,
                    j.RequiredCertifications,
                    j.EducationRequirements,
                    j.WorkSchedule,
                    j.IsFullTime,
                    j.IsPartTime,
                    j.IsShiftWork,
                    j.Benefits,
                    j.ReportingTo,
                    j.VacationDays,
                    j.SickDays,
                    j.PerformanceMetrics
                }).ToList();

                _logger.LogInformation("DATABASE RESULT: Retrieved {JobCount} jobs", result.Count);
                _logger.LogInformation("FULL JOBS DATA: {JobsData}", JsonSerializer.Serialize(result));
                
                foreach (var job in result)
                {
                    _logger.LogDebug("Job ID: {JobId}, Title: {JobTitle}, Internship: {Internship}, Department: {Department}", 
                        job.JobId, job.JobTitle, job.Internship, job.Department);
                }
                
                _logger.LogInformation("RESPONSE SENT: Returning {JobCount} jobs to client", result.Count);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all jobs");
                return StatusCode(500, "An error occurred while retrieving jobs");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetJob(uint id)
        {
            _logger.LogInformation("REQUEST RECEIVED: GetJob with ID {JobId}", id);
            
            try
            {
                _logger.LogInformation("DATABASE OPERATION: GetJobByIdAsync for ID {JobId}", id);
                var job = await _employeeService.GetJobByIdAsync(id);

                if (job == null)
                {
                    _logger.LogWarning("DATABASE RESULT: Job with ID {JobId} not found", id);
                    return NotFound();
                }

                // Map to anonymous type
                var result = new {
                    job.JobId,
                    job.JobTitle,
                    job.Internship
                };

                _logger.LogInformation("DATABASE RESULT: Successfully retrieved job with ID {JobId}", id);
                _logger.LogInformation("FULL JOB DATA: {JobData}", JsonSerializer.Serialize(result));
                _logger.LogInformation("RESPONSE SENT: Job details for ID {JobId}, Title: {JobTitle}, Internship: {Internship}", 
                    result.JobId, result.JobTitle, result.Internship);
                
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving job with ID {JobId}", id);
                return StatusCode(500, $"An error occurred while retrieving job with ID {id}");
            }
        }

        [HttpPost]
        public async Task<ActionResult<Job>> CreateJob([FromBody] CreateJobModel model)
        {
            _logger.LogInformation("REQUEST RECEIVED: CreateJob with data: {JobData}", JsonSerializer.Serialize(model));
            
            try
            {
                if (!IsAdmin())
                {
                    _logger.LogWarning("AUTHORIZATION FAILED: Unauthorized attempt to create job by non-admin user");
                    return Forbid();
                }

                _logger.LogInformation("DATABASE OPERATION: CreateJobAsync with title {JobTitle}, internship {JobInternship}", 
                    model.JobTitle, model.JobInternship);

                var success = await _employeeService.CreateJobAsync(model.JobTitle, model.JobInternship);
                if (!success)
                {
                    _logger.LogWarning("DATABASE RESULT: Failed to create job with title {JobTitle}", model.JobTitle);
                    return BadRequest("Failed to create job");
                }

                // Get the newly created job
                _logger.LogInformation("DATABASE OPERATION: GetAllJobsAsync to retrieve newly created job");
                var jobs = await _employeeService.GetAllJobsAsync();
                _logger.LogInformation("DATABASE RESULT: Retrieved {JobCount} jobs after creation", jobs.Count);
                _logger.LogInformation("FULL JOBS DATA AFTER CREATION: {JobsData}", JsonSerializer.Serialize(jobs));
                
                var job = jobs.LastOrDefault();

                if (job == null)
                {
                    _logger.LogError("DATABASE RESULT: Job was created but could not be retrieved");
                    return StatusCode(500, "Job was created but could not be retrieved");
                }

                _logger.LogInformation("RESPONSE SENT: Successfully created job with ID {JobId}, Title: {JobTitle}, Internship: {Internship}", 
                    job.JobId, job.JobTitle, job.Internship);
                return CreatedAtAction(nameof(GetJob), new { id = job.JobId }, job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating job with title {JobTitle}", model.JobTitle);
                return StatusCode(500, "An error occurred while creating the job");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateJob(uint id, [FromBody] UpdateJobModel model)
        {
            _logger.LogInformation("REQUEST RECEIVED: UpdateJob for ID {JobId} with data: {JobData}", 
                id, JsonSerializer.Serialize(model));
            
            try
            {
                if (!IsAdmin())
                {
                    _logger.LogWarning("AUTHORIZATION FAILED: Unauthorized attempt to update job {JobId} by non-admin user", id);
                    return Forbid();
                }

                _logger.LogInformation("DATABASE OPERATION: UpdateJobAsync for ID {JobId}, Title: {JobTitle}, Internship: {JobInternship}", 
                    id, model.JobTitle, model.JobInternship);

                var success = await _employeeService.UpdateJobAsync(id, model.JobTitle, model.JobInternship);
                if (!success)
                {
                    _logger.LogWarning("DATABASE RESULT: Job with ID {JobId} not found for update", id);
                    return NotFound();
                }

                // Get the updated job for logging
                var updatedJob = await _employeeService.GetJobByIdAsync(id);
                _logger.LogInformation("UPDATED JOB DATA: {JobData}", JsonSerializer.Serialize(updatedJob));
                
                _logger.LogInformation("RESPONSE SENT: Successfully updated job with ID {JobId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating job with ID {JobId}", id);
                return StatusCode(500, $"An error occurred while updating job with ID {id}");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteJob(uint id)
        {
            _logger.LogInformation("REQUEST RECEIVED: DeleteJob with ID {JobId}", id);
            
            try
            {
                if (!IsAdmin())
                {
                    _logger.LogWarning("AUTHORIZATION FAILED: Unauthorized attempt to delete job {JobId} by non-admin user", id);
                    return Forbid();
                }

                // Get the job before deletion for logging
                var jobToDelete = await _employeeService.GetJobByIdAsync(id);
                _logger.LogInformation("JOB TO DELETE: {JobData}", JsonSerializer.Serialize(jobToDelete));
                
                _logger.LogInformation("DATABASE OPERATION: DeleteJobAsync for ID {JobId}", id);
                var success = await _employeeService.DeleteJobAsync(id);
                if (!success)
                {
                    _logger.LogWarning("DATABASE RESULT: Job with ID {JobId} not found for deletion", id);
                    return NotFound();
                }

                _logger.LogInformation("RESPONSE SENT: Successfully deleted job with ID {JobId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting job with ID {JobId}", id);
                return StatusCode(500, $"An error occurred while deleting job with ID {id}");
            }
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<dynamic>>> SearchJobs(
            [FromQuery] string? jobTitle = null,
            [FromQuery] string? internship = null)
        {
            _logger.LogInformation("REQUEST RECEIVED: SearchJobs with parameters - Title: {JobTitle}, Internship: {Internship}", 
                jobTitle ?? "any", internship ?? "any");
            
            try
            {
                _logger.LogInformation("DATABASE OPERATION: GetAllJobsAsync for search");
                var jobs = await _employeeService.GetAllJobsAsync();
                _logger.LogInformation("DATABASE RESULT: Retrieved {JobCount} total jobs before filtering", jobs.Count);
                _logger.LogInformation("FULL JOBS DATA BEFORE FILTERING: {JobsData}", JsonSerializer.Serialize(jobs));

                if (!string.IsNullOrEmpty(jobTitle))
                {
                    _logger.LogInformation("FILTERING: Applying job title filter '{JobTitle}'", jobTitle);
                    jobs = jobs.Where(j => j.JobTitle.Contains(jobTitle, StringComparison.OrdinalIgnoreCase)).ToList();
                    _logger.LogInformation("FILTERING RESULT: {JobCount} jobs after title filter", jobs.Count);
                }

                if (!string.IsNullOrEmpty(internship))
                {
                    _logger.LogInformation("FILTERING: Applying internship filter '{Internship}'", internship);
                    jobs = jobs.Where(j => j.Internship.Contains(internship, StringComparison.OrdinalIgnoreCase)).ToList();
                    _logger.LogInformation("FILTERING RESULT: {JobCount} jobs after internship filter", jobs.Count);
                }

                // Map to anonymous type
                var result = jobs.Select(j => new {
                    j.JobId,
                    j.JobTitle,
                    j.Internship
                }).ToList();

                _logger.LogInformation("SEARCH RESULTS: Found {JobCount} jobs matching search criteria", result.Count);
                _logger.LogInformation("FULL SEARCH RESULTS DATA: {JobsData}", JsonSerializer.Serialize(result));
                
                foreach (var job in result)
                {
                    _logger.LogDebug("Search Result - Job ID: {JobId}, Title: {JobTitle}, Internship: {Internship}", 
                        job.JobId, job.JobTitle, job.Internship);
                }
                
                _logger.LogInformation("RESPONSE SENT: Returning {JobCount} jobs matching search criteria to client", result.Count);
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching jobs with title: {JobTitle}, internship: {Internship}", 
                    jobTitle ?? "any", internship ?? "any");
                return StatusCode(500, "An error occurred while searching jobs");
            }
        }
    }

    public class CreateJobModel
    {
        public required string JobTitle { get; set; }
        public required string JobInternship { get; set; }
    }

    public class UpdateJobModel
    {
        public string? JobTitle { get; set; }
        public string? JobInternship { get; set; }
    }
}