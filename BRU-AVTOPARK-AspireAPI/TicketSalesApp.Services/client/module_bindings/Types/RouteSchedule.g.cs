// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    [SpacetimeDB.Type]
    [DataContract]
    public sealed partial class RouteSchedule
    {
        [DataMember(Name = "ScheduleId")]
        public uint ScheduleId;
        [DataMember(Name = "RouteId")]
        public uint RouteId;
        [DataMember(Name = "StartPoint")]
        public string? StartPoint;
        [DataMember(Name = "RouteStops")]
        public System.Collections.Generic.List<string>? RouteStops;
        [DataMember(Name = "EndPoint")]
        public string? EndPoint;
        [DataMember(Name = "DepartureTime")]
        public ulong DepartureTime;
        [DataMember(Name = "ArrivalTime")]
        public ulong ArrivalTime;
        [DataMember(Name = "Price")]
        public double Price;
        [DataMember(Name = "AvailableSeats")]
        public uint AvailableSeats;
        [DataMember(Name = "SeatedCapacity")]
        public uint? SeatedCapacity;
        [DataMember(Name = "StandingCapacity")]
        public uint? StandingCapacity;
        [DataMember(Name = "DaysOfWeek")]
        public System.Collections.Generic.List<string>? DaysOfWeek;
        [DataMember(Name = "BusTypes")]
        public System.Collections.Generic.List<string>? BusTypes;
        [DataMember(Name = "IsActive")]
        public bool IsActive;
        [DataMember(Name = "ValidFrom")]
        public ulong ValidFrom;
        [DataMember(Name = "ValidUntil")]
        public ulong? ValidUntil;
        [DataMember(Name = "StopDurationMinutes")]
        public uint? StopDurationMinutes;
        [DataMember(Name = "IsRecurring")]
        public bool IsRecurring;
        [DataMember(Name = "EstimatedStopTimes")]
        public System.Collections.Generic.List<string>? EstimatedStopTimes;
        [DataMember(Name = "StopDistances")]
        public System.Collections.Generic.List<double>? StopDistances;
        [DataMember(Name = "Notes")]
        public string? Notes;
        [DataMember(Name = "CreatedAt")]
        public ulong CreatedAt;
        [DataMember(Name = "UpdatedAt")]
        public ulong? UpdatedAt;
        [DataMember(Name = "UpdatedBy")]
        public string? UpdatedBy;
        [DataMember(Name = "PeakHourLoad")]
        public double? PeakHourLoad;
        [DataMember(Name = "OffPeakHourLoad")]
        public double? OffPeakHourLoad;
        [DataMember(Name = "IsSpecialEvent")]
        public bool? IsSpecialEvent;
        [DataMember(Name = "SpecialEventName")]
        public string? SpecialEventName;
        [DataMember(Name = "IsHoliday")]
        public bool? IsHoliday;
        [DataMember(Name = "HolidayName")]
        public string? HolidayName;
        [DataMember(Name = "IsWeekend")]
        public bool? IsWeekend;
        [DataMember(Name = "SeatConfigurationId")]
        public uint? SeatConfigurationId;
        [DataMember(Name = "RequiresSeatReservation")]
        public bool? RequiresSeatReservation;
        [DataMember(Name = "RouteType")]
        public string? RouteType;

        public RouteSchedule(
            uint ScheduleId,
            uint RouteId,
            string? StartPoint,
            System.Collections.Generic.List<string>? RouteStops,
            string? EndPoint,
            ulong DepartureTime,
            ulong ArrivalTime,
            double Price,
            uint AvailableSeats,
            uint? SeatedCapacity,
            uint? StandingCapacity,
            System.Collections.Generic.List<string>? DaysOfWeek,
            System.Collections.Generic.List<string>? BusTypes,
            bool IsActive,
            ulong ValidFrom,
            ulong? ValidUntil,
            uint? StopDurationMinutes,
            bool IsRecurring,
            System.Collections.Generic.List<string>? EstimatedStopTimes,
            System.Collections.Generic.List<double>? StopDistances,
            string? Notes,
            ulong CreatedAt,
            ulong? UpdatedAt,
            string? UpdatedBy,
            double? PeakHourLoad,
            double? OffPeakHourLoad,
            bool? IsSpecialEvent,
            string? SpecialEventName,
            bool? IsHoliday,
            string? HolidayName,
            bool? IsWeekend,
            uint? SeatConfigurationId,
            bool? RequiresSeatReservation,
            string? RouteType
        )
        {
            this.ScheduleId = ScheduleId;
            this.RouteId = RouteId;
            this.StartPoint = StartPoint;
            this.RouteStops = RouteStops;
            this.EndPoint = EndPoint;
            this.DepartureTime = DepartureTime;
            this.ArrivalTime = ArrivalTime;
            this.Price = Price;
            this.AvailableSeats = AvailableSeats;
            this.SeatedCapacity = SeatedCapacity;
            this.StandingCapacity = StandingCapacity;
            this.DaysOfWeek = DaysOfWeek;
            this.BusTypes = BusTypes;
            this.IsActive = IsActive;
            this.ValidFrom = ValidFrom;
            this.ValidUntil = ValidUntil;
            this.StopDurationMinutes = StopDurationMinutes;
            this.IsRecurring = IsRecurring;
            this.EstimatedStopTimes = EstimatedStopTimes;
            this.StopDistances = StopDistances;
            this.Notes = Notes;
            this.CreatedAt = CreatedAt;
            this.UpdatedAt = UpdatedAt;
            this.UpdatedBy = UpdatedBy;
            this.PeakHourLoad = PeakHourLoad;
            this.OffPeakHourLoad = OffPeakHourLoad;
            this.IsSpecialEvent = IsSpecialEvent;
            this.SpecialEventName = SpecialEventName;
            this.IsHoliday = IsHoliday;
            this.HolidayName = HolidayName;
            this.IsWeekend = IsWeekend;
            this.SeatConfigurationId = SeatConfigurationId;
            this.RequiresSeatReservation = RequiresSeatReservation;
            this.RouteType = RouteType;
        }

        public RouteSchedule()
        {
        }
    }
}
