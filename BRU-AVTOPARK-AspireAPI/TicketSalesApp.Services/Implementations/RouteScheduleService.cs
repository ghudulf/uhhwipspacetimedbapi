using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class RouteScheduleService : IRouteScheduleService
    {
        private readonly ISpacetimeDBService _spacetimeDBService;
        private readonly ILogger<RouteScheduleService> _logger;

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
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.RouteSchedule.Iter()
                    .FirstOrDefault(s => s.ScheduleId == scheduleId);
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
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.RouteSchedule.Iter()
                    .Where(s => s.RouteId == routeId)
                    .OrderBy(s => s.DepartureTime)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving schedules for route: {RouteId}", routeId);
                throw;
            }
        }

        public async Task<bool> CreateScheduleAsync(
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

                var route = connection.Db.Route.Iter()
                    .FirstOrDefault(r => r.RouteId == routeId);
                if (route == null)
                {
                    _logger.LogWarning("Route not found: {RouteId}", routeId);
                    return false;
                }

                // Call the CreateRouteSchedule reducer
                connection.Reducers.CreateRouteSchedule(
                    routeId ?? throw new ArgumentNullException(nameof(routeId)), // Ensure routeId is not null
                    departureTime ?? 0, // departureTime, default to 0 if null
                    price ?? 0.0, // price, default to 0.0 if null
                    availableSeats ?? 0, // availableSeats, default to 0 if null
                    daysOfWeek?.ToList(), // daysOfWeek, convert to List<string> if not null
                    route.StartPoint, // startPoint
                    route.EndPoint, // endPoint
                    routeStops?.ToList(), // routeStops, convert to List<string> if not null
                    (departureTime ?? 0) + 3600000, // arrivalTime, add 1 hour for arrival time, default to 0 if null
                    stopDurationMinutes, // stopDurationMinutes
                    isRecurring, // isRecurring
                    estimatedStopTimes?.ToList() ?? new List<string>(), // estimatedStopTimes, convert to List<string> if not null, default to empty list
                    stopDistances?.ToList() ?? new List<double>(), // stopDistances, convert to List<double> if not null, default to empty list
                    notes // notes
                    
                );

                return true;
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

                var schedule = connection.Db.RouteSchedule.Iter()
                    .FirstOrDefault(s => s.ScheduleId == scheduleId);
                if (schedule == null)
                {
                    _logger.LogWarning("Schedule not found: {ScheduleId}", scheduleId);
                    return false;
                }

                if (routeId.HasValue)
                {
                    var route = connection.Db.Route.Iter()
                        .FirstOrDefault(r => r.RouteId == routeId);
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

                var schedule = connection.Db.RouteSchedule.Iter()
                    .FirstOrDefault(s => s.ScheduleId == scheduleId);
                if (schedule == null)
                {
                    _logger.LogWarning("Schedule not found: {ScheduleId}", scheduleId);
                    return false;
                }

                // Check if schedule has tickets
                var tickets = connection.Db.Ticket.Iter()
                    .Where(t => t.RouteId == schedule.RouteId)
                    .ToList();
                if (tickets.Any())
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
                return connection.Db.RouteSchedule.Iter()
                    .Where(s => s.DaysOfWeek.Contains(dayOfWeek) &&
                               s.DepartureTime >= date &&
                               s.DepartureTime < date + 86400000) // 24 hours in milliseconds
                    .OrderBy(s => s.DepartureTime)
                    .ToList();
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
                return connection.Db.RouteSchedule.Iter()
                    .Where(s => s.DepartureTime >= startDate && s.DepartureTime <= endDate)
                    .OrderBy(s => s.DepartureTime)
                    .ToList();
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