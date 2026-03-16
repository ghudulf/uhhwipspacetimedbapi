using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using TicketSalesApp.Services.Models;

namespace TicketSalesApp.Services.Implementations
{
    public class RouteScheduleService : IRouteScheduleService, IDisposable
    {
        private readonly ISpacetimeDBService _spacetimeDBService;
        private readonly ILogger<RouteScheduleService> _logger;
        private readonly IConfiguration _configuration;
        private readonly int _maxPageSize;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<uint?>> _pendingCreates = new();
        private readonly SemaphoreSlim _handlerLock = new(1, 1);
        private int _disposedFlag;

        public RouteScheduleService(ISpacetimeDBService spacetimeDBService, ILogger<RouteScheduleService> logger, IConfiguration configuration)
        {
            _spacetimeDBService = spacetimeDBService ?? throw new ArgumentNullException(nameof(spacetimeDBService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _maxPageSize = configuration.GetValue<int>("RouteSchedule:MaxPageSize", 5000);
            if (_maxPageSize < 1) _maxPageSize = 5000;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return;
            
            _handlerLock?.Dispose();
            
            foreach (var pending in _pendingCreates.Values)
            {
                pending.TrySetCanceled();
            }
            _pendingCreates.Clear();
            
            GC.SuppressFinalize(this);
        }

        public async Task<List<RouteSchedule>> GetAllSchedulesAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all route schedules");
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.RouteSchedule.Iter().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all route schedules");
                throw;
            }
        }

        public async Task<(List<RouteSchedule> items, int totalCount)> GetSchedulesPageAsync(
            int page, int pageSize, ScheduleQuery? query = null)
         {
            query ??= ScheduleQuery.Empty;
            try
            {
                 _logger.LogInformation(
                    "Retrieving schedules page {Page} with page size {PageSize} (filters: routeId={RouteId} isActive={IsActive} start={Start} end={End} text={Text})",
                    page, pageSize,
                    query.RouteId, query.IsActive, query.StartDate, query.EndDate, query.SearchText);

                // Clamp page and pageSize to valid ranges (lenient – never throw).
                // page < 1 is treated as page 1; pageSize is clamped to [1, _maxPageSize].
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, _maxPageSize);

                var connection = _spacetimeDBService.GetConnection();

                // Apply server-side filters before counting / slicing – single enumeration.
               IEnumerable<RouteSchedule> filtered = connection.Db.RouteSchedule.Iter();

              if (query.RouteId.HasValue)
                 filtered = filtered.Where(s => s.RouteId == query.RouteId.Value);

               if (query.IsActive.HasValue)
                   filtered = filtered.Where(s => s.IsActive == query.IsActive.Value);
               if (query.StartDate.HasValue)
                  filtered = filtered.Where(s => s.DepartureTime >= query.StartDate.Value);

                if (query.EndDate.HasValue)
                    filtered = filtered.Where(s => s.DepartureTime <= query.EndDate.Value);

               if (!string.IsNullOrWhiteSpace(query.SearchText))
                {
                   var text = query.SearchText.Trim().ToLowerInvariant();
                   filtered = filtered.Where(s =>
                       (s.StartPoint ?? "").ToLowerInvariant().Contains(text) ||
                       (s.EndPoint   ?? "").ToLowerInvariant().Contains(text));
                }

                var materialised = filtered.ToList();   // single enumeration
                var totalCount   = materialised.Count;
                // Use 64-bit arithmetic to avoid int overflow on large page/pageSize combinations.
                var skipLong = ((long)page - 1) * pageSize;
                if (skipLong < 0) skipLong = 0;
                var skip = skipLong > int.MaxValue ? int.MaxValue : (int)skipLong;
                var items        = materialised
                                       .Skip(skip).Take(pageSize)
                                       .ToList();

                _logger.LogInformation("Retrieved {ItemCount} schedules out of {TotalCount} total", items.Count, totalCount);
                return (items, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving schedules page {Page}", page);
                throw;
            }
        }

