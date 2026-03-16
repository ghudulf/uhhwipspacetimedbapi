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
        /// <paramref name="page"/> is 1-based; values below 1 are treated as 1.
        /// <paramref name="pageSize"/> is clamped to [1, MaxPageSize] server-side.
        /// Passing <see langword="null"/> or omitting <paramref name="query"/> is equivalent to
        /// passing <see cref="ScheduleQuery.Empty"/> — both mean "no filtering".
        /// </summary>
        Task<(List<RouteSchedule> items, int totalCount)> GetSchedulesPageAsync(
            int page, int pageSize, ScheduleQuery? query = null);
        Task<RouteSchedule?> GetScheduleByIdAsync(uint scheduleId);
        Task<List<RouteSchedule>> GetSchedulesByRouteIdAsync(uint routeId);
        /// <summary>
        /// Creates a new route schedule with the supplied parameters.
        /// </summary>
        /// <returns>
        /// The <c>uint</c> ID of the newly created schedule on success,
        /// or <see langword="null"/> if the underlying SpacetimeDB reducer did not return an ID
        /// (e.g. the reducer completed but the row was not yet visible in the local snapshot).
        /// </returns>
        /// <exception cref="System.Exception">Thrown on network failure, reducer rejection, or invalid arguments.</exception>
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