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
    [AllowAnonymous] // Allow all authenticated users to read
    public class RouteSchedulesController : BaseController
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

       

        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetRouteSchedules()
        {
            try
            {
                _logger.LogInformation("Fetching all route schedules");
                var schedules = await _routeScheduleService.GetAllSchedulesAsync();
                
                // Map to anonymous type
                var result = schedules.Select(s => new {
                    s.ScheduleId,
                    s.RouteId,
                    s.StartPoint,
                    s.EndPoint,
                    s.RouteStops,
                    DepartureTime = DateTimeOffset.FromUnixTimeMilliseconds((long)s.DepartureTime).DateTime,
                    ArrivalTime = DateTimeOffset.FromUnixTimeMilliseconds((long)s.ArrivalTime).DateTime,
                    s.Price,
                    s.AvailableSeats,
                    s.DaysOfWeek,
                    s.BusTypes,
                    s.StopDurationMinutes,
                    s.IsRecurring,
                    s.EstimatedStopTimes,
                    s.StopDistances,
                    s.Notes
                }).ToList();

                _logger.LogDebug("Retrieved {Count} schedules", result.Count);
                _logger.LogInformation("FULL SCHEDULE DATA: {SchedulesData}", JsonSerializer.Serialize(result));
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving route schedules");
                return StatusCode(500, "An error occurred while retrieving route schedules");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetRouteSchedule(uint id)
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

                // Map to anonymous type
                var result = new {
                    schedule.ScheduleId,
                    schedule.RouteId,
                    schedule.StartPoint,
                    schedule.EndPoint,
                    schedule.RouteStops,
                    DepartureTime = DateTimeOffset.FromUnixTimeMilliseconds((long)schedule.DepartureTime).DateTime,
                    ArrivalTime = DateTimeOffset.FromUnixTimeMilliseconds((long)schedule.ArrivalTime).DateTime,
                    schedule.Price,
                    schedule.AvailableSeats,
                    schedule.DaysOfWeek,
                    schedule.BusTypes,
                    schedule.StopDurationMinutes,
                    schedule.IsRecurring,
                    schedule.EstimatedStopTimes,
                    schedule.StopDistances,
                    schedule.Notes
                };

                _logger.LogInformation("Successfully retrieved schedule {ScheduleId}", id);
                _logger.LogInformation("FULL SCHEDULE DATA: {ScheduleData}", JsonSerializer.Serialize(result));
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving route schedule {ScheduleId}", id);
                return StatusCode(500, "An error occurred while retrieving the route schedule");
            }
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<dynamic>>> SearchRouteSchedules(
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
                var query = schedules.AsEnumerable(); // Start query on IEnumerable

                if (routeId.HasValue)
                    query = query.Where(s => s.RouteId == routeId.Value);

                if (date.HasValue)
                {
                    // Convert the target date to start and end timestamps for the entire day (UTC)
                    var startOfDay = new DateTimeOffset(date.Value.Date).ToUnixTimeMilliseconds();
                    var endOfDay = startOfDay + 86400000; // Add 24 hours in milliseconds
                    _logger.LogDebug("Filtering by date: Start={StartTimestamp}, End={EndTimestamp}", startOfDay, endOfDay);
                    query = query.Where(s => s.DepartureTime >= (ulong)startOfDay && s.DepartureTime < (ulong)endOfDay);
                }

                if (!string.IsNullOrEmpty(dayOfWeek))
                    query = query.Where(s => s.DaysOfWeek.Contains(dayOfWeek, StringComparer.OrdinalIgnoreCase)); // Use StringComparer
                
                // isActive filter needs to be added if RouteSchedule entity has an IsActive property
                // if (isActive.HasValue)
                //     query = query.Where(s => s.IsActive == isActive.Value);

                // Map to anonymous type after filtering
                var result = query.Select(s => new {
                    s.ScheduleId,
                    s.RouteId,
                    s.StartPoint,
                    s.EndPoint,
                    s.RouteStops,
                    DepartureTime = DateTimeOffset.FromUnixTimeMilliseconds((long)s.DepartureTime).DateTime,
                    ArrivalTime = DateTimeOffset.FromUnixTimeMilliseconds((long)s.ArrivalTime).DateTime,
                    s.Price,
                    s.AvailableSeats,
                    s.DaysOfWeek,
                    s.BusTypes,
                    s.StopDurationMinutes,
                    s.IsRecurring,
                    s.EstimatedStopTimes,
                    s.StopDistances,
                    s.Notes
                }).ToList();

                _logger.LogDebug("Found {Count} matching schedules", result.Count);
                _logger.LogInformation("FULL SEARCH RESULTS DATA: {SchedulesData}", JsonSerializer.Serialize(result));
                return Ok(result);
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