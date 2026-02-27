using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using TicketSalesApp.Core.Data;
using TicketSalesApp.Core.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Serilog;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Allow all authenticated users to read
    public class JobsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public JobsController(AppDbContext context)
        {
            _context = context;
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
            var jobs = await _context.Jobs
                .Include(j => j.Employees)
                .ToListAsync();
            
            Log.Debug("Retrieved {JobCount} jobs", jobs.Count);
            return jobs;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Job>> GetJob(long id)
        {
            Log.Information("Fetching job with ID {JobId}", id);
            var job = await _context.Jobs
                .Include(j => j.Employees)
                .FirstOrDefaultAsync(j => j.JobId == id);

            if (job == null)
            {
                Log.Warning("Job with ID {JobId} not found", id);
                return NotFound();
            }

            Log.Debug("Successfully retrieved job with ID {JobId}", id);
            return job;
        }

        [HttpPost]
        public async Task<ActionResult<Job>> CreateJob(Job job)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to create job by non-admin user");
                return Forbid();
            }

            Log.Information("Creating new job with title {JobTitle}", job.JobTitle);
            _context.Jobs.Add(job);
            await _context.SaveChangesAsync();

            Log.Information("Successfully created job with ID {JobId}", job.JobId);
            return CreatedAtAction(nameof(GetJob), new { id = job.JobId }, job);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateJob(long id, Job job)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to update job by non-admin user");
                return Forbid();
            }

            if (id != job.JobId)
            {
                Log.Warning("Job ID mismatch in update request. Path ID: {PathId}, Body ID: {BodyId}", id, job.JobId);
                return BadRequest();
            }

            Log.Information("Updating job with ID {JobId}", id);
            _context.Entry(job).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
                Log.Information("Successfully updated job with ID {JobId}", id);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                Log.Error(ex, "Concurrency error updating job with ID {JobId}", id);
                if (!await JobExists(id))
                {
                    return NotFound();
                }
                throw;
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteJob(long id)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to delete job by non-admin user");
                return Forbid();
            }

            Log.Information("Deleting job with ID {JobId}", id);
            var job = await _context.Jobs.FindAsync(id);
            if (job == null)
            {
                Log.Warning("Job with ID {JobId} not found for deletion", id);
                return NotFound();
            }

            _context.Jobs.Remove(job);
            await _context.SaveChangesAsync();

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

            var query = _context.Jobs
                .Include(j => j.Employees)
                .AsQueryable();

            if (!string.IsNullOrEmpty(jobTitle))
                query = query.Where(j => j.JobTitle.Contains(jobTitle));

            if (!string.IsNullOrEmpty(internship))
                query = query.Where(j => j.Internship.Contains(internship));

            var results = await query.ToListAsync();
            Log.Debug("Found {JobCount} jobs matching search criteria", results.Count);
            return results;
        }

        private async Task<bool> JobExists(long id)
        {
            return await _context.Jobs.AnyAsync(e => e.JobId == id);
        }
    }
}