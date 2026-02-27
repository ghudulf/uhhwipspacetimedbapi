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

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class JobsController : BaseController
    {
        private readonly IEmployeeService _employeeService;
        private readonly ILogger<JobsController> _logger;

        public JobsController(
            IEmployeeService employeeService,
            ILogger<JobsController> logger)
        {
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetJobs()
        {
            _logger.LogInformation("REQUEST RECEIVED: GetJobs - Fetching all jobs");
            
            try
            {
                _logger.LogInformation("DATABASE OPERATION: GetAllJobsAsync");
                var jobs = await _employeeService.GetAllJobsAsync();
                
                // Map to anonymous type
                var result = jobs.Select(j => new {
                    j.JobId,
                    j.JobTitle,
                    j.Internship
                }).ToList();

                _logger.LogInformation("DATABASE RESULT: Retrieved {JobCount} jobs", result.Count);
                _logger.LogInformation("FULL JOBS DATA: {JobsData}", JsonSerializer.Serialize(result));
                
                foreach (var job in result)
                {
                    _logger.LogDebug("Job ID: {JobId}, Title: {JobTitle}, Internship: {Internship}", 
                        job.JobId, job.JobTitle, job.Internship);
                }
                
                _logger.LogInformation("RESPONSE SENT: Returning {JobCount} jobs to client", result.Count);
                return Ok(result); // Return mapped result
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