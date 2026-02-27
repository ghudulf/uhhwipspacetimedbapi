using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Serilog;// NO THIS STAYS
using System.Text.Json; // Added for serialization logging
using Log = Serilog.Log;
using SpacetimeDB.Types;
using SpacetimeDB; // Added for direct DB access
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allow all authenticated users to read
    public class MaintenanceController : BaseController
    {
        private readonly IMaintenanceService _maintenanceService;
        private readonly ILogger<MaintenanceController> _logger;
        private readonly ISpacetimeDBService _spacetimeService; // Added SpacetimeDBService

        public MaintenanceController(
            IMaintenanceService maintenanceService,
            ILogger<MaintenanceController> logger,
            ISpacetimeDBService spacetimeService) // Added SpacetimeDBService
        {
            _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService)); // Added SpacetimeDBService
        }

        

        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetMaintenanceRecords() // Changed return type
        {
            Log.Information("Fetching all maintenance records");
            var records = await _maintenanceService.GetAllMaintenanceRecordsAsync();
            var conn = _spacetimeService.GetConnection();
            
            // Map to anonymous type with Bus details
            var result = records.Select(m => {
                var bus = conn.Db.Bus.BusId.Find(m.BusId);
                return new {
                    m.MaintenanceId,
                    m.BusId,
                    Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                    LastServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)m.LastServiceDate).DateTime,
                    m.ServiceEngineer,
                    m.FoundIssues,
                    NextServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)m.NextServiceDate).DateTime,
                    m.Roadworthiness,
                    m.MaintenanceType,
                    m.MileageThreshold
                };
            }).ToList();

            Log.Debug("Retrieved {RecordCount} maintenance records", result.Count);
            _logger.LogInformation("FULL MAINTENANCE DATA: {MaintenanceData}", JsonSerializer.Serialize(result)); // Added JSON logging
            return Ok(result); // Return mapped result
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetMaintenanceRecord(uint id) // Changed return type
        {
            Log.Information("Fetching maintenance record with ID {MaintenanceId}", id);
            var maintenance = await _maintenanceService.GetMaintenanceByIdAsync(id);

            if (maintenance == null)
            {
                Log.Warning("Maintenance record with ID {MaintenanceId} not found", id);
                return NotFound();
            }

            var conn = _spacetimeService.GetConnection();
            var bus = conn.Db.Bus.BusId.Find(maintenance.BusId);

            // Map to anonymous type
            var result = new {
                maintenance.MaintenanceId,
                maintenance.BusId,
                Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                LastServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)maintenance.LastServiceDate).DateTime,
                maintenance.ServiceEngineer,
                maintenance.FoundIssues,
                NextServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)maintenance.NextServiceDate).DateTime,
                maintenance.Roadworthiness,
                maintenance.MaintenanceType,
                maintenance.MileageThreshold
            };

            Log.Debug("Successfully retrieved maintenance record with ID {MaintenanceId}", id);
            _logger.LogInformation("FULL MAINTENANCE DATA: {MaintenanceData}", JsonSerializer.Serialize(result)); // Added JSON logging
            return Ok(result); // Return mapped result
        }

        [HttpPost]
        public async Task<ActionResult<Maintenance>> CreateMaintenanceRecord([FromBody] CreateMaintenanceModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to create maintenance record by non-admin user");
                return Forbid();
            }

            Log.Information("Creating new maintenance record for bus ID {BusId}", model.BusId);

            var success = await _maintenanceService.CreateMaintenanceAsync(
                model.BusId,
                (ulong)new DateTimeOffset(model.LastServiceDate).ToUnixTimeMilliseconds(),
                model.ServiceEngineer,
                model.FoundIssues,
                (ulong)new DateTimeOffset(model.NextServiceDate).ToUnixTimeMilliseconds(),
                model.Roadworthiness,
                "Regular" // Default maintenance type
            );

            if (!success)
            {
                Log.Warning("Failed to create maintenance record");
                return BadRequest("Failed to create maintenance record");
            }

            // Get the newly created record
            var records = await _maintenanceService.GetMaintenanceByBusIdAsync(model.BusId);
            var record = records.OrderByDescending(r => r.LastServiceDate).FirstOrDefault();

            if (record == null)
            {
                Log.Error("Maintenance record was created but could not be retrieved");
                return StatusCode(500, "Maintenance record was created but could not be retrieved");
            }

            Log.Information("Successfully created maintenance record with ID {MaintenanceId}", record.MaintenanceId);
            return CreatedAtAction(nameof(GetMaintenanceRecord), new { id = record.MaintenanceId }, record);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMaintenanceRecord(uint id, [FromBody] UpdateMaintenanceModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to update maintenance record by non-admin user");
                return Forbid();
            }

            Log.Information("Updating maintenance record with ID {MaintenanceId}", id);

            var success = await _maintenanceService.UpdateMaintenanceAsync(
                id,
                model.BusId,
                model.LastServiceDate.HasValue ? (ulong)new DateTimeOffset(model.LastServiceDate.Value).ToUnixTimeMilliseconds() : null,
                model.ServiceEngineer,
                model.FoundIssues,
                model.NextServiceDate.HasValue ? (ulong)new DateTimeOffset(model.NextServiceDate.Value).ToUnixTimeMilliseconds() : null,
                model.Roadworthiness
            );

            if (!success)
            {
                Log.Warning("Maintenance record with ID {MaintenanceId} not found for update", id);
                return NotFound();
            }

            Log.Information("Successfully updated maintenance record with ID {MaintenanceId}", id);
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaintenanceRecord(uint id)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to delete maintenance record by non-admin user");
                return Forbid();
            }

            Log.Information("Deleting maintenance record with ID {MaintenanceId}", id);

            var success = await _maintenanceService.DeleteMaintenanceAsync(id);
            if (!success)
            {
                Log.Warning("Maintenance record with ID {MaintenanceId} not found for deletion", id);
                return NotFound();
            }

            Log.Information("Successfully deleted maintenance record with ID {MaintenanceId}", id);
            return NoContent();
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<dynamic>>> SearchMaintenanceRecords( // Changed return type
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string? busModel = null,
            [FromQuery] string? engineer = null,
            [FromQuery] string? roadworthiness = null)
        {
            Log.Information("Searching maintenance records with parameters: StartDate={StartDate}, EndDate={EndDate}, BusModel={BusModel}, Engineer={Engineer}, Roadworthiness={Roadworthiness}",
                startDate, endDate, busModel, engineer, roadworthiness);

            var records = await _maintenanceService.GetAllMaintenanceRecordsAsync();
            var conn = _spacetimeService.GetConnection();
            var query = records.AsEnumerable();

            if (startDate.HasValue)
            {
                var startTimestamp = (ulong)new DateTimeOffset(startDate.Value).ToUnixTimeMilliseconds();
                query = query.Where(m => m.LastServiceDate >= startTimestamp);
            }

            if (endDate.HasValue)
            {
                var endTimestamp = (ulong)new DateTimeOffset(endDate.Value).ToUnixTimeMilliseconds();
                query = query.Where(m => m.LastServiceDate <= endTimestamp);
            }

            if (!string.IsNullOrEmpty(engineer))
                query = query.Where(m => m.ServiceEngineer.Contains(engineer, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(roadworthiness))
                query = query.Where(m => m.Roadworthiness.Equals(roadworthiness, StringComparison.OrdinalIgnoreCase));

            // Filter by bus model (requires joining with Bus table)
            if (!string.IsNullOrEmpty(busModel))
            {
                query = query.Where(m => {
                    var bus = conn.Db.Bus.BusId.Find(m.BusId);
                    return bus != null && bus.Model.Contains(busModel, StringComparison.OrdinalIgnoreCase);
                });
            }

            // Map to anonymous type
            var result = query.Select(m => {
                var bus = conn.Db.Bus.BusId.Find(m.BusId);
                return new {
                    m.MaintenanceId,
                    m.BusId,
                    Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                    LastServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)m.LastServiceDate).DateTime,
                    m.ServiceEngineer,
                    m.FoundIssues,
                    NextServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)m.NextServiceDate).DateTime,
                    m.Roadworthiness,
                    m.MaintenanceType,
                    m.MileageThreshold
                };
            }).ToList();

            Log.Debug("Found {RecordCount} maintenance records matching search criteria", result.Count);
            _logger.LogInformation("FULL SEARCH RESULTS DATA: {MaintenanceData}", JsonSerializer.Serialize(result)); // Added JSON logging
            return Ok(result); // Return mapped result
        }

        [HttpGet("due-maintenance")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetDueMaintenanceRecords() // Changed return type
        {
            Log.Information("Fetching due maintenance records");
            var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var records = await _maintenanceService.GetAllMaintenanceRecordsAsync();
            var conn = _spacetimeService.GetConnection();
            
            var dueRecords = records.Where(m => m.NextServiceDate <= now)
                           .OrderBy(m => m.NextServiceDate)
                           .ToList();
            
            // Map to anonymous type
            var result = dueRecords.Select(m => {
                var bus = conn.Db.Bus.BusId.Find(m.BusId);
                return new {
                    m.MaintenanceId,
                    m.BusId,
                    Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                    LastServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)m.LastServiceDate).DateTime,
                    m.ServiceEngineer,
                    m.FoundIssues,
                    NextServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)m.NextServiceDate).DateTime,
                    m.Roadworthiness,
                    m.MaintenanceType,
                    m.MileageThreshold
                };
            }).ToList();
            
            Log.Debug("Found {RecordCount} due maintenance records", result.Count);
            _logger.LogInformation("FULL DUE MAINTENANCE DATA: {MaintenanceData}", JsonSerializer.Serialize(result)); // Added JSON logging
            return Ok(result); // Return mapped result
        }
    }

    public class CreateMaintenanceModel
    {
        public required uint BusId { get; set; }
        public required DateTime LastServiceDate { get; set; }
        public required string ServiceEngineer { get; set; }
        public required string FoundIssues { get; set; }
        public required DateTime NextServiceDate { get; set; }
        public required string Roadworthiness { get; set; }
    }

    public class UpdateMaintenanceModel
    {
        public uint? BusId { get; set; }
        public DateTime? LastServiceDate { get; set; }
        public string? ServiceEngineer { get; set; }
        public string? FoundIssues { get; set; }
        public DateTime? NextServiceDate { get; set; }
        public string? Roadworthiness { get; set; }
    }
} 