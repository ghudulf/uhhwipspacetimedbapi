using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.ClientApi;
using SpacetimeDB.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class RouteScheduleService : IRouteScheduleService
    {
        private readonly ISpacetimeDBService _spacetimeDBService;
        private readonly ILogger<RouteScheduleService> _logger;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<uint?>> _pendingCreates = new();
        private readonly SemaphoreSlim _handlerLock = new(1, 1);

        public RouteScheduleService(ISpacetimeDBService spacetimeDBService, ILogger<RouteScheduleService> logger)
        {
            _spacetimeDBService = spacetimeDBService ?? throw new ArgumentNullException(nameof(spacetimeDBService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

                // Generate correlation ID and store in notes field
                var correlationId = Guid.NewGuid();
                var correlationTag = $"[CORRELATION:{correlationId}]";
                var notesWithCorrelation = string.IsNullOrEmpty(notes) 
                    ? correlationTag 
                    : $"{notes} {correlationTag}";

                // Create TaskCompletionSource for this operation
                var tcs = new TaskCompletionSource<uint?>();
                _pendingCreates[correlationId] = tcs;

                // Set up event handler to capture the created schedule ID
                void OnScheduleCreated(ReducerEventContext ctx, uint routeIdParam, ulong departureTimeParam, 
                    double priceParam, uint availableSeatsParam, List<string>? daysOfWeekParam, 
                    string? startPointParam, string? endPointParam, List<string>? routeStopsParam, 
                    ulong? arrivalTimeParam, uint? stopDurationMinutesParam, bool? isRecurringParam, 
                    List<string>? estimatedStopTimesParam, List<double>? stopDistancesParam, string? notesParam)
                {
                    _handlerLock.Wait();
                    try
                    {
                        // Check if reducer succeeded
                        var status = ctx.Event.Status;
                        if (status is Status.Failed(var reason))
                        {
                            _logger.LogError("CreateRouteSchedule reducer failed: {Reason}", reason);
                            
                            // Try to extract correlation ID from notes
                            if (!string.IsNullOrEmpty(notesParam) && notesParam.Contains("[CORRELATION:"))
                            {
                                var startIdx = notesParam.IndexOf("[CORRELATION:") + 13;
                                var endIdx = notesParam.IndexOf("]", startIdx);
                                if (endIdx > startIdx && Guid.TryParse(notesParam.Substring(startIdx, endIdx - startIdx), out var guid))
                                {
                                    if (_pendingCreates.TryRemove(guid, out var pendingTcs))
                                    {
                                        pendingTcs.TrySetException(new Exception(reason));
                                    }
                                }
                            }
                            return;
                        }
                        else if (status is Status.OutOfEnergy)
                        {
                            _logger.LogError("CreateRouteSchedule reducer out of energy");
                            
                            if (!string.IsNullOrEmpty(notesParam) && notesParam.Contains("[CORRELATION:"))
                            {
                                var startIdx = notesParam.IndexOf("[CORRELATION:") + 13;
                                var endIdx = notesParam.IndexOf("]", startIdx);
                                if (endIdx > startIdx && Guid.TryParse(notesParam.Substring(startIdx, endIdx - startIdx), out var guid))
                                {
                                    if (_pendingCreates.TryRemove(guid, out var pendingTcs))
                                    {
                                        pendingTcs.TrySetException(new Exception("Out of energy"));
                                    }
                                }
                            }
                            return;
                        }

                        // Reducer succeeded - find the created schedule by correlation ID
                        if (!string.IsNullOrEmpty(notesParam) && notesParam.Contains("[CORRELATION:"))
                        {
                            var startIdx = notesParam.IndexOf("[CORRELATION:") + 13;
                            var endIdx = notesParam.IndexOf("]", startIdx);
                            if (endIdx > startIdx && Guid.TryParse(notesParam.Substring(startIdx, endIdx - startIdx), out var guid))
                            {
                                if (_pendingCreates.TryRemove(guid, out var pendingTcs))
                                {
                                    // Query database to find the created schedule
                                    var allSchedules = ctx.Db.RouteSchedule.Iter().ToList();
                                    var createdSchedule = allSchedules
                                        .Where(s => s.RouteId == routeIdParam &&
                                                   s.DepartureTime == departureTimeParam &&
                                                   s.Notes != null &&
                                                   s.Notes.Contains($"[CORRELATION:{guid}]"))
                                        .OrderByDescending(s => s.ScheduleId)
                                        .FirstOrDefault();
                                    
                                    if (createdSchedule != null)
                                    {
                                        _logger.LogInformation("Successfully created schedule with ID: {ScheduleId}", createdSchedule.ScheduleId);
                                        pendingTcs.TrySetResult(createdSchedule.ScheduleId);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Could not find created schedule with correlation ID: {CorrelationId}", guid);
                                        pendingTcs.TrySetResult(null);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        _handlerLock.Release();
                    }
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
                        route.StartPoint,
                        route.EndPoint,
                        routeStops?.ToList(),
                        arrivalTime ?? (departureTime ?? 0) + 3600000,
                        stopDurationMinutes,
                        isRecurring,
                        estimatedStopTimes?.ToList() ?? [],
                        stopDistances?.ToList() ?? [],
                        notesWithCorrelation
                    );

                    // Wait for reducer to complete with timeout
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(-1, cts.Token));

                    if (completedTask == tcs.Task)
                    {
                        return await tcs.Task;
                    }
                    else
                    {
                        _logger.LogWarning("CreateScheduleAsync timed out waiting for reducer confirmation");
                        _pendingCreates.TryRemove(correlationId, out _);
                        
                        // Fallback: try to find the schedule by matching attributes
                        var allSchedules = connection.Db.RouteSchedule.Iter().ToList();
                        var newSchedule = allSchedules
                            .Where(s => s.RouteId == routeId &&
                                       s.DepartureTime == departureTime &&
                                       s.StartPoint == route.StartPoint &&
                                       s.EndPoint == route.EndPoint)
                            .OrderByDescending(s => s.ScheduleId)
                            .FirstOrDefault();
                        return newSchedule?.ScheduleId;
                    }
                }
                finally
                {
                    // Clean up event handler
                    connection.Reducers.OnCreateRouteSchedule -= OnScheduleCreated;
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
                _logger.LogInformation("Retrieving schedules for date: {Date}", DateTimeOffset.FromUnixTimeSeconds((long)date).ToString());
                var connection = _spacetimeDBService.GetConnection();

                var dayOfWeek = DateTimeOffset.FromUnixTimeSeconds((long)date).DayOfWeek.ToString();
                
                var allSchedules = connection.Db.RouteSchedule.Iter().ToList();
                
                List<RouteSchedule> matchingSchedules = new List<RouteSchedule>();
                
                foreach (var schedule in allSchedules)
                {
                    if (schedule.DaysOfWeek != null && 
                        schedule.DaysOfWeek.Contains(dayOfWeek) &&
                        schedule.DepartureTime >= date &&
                        schedule.DepartureTime < date + 86400000)
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
                _logger.LogError(ex, "Error retrieving schedules for date: {Date}", DateTimeOffset.FromUnixTimeSeconds((long)date).ToString());
                throw;
            }
        }

        public async Task<List<RouteSchedule>> GetSchedulesByDateRangeAsync(ulong startDate, ulong endDate)
        {
            try
            {
                _logger.LogInformation("Retrieving schedules between {StartDate} and {EndDate}",
                    DateTimeOffset.FromUnixTimeSeconds((long)startDate).ToString(),
                    DateTimeOffset.FromUnixTimeSeconds((long)endDate).ToString());

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
                    DateTimeOffset.FromUnixTimeSeconds((long)startDate).ToString(),
                    DateTimeOffset.FromUnixTimeSeconds((long)endDate).ToString());
                throw;
            }
        }
    }
}