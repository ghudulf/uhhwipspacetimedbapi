using System;

namespace TicketSalesApp.Services.Models
{
    /// <summary>
    /// Server-side filter parameters for paged route-schedule queries.
    /// All fields are optional; omitted fields apply no filter.
    /// </summary>
    public sealed record ScheduleQuery
    {
        /// <summary>Filter by RouteSchedule.RouteId.</summary>
        public uint? RouteId { get; init; }

        /// <summary>Return only active (true) or inactive (false) schedules.</summary>
        public bool? IsActive { get; init; }

        /// <summary>
        /// Inclusive lower bound for DepartureTime
        /// (milliseconds since epoch – matches the SpacetimeDB ulong convention).
        /// </summary>
        public ulong? StartDate { get; init; }

        /// <summary>Inclusive upper bound for DepartureTime (milliseconds since epoch).</summary>
        public ulong? EndDate { get; init; }

        /// <summary>
        /// Case-insensitive substring match against StartPoint or EndPoint.
        /// </summary>
        public string? SearchText { get; init; }

        /// <summary>Convenience singleton – no filters applied.</summary>
        public static readonly ScheduleQuery Empty = new();
    }
}