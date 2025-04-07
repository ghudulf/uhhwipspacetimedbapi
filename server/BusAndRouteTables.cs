 using System.Text;
using SpacetimeDB;

public static partial class Module
{ // ***** Fleet Management *****

    [SpacetimeDB.Table(Public = true)]
    public partial class Bus
    {
        //optional fields needs no init value
        [PrimaryKey]
        public uint BusId;              // Auto-incremented
        public string Model = "";       // BUS MODEL
        public string? RegistrationNumber = ""; // BUS REGISTRATION NUMBER
        public bool IsActive;           // IS BUS ACTIVE
        public string BusType = "";     // "Regular", "Trolleybus", "Urban", "Intercity", etc.
        public uint Capacity = 0;           // Total passenger capacity
        public uint? SeatedCapacity;    // Number of seated passengers
        public uint? StandingCapacity;  // Number of standing passengers
        public uint Year = 0;               // Year of manufacture
        public string? VIN;             // Vehicle Identification Number
        public string? LicensePlate;    // License plate number
        public string? CurrentStatus;   // "In Service", "Maintenance", "Out of Service"
        public string? CurrentLocation; // Current GPS location if available
        public ulong? LastLocationUpdate; // When location was last updated
        public double? FuelConsumption;  // Average fuel consumption
        public double? CurrentFuelLevel; // Current fuel level
        public string? FuelType;         // Type of fuel used
        public uint? MileageTotal;       // Total mileage of the bus
        public uint? MileageSinceService; // Mileage since last service
        public bool? HasAccessibility;   // Accessibility features for disabled passengers
        public bool? HasAirConditioning; // Whether the bus has air conditioning
        public bool? HasWifi;            // Whether the bus has WiFi
        public bool? HasUSBCharging;     // Whether the bus has USB charging ports
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Maintenance
    {
        [PrimaryKey]
        public uint MaintenanceId;       // Auto-incremented
        public uint BusId;               // References Bus.BusId
        public ulong LastServiceDate;
        public string? MileageThreshold;
        public string? MaintenanceType;  // "Regular", "Emergency", "Preventive"
        public string? ServiceEngineer;
        public string? FoundIssues;
        public ulong NextServiceDate;
        public string? Roadworthiness;
        public double MaintenanceCost;   // Cost of maintenance
        public string? PartsReplaced;    // Parts that were replaced
        public ulong MaintenanceDuration; // Duration of maintenance in hours
        public bool IsScheduled;         // Whether maintenance was scheduled or emergency
        public string? MaintenanceLocation; // Where maintenance was performed
        public uint? ScheduledByEmployeeId; // Who scheduled the maintenance
        public uint? CompletedByEmployeeId; // Who completed the maintenance
        public string? MaintenanceNotes; // Additional notes about the maintenance
        public string? MaintenanceStatus; // "Scheduled", "In Progress", "Completed", "Cancelled"
        public string[]? DiagnosticCodes; // Diagnostic codes from the bus computer
        public double? LaborCost;        // Cost of labor
        public double? PartsCost;        // Cost of parts
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Route
    {
        [PrimaryKey]
        public uint RouteId;             // Auto-incremented
        public string RouteNumber;       // Route identifier (e.g., "123", "A5")
        public string StartPoint;
        public string EndPoint;
        public uint DriverId;            // References Employee.EmployeeId
        public uint BusId;               // References Bus.BusId
        public string? TravelTime;       // String or numeric (minutes)
        public uint StopCount;           // Number of stops on the route
        public string? RouteDescription; // Description of the route
        public double RouteLength;       // Length of the route in kilometers
        public bool IsActive;
        public string? RouteType;        // "Urban", "Suburban", "Express", etc.
        public string[]? AlternativeRoutes; // Alternative routes in case of road closures
        public string[]? PeakHours;      // Peak hours for this route
        public uint? FrequencyPeak;      // Frequency during peak hours (minutes)
        public uint? FrequencyOffPeak;   // Frequency during off-peak hours (minutes)
        public string[]? SpecialInstructions; // Special instructions for drivers
        public bool? IsAccessible;       // Whether the route is accessible for disabled passengers
        public string[]? RouteFeatures;  // Special features of the route
        public ulong CreatedAt;          // When the route was created
        public ulong? UpdatedAt;         // When the route was last updated
        public string? UpdatedBy;        // Who last updated the route
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class RouteSchedule
    {
        [PrimaryKey]
        public uint ScheduleId;          // Auto-incremented
        public uint RouteId;             // References Route.RouteId
        public string? StartPoint;
        public string[]? RouteStops;     // Names of all stops on the route
        public string? EndPoint;
        public ulong DepartureTime;
        public ulong ArrivalTime;
        public double Price;
        public uint AvailableSeats;
        public uint? SeatedCapacity;     // Number of seated passengers
        public uint? StandingCapacity;   // Number of standing passengers
        public string[]? DaysOfWeek;
        public string[]? BusTypes;       // "MAZ-103", "MAZ-206", etc.
        public bool IsActive;
        public ulong ValidFrom;
        public ulong? ValidUntil;
        public uint? StopDurationMinutes;
        public bool IsRecurring;
        public string[]? EstimatedStopTimes;
        public double[]? StopDistances;
        public string? Notes;
        public ulong CreatedAt;
        public ulong? UpdatedAt;
        public string? UpdatedBy;
        public double? PeakHourLoad;     // Average passenger load during peak hours
        public double? OffPeakHourLoad;  // Average passenger load during off-peak hours
        public bool? IsSpecialEvent;     // Whether this is a special event schedule
        public string? SpecialEventName; // Name of the special event
        public bool? IsHoliday;          // Whether this is a holiday schedule
        public string? HolidayName;      // Name of the holiday
        public bool? IsWeekend;          // Whether this is a weekend schedule
        public uint? SeatConfigurationId;
        public bool? RequiresSeatReservation;
        public string? RouteType;
    } 
    
    
    
    [SpacetimeDB.Table(Public = true)]
    public partial class BusLocation
    {
        [PrimaryKey]
        public uint LocationId;         // Auto-incremented
        public uint BusId;              // References Bus.BusId
        public double Latitude;         // Current latitude
        public double Longitude;        // Current longitude
        public ulong Timestamp;         // When this location was recorded
        public uint? RouteId;           // References Route.RouteId
        public uint? ScheduleId;        // References RouteSchedule.ScheduleId
        public double? Speed;           // Current speed in km/h
        public double? Heading;         // Current heading in degrees
        public string? Status;          // "On Route", "Off Route", "At Stop", "Delayed"
        public uint? NextStopId;        // ID of the next stop
        public ulong? EstimatedArrivalTime; // Estimated arrival time at next stop
        public bool? IsDelayed;         // Whether the bus is delayed
        public uint? DelayMinutes;      // Minutes of delay
        public string? DelayReason;     // Reason for delay
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class BusStop
    {
        [PrimaryKey]
        public uint StopId;             // Auto-incremented
        public string StopName;         // Name of the stop
        public double Latitude;         // Latitude of the stop
        public double Longitude;        // Longitude of the stop
        public string? StopCode;        // Code of the stop
        public string? StopDescription; // Description of the stop
        public bool? HasShelter;        // Whether the stop has a shelter
        public bool? HasBench;          // Whether the stop has a bench
        public bool? HasLighting;       // Whether the stop has lighting
        public bool? IsAccessible;      // Whether the stop is accessible for disabled passengers
        public string[]? Routes;        // Routes that serve this stop
        public string? StopType;        // "Regular", "Terminal", "Transfer", "Express"
        public string? Zone;            // Zone of the stop
        public string? Address;         // Address of the stop
        public string[]? Amenities;     // Amenities at the stop
        public string[]? NearbyLandmarks; // Nearby landmarks
        public ulong? LastUpdated;      // When the stop information was last updated
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class FuelRecord
    {
        [PrimaryKey]
        public uint FuelRecordId;       // Auto-incremented
        public uint BusId;              // References Bus.BusId
        public ulong Date;              // Date of the fuel record
        public double Amount;           // Amount of fuel added in liters
        public double Cost;             // Cost of the fuel
        public double Odometer;         // Odometer reading at the time of fueling
        public string? FuelType;        // Type of fuel
        public string? FuelStation;     // Name of the fuel station
        public uint? EmployeeId;        // Who fueled the bus
        public string? Notes;           // Additional notes
        public double? FuelEconomy;     // Calculated fuel economy (km/l)
        public double? PreviousOdometer; // Previous odometer reading
        public double? Distance;        // Distance traveled since last fueling
    }


    [SpacetimeDB.Table(Public = true)]
    public partial class SeatConfiguration
    {
        [PrimaryKey]
        public uint ConfigurationId;
        public uint BusId;
        public uint SeatNumber;
        public string SeatType;
        public string SeatStatus;
        public uint SeatRow;
        public uint SeatColumn;
        public bool IsAccessible;
        public bool IsEmergencyExit;
        public string? Notes;
        public ulong CreatedAt;
        public string? CreatedBy;
        public ulong? UpdatedAt;
        public string? UpdatedBy;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class PassengerCount
    {
        [PrimaryKey]
        public uint PassengerCountId;  // Auto-incremented
        public ulong Timestamp;        // When the passenger count was recorded
        public uint RouteId;           // References Route.RouteId
        public uint StopId;            // References BusStop.StopId
        public uint PassengerCountNumber;    // Number of passengers counted during the stop
        public string? Notes;           // Additional notes
        public uint? EmployeeId;        // Who recorded the passenger count
    }

}
 
 