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
    public class RouteSchedulesController : BaseController
    {
        private readonly IRouteScheduleService _routeScheduleService;
        private readonly ILogger<RouteSchedulesController> _logger;
        private readonly IRealtimeEventBus _realtimeEventBus;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of <see cref="RouteSchedulesController"/> with its required services.
        /// </summary>
        /// <param name="routeScheduleService">Service for managing route schedules.</param>
        /// <param name="logger">Logger for controller operations.</param>
        /// <param name="realtimeEventBus">Event bus used to publish and subscribe realtime schedule events.</param>
        /// <param name="configuration">Application configuration for reading runtime settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when any of the provided dependencies is null.</exception>
        public RouteSchedulesController(
            IRouteScheduleService routeScheduleService,
            ILogger<RouteSchedulesController> logger,
            IRealtimeEventBus realtimeEventBus,
            IConfiguration configuration)
        {
            _routeScheduleService = routeScheduleService ?? throw new ArgumentNullException(nameof(routeScheduleService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

       

        /// <summary>
        /// Opens a WebSocket stream that serves realtime CRUD events for route schedules.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the streaming session.</param>
        [HttpGet("realtime/ws")]
        public async Task StreamRealtimeEvents(CancellationToken cancellationToken)
        {
            if (!IsAuthenticated())
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await WebSocketEventStreamWriter.StreamCrudSessionAsync(
                HttpContext,
                _realtimeEventBus.SubscribeAsync("route-schedules", cancellationToken),
                HandleRealtimeCrudAsync,
                _logger,
                cancellationToken);
        }

        /// <summary>
        /// Handle a realtime CRUD request for route schedules by dispatching the request command to the corresponding operation.
        /// </summary>
        /// <param name="request">The realtime CRUD request containing the command, optional id, and payload.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>
        /// An object whose shape depends on the command:
        /// - For "read_all": { schedules = IEnumerable of schedules, pagination = { page, pageSize, totalCount, totalPages } }.
        /// - For "read": { schedule = the schedule with the specified id }.
        /// - For "create": an operation result object containing operation, success flag and a snapshot of schedules after creation.
        /// - For "update": an operation result object containing operation, success flag, the updated entity and a snapshot of schedules.
        /// - For "delete": an operation result object containing operation, success flag, deletedId and a snapshot of schedules.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a required id is missing for "read" or when the command is unsupported.
        /// </exception>
        private async Task<object> HandleRealtimeCrudAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
        {
            var command = (request.Command ?? string.Empty).Trim().ToLowerInvariant();
            
            // Extract pagination parameters from root-level properties OR payload
            int? page = request.Page;  // Try root-level property first
            int? pageSize = request.PageSize;
            
            // Fallback to payload if root-level properties not set
            if (request.Payload.HasValue && (!page.HasValue || !pageSize.HasValue))
            {
                try
                {
                    var rootElement = request.Payload.Value;
                    
                    // Check root level
                    if (!page.HasValue && rootElement.TryGetProperty("page", out var pageEl))
                        page = pageEl.GetInt32();
                    if (!pageSize.HasValue && rootElement.TryGetProperty("pageSize", out var pageSizeEl))
                        pageSize = pageSizeEl.GetInt32();
                    
                    // Fallback: check nested payload property
                    if (!page.HasValue || !pageSize.HasValue)
                    {
                        if (rootElement.TryGetProperty("payload", out var nestedPayload))
                        {
                            if (!page.HasValue && nestedPayload.TryGetProperty("page", out var nestedPageEl))
                                page = nestedPageEl.GetInt32();
                            if (!pageSize.HasValue && nestedPayload.TryGetProperty("pageSize", out var nestedPageSizeEl))
                                pageSize = nestedPageSizeEl.GetInt32();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse pagination parameters from payload (page/pageSize). Using defaults. Payload: {Payload}",
                        SanitizePayloadForLogging(request.Payload.Value));
                }
            }
            
            // Handle pagination navigation commands
            switch (command)
            {
                case "read_all":
                case "next_page":
                case "prev_page":
                case "first_page":
                case "last_page":
                case "goto_page":
                    return await HandleNavigationCommandAsync(command, page, pageSize);
                
                case "read":
                    return await HandleReadCommandAsync(request);
                
                case "create":
                    return await HandleCreateCommandAsync(request);
                
                case "update":
                    return await HandleUpdateCommandAsync(request);
                
                case "delete":
                    return await HandleDeleteCommandAsync(request);
                
                default:
                    throw new InvalidOperationException($"Unsupported command '{request.Command}'");
            }
        }

        /// <summary>
        /// Handle navigation commands by using server-side paging to avoid materializing entire table.
        /// </summary>
        private async Task<object> HandleNavigationCommandAsync(string command, int? page, int? pageSize)
        {
            // Calculate page size (enforcement deferred to service layer)
            var currentPageSize = pageSize ?? 100;
            if (currentPageSize < 1) currentPageSize = 100;

            // Normalize initial page value
            var initialPage = Math.Max(1, page ?? 1);

            // Get first page to determine total count and pages
            (List<RouteSchedule> initialItems, int totalCount) firstResult;
            try
            {
                firstResult = await _routeScheduleService.GetSchedulesPageAsync(initialPage, currentPageSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RouteSchedules WebSocket {Command} - failed to fetch initial page", command);
                // throw; NO THROWS HERE - WE DONT WANT RUNTIME CRASHES 
                   return new { error = "Failed to retrieve schedules", command };
            }

            var (initialItems, totalCount) = firstResult;
            // If we got fewer items than requested, we're on the last page
            var effectivePageSize = (initialItems.Count > 0 && initialItems.Count < currentPageSize)
                ? initialItems.Count
                : currentPageSize;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)currentPageSize));

            // Normalize page within bounds
            var currentPage = Math.Max(1, Math.Min(initialPage, totalPages));

            // Apply navigation logic
            switch (command)
            {
                case "next_page":
                    currentPage = Math.Min(currentPage + 1, totalPages);
                    break;
                case "prev_page":
                    currentPage = Math.Max(currentPage - 1, 1);
                    break;
                case "first_page":
                    currentPage = 1;
                    break;
                case "last_page":
                    currentPage = totalPages;
                    break;
                case "goto_page":
                    // Already normalized above
                    break;
            }

            _logger.LogInformation("RouteSchedules WebSocket {Command} - Page: {Page}/{TotalPages}, PageSize: {PageSize}, Total: {TotalCount}",
                command, currentPage, totalPages, currentPageSize, totalCount);

            // Reuse initialItems if page didn't change, otherwise fetch the new page
            List<RouteSchedule> schedules;
            if (currentPage == initialPage)
            {
                schedules = initialItems;
            }
            else
            {
                try
                {
                    schedules = (await _routeScheduleService.GetSchedulesPageAsync(currentPage, currentPageSize)).Item1;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RouteSchedules WebSocket {Command} - failed to fetch page {Page}", command, currentPage);
                    return new { error = ex.Message, command };
                }
            }

            // Project schedules and return with pagination metadata
            var result = schedules.Select(ProjectScheduleForList).ToList();

            return new
            {
                schedules = result,
                pagination = new
                {
                    page = currentPage,
                    pageSize = effectivePageSize,
                    totalCount = totalCount,
                    totalPages = totalPages,
                    hasNextPage = currentPage < totalPages,
                    hasPreviousPage = currentPage > 1
                }
            };
        }

        /// <summary>
        /// Shared helper method to apply pagination logic and return paginated schedules with metadata.
        /// </summary>
        /// <param name="schedules">The full collection of schedules to paginate.</param>
        /// <param name="page">Page number (1-based). Defaults to 1 if not specified.</param>
        /// <param name="pageSize">Number of items per page. Defaults to 100 if not specified. Maximum 500.</param>
        /// <returns>An object with a `schedules` property containing paginated route schedules and a `pagination` property with metadata.</returns>
        /// <remarks>
        /// TODO: Refactor to use centralized pagination config and derive effectivePageSize from actual returned items count
        /// to match the pattern used in HandleNavigationCommandAsync. Currently uses hardcoded defaults and requested pageSize.
        /// </remarks>
        private object ApplyPaginationAndProject(IReadOnlyList<RouteSchedule> schedules, int? page, int? pageSize)
        {
            // Apply defaults and validation
            var currentPage = page ?? 1;
            var currentPageSize = pageSize ?? 100;

            if (currentPage < 1) currentPage = 1;
            if (currentPageSize < 1) currentPageSize = 100;
            if (currentPageSize > 500) currentPageSize = 500;

            var totalCount = schedules.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)currentPageSize));

            // Clamp page to valid range
            currentPage = Math.Max(1, Math.Min(currentPage, totalPages));

            // Apply pagination
            var pagedSchedules = schedules
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize);

            // Map using centralized projection helper
            var result = pagedSchedules.Select(ProjectScheduleForList).ToList();

            return new {
                schedules = result,
                pagination = new {
                    page = currentPage,
                    pageSize = currentPageSize,
                    totalCount = totalCount,
                    totalPages = totalPages,
                    hasNextPage = currentPage < totalPages,
                    hasPreviousPage = currentPage > 1
                }
            };
        }


        /// <summary>
        /// Handle a realtime "read" command and return a single projected route schedule matching the REST DTO shape with timestamp conversions.
        /// </summary>
        /// <param name="request">The realtime request; its Id must be provided to identify the route schedule.</param>
        /// <returns>An object with a `schedule` property containing the projected route schedule, or null if not found.</returns>
        /// <exception cref="InvalidOperationException">Thrown when request.Id is not provided.</exception>
        private async Task<object> HandleReadCommandAsync(RealtimeCrudRequest request)
        {
            var id = request.Id ?? throw new InvalidOperationException("id is required for read");
            var schedule = await _routeScheduleService.GetScheduleByIdAsync(id);

            if (schedule == null)
            {
                return new { schedule = (object?)null };
            }

            // Map to anonymous type matching REST endpoint projection
            var result = new {
                schedule.ScheduleId,
                schedule.RouteId,
                schedule.StartPoint,
                schedule.EndPoint,
                schedule.RouteStops,
                DepartureTime = DateTimeOffset.FromUnixTimeMilliseconds((long)schedule.DepartureTime).UtcDateTime,
                ArrivalTime = DateTimeOffset.FromUnixTimeMilliseconds((long)schedule.ArrivalTime).UtcDateTime,
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

            return new { schedule = result };
        }

        /// <summary>
        /// Processes a realtime "create" CRUD request: authorizes the caller, deserializes the payload into a CreateRouteScheduleModel, creates the schedule, and returns an operation result with a fresh snapshot of all schedules.
        /// </summary>
        /// <returns>An object containing `operation` set to "create", `success` as a boolean indicating creation outcome, and `snapshot` containing the full list of schedules after the operation.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks admin rights and the "schedules.create" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request payload is missing or cannot be deserialized into a CreateRouteScheduleModel.</exception>
        private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin()) throw new UnauthorizedAccessException("Not authorized for schedules.create");
            var m = request.Payload?.Deserialize<CreateRouteScheduleModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for create");
            var scheduleId = await _routeScheduleService.CreateScheduleAsync(m.RouteId, m.StartPoint, m.EndPoint, m.RouteStops?.ToList(), (ulong)new DateTimeOffset(m.DepartureTime).ToUnixTimeMilliseconds(), (ulong)new DateTimeOffset(m.ArrivalTime).ToUnixTimeMilliseconds(), m.Price, m.AvailableSeats, m.DaysOfWeek?.ToList(), m.BusTypes?.ToList(), m.StopDurationMinutes, m.IsRecurring, m.EstimatedStopTimes?.ToList(), m.StopDistances?.ToList(), m.Notes, true, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), null);
            var success = scheduleId.HasValue;
            var result = new { operation = "create", success, scheduleId };

            if (success)
            {
                try
                {
                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "route-schedule.created",
                        Resource: "route-schedules",
                        HttpMethod: "POST",
                        StatusCode: 201,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: null,
                        UserName: null,
                        Tenant: null,
                        SourceIp: "internal",
                        Metadata: new Dictionary<string, string> { ["operation"] = "create", ["success"] = "true", ["scheduleId"] = scheduleId.ToString() ?? "null" }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for route-schedule.created (Resource: route-schedules, EventName: route-schedule.created, Payload: {Payload})", JsonSerializer.Serialize(result));
                }
            }

            return result;
        }

        /// <summary>
        /// Processes a realtime "update" CRUD request, applies the schedule update, and returns the updated entity.
        /// </summary>
        /// <returns>
        /// An object containing:
        /// - `operation`: the string "update",
        /// - `success`: a boolean indicating whether the update succeeded,
        /// - `entity`: the updated schedule entity (or null if not found).
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin and lacks the "schedules.edit" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request is missing the required `id` or `payload` for the update.</exception>
        private async Task<object> HandleUpdateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin()) throw new UnauthorizedAccessException("Not authorized for schedules.update");
            var id = request.Id ?? throw new InvalidOperationException("id is required for update");
            var m = request.Payload?.Deserialize<UpdateRouteScheduleModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for update");
            var success = await _routeScheduleService.UpdateScheduleAsync(id, m.RouteId, m.StartPoint, m.EndPoint, m.RouteStops?.ToList(), m.DepartureTime.HasValue ? (ulong)new DateTimeOffset(m.DepartureTime.Value).ToUnixTimeMilliseconds() : null, m.ArrivalTime.HasValue ? (ulong)new DateTimeOffset(m.ArrivalTime.Value).ToUnixTimeMilliseconds() : null, m.Price, m.AvailableSeats, m.DaysOfWeek?.ToList(), m.BusTypes?.ToList(), m.StopDurationMinutes, m.IsRecurring, m.EstimatedStopTimes?.ToList(), m.StopDistances?.ToList(), m.Notes, m.IsActive, null, m.ValidUntil.HasValue ? (ulong)new DateTimeOffset(m.ValidUntil.Value).ToUnixTimeMilliseconds() : null);
            var schedule = await _routeScheduleService.GetScheduleByIdAsync(id);

            // Project entity using the shared helper to ensure consistent shape and UtcDateTime usage
            object? projectedEntity = schedule != null ? ProjectScheduleForList(schedule) : null;

            var result = new { operation = "update", success, entity = projectedEntity };

            if (success)
            {
                try
                {
                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "route-schedule.updated",
                        Resource: "route-schedules",
                        HttpMethod: "PUT",
                        StatusCode: 200,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: null,
                        UserName: null,
                        Tenant: null,
                        SourceIp: "internal",
                        Metadata: new Dictionary<string, string> { ["operation"] = "update", ["success"] = "true" }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for route-schedule.updated (Resource: route-schedules, EventName: route-schedule.updated, ScheduleId: {ScheduleId})", id);
                }
            }

            return result;
        }

        /// <summary>
        /// Handle a realtime "delete" CRUD command for route schedules.
        /// </summary>
        /// <param name="request">The realtime CRUD request; must include the Id of the schedule to delete.</param>
        /// <returns>
        /// An object with the following properties:
        /// - operation: the string "delete"
        /// - success: `true` if the schedule was deleted, `false` otherwise
        /// - deletedId: the id of the schedule that was targeted for deletion
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin and lacks the "schedules.delete" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request does not include an Id.</exception>
        private async Task<object> HandleDeleteCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin()) throw new UnauthorizedAccessException("Not authorized for schedules.delete");
            var id = request.Id ?? throw new InvalidOperationException("id is required for delete");
            var success = await _routeScheduleService.DeleteScheduleAsync(id);
            var result = new { operation = "delete", success, deletedId = id };

            if (success)
            {
                try
                {
                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "route-schedule.deleted",
                        Resource: "route-schedules",
                        HttpMethod: "DELETE",
                        StatusCode: 200,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: null,
                        UserName: null,
                        Tenant: null,
                        SourceIp: "internal",
                        Metadata: new Dictionary<string, string> { ["operation"] = "delete", ["success"] = "true" }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for route-schedule.deleted (Resource: route-schedules, EventName: route-schedule.deleted, DeletedId: {DeletedId})", id);
                }
            }

            return result;
        }

        /// <summary>
        /// Retrieves a paginated list of route schedules, optionally filtered by active status.
        /// </summary>
        /// <param name="page">1-based page number to return (default is 1).</param>
        /// <param name="pageSize">Number of items per page (default is 100).</param>
        /// <param name="isActive">If specified, limits results to schedules with the given active state; otherwise returns all schedules.</param>
        /// <returns>A collection of schedule objects for the requested page. Pagination metadata is provided in response headers: X-Total-Count, X-Page, X-Page-Size, and X-Total-Pages.</returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetRouteSchedules(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100,
            [FromQuery] bool? isActive = null)
        {
            try
            {
                if (pageSize < 1) pageSize = 1;
                if (page < 1) page = 1;

                // Read max page size from configuration; fall back to 5000 if missing or invalid
                var maxPageSize = _configuration.GetValue<int?>("RouteSchedule:MaxPageSize") ?? 5000;
                if (maxPageSize < 1) maxPageSize = 5000;
                var effectivePageSize = Math.Clamp(pageSize, 1, maxPageSize);

                _logger.LogInformation("Fetching route schedules - Page: {Page}, PageSize: {PageSize}, IsActive: {IsActive}",
                    page, effectivePageSize, isActive);

                var query = new TicketSalesApp.Services.Models.ScheduleQuery { IsActive = isActive };
                var (paged, totalCount) = await _routeScheduleService.GetSchedulesPageAsync(page, effectivePageSize, query);

                var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)effectivePageSize));

                // Clamp page to valid range
                var normalizedPage = Math.Max(1, Math.Min(page, totalPages));

                // If page was out of bounds, re-fetch with normalized page
                List<RouteSchedule> finalPaged;
                if (normalizedPage != page)
                {
                    (finalPaged, _) = await _routeScheduleService.GetSchedulesPageAsync(normalizedPage, effectivePageSize, query);
                }
                else
                {
                    finalPaged = paged;
                }

                var result = finalPaged.Select(ProjectScheduleForList).ToList();

                _logger.LogInformation("Returning {Count} schedules (Page {Page}/{TotalPages}, Total: {TotalCount})",
                    result.Count, normalizedPage, totalPages, totalCount);

                Response.Headers["X-Total-Count"] = totalCount.ToString();
                Response.Headers["X-Page"] = normalizedPage.ToString();
                Response.Headers["X-Page-Size"] = effectivePageSize.ToString();
                Response.Headers["X-Total-Pages"] = totalPages.ToString();

                return Ok(result);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogWarning(ex, "Invalid pagination parameters: page={Page}, pageSize={PageSize}", page, pageSize);
                return BadRequest($"Invalid pagination parameter: {ex.ParamName}. {ex.Message}");
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
                    DepartureTime = DateTimeOffset.FromUnixTimeMilliseconds((long)schedule.DepartureTime).UtcDateTime,
                    ArrivalTime = DateTimeOffset.FromUnixTimeMilliseconds((long)schedule.ArrivalTime).UtcDateTime,
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
                    
                    // Materialize query once before counting to avoid multiple enumerations
                    var preFilterList = query.ToList();
                    var totalBeforeFilter = preFilterList.Count;
                    var recurringBeforeFilter = preFilterList.Count(s => s.IsRecurring);
                    var nonRecurringBeforeFilter = totalBeforeFilter - recurringBeforeFilter;
                    
                    _logger.LogInformation("Total schedules before date filter: {Count}", totalBeforeFilter);
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
                    var matchedList = query.ToList();
                    var totalAfterFilter = matchedList.Count;
                    var recurringAfterFilter = matchedList.Count(s => s.IsRecurring);
                    var nonRecurringAfterFilter = totalAfterFilter - recurringAfterFilter;
                    
                    // Use matchedList for all subsequent operations to avoid re-executing the query
                    query = matchedList.AsQueryable();
                    
                    _logger.LogInformation("Schedules after date filter: {Total} total ({Recurring} recurring, {NonRecurring} non-recurring)", 
                        totalAfterFilter, recurringAfterFilter, nonRecurringAfterFilter);
                    _logger.LogInformation("Filter removed {Removed} schedules ({RemovedRecurring} recurring, {RemovedNonRecurring} non-recurring)",
                        totalBeforeFilter - totalAfterFilter,
                        recurringBeforeFilter - recurringAfterFilter,
                        nonRecurringBeforeFilter - nonRecurringAfterFilter);
                    
                    // Sample logging: show first few matching schedules for verification
                    var sampleSchedules = matchedList.Take(3).ToList();
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
                        
                        // DIAGNOSTIC: Sample excluded schedules to show WHY they were filtered out
                        var excludedSchedules = preFilterList.Except(matchedList).Take(5).ToList();
                        if (excludedSchedules.Any())
                        {
                            _logger.LogWarning("=== SAMPLE FILTERED-OUT SCHEDULES (showing why they were excluded) ===");
                            foreach (var sample in excludedSchedules)
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

                // Validate and normalize pagination to avoid divide-by-zero and 500s.
                if (pageSize <= 0) pageSize = 50;
                if (page < 1) page = 1;
                var totalPages = totalCount > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 1;
                if (page > totalPages) page = totalPages;

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
                    DepartureTime = DateTimeOffset.FromUnixTimeMilliseconds((long)s.DepartureTime).UtcDateTime,
                    ArrivalTime = DateTimeOffset.FromUnixTimeMilliseconds((long)s.ArrivalTime).UtcDateTime,
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

                var scheduleId = await _routeScheduleService.CreateScheduleAsync(
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

                if (!scheduleId.HasValue)
                {
                    _logger.LogWarning("Failed to create route schedule");
                    return BadRequest("Failed to create route schedule");
                }

                // Use the returned scheduleId to fetch the created schedule directly
                var schedule = await _routeScheduleService.GetScheduleByIdAsync(scheduleId.Value);

                if (schedule == null)
                {
                    _logger.LogError("Schedule {ScheduleId} was created but could not be retrieved", scheduleId.Value);
                    return StatusCode(500, "Schedule was created but could not be retrieved");
                }

                _logger.LogInformation("Successfully created route schedule {ScheduleId}", schedule.ScheduleId);
                return CreatedAtAction(nameof(GetRouteSchedule), new { id = schedule.ScheduleId }, ProjectScheduleForList(schedule));
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

        /// <summary>
        /// Projects a RouteSchedule entity to an anonymous object for list responses.
        /// Centralizes the projection logic to avoid duplication across multiple methods.
        /// </summary>
        private static object ProjectScheduleForList(RouteSchedule s)
        {
            return new
            {
                s.ScheduleId,
                s.RouteId,
                s.StartPoint,
                s.RouteStops,
                s.EndPoint,
                DepartureTime = DateTimeOffset.FromUnixTimeMilliseconds((long)s.DepartureTime).UtcDateTime,
                ArrivalTime = DateTimeOffset.FromUnixTimeMilliseconds((long)s.ArrivalTime).UtcDateTime,
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
            };
        }

        /// <summary>
        /// Sanitizes a JSON payload for logging by masking sensitive fields and limiting length.
        /// </summary>
        private string SanitizePayloadForLogging(JsonElement payload)
        {
            const int MaxLength = 500;
            var sensitiveFields = new[] { "password", "token", "secret", "apikey", "api_key" };

            try
            {
                var payloadStr = payload.ToString();
                if (string.IsNullOrEmpty(payloadStr))
                    return "[empty]";

                // Parse and mask sensitive fields
                using var doc = JsonDocument.Parse(payloadStr);
                var sanitized = JsonSerializer.Serialize(SanitizeJsonElement(doc.RootElement, sensitiveFields));
                var result = sanitized.Length > MaxLength
                    ? sanitized.Substring(0, MaxLength) + "... [truncated]"
                    : sanitized;
                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse JSON payload for sanitization");
                return "[invalid JSON]";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to parse JSON payload for sanitization (unexpected exception)");
                return "[invalid JSON]";
            }
        }

        private object SanitizeJsonElement(JsonElement element, string[] sensitiveFields)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var sanitizedObj = new Dictionary<string, object>();
                foreach (var prop in element.EnumerateObject())
                {
                    var key = prop.Name;
                    var isSensitive = sensitiveFields.Any(sf => key.Contains(sf, StringComparison.OrdinalIgnoreCase));

                    if (isSensitive)
                    {
                        sanitizedObj[key] = "***";
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        sanitizedObj[key] = SanitizeJsonElement(prop.Value, sensitiveFields);
                    }
                    else
                    {
                        sanitizedObj[key] = prop.Value.ToString();
                    }
                }
                return sanitizedObj;
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var sanitizedArray = new List<object>();
                foreach (var item in element.EnumerateArray())
                {
                    sanitizedArray.Add(SanitizeJsonElement(item, sensitiveFields));
                }
                return sanitizedArray;
            }

            return element.ToString();
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