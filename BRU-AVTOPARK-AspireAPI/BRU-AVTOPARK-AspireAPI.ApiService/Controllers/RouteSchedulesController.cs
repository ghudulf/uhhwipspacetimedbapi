using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Serilog;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Allow all authenticated users to read
    public class RouteSchedulesController : ControllerBase
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<RouteSchedulesController> _logger;

        public RouteSchedulesController(ISpacetimeDBService spacetimeService, ILogger<RouteSchedulesController> logger)
        {
            _spacetimeService = spacetimeService;
            _logger = logger;
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
        public async Task<ActionResult<IEnumerable<RouteSchedule>>> GetRouteSchedules()
        {
            Log.Information("Fetching all route schedules");
            try
            {
                var conn = _spacetimeService.GetConnection();
                var schedules = conn.Db.RouteSchedule.Iter().ToList();
                Log.Debug("Retrieved {ScheduleCount} route schedules", schedules.Count);
                return schedules;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving route schedules");
                return StatusCode(500, "An error occurred while retrieving route schedules");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RouteSchedule>> GetRouteSchedule(uint id)
        {
            Log.Information("Fetching route schedule with ID {ScheduleId}", id);
            try
            {
                var conn = _spacetimeService.GetConnection();
                var schedule = conn.Db.RouteSchedule.ScheduleId.Find(id);

                if (schedule == null)
                {
                    Log.Warning("Route schedule with ID {ScheduleId} not found", id);
                    return NotFound();
                }

                Log.Debug("Successfully retrieved route schedule with ID {ScheduleId}", id);
                return schedule;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving route schedule {ScheduleId}", id);
                return StatusCode(500, "An error occurred while retrieving the route schedule");
            }
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<RouteSchedule>>> SearchRouteSchedules(
            [FromQuery] uint? routeId = null,
            [FromQuery] DateTime? date = null,
            [FromQuery] string? dayOfWeek = null,
            [FromQuery] bool? isActive = null)
        {
            try
            {
                Log.Information("Searching route schedules with parameters - RouteId: {RouteId}, Date: {Date}, Day: {Day}, Active: {Active}",
                    routeId?.ToString() ?? "any", date?.ToString() ?? "any", dayOfWeek ?? "any", isActive?.ToString() ?? "any");

                var conn = _spacetimeService.GetConnection();
                var query = conn.Db.RouteSchedule.Iter().AsEnumerable();

                if (routeId.HasValue)
                {
                    Log.Debug("Filtering by RouteId: {RouteId}", routeId.Value);
                    query = query.Where(rs => rs.RouteId == routeId.Value);
                }

                // Make date filtering very lenient
                if (date.HasValue)
                {
                    var now = DateTime.Now.Date;
                    Log.Debug("Current date: {Now}, Target date: {Date}", now, date.Value);
                    
                    var targetTimestamp = date.Value.ToUnixTimeMilliseconds();
                    var nowTimestamp = now.ToUnixTimeMilliseconds();
                    
                    // Check if schedule is valid (either no end date, or end date is in the future)
                    query = query.Where(rs => 
                        !rs.ValidUntil.HasValue || // No end date
                        rs.ValidUntil >= nowTimestamp || // Still valid
                        rs.ValidFrom <= targetTimestamp); // Starting within target date
                }

                if (!string.IsNullOrEmpty(dayOfWeek))
                {
                    Log.Debug("Filtering by day of week: {Day}", dayOfWeek);
                    query = query.Where(rs => rs.DaysOfWeek != null && rs.DaysOfWeek.Contains(dayOfWeek));
                }

                if (isActive.HasValue)
                {
                    Log.Debug("Filtering by active status: {Active}", isActive.Value);
                    query = query.Where(rs => rs.IsActive == isActive.Value);
                }

                var results = query.ToList();
                Log.Debug("Found {Count} route schedules matching search criteria", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error searching route schedules");
                return StatusCode(500, "An error occurred while searching route schedules");
            }
        }

        [HttpPost]
        public async Task<ActionResult<RouteSchedule>> CreateRouteSchedule([FromBody] CreateRouteScheduleModel model)
        {
            try
            {
                if (!IsAdmin())
                {
                    Log.Warning("Unauthorized attempt to create route schedule by non-admin user");
                    return Forbid();
                }

                Log.Information("Creating new route schedule for route {RouteId}", model.RouteId);

                // Validate model
                if (model.RouteStops == null || model.RouteStops.Length < 2)
                {
                    Log.Warning("Invalid route stops provided: must have at least 2 stops");
                    return BadRequest("Route must have at least 2 stops");
                }

                if (model.DepartureTime >= model.ArrivalTime)
                {
                    Log.Warning("Invalid time range: departure time must be before arrival time");
                    return BadRequest("Departure time must be before arrival time");
                }

                if (model.Price <= 0)
                {
                    Log.Warning("Invalid price: must be greater than 0");
                    return BadRequest("Price must be greater than 0");
                }

                if (model.AvailableSeats <= 0)
                {
                    Log.Warning("Invalid seats: must be greater than 0");
                    return BadRequest("Available seats must be greater than 0");
                }

                var conn = _spacetimeService.GetConnection();
                var route = conn.Db.Route.RouteId.Find(model.RouteId);
                if (route == null)
                {
                    Log.Warning("Invalid route ID {RouteId} provided for schedule creation", model.RouteId);
                    return BadRequest("Invalid route ID");
                }

                // Ensure arrays are initialized
                model.DaysOfWeek ??= new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
                model.BusTypes ??= new[] { "МАЗ-103", "МАЗ-107" };
                model.EstimatedStopTimes ??= new string[model.RouteStops.Length];
                model.StopDistances ??= new double[model.RouteStops.Length];

                // Call the CreateRouteSchedule reducer
                conn.Reducers.CreateRouteSchedule(
                    model.RouteId,
                    model.StartPoint,
                    model.EndPoint,
                    model.RouteStops,
                    model.DepartureTime.ToUnixTimeMilliseconds(),
                    model.ArrivalTime.ToUnixTimeMilliseconds(),
                    model.Price,
                    model.AvailableSeats,
                    model.DaysOfWeek,
                    model.BusTypes,
                    true, // IsActive
                    DateTime.Now.ToUnixTimeMilliseconds(), // ValidFrom
                    model.StopDurationMinutes,
                    model.IsRecurring,
                    model.EstimatedStopTimes,
                    model.StopDistances,
                    model.Notes
                );

                // Wait a moment for the reducer to complete and the subscription to update
                await Task.Delay(100);

                // Find the newly created schedule
                var schedule = conn.Db.RouteSchedule.Iter()
                    .OrderByDescending(rs => rs.ScheduleId)
                    .FirstOrDefault();

                if (schedule == null)
                {
                    Log.Error("Schedule was not created properly");
                    return StatusCode(500, "Failed to create schedule");
                }

                Log.Information("Successfully created route schedule with ID {ScheduleId}", schedule.ScheduleId);
                return CreatedAtAction(nameof(GetRouteSchedule), new { id = schedule.ScheduleId }, schedule);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating route schedule");
                return StatusCode(500, "An error occurred while creating the route schedule");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRouteSchedule(uint id, [FromBody] UpdateRouteScheduleModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to update route schedule by non-admin user");
                return Forbid();
            }

            try
            {
                Log.Information("Updating route schedule with ID {ScheduleId}", id);
                var conn = _spacetimeService.GetConnection();

                var schedule = conn.Db.RouteSchedule.ScheduleId.Find(id);
                if (schedule == null)
                {
                    Log.Warning("Route schedule with ID {ScheduleId} not found for update", id);
                    return NotFound();
                }

                if (model.RouteId.HasValue)
                {
                    var route = conn.Db.Route.RouteId.Find(model.RouteId.Value);
                    if (route == null)
                    {
                        Log.Warning("Invalid route ID {RouteId} provided for schedule update", model.RouteId.Value);
                        return BadRequest("Invalid route ID");
                    }
                }

                // Call the UpdateRouteSchedule reducer
                conn.Reducers.UpdateRouteSchedule(
                    id,
                    model.RouteId ?? schedule.RouteId,
                    model.StartPoint ?? schedule.StartPoint,
                    model.EndPoint ?? schedule.EndPoint,
                    model.RouteStops ?? schedule.RouteStops,
                    model.DepartureTime?.ToUnixTimeMilliseconds() ?? schedule.DepartureTime,
                    model.ArrivalTime?.ToUnixTimeMilliseconds() ?? schedule.ArrivalTime,
                    model.Price ?? schedule.Price,
                    model.AvailableSeats ?? schedule.AvailableSeats,
                    model.DaysOfWeek ?? schedule.DaysOfWeek,
                    model.BusTypes ?? schedule.BusTypes,
                    model.IsActive ?? schedule.IsActive,
                    model.ValidUntil?.ToUnixTimeMilliseconds(),
                    model.StopDurationMinutes ?? schedule.StopDurationMinutes,
                    model.IsRecurring ?? schedule.IsRecurring,
                    model.EstimatedStopTimes ?? schedule.EstimatedStopTimes,
                    model.StopDistances ?? schedule.StopDistances,
                    model.Notes ?? schedule.Notes,
                    User.Identity?.Name
                );

                Log.Information("Successfully updated route schedule with ID {ScheduleId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating route schedule {ScheduleId}", id);
                return StatusCode(500, "An error occurred while updating the route schedule");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRouteSchedule(uint id)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to delete route schedule by non-admin user");
                return Forbid();
            }

            try
            {
                Log.Information("Deleting route schedule with ID {ScheduleId}", id);
                var conn = _spacetimeService.GetConnection();

                var schedule = conn.Db.RouteSchedule.ScheduleId.Find(id);
                if (schedule == null)
                {
                    Log.Warning("Route schedule with ID {ScheduleId} not found for deletion", id);
                    return NotFound();
                }

                // Call the DeleteRouteSchedule reducer
                conn.Reducers.DeleteRouteSchedule(id);

                Log.Information("Successfully deleted route schedule with ID {ScheduleId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting route schedule {ScheduleId}", id);
                return StatusCode(500, "An error occurred while deleting the route schedule");
            }
        }
    }

    public class CreateRouteScheduleModel
    {
        public required uint RouteId { get; set; }
        public required string StartPoint { get; set; }
        public required string EndPoint { get; set; }
        public required string[] RouteStops { get; set; }
        public required DateTime DepartureTime { get; set; }
        public required DateTime ArrivalTime { get; set; }
        public required double Price { get; set; }
        public required uint AvailableSeats { get; set; }
        public required string[] DaysOfWeek { get; set; }
        public required string[] BusTypes { get; set; }
        public uint StopDurationMinutes { get; set; } = 5;
        public bool IsRecurring { get; set; } = true;
        public string[]? EstimatedStopTimes { get; set; }
        public double[]? StopDistances { get; set; }
        public string? Notes { get; set; }
    }

    public class UpdateRouteScheduleModel
    {
        public uint? RouteId { get; set; }
        public string? StartPoint { get; set; }
        public string? EndPoint { get; set; }
        public string[]? RouteStops { get; set; }
        public DateTime? DepartureTime { get; set; }
        public DateTime? ArrivalTime { get; set; }
        public double? Price { get; set; }
        public uint? AvailableSeats { get; set; }
        public string[]? DaysOfWeek { get; set; }
        public string[]? BusTypes { get; set; }
        public bool? IsActive { get; set; }
        public DateTime? ValidUntil { get; set; }
        public uint? StopDurationMinutes { get; set; }
        public bool? IsRecurring { get; set; }
        public string[]? EstimatedStopTimes { get; set; }
        public double[]? StopDistances { get; set; }
        public string? Notes { get; set; }
    }
} 