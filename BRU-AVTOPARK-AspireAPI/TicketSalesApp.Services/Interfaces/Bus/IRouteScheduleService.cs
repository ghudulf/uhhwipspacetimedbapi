using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Models;


namespace TicketSalesApp.Services.Interfaces
{
    public interface IRouteScheduleService
    {
        Task<List<RouteSchedule>> GetAllSchedulesAsync();
        /// <summary>
        /// Returns a single page of schedules that match <paramref name="query"/>.
        /// <paramref name="page"/> is 1-based; both page and pageSize are validated
        /// and capped server-side against the configured maximum (RouteSchedule:MaxPageSize).
        /// Pass <see cref="ScheduleQuery.Empty"/> for an unfiltered page.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="page"/> or <paramref name="pageSize"/> is less than 1,
        /// or when <paramref name="pageSize"/> exceeds the configured maximum.
        /// </exception>
        Task<(List<RouteSchedule> items, int totalCount)> GetSchedulesPageAsync(
            int page, int pageSize, ScheduleQuery? query = null);
        Task<RouteSchedule?> GetScheduleByIdAsync(uint scheduleId);
        Task<List<RouteSchedule>> GetSchedulesByRouteIdAsync(uint routeId);
        Task<uint?> CreateScheduleAsync(
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