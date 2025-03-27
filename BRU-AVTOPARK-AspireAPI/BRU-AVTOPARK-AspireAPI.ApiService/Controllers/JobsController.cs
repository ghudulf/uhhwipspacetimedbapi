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
    [Authorize] // Allow all authenticated users to read
    public class JobsController : ControllerBase
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

        private bool IsAdmin()
        {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return false;

            var token = authHeader.Substring("Bearer ".Length);
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(token);
            var roleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "role");
            return roleClaim?.Value == "1";
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Job>>> GetJobs()
        {
            Log.Information("Fetching all jobs with their employees");
            var jobs = await _employeeService.GetAllJobsAsync();
            Log.Debug("Retrieved {JobCount} jobs", jobs.Count);
            return Ok(jobs);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Job>> GetJob(uint id)
        {
            Log.Information("Fetching job with ID {JobId}", id);
            var job = await _employeeService.GetJobByIdAsync(id);

            if (job == null)
            {
                Log.Warning("Job with ID {JobId} not found", id);
                return NotFound();
            }

            Log.Debug("Successfully retrieved job with ID {JobId}", id);
            return Ok(job);
        }

        [HttpPost]
        public async Task<ActionResult<Job>> CreateJob([FromBody] CreateJobModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to create job by non-admin user");
                return Forbid();
            }

            Log.Information("Creating new job with title {JobTitle}", model.JobTitle);

            var success = await _employeeService.CreateJobAsync(model.JobTitle, model.JobInternship);
            if (!success)
            {
                Log.Warning("Failed to create job");
                return BadRequest("Failed to create job");
            }

            // Get the newly created job
            var jobs = await _employeeService.GetAllJobsAsync();
            var job = jobs.LastOrDefault();

            if (job == null)
            {
                Log.Error("Job was created but could not be retrieved");
                return StatusCode(500, "Job was created but could not be retrieved");
            }

            Log.Information("Successfully created job with ID {JobId}", job.JobId);
            return CreatedAtAction(nameof(GetJob), new { id = job.JobId }, job);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateJob(uint id, [FromBody] UpdateJobModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to update job by non-admin user");
                return Forbid();
            }

            Log.Information("Updating job with ID {JobId}", id);

            var success = await _employeeService.UpdateJobAsync(id, model.JobTitle, model.JobInternship);
            if (!success)
            {
                Log.Warning("Job with ID {JobId} not found for update", id);
                return NotFound();
            }

            Log.Information("Successfully updated job with ID {JobId}", id);
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteJob(uint id)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to delete job by non-admin user");
                return Forbid();
            }

            Log.Information("Deleting job with ID {JobId}", id);

            var success = await _employeeService.DeleteJobAsync(id);
            if (!success)
            {
                Log.Warning("Job with ID {JobId} not found for deletion", id);
                return NotFound();
            }

            Log.Information("Successfully deleted job with ID {JobId}", id);
            return NoContent();
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<Job>>> SearchJobs(
            [FromQuery] string? jobTitle = null,
            [FromQuery] string? internship = null)
        {
            Log.Information("Searching jobs with title: {JobTitle}, internship: {Internship}", 
                jobTitle ?? "any", internship ?? "any");

            var jobs = await _employeeService.GetAllJobsAsync();

            if (!string.IsNullOrEmpty(jobTitle))
                jobs = jobs.Where(j => j.JobTitle.Contains(jobTitle, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrEmpty(internship))
                jobs = jobs.Where(j => j.Internship.Contains(internship, StringComparison.OrdinalIgnoreCase)).ToList();

            Log.Debug("Found {JobCount} jobs matching search criteria", jobs.Count);
            return Ok(jobs);
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