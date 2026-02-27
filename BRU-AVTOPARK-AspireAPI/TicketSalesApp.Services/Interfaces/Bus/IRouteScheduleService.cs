using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IRouteScheduleService
    {
        Task<List<RouteSchedule>> GetAllSchedulesAsync();
        Task<RouteSchedule?> GetScheduleByIdAsync(uint scheduleId);
        Task<List<RouteSchedule>> GetSchedulesByRouteIdAsync(uint routeId);
        Task<bool> CreateScheduleAsync(
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
        );
        Task<bool> UpdateScheduleAsync(
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
        );
        Task<bool> DeleteScheduleAsync(uint scheduleId, Identity? actingUser = null);
        Task<List<RouteSchedule>> GetSchedulesByDateAsync(ulong date);
        Task<List<RouteSchedule>> GetSchedulesByDateRangeAsync(ulong startDate, ulong endDate);
    }
}