        public async Task<RouteSchedule?> GetScheduleByIdAsync(uint scheduleId)
        {
            try
            {
                _logger.LogInformation("Retrieving schedule by ID: {ScheduleId}", scheduleId);
                _logger.LogDebug("Starting full data retrieval for schedule lookup");
                
                var connection = _spacetimeDBService.GetConnection();
                var allSchedules = connection.Db.RouteSchedule.Iter().ToList();
                
                _logger.LogDebug("Retrieved {Count} total schedules from database", allSchedules.Count);
                
                RouteSchedule? matchingSchedule = null;
                
                _logger.LogDebug("Beginning manual iteration through schedules to find ID: {ScheduleId}", scheduleId);
                foreach (var schedule in allSchedules)
                {
                    _logger.LogTrace("Checking schedule ID: {CurrentId} against target: {TargetId}", 
                        schedule.ScheduleId, scheduleId);
                    
                    if (schedule.ScheduleId == scheduleId)
                    {
                        _logger.LogDebug("Found matching schedule with ID: {ScheduleId}", scheduleId);
                        matchingSchedule = schedule;
                        break;
                    }
                }
                
                if (matchingSchedule == null)
                {
                    _logger.LogWarning("No schedule found with ID: {ScheduleId}", scheduleId);
                }
                else
                {
                    _logger.LogInformation("Successfully retrieved schedule with ID: {ScheduleId}, DepartureTime: {DepartureTime}", 
                        matchingSchedule.ScheduleId, matchingSchedule.DepartureTime);
                }
                
                return matchingSchedule;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving schedule by ID: {ScheduleId}", scheduleId);
                throw;
            }
        }

        public async Task<List<RouteSchedule>> GetSchedulesByRouteIdAsync(uint routeId)
        {
            try
            {
                _logger.LogInformation("Retrieving schedules for route: {RouteId}", routeId);
                _logger.LogDebug("Starting full data retrieval for route schedule lookup");
                
                var connection = _spacetimeDBService.GetConnection();
                var allSchedules = connection.Db.RouteSchedule.Iter().ToList();
                
                _logger.LogDebug("Retrieved {Count} total schedules from database", allSchedules.Count);
                
                List<RouteSchedule> matchingSchedules = new List<RouteSchedule>();
                
                _logger.LogDebug("Beginning manual iteration through schedules to find RouteId: {RouteId}", routeId);
                foreach (var schedule in allSchedules)
                {
                    _logger.LogTrace("Checking schedule RouteId: {CurrentRouteId} against target: {TargetRouteId}", 
                        schedule.RouteId, routeId);
                    
                    if (schedule.RouteId == routeId)
                    {
                        _logger.LogDebug("Found matching schedule with ID: {ScheduleId} for RouteId: {RouteId}", 
                            schedule.ScheduleId, routeId);
                        matchingSchedules.Add(schedule);
                    }
                }
                
                _logger.LogInformation("Found {Count} schedules for RouteId: {RouteId}", matchingSchedules.Count, routeId);
                
                // Sort the matching schedules by departure time
                matchingSchedules.Sort((a, b) => a.DepartureTime.CompareTo(b.DepartureTime));
                
                _logger.LogDebug("Sorted {Count} schedules by departure time", matchingSchedules.Count);
                
                foreach (var schedule in matchingSchedules)
                {
                    _logger.LogTrace("Sorted schedule - ID: {ScheduleId}, RouteId: {RouteId}, DepartureTime: {DepartureTime}", 
                        schedule.ScheduleId, schedule.RouteId, schedule.DepartureTime);
                }
                
                return matchingSchedules;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving schedules for route: {RouteId}", routeId);
                throw;
            }
        }

        public async Task<uint?> CreateScheduleAsync(
            uint? routeId = null,
            string? startPoint = null,
            string? endPoint = null,
            List<string>? routeStops = null,
            ulong? departureTime = null,
            ulong? arrivalTime = null,
            double? price = null,
            uint? availableSeats = null,
            List<string>? daysOfWeek = null,
            List<string>? busTypes = null,
            uint? stopDurationMinutes = null,
            bool? isRecurring = null,
            List<string>? estimatedStopTimes = null,
            List<double>? stopDistances = null,
            string? notes = null,
            bool? isActive = null,
            ulong? validFrom = null,
            ulong? validUntil = null,
            string? updatedBy = null,
            Identity? actingUser = null
        )
        {
            try
            {
                _logger.LogInformation("Creating schedule for route: {RouteId}", routeId);
                var connection = _spacetimeDBService.GetConnection();

                var allRoutes = connection.Db.Route.Iter().ToList();
                
                Route? route = null;
                foreach (var r in allRoutes)
                {
                    if (r.RouteId == routeId)
                    {
                        route = r;
                        break;
                    }
                }
                
                if (route == null)
                {
                    _logger.LogWarning("Route not found: {RouteId}", routeId);
                    return null;
                }

                // WORKAROUND: SpacetimeDB lacks support for transient/internal correlation fields.
                // We temporarily embed correlation ID in the user-facing Notes field with a clear marker.
                // This is acceptable because:
                // 1. Notes is optional and user-controlled
                // 2. The correlation tag is clearly marked with [CORRELATION:guid] format
                // 3. It's automatically cleaned up after the operation completes
                // 4. A proper solution requires SpacetimeDB schema changes to add a dedicated correlation field
                // TODO: Remove this workaround when SpacetimeDB supports transient fields or add a dedicated CorrelationId column
                
                // Check for existing correlation marker and strip it to avoid collisions
                // Use LastIndexOf to avoid accidentally removing legitimate user text that
                // happens to contain the marker pattern earlier in the string.
                var cleanedNotes = notes;
                if (!string.IsNullOrEmpty(notes) && notes.Contains("[CORRELATION:"))
                {
                    // Strip the LAST correlation marker only
                    var startIdx = notes.LastIndexOf("[CORRELATION:");
                    var endIdx = notes.IndexOf(']', startIdx);
                    if (endIdx > startIdx)
                    {
                        cleanedNotes = notes.Remove(startIdx, endIdx - startIdx + 1).Trim();
                        _logger.LogWarning("Stripped existing correlation marker from notes");
                    }
                }
                
                var correlationId = Guid.NewGuid();
                var correlationTag = $"[CORRELATION:{correlationId}]";
                var notesWithCorrelation = string.IsNullOrEmpty(cleanedNotes) 
                    ? correlationTag 
                    : $"{cleanedNotes} {correlationTag}";

                // Create TaskCompletionSource for this operation
                var tcs = new TaskCompletionSource<uint?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingCreates[correlationId] = tcs;

                // Capture the original cleaned notes before the event handler to avoid closure bug
                var originalCleanedNotes = cleanedNotes;

                // TODO: Replace per-request OnCreateRouteSchedule subscription with a single long-lived handler
                // registered at service initialization. Use correlationId to resolve TaskCompletionSource.
                // This avoids creating/destroying event handlers on every request which can leak memory.
                // See: https://github.com/your-org/repo/issues/NNN

                // Set up event handler to capture the created schedule ID
                async void OnScheduleCreated(ReducerEventContext ctx, uint routeIdParam, ulong departureTimeParam,
                    double priceParam, uint availableSeatsParam, List<string>? daysOfWeekParam,
                    string? startPointParam, string? endPointParam, List<string>? routeStopsParam,
                    ulong? arrivalTimeParam, uint? stopDurationMinutesParam, bool? isRecurringParam,
                    List<string>? estimatedStopTimesParam, List<double>? stopDistancesParam, string? notesParam)
                {
                    try
                    {
                        await _handlerLock.WaitAsync();
                        try
                        {
                        // Check if reducer succeeded
                        var status = ctx.Event.Status;
                        if (status is Status.Failed(var reason))
                        {
                            _logger.LogError("CreateRouteSchedule reducer failed: {Reason}", reason);
                            
                            if (TryExtractCorrelationId(notesParam, out var guid) && guid == correlationId)
                            {
                                if (_pendingCreates.TryRemove(guid, out var pendingTcs))
                                {
                                    pendingTcs.TrySetException(new Exception(reason));
                                }
                            }
                            return;
                        }
                        else if (status is Status.OutOfEnergy)
                        {
                            _logger.LogError("CreateRouteSchedule reducer out of energy");
                            
                            if (TryExtractCorrelationId(notesParam, out var guid) && guid == correlationId)
                            {
                                if (_pendingCreates.TryRemove(guid, out var pendingTcs))
                                {
                                    pendingTcs.TrySetException(new Exception("Out of energy"));
                                }
                            }
                            return;
                        }

                        // Reducer succeeded - find the created schedule by correlation ID
                        if (TryExtractCorrelationId(notesParam, out var correlationGuid) && correlationGuid == correlationId)
                        {
                            // Guard: only proceed if this correlation ID belongs to a pending create on this instance
                            if (!_pendingCreates.TryGetValue(correlationGuid, out _))
                                return;

                            if (_pendingCreates.TryRemove(correlationGuid, out var pendingTcs))
                            {
                                // Query database to find the created schedule
                                var allSchedules = ctx.Db.RouteSchedule.Iter().ToList();
                                var createdSchedule = allSchedules
                                    .Where(s => s.RouteId == routeIdParam &&
                                               s.DepartureTime == departureTimeParam &&
                                               s.Notes != null &&
                                               s.Notes.Contains($"[CORRELATION:{correlationGuid}]"))
                                    .OrderByDescending(s => s.ScheduleId)
                                    .FirstOrDefault();
                                
                                if (createdSchedule != null)
                                {
                                    _logger.LogInformation("Successfully created schedule with ID: {ScheduleId}", createdSchedule.ScheduleId);

                                    // Set result immediately after finding the created schedule
                                    pendingTcs.TrySetResult(createdSchedule.ScheduleId);

                                    // Clean up correlation marker from Notes field now that we have the ID
                                    if (!string.IsNullOrEmpty(createdSchedule.Notes) && createdSchedule.Notes.Contains($"[CORRELATION:{correlationGuid}]"))
                                    {
                                        _logger.LogDebug("Cleaning up correlation marker from schedule {ScheduleId} Notes field", createdSchedule.ScheduleId);

                                        // Call UpdateRouteSchedule to remove correlation marker
                                        try
                                        {
                                            ctx.Reducers.UpdateRouteSchedule(
                                                createdSchedule.ScheduleId,
                                                createdSchedule.RouteId,
                                                createdSchedule.StartPoint,
                                                createdSchedule.EndPoint,
                                                createdSchedule.RouteStops,
                                                createdSchedule.DepartureTime,
                                                createdSchedule.ArrivalTime,
                                                createdSchedule.Price,
                                                createdSchedule.AvailableSeats,
                                                createdSchedule.DaysOfWeek,
                                                createdSchedule.BusTypes,
                                                createdSchedule.StopDurationMinutes,
                                                createdSchedule.IsRecurring,
                                                createdSchedule.EstimatedStopTimes,
                                                createdSchedule.StopDistances,
                                                originalCleanedNotes, // Use captured original notes without correlation marker
                                                null // actingUser
                                            );
                                            _logger.LogDebug("Correlation marker cleaned up from schedule {ScheduleId}", createdSchedule.ScheduleId);
                                        }
                                        catch (Exception cleanupEx)
                                        {
                                            _logger.LogWarning(cleanupEx, "Failed to clean up correlation marker from schedule {ScheduleId} - marker will remain in Notes", createdSchedule.ScheduleId);
                                            // Don't fail the operation - schedule was created successfully
                                        }
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("Could not find created schedule with correlation ID: {CorrelationId}", correlationGuid);
                                    pendingTcs.TrySetResult(null);
                                }
                            }
                        }
                        }
                        finally
                        {
                            _handlerLock.Release();
                        }
                    }
                    catch (Exception handlerEx)
                    {
                        _logger.LogError(handlerEx, "Unhandled exception in OnScheduleCreated async void handler - preventing crash");
                    }
                }

                // Helper method to extract correlation ID from notes
                static bool TryExtractCorrelationId(string? notes, out Guid correlationId)
                {
                    correlationId = Guid.Empty;
                    if (string.IsNullOrEmpty(notes) || !notes.Contains("[CORRELATION:"))
                        return false;
                    
                    // Use LastIndexOf to pick up the last marker, avoiding user-supplied earlier markers
                    var startIdx = notes.LastIndexOf("[CORRELATION:") + 13;
                    if (startIdx < 13) return false; // LastIndexOf returned -1
                    var endIdx = notes.IndexOf(']', startIdx);
                    if (endIdx > startIdx && Guid.TryParse(notes.AsSpan(startIdx, endIdx - startIdx), out correlationId))
                    {
                        return true;
                    }
                    return false;
                }

                // Attach event handler
                connection.Reducers.OnCreateRouteSchedule += OnScheduleCreated;

                try
                {
                    // Call the CreateRouteSchedule reducer
                    connection.Reducers.CreateRouteSchedule(
                        routeId ?? throw new ArgumentNullException(nameof(routeId)),
                        departureTime ?? 0,
                        price ?? 0.0,
                        availableSeats ?? 0,
                        daysOfWeek?.ToList(),
                        startPoint ?? route.StartPoint,
                        endPoint ?? route.EndPoint,
                        routeStops?.ToList(),
                        arrivalTime ?? (departureTime ?? 0) + 3600000,
                        stopDurationMinutes,
                        isRecurring,
                        estimatedStopTimes?.ToList() ?? [],
                        stopDistances?.ToList() ?? [],
                        notesWithCorrelation
                    );

                    // Wait for reducer to complete with explicit timeout
                    try
                    {
                        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("CreateScheduleAsync timed out waiting for reducer confirmation");
                        
                        // CRITICAL: Timeout fallback - check if schedule was created but event missed
                        // Final check: schedule may have been created but event missed
                        var scheduleWithTag = connection.Db.RouteSchedule.Iter()
                            .FirstOrDefault(s => s.Notes != null && s.Notes.Contains(correlationTag));
                        
                        if (scheduleWithTag != null)
                        {
                            _logger.LogInformation("Found schedule {ScheduleId} with correlation tag after timeout - cleaning tag", scheduleWithTag.ScheduleId);
                            
                            // Clean the correlation tag from Notes
                            var cleanedNotesAfterTimeout = scheduleWithTag.Notes?.Replace(correlationTag, "").Trim();
                            if (string.IsNullOrWhiteSpace(cleanedNotesAfterTimeout))
                            {
                                cleanedNotesAfterTimeout = null;
                            }
                            
                            // Best-effort cleanup: try to remove correlation tag
                            try
                            {
                                connection.Reducers.UpdateRouteSchedule(
                                    scheduleWithTag.ScheduleId,
                                    scheduleWithTag.RouteId,
                                    scheduleWithTag.StartPoint,
                                    scheduleWithTag.EndPoint,
                                    scheduleWithTag.RouteStops,
                                    scheduleWithTag.DepartureTime,
                                    scheduleWithTag.ArrivalTime,
                                    scheduleWithTag.Price,
                                    scheduleWithTag.AvailableSeats,
                                    scheduleWithTag.DaysOfWeek,
                                    scheduleWithTag.BusTypes,
                                    scheduleWithTag.StopDurationMinutes,
                                    scheduleWithTag.IsRecurring,
                                    scheduleWithTag.EstimatedStopTimes,
                                    scheduleWithTag.StopDistances,
                                    cleanedNotesAfterTimeout,
                                    null
                                );
                                connection.FrameTick();
                            }
                            catch (Exception cleanupEx)
                            {
                                _logger.LogWarning(cleanupEx, "Failed to clean correlation tag from schedule {ScheduleId} - tag will remain", scheduleWithTag.ScheduleId);
                                // Continue anyway - we found the schedule
                            }

                            return scheduleWithTag.ScheduleId;
                        }
                        
                        // CRITICAL: Timeout fallback returns null instead of guessing with non-unique fields
                        // The previous fallback used non-unique fields (RouteId, DepartureTime, StartPoint, EndPoint)
                        // which could match the wrong schedule under concurrency.
                        // TODO: Add dedicated CorrelationId column to RouteSchedule table in SpacetimeDB
                        // or use a more unique combination (e.g., include millisecond timestamp in DepartureTime)
                        _logger.LogError("Cannot reliably identify created schedule after timeout - returning null");
                        return null;
                    }
                }
                finally
                {
                    // Clean up event handler and pending correlation
                    connection.Reducers.OnCreateRouteSchedule -= OnScheduleCreated;
                    _pendingCreates.TryRemove(correlationId, out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating schedule for route: {RouteId}", routeId);
                throw;
            }
        }

        public async Task<bool> UpdateScheduleAsync(
            uint scheduleId,
            uint? routeId = null,
            string? startPoint = null,
            string? endPoint = null,
            List<string>? routeStops = null,
            ulong? departureTime = null,
            ulong? arrivalTime = null,
            double? price = null,
            uint? availableSeats = null,
            List<string>? daysOfWeek = null,
            List<string>? busTypes = null,
            uint? stopDurationMinutes = null,
            bool? isRecurring = null,
            List<string>? estimatedStopTimes = null,
            List<double>? stopDistances = null,
            string? notes = null,
            bool? isActive = null,
            ulong? validFrom = null,
            ulong? validUntil = null,
            string? updatedBy = null,
            Identity? actingUser = null
        )
        {
            try
            {
                _logger.LogInformation("Updating schedule: {ScheduleId}", scheduleId);
                var connection = _spacetimeDBService.GetConnection();

                var allSchedules = connection.Db.RouteSchedule.Iter().ToList();
                
                RouteSchedule? schedule = null;
                foreach (var s in allSchedules)
                {
                    if (s.ScheduleId == scheduleId)
                    {
                        schedule = s;
                        break;
                    }
                }
                
                if (schedule == null)
                {
                    _logger.LogWarning("Schedule not found: {ScheduleId}", scheduleId);
                    return false;
                }

                if (routeId.HasValue)
                {
                    var allRoutes = connection.Db.Route.Iter().ToList();
                    
                    Route? route = null;
                    foreach (var r in allRoutes)
                    {
                        if (r.RouteId == routeId)
                        {
                            route = r;
                            break;
                        }
                    }
                    
                    if (route == null)
                    {
                        _logger.LogWarning("Route not found: {RouteId}", routeId);
                        return false;
                    }
                }

                // Call the UpdateRouteSchedule reducer
                connection.Reducers.UpdateRouteSchedule(
                    scheduleId,
                    routeId ?? schedule.RouteId,
                    startPoint ?? schedule.StartPoint,
                    endPoint ?? schedule.EndPoint,
                    routeStops ?? schedule.RouteStops,
                    departureTime ?? schedule.DepartureTime,
                    arrivalTime ?? schedule.ArrivalTime,
                    price ?? schedule.Price,
                    availableSeats ?? schedule.AvailableSeats,
                    daysOfWeek ?? schedule.DaysOfWeek,
                    busTypes ?? schedule.BusTypes,
                    stopDurationMinutes ?? schedule.StopDurationMinutes,
                    isRecurring ?? schedule.IsRecurring,
                    estimatedStopTimes ?? schedule.EstimatedStopTimes,
                    stopDistances ?? schedule.StopDistances,
                    notes ?? schedule.Notes,
                    actingUser
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating schedule: {ScheduleId}", scheduleId);
                throw;
            }
        }

        public async Task<bool> DeleteScheduleAsync(uint scheduleId, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Deleting schedule: {ScheduleId}", scheduleId);
                var connection = _spacetimeDBService.GetConnection();

                var allSchedules = connection.Db.RouteSchedule.Iter().ToList();
                
                RouteSchedule? schedule = null;
                foreach (var s in allSchedules)
                {
                    if (s.ScheduleId == scheduleId)
                    {
                        schedule = s;
                        break;
                    }
                }
                
                if (schedule == null)
                {
                    _logger.LogWarning("Schedule not found: {ScheduleId}", scheduleId);
                    return false;
                }

                // Check if schedule has tickets
                var allTickets = connection.Db.Ticket.Iter().ToList();
                
                bool hasTickets = false;
                foreach (var ticket in allTickets)
                {
                    if (ticket.RouteId == schedule.RouteId)
                    {
                        hasTickets = true;
                        break;
                    }
                }
                
                if (hasTickets)
                {
                    _logger.LogWarning("Cannot delete schedule {ScheduleId} as it has tickets", scheduleId);
                    return false;
                }

                // Call the DeleteRouteSchedule reducer
                connection.Reducers.DeleteRouteSchedule(scheduleId, actingUser);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting schedule: {ScheduleId}", scheduleId);
                throw;
            }
        }

        public async Task<List<RouteSchedule>> GetSchedulesByDateAsync(ulong date)
        {
            try
            {
                // Treat date as milliseconds (consistent with DepartureTime storage)
                _logger.LogInformation("Retrieving schedules for date: {Date}", DateTimeOffset.FromUnixTimeMilliseconds((long)date).ToString());
                var connection = _spacetimeDBService.GetConnection();

                // Convert DayOfWeek enum to Russian names matching stored values
                var dayOfWeekEnum = DateTimeOffset.FromUnixTimeMilliseconds((long)date).DayOfWeek;
                var dayOfWeek = dayOfWeekEnum switch
                {
                    DayOfWeek.Monday => "Понедельник",
                    DayOfWeek.Tuesday => "Вторник",
                    DayOfWeek.Wednesday => "Среда",
                    DayOfWeek.Thursday => "Четверг",
                    DayOfWeek.Friday => "Пятница",
                    DayOfWeek.Saturday => "Суббота",
                    DayOfWeek.Sunday => "Воскресенье",
                    _ => dayOfWeekEnum.ToString() // Fallback to English if unknown
                };

                var allSchedules = connection.Db.RouteSchedule.Iter().ToList();

                List<RouteSchedule> matchingSchedules = [];

                foreach (var schedule in allSchedules)
                {
                    // Use IsRecurring to distinguish recurring vs one-off schedules
                    // A recurring schedule with null DaysOfWeek is malformed — do NOT match every day.
                    bool matchesDay = !schedule.IsRecurring || (schedule.IsRecurring && schedule.DaysOfWeek != null && schedule.DaysOfWeek.Contains(dayOfWeek, StringComparer.OrdinalIgnoreCase));

                    bool matchesTimeWindow;
                    if (!schedule.IsRecurring)
                    {
                        // One-off schedule: use exact timestamp matching within the target day
                        matchesTimeWindow = schedule.DepartureTime >= date && schedule.DepartureTime < date + 86400000; // 86400000 ms = 24 hours
                    }
                    else
                    {
                        // Recurring schedule: match by time-of-day (DepartureTime % 86400000)
                        var timeOfDay = schedule.DepartureTime % 86400000;
                        matchesTimeWindow = timeOfDay >= 0 && timeOfDay < 86400000; // Always true for recurring, just sanity check

                        // Enforce validity window: skip if the queried date falls outside [ValidFrom, ValidUntil].
                        if (date < schedule.ValidFrom)
                            matchesTimeWindow = false;
                        if (schedule.ValidUntil.HasValue && date > schedule.ValidUntil.Value)
                            matchesTimeWindow = false;
                    }

                    if (matchesDay && matchesTimeWindow)
                    {
                        matchingSchedules.Add(schedule);
                    }
                }
                
                // Sort by departure time
                matchingSchedules.Sort((a, b) => a.DepartureTime.CompareTo(b.DepartureTime));
                
                return matchingSchedules;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving schedules for date: {Date}", DateTimeOffset.FromUnixTimeMilliseconds((long)date).ToString());
                throw;
            }
        }

        public async Task<List<RouteSchedule>> GetSchedulesByDateRangeAsync(ulong startDate, ulong endDate)
        {
            // NOTE: This method performs pure timestamp-based filtering — it matches schedules whose
            // stored DepartureTime falls within [startDate, endDate]. Recurring RouteSchedule entries
            // whose DepartureTime is outside the range but have DaysOfWeek occurrences inside the range
            // are NOT included. Use GetSchedulesByDateAsync for occurrence-aware (recurring) filtering.
            try
            {
                _logger.LogInformation("Retrieving schedules between {StartDate} and {EndDate}",
                    DateTimeOffset.FromUnixTimeMilliseconds((long)startDate).ToString(),
                    DateTimeOffset.FromUnixTimeMilliseconds((long)endDate).ToString());

                var connection = _spacetimeDBService.GetConnection();
                
                var allSchedules = connection.Db.RouteSchedule.Iter().ToList();
                
                List<RouteSchedule> matchingSchedules = new List<RouteSchedule>();
                
                foreach (var schedule in allSchedules)
                {
                    if (schedule.DepartureTime >= startDate && schedule.DepartureTime <= endDate)
                    {
                        matchingSchedules.Add(schedule);
                    }
                }
                
                // Sort by departure time
                matchingSchedules.Sort((a, b) => a.DepartureTime.CompareTo(b.DepartureTime));
                
                return matchingSchedules;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving schedules between {StartDate} and {EndDate}",
                    DateTimeOffset.FromUnixTimeMilliseconds((long)startDate).ToString(),
                    DateTimeOffset.FromUnixTimeMilliseconds((long)endDate).ToString());
                throw;
            }
        }
    }
}