using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
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
    public class MaintenanceController : ControllerBase
    {
        private readonly AppDbContext _context;

        public MaintenanceController(AppDbContext context)
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
        public async Task<ActionResult<IEnumerable<Obsluzhivanie>>> GetMaintenanceRecords()
        {
            Log.Information("Fetching all maintenance records");
            var records = await _context.Obsluzhivanies
                .Include(m => m.Avtobus)
                .OrderByDescending(m => m.LastServiceDate)
                .ToListAsync();
            
            Log.Debug("Retrieved {RecordCount} maintenance records", records.Count);
            return records;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Obsluzhivanie>> GetMaintenanceRecord(long id)
        {
            Log.Information("Fetching maintenance record with ID {MaintenanceId}", id);
            var maintenance = await _context.Obsluzhivanies
                .Include(m => m.Avtobus)
                .FirstOrDefaultAsync(m => m.MaintenanceId == id);

            if (maintenance == null)
            {
                Log.Warning("Maintenance record with ID {MaintenanceId} not found", id);
                return NotFound();
            }

            Log.Debug("Successfully retrieved maintenance record with ID {MaintenanceId}", id);
            return maintenance;
        }

        [HttpPost]
        public async Task<ActionResult<Obsluzhivanie>> CreateMaintenanceRecord([FromBody] CreateMaintenanceModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to create maintenance record by non-admin user");
                return Forbid();
            }

            Log.Information("Creating new maintenance record for bus ID {BusId}", model.BusId);
            var bus = await _context.Avtobusy.FindAsync(model.BusId);
            if (bus == null)
            {
                Log.Warning("Invalid bus ID {BusId} provided for maintenance record creation", model.BusId);
                return BadRequest("Invalid bus ID");
            }

            var maintenance = new Obsluzhivanie
            {
                BusId = model.BusId,
                LastServiceDate = model.LastServiceDate,
                ServiceEngineer = model.ServiceEngineer,
                FoundIssues = model.FoundIssues,
                NextServiceDate = model.NextServiceDate,
                Roadworthiness = model.Roadworthiness
            };

            _context.Obsluzhivanies.Add(maintenance);
            await _context.SaveChangesAsync();

            // Reload with navigation properties
            maintenance = await _context.Obsluzhivanies
                .Include(m => m.Avtobus)
                .FirstAsync(m => m.MaintenanceId == maintenance.MaintenanceId);

            Log.Information("Successfully created maintenance record with ID {MaintenanceId}", maintenance.MaintenanceId);
            return CreatedAtAction(nameof(GetMaintenanceRecord), new { id = maintenance.MaintenanceId }, maintenance);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMaintenanceRecord(long id, [FromBody] UpdateMaintenanceModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to update maintenance record by non-admin user");
                return Forbid();
            }

            Log.Information("Updating maintenance record with ID {MaintenanceId}", id);
            var maintenance = await _context.Obsluzhivanies.FindAsync(id);
            if (maintenance == null)
            {
                Log.Warning("Maintenance record with ID {MaintenanceId} not found for update", id);
                return NotFound();
            }

            if (model.BusId.HasValue)
            {
                var bus = await _context.Avtobusy.FindAsync(model.BusId.Value);
                if (bus == null)
                {
                    Log.Warning("Invalid bus ID {BusId} provided for maintenance record update", model.BusId.Value);
                    return BadRequest("Invalid bus ID");
                }
                maintenance.BusId = model.BusId.Value;
            }

            if (model.LastServiceDate.HasValue)
                maintenance.LastServiceDate = model.LastServiceDate.Value;

            if (model.NextServiceDate.HasValue)
                maintenance.NextServiceDate = model.NextServiceDate.Value;

            if (model.ServiceEngineer != null)
                maintenance.ServiceEngineer = model.ServiceEngineer;

            if (model.FoundIssues != null)
                maintenance.FoundIssues = model.FoundIssues;

            if (model.Roadworthiness != null)
                maintenance.Roadworthiness = model.Roadworthiness;

            try
            {
                await _context.SaveChangesAsync();
                Log.Information("Successfully updated maintenance record with ID {MaintenanceId}", id);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                Log.Error(ex, "Concurrency error updating maintenance record with ID {MaintenanceId}", id);
                if (!await MaintenanceExists(id))
                {
                    return NotFound();
                }
                throw;
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaintenanceRecord(long id)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to delete maintenance record by non-admin user");
                return Forbid();
            }

            Log.Information("Deleting maintenance record with ID {MaintenanceId}", id);
            var maintenance = await _context.Obsluzhivanies.FindAsync(id);
            if (maintenance == null)
            {
                Log.Warning("Maintenance record with ID {MaintenanceId} not found for deletion", id);
                return NotFound();
            }

            _context.Obsluzhivanies.Remove(maintenance);
            await _context.SaveChangesAsync();

            Log.Information("Successfully deleted maintenance record with ID {MaintenanceId}", id);
            return NoContent();
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<Obsluzhivanie>>> SearchMaintenanceRecords(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string? busModel = null,
            [FromQuery] string? engineer = null,
            [FromQuery] string? roadworthiness = null)
        {
            Log.Information("Searching maintenance records with parameters: StartDate={StartDate}, EndDate={EndDate}, BusModel={BusModel}, Engineer={Engineer}, Roadworthiness={Roadworthiness}",
                startDate, endDate, busModel, engineer, roadworthiness);

            var query = _context.Obsluzhivanies
                .Include(m => m.Avtobus)
                .AsQueryable();

            if (startDate.HasValue)
                query = query.Where(m => m.LastServiceDate >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(m => m.LastServiceDate <= endDate.Value);

            if (!string.IsNullOrEmpty(busModel))
                query = query.Where(m => m.Avtobus.Model.Contains(busModel));

            if (!string.IsNullOrEmpty(engineer))
                query = query.Where(m => m.ServiceEngineer.Contains(engineer));

            if (!string.IsNullOrEmpty(roadworthiness))
                query = query.Where(m => m.Roadworthiness == roadworthiness);

            var results = await query.OrderByDescending(m => m.LastServiceDate).ToListAsync();
            Log.Debug("Found {RecordCount} maintenance records matching search criteria", results.Count);
            return results;
        }

        [HttpGet("due-maintenance")]
        public async Task<ActionResult<IEnumerable<Obsluzhivanie>>> GetDueMaintenanceRecords()
        {
            Log.Information("Fetching due maintenance records");
            var today = DateTime.Today;
            var records = await _context.Obsluzhivanies
                .Include(m => m.Avtobus)
                .Where(m => m.NextServiceDate <= today)
                .OrderBy(m => m.NextServiceDate)
                .ToListAsync();
            
            Log.Debug("Found {RecordCount} due maintenance records", records.Count);
            return records;
        }

        private async Task<bool> MaintenanceExists(long id)
        {
            return await _context.Obsluzhivanies.AnyAsync(e => e.MaintenanceId == id);
        }
    }

    public class CreateMaintenanceModel
    {
        public required long BusId { get; set; }
        public required DateTime LastServiceDate { get; set; }
        public required string ServiceEngineer { get; set; }
        public required string FoundIssues { get; set; }
        public required DateTime NextServiceDate { get; set; }
        public required string Roadworthiness { get; set; }
    }

    public class UpdateMaintenanceModel
    {
        public long? BusId { get; set; }
        public DateTime? LastServiceDate { get; set; }
        public string? ServiceEngineer { get; set; }
        public string? FoundIssues { get; set; }
        public DateTime? NextServiceDate { get; set; }
        public string? Roadworthiness { get; set; }
    }
} 