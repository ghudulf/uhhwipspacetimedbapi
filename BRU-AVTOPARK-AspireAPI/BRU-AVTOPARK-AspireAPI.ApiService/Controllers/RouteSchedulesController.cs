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
    public class RouteSchedulesController : ControllerBase
    {
        private readonly IRouteScheduleService _routeScheduleService;
        private readonly ILogger<RouteSchedulesController> _logger;

        public RouteSchedulesController(
            IRouteScheduleService routeScheduleService,
            ILogger<RouteSchedulesController> logger)
        {
            _routeScheduleService = routeScheduleService ?? throw new ArgumentNullException(nameof(routeScheduleService));
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
        public async Task<ActionResult<IEnumerable<RouteSchedule>>> GetRouteSchedules()
        {
            try
            {
                _logger.LogInformation("Fetching all route schedules");
                var schedules = await _routeScheduleService.GetAllSchedulesAsync();
                _logger.LogDebug("Retrieved {Count} schedules", schedules.Count);
                return Ok(schedules);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving route schedules");
                return StatusCode(500, "An error occurred while retrieving route schedules");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RouteSchedule>> GetRouteSchedule(uint id)
        {
            try
            {
                _logger.LogInformation("Fetching route schedule {ScheduleId}", id);
                var schedule = await _routeScheduleService.GetScheduleByIdAsync(id);

                if (schedule == null)
                {
                    _logger.LogWarning("Route schedule {ScheduleId} not found", id);
                    return NotFound();
                }

                return Ok(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving route schedule {ScheduleId}", id);
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
                _logger.LogInformation("Searching route schedules with routeId: {RouteId}, date: {Date}, dayOfWeek: {DayOfWeek}, isActive: {IsActive}",
                    routeId, date, dayOfWeek, isActive);

                var schedules = await _routeScheduleService.GetAllSchedulesAsync();

                if (routeId.HasValue)
                    schedules = schedules.Where(s => s.RouteId == routeId.Value).ToList();

                if (date.HasValue)
                {
                    var timestamp = (ulong)new DateTimeOffset(date.Value).ToUnixTimeMilliseconds();
                    schedules = schedules.Where(s => s.DepartureTime >= timestamp && 
                                                   s.DepartureTime < timestamp + 86400000).ToList(); // 24 hours in milliseconds
                }

                if (!string.IsNullOrEmpty(dayOfWeek))
                    schedules = schedules.Where(s => s.DaysOfWeek.Contains(dayOfWeek)).ToList();

                _logger.LogDebug("Found {Count} matching schedules", schedules.Count);
                return Ok(schedules);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching route schedules");
                return StatusCode(500, "An error occurred while searching route schedules");
            }
        }

        [HttpPost]
        public async Task<ActionResult<RouteSchedule>> CreateRouteSchedule([FromBody] CreateRouteScheduleModel model)
        {
            if (!IsAdmin())
            {
                _logger.LogWarning("Unauthorized attempt to create route schedule");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Creating new route schedule for route {RouteId}", model.RouteId);

                var success = await _routeScheduleService.CreateScheduleAsync(
                    routeId: model.RouteId,
                    startPoint: model.StartPoint,
                    endPoint: model.EndPoint,
                    routeStops: model.RouteStops?.ToList(),
                    departureTime: (ulong)new DateTimeOffset(model.DepartureTime).ToUnixTimeMilliseconds(),
                    arrivalTime: (ulong)new DateTimeOffset(model.ArrivalTime).ToUnixTimeMilliseconds(),
                    price: model.Price,
                    availableSeats: model.AvailableSeats,
                    daysOfWeek: model.DaysOfWeek?.ToList(),
                    busTypes: model.BusTypes?.ToList(),
                    stopDurationMinutes: model.StopDurationMinutes,
                    isRecurring: model.IsRecurring,
                    estimatedStopTimes: model.EstimatedStopTimes?.ToList(),
                    stopDistances: model.StopDistances?.ToList(),
                    notes: model.Notes
                );

                if (!success)
                {
                    _logger.LogWarning("Failed to create route schedule");
                    return BadRequest("Failed to create route schedule");
                }

                // Get the newly created schedule
                var schedules = await _routeScheduleService.GetAllSchedulesAsync();
                var schedule = schedules.LastOrDefault();

                if (schedule == null)
                {
                    _logger.LogError("Schedule was created but could not be retrieved");
                    return StatusCode(500, "Schedule was created but could not be retrieved");
                }

                _logger.LogInformation("Successfully created route schedule {ScheduleId}", schedule.ScheduleId);
                return CreatedAtAction(nameof(GetRouteSchedule), new { id = schedule.ScheduleId }, schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating route schedule");
                return StatusCode(500, "An error occurred while creating the route schedule");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRouteSchedule(uint id, [FromBody] UpdateRouteScheduleModel model)
        {
            if (!IsAdmin())
            {
                _logger.LogWarning("Unauthorized attempt to update route schedule");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Updating route schedule {ScheduleId}", id);

                var success = await _routeScheduleService.UpdateScheduleAsync(
                    scheduleId: id,
                    routeId: model.RouteId,
                    startPoint: model.StartPoint,
                    endPoint: model.EndPoint,
                    routeStops: model.RouteStops?.ToList(),
                    departureTime: model.DepartureTime.HasValue ? (ulong)new DateTimeOffset(model.DepartureTime.Value).ToUnixTimeMilliseconds() : null,
                    arrivalTime: model.ArrivalTime.HasValue ? (ulong)new DateTimeOffset(model.ArrivalTime.Value).ToUnixTimeMilliseconds() : null,
                    price: model.Price,
                    availableSeats: model.AvailableSeats,
                    daysOfWeek: model.DaysOfWeek?.ToList(),
                    busTypes: model.BusTypes?.ToList(),
                    stopDurationMinutes: model.StopDurationMinutes,
                    isRecurring: model.IsRecurring,
                    estimatedStopTimes: model.EstimatedStopTimes?.ToList(),
                    stopDistances: model.StopDistances?.ToList(),
                    notes: model.Notes
                );

                if (!success)
                {
                    _logger.LogWarning("Route schedule {ScheduleId} not found", id);
                    return NotFound();
                }

                _logger.LogInformation("Successfully updated route schedule {ScheduleId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating route schedule {ScheduleId}", id);
                return StatusCode(500, "An error occurred while updating the route schedule");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRouteSchedule(uint id)
        {
            if (!IsAdmin())
            {
                _logger.LogWarning("Unauthorized attempt to delete route schedule");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Deleting route schedule {ScheduleId}", id);

                var success = await _routeScheduleService.DeleteScheduleAsync(id);
                if (!success)
                {
                    _logger.LogWarning("Route schedule {ScheduleId} not found", id);
                    return NotFound();
                }

                _logger.LogInformation("Successfully deleted route schedule {ScheduleId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting route schedule {ScheduleId}", id);
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