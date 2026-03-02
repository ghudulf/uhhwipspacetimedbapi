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
    [AllowAnonymous] // Allows both custom JWT (manual parsing) and ASP.NET Core auth (OpenIddict)
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
        public async Task<ActionResult<IEnumerable<dynamic>>> GetRouteSchedules(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100,
            [FromQuery] bool? isActive = null)
        {
            try
            {
                _logger.LogInformation("Fetching route schedules - Page: {Page}, PageSize: {PageSize}, IsActive: {IsActive}", 
                    page, pageSize, isActive);
                
                var schedules = await _routeScheduleService.GetAllSchedulesAsync();
                
                _logger.LogInformation("Retrieved {TotalCount} total schedules from database", schedules.Count());
                
                // Filter by IsActive if specified
                var filtered = schedules.AsEnumerable();
                if (isActive.HasValue)
                {
                    filtered = filtered.Where(s => s.IsActive == isActive.Value);
                    _logger.LogDebug("Filtered to {Count} schedules with IsActive={IsActive}", filtered.Count(), isActive.Value);
                }
                
                // Apply pagination
                var totalCount = filtered.Count();
                var paged = filtered
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize);
                
                // Map to anonymous type with ALL fields - CRITICAL for client deserialization
                var result = paged.Select(s => new {
                    s.ScheduleId,
                    s.RouteId,
                    s.StartPoint,
                    s.RouteStops,
                    s.EndPoint,
                    s.DepartureTime,
                    s.ArrivalTime,
                    s.Price,
                    s.AvailableSeats,
                    s.SeatedCapacity,
                    s.StandingCapacity,
                    s.DaysOfWeek,
                    s.BusTypes,
                    s.IsActive,
                    s.ValidFrom,
                    s.ValidUntil,
                    s.StopDurationMinutes,
                    s.IsRecurring,
                    s.EstimatedStopTimes,
                    s.StopDistances,
                    s.Notes,
                    s.CreatedAt,
                    s.UpdatedAt,
                    s.UpdatedBy,
                    s.PeakHourLoad,
                    s.OffPeakHourLoad,
                    s.IsSpecialEvent,
                    s.SpecialEventName,
                    s.IsHoliday,
                    s.HolidayName,
                    s.IsWeekend,
                    s.SeatConfigurationId,
                    s.RequiresSeatReservation,
                    s.RouteType
                }).ToList();

                _logger.LogInformation("Returning {Count} schedules (Page {Page}/{TotalPages}, Total: {TotalCount})", 
                    result.Count, page, (int)Math.Ceiling(totalCount / (double)pageSize), totalCount);
                
                // Add pagination metadata to response headers
                Response.Headers.Add("X-Total-Count", totalCount.ToString());
                Response.Headers.Add("X-Page", page.ToString());
                Response.Headers.Add("X-Page-Size", pageSize.ToString());
                Response.Headers.Add("X-Total-Pages", ((int)Math.Ceiling(totalCount / (double)pageSize)).ToString());
                
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
            [FromQuery] bool? isActive = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                _logger.LogInformation("=== ROUTE SCHEDULES SEARCH REQUEST START ===");
                _logger.LogInformation("Search Parameters: routeId={RouteId}, date={Date}, dayOfWeek={DayOfWeek}, isActive={IsActive}, page={Page}, pageSize={PageSize}",
                    routeId, date, dayOfWeek, isActive, page, pageSize);

                var schedules = await _routeScheduleService.GetAllSchedulesAsync();
                var totalSchedulesInDatabase = schedules.Count();
                
                _logger.LogInformation("TOTAL ROUTE SCHEDULES IN DATABASE: {TotalCount}", totalSchedulesInDatabase);
                _logger.LogInformation("Database statistics: {Recurring} recurring, {NonRecurring} non-recurring schedules",
                    schedules.Count(s => s.IsRecurring),
                    schedules.Count(s => !s.IsRecurring));
                
                var query = schedules.AsEnumerable(); // Start query on IEnumerable

                if (routeId.HasValue)
                {
                    var beforeRouteFilter = query.Count();
                    query = query.Where(s => s.RouteId == routeId.Value);
                    var afterRouteFilter = query.Count();
                    
                    _logger.LogInformation("Route filter applied: RouteId={RouteId}, Schedules before={Before}, after={After}, removed={Removed}",
                        routeId.Value, beforeRouteFilter, afterRouteFilter, beforeRouteFilter - afterRouteFilter);
                }

                if (date.HasValue)
                {
                    // COMPREHENSIVE DATE FILTERING WITH EXTENSIVE LOGGING AND VALIDATION
                    var targetDate = date.Value.Date;
                    var targetDayOfWeek = targetDate.DayOfWeek.ToString(); // English day name
                    
                    // CRITICAL FIX: Database stores days in RUSSIAN, not English
                    // Map English day names to Russian equivalents for comparison
                    var dayOfWeekMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "Monday", "Понедельник" },
                        { "Tuesday", "Вторник" },
                        { "Wednesday", "Среда" },
                        { "Thursday", "Четверг" },
                        { "Friday", "Пятница" },
                        { "Saturday", "Суббота" },
                        { "Sunday", "Воскресенье" }
                    };
                    
                    var targetDayOfWeekRussian = dayOfWeekMapping.ContainsKey(targetDayOfWeek) 
                        ? dayOfWeekMapping[targetDayOfWeek] 
                        : targetDayOfWeek;
                    
                    var targetDateStartMs = (ulong)new DateTimeOffset(targetDate).ToUnixTimeMilliseconds();
                    var targetDateEndMs = targetDateStartMs + 86400000; // Add 24 hours in milliseconds
                    
                    _logger.LogInformation("=== DATE FILTER ANALYSIS START ===");
                    _logger.LogInformation("Target Date: {Date} ({DayOfWeek} / {DayOfWeekRussian})", 
                        targetDate.ToString("yyyy-MM-dd"), targetDayOfWeek, targetDayOfWeekRussian);
                    _logger.LogInformation("Target Date Range (Unix ms): {Start} to {End}", targetDateStartMs, targetDateEndMs);
                    _logger.LogInformation("Total schedules before date filter: {Count}", query.Count());
                    
                    // Count schedules by type before filtering for diagnostic purposes
                    var totalBeforeFilter = query.Count();
                    var recurringBeforeFilter = query.Count(s => s.IsRecurring);
                    var nonRecurringBeforeFilter = totalBeforeFilter - recurringBeforeFilter;
                    
                    _logger.LogInformation("Schedules breakdown: {Total} total ({Recurring} recurring, {NonRecurring} non-recurring)", 
                        totalBeforeFilter, recurringBeforeFilter, nonRecurringBeforeFilter);
                    
                    // Apply comprehensive date filtering with multiple conditions
                    // CRITICAL: Use Russian day names for comparison since database stores them in Russian
                    // NOTE: We do NOT filter by ValidFrom/ValidUntil to avoid 1970 epoch bug issues
                    query = query.Where(s => 
                    {
                        // CONDITION 1: Non-recurring schedule with exact date match
                        // Check if the departure time falls within the target date (00:00:00 to 23:59:59)
                        var isExactDateMatch = !s.IsRecurring && 
                                               s.DepartureTime >= targetDateStartMs && 
                                               s.DepartureTime < targetDateEndMs;
                        
                        // CONDITION 2: Recurring schedule that runs on this day of week
                        // Must satisfy ALL of the following:
                        // - Schedule is marked as recurring
                        // - DaysOfWeek list is not null and not empty
                        // - DaysOfWeek contains the target day IN RUSSIAN (case-insensitive)
                        // NOTE: ValidFrom/ValidUntil checks REMOVED to avoid date range issues
                        var isRecurringMatch = false;
                        if (s.IsRecurring)
                        {
                            var hasDaysOfWeek = s.DaysOfWeek != null && s.DaysOfWeek.Count > 0;
                            
                            // CRITICAL FIX: Compare against RUSSIAN day name
                            var matchesDayOfWeek = hasDaysOfWeek && 
                                                   s.DaysOfWeek.Any(day => 
                                                       string.Equals(day, targetDayOfWeekRussian, StringComparison.OrdinalIgnoreCase));
                            
                            isRecurringMatch = hasDaysOfWeek && matchesDayOfWeek;
                        }
                        
                        // CONDITION 3: One-time schedule with departure time on exact date
                        // This handles schedules that are not recurring but have a specific departure date
                        var isOneTimeScheduleMatch = !s.IsRecurring && 
                                                     s.DepartureTime >= targetDateStartMs && 
                                                     s.DepartureTime < targetDateEndMs;
                        
                        // Return true if ANY of the conditions are met
                        return isExactDateMatch || isRecurringMatch || isOneTimeScheduleMatch;
                    });
                    
                    // Post-filter analysis and logging
                    var totalAfterFilter = query.Count();
                    var recurringAfterFilter = query.Count(s => s.IsRecurring);
                    var nonRecurringAfterFilter = totalAfterFilter - recurringAfterFilter;
                    
                    _logger.LogInformation("Schedules after date filter: {Total} total ({Recurring} recurring, {NonRecurring} non-recurring)", 
                        totalAfterFilter, recurringAfterFilter, nonRecurringAfterFilter);
                    _logger.LogInformation("Filter removed {Removed} schedules ({RemovedRecurring} recurring, {RemovedNonRecurring} non-recurring)",
                        totalBeforeFilter - totalAfterFilter,
                        recurringBeforeFilter - recurringAfterFilter,
                        nonRecurringBeforeFilter - nonRecurringAfterFilter);
                    
                    // Sample logging: show first few matching schedules for verification
                    var sampleSchedules = query.Take(3).ToList();
                    if (sampleSchedules.Any())
                    {
                        _logger.LogDebug("Sample matching schedules:");
                        foreach (var sample in sampleSchedules)
                        {
                            var daysOfWeekStr = sample.DaysOfWeek != null ? string.Join(", ", sample.DaysOfWeek) : "null";
                            var validFromDate = DateTimeOffset.FromUnixTimeMilliseconds((long)sample.ValidFrom).ToString("yyyy-MM-dd");
                            var validUntilDate = sample.ValidUntil.HasValue 
                                ? DateTimeOffset.FromUnixTimeMilliseconds((long)sample.ValidUntil.Value).ToString("yyyy-MM-dd") 
                                : "null (no expiration)";
                            
                            _logger.LogDebug("  Schedule {ScheduleId}: Route {RouteId}, IsRecurring={IsRecurring}, " +
                                           "DaysOfWeek=[{DaysOfWeek}], ValidFrom={ValidFrom}, ValidUntil={ValidUntil}",
                                sample.ScheduleId, sample.RouteId, sample.IsRecurring, 
                                daysOfWeekStr, validFromDate, validUntilDate);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("NO SCHEDULES MATCHED THE DATE FILTER - This may indicate a data or logic issue");
                        _logger.LogWarning("Verify that:");
                        _logger.LogWarning("  1. Schedules exist for route {RouteId}", routeId);
                        _logger.LogWarning("  2. Recurring schedules have DaysOfWeek that include '{DayOfWeek}' (Russian: '{DayOfWeekRussian}')", 
                            targetDayOfWeek, targetDayOfWeekRussian);
                        _logger.LogWarning("  NOTE: ValidFrom/ValidUntil checks are disabled to avoid date range issues");
                        
                        // DIAGNOSTIC: Sample filtered-out schedules to show WHY they were excluded
                        var sampleFiltered = query.Take(5).ToList();
                        if (sampleFiltered.Any())
                        {
                            _logger.LogWarning("=== SAMPLE FILTERED-OUT SCHEDULES (showing why they were excluded) ===");
                            foreach (var sample in sampleFiltered)
                            {
                                var daysOfWeekStr = sample.DaysOfWeek != null ? string.Join(", ", sample.DaysOfWeek) : "null";
                                var validFromDate = DateTimeOffset.FromUnixTimeMilliseconds((long)sample.ValidFrom).ToString("yyyy-MM-dd");
                                var validUntilDate = sample.ValidUntil.HasValue 
                                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)sample.ValidUntil.Value).ToString("yyyy-MM-dd") 
                                    : "null (no expiration)";
                                
                                // Determine exclusion reasons
                                var reasons = new List<string>();
                                
                                var hasDaysOfWeek = sample.DaysOfWeek != null && sample.DaysOfWeek.Count > 0;
                                var matchesDayOfWeek = hasDaysOfWeek && 
                                                       sample.DaysOfWeek.Any(day => 
                                                           string.Equals(day, targetDayOfWeekRussian, StringComparison.OrdinalIgnoreCase));
                                
                                if (!hasDaysOfWeek)
                                    reasons.Add("DaysOfWeek is null or empty");
                                else if (!matchesDayOfWeek)
                                    reasons.Add($"DaysOfWeek [{daysOfWeekStr}] does not include '{targetDayOfWeekRussian}'");
                                
                                // NOTE: ValidFrom/ValidUntil checks removed from filter logic
                                // Showing dates for informational purposes only
                                
                                var reasonsStr = reasons.Any() ? string.Join("; ", reasons) : "UNKNOWN - should have matched!";
                                
                                _logger.LogWarning("  Schedule {ScheduleId} (Route {RouteId}): IsRecurring={IsRecurring}, " +
                                               "DaysOfWeek=[{DaysOfWeek}], ValidFrom={ValidFrom}, ValidUntil={ValidUntil} | EXCLUDED: {Reasons}",
                                    sample.ScheduleId, sample.RouteId, sample.IsRecurring, 
                                    daysOfWeekStr, validFromDate, validUntilDate, reasonsStr);
                            }
                            _logger.LogWarning("=== END SAMPLE FILTERED-OUT SCHEDULES ===");
                        }
                    }
                    
                    _logger.LogInformation("=== DATE FILTER ANALYSIS END ===");
                }

                if (!string.IsNullOrEmpty(dayOfWeek))
                {
                    var beforeDayFilter = query.Count();
                    query = query.Where(s => s.DaysOfWeek != null && s.DaysOfWeek.Contains(dayOfWeek, StringComparer.OrdinalIgnoreCase));
                    var afterDayFilter = query.Count();
                    
                    _logger.LogInformation("Day of week filter applied: DayOfWeek={DayOfWeek}, Schedules before={Before}, after={After}, removed={Removed}",
                        dayOfWeek, beforeDayFilter, afterDayFilter, beforeDayFilter - afterDayFilter);
                }
                
                if (isActive.HasValue)
                {
                    var beforeActiveFilter = query.Count();
                    query = query.Where(s => s.IsActive == isActive.Value);
                    var afterActiveFilter = query.Count();
                    
                    _logger.LogInformation("IsActive filter applied: IsActive={IsActive}, Schedules before={Before}, after={After}, removed={Removed}",
                        isActive.Value, beforeActiveFilter, afterActiveFilter, beforeActiveFilter - afterActiveFilter);
                }

                // Get total count before pagination
                var totalCount = query.Count();
                
                // Apply pagination
                var paged = query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize);

                // Map to anonymous type after filtering
                var result = paged.Select(s => new {
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

                // Add pagination metadata to response headers
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                var metadata = new
                {
                    TotalCount = totalCount,
                    PageSize = pageSize,
                    CurrentPage = page,
                    TotalPages = totalPages,
                    HasNext = page < totalPages,
                    HasPrevious = page > 1,
                    NextPage = page < totalPages ? page + 1 : page,
                    PreviousPage = page > 1 ? page - 1 : 1,
                    FirstPage = 1,
                    LastPage = totalPages
                };

                Response.Headers.Add("X-Pagination", JsonSerializer.Serialize(metadata));

                _logger.LogInformation("=== ROUTE SCHEDULES SEARCH REQUEST END ===");
                _logger.LogInformation("FINAL RESULT: Found {Count} matching schedules (Page {Page}/{TotalPages}, Total: {TotalCount})", 
                    result.Count, page, totalPages, totalCount);
                _logger.LogInformation("Total database schedules: {DatabaseTotal}, After all filters: {FilteredTotal}, Returned on this page: {PageCount}",
                    totalSchedulesInDatabase, totalCount, result.Count);
                
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