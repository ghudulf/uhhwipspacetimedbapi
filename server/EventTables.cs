using System.Text;
using SpacetimeDB;

public static partial class Module
{
    // ***** Event Tables for SpacetimeDB 2.0 *****
    // Event tables are transient - they publish events to subscribers but don't persist rows
    // They replace the old reducer callback system for cross-client notifications

    /// <summary>
    /// Event table for authentication-related events
    /// Publishes events when users log in, log out, or authentication fails
    /// </summary>
    [SpacetimeDB.Table(Event = true, Public = true)]
    public partial class AuthenticationEvent
    {
        public Identity UserId;          // Identity of the user (Default for failed attempts)
        public string EventType;         // "Login", "Logout", "Failed", "TokenRefresh"
        public ulong Timestamp;          // Unix timestamp in milliseconds
        public string? Details;          // Additional details (e.g., error message for failed attempts)
        public string? IpAddress;        // IP address of the client (if available)
    }

    /// <summary>
    /// Event table for ticket sale events
    /// Publishes events when tickets are sold, updated, or cancelled
    /// </summary>
    [SpacetimeDB.Table(Event = true, Public = true)]
    public partial class TicketSaleEvent
    {
        public uint SaleId;              // ID of the sale
        public uint TicketId;            // ID of the ticket sold
        public uint RouteId;             // ID of the route
        public Identity BuyerId;         // Identity of the buyer
        public double Amount;            // Sale amount
        public ulong Timestamp;          // Unix timestamp in milliseconds
        public string PaymentMethod;     // Payment method used (e.g., "Cash", "Card", "Mobile")
    }

    /// <summary>
    /// Event table for bus status changes
    /// Publishes events when bus status changes (active/inactive/maintenance)
    /// </summary>
    [SpacetimeDB.Table(Event = true, Public = true)]
    public partial class BusStatusEvent
    {
        public uint BusId;               // ID of the bus
        public string PreviousStatus;    // Previous status (e.g., "Active", "Inactive", "Maintenance")
        public string NewStatus;         // New status
        public ulong Timestamp;          // Unix timestamp in milliseconds
        public Identity ChangedBy;       // Identity of the user who changed the status
        public string? Reason;           // Reason for the status change (optional)
    }

    /// <summary>
    /// Event table for route schedule events
    /// Publishes events when route schedules are created, updated, or cancelled
    /// </summary>
    [SpacetimeDB.Table(Event = true, Public = true)]
    public partial class RouteScheduleEvent
    {
        public uint ScheduleId;          // ID of the schedule
        public uint RouteId;             // ID of the route
        public string EventType;         // "Created", "Updated", "Cancelled"
        public ulong Timestamp;          // Unix timestamp in milliseconds
        public Identity ChangedBy;       // Identity of the user who made the change
    }

    /// <summary>
    /// Event table for maintenance events
    /// Publishes events when maintenance is scheduled, started, or completed
    /// </summary>
    [SpacetimeDB.Table(Event = true, Public = true)]
    public partial class MaintenanceEvent
    {
        public uint MaintenanceId;       // ID of the maintenance record
        public uint BusId;               // ID of the bus being maintained
        public string EventType;         // "Scheduled", "Started", "Completed", "Cancelled"
        public ulong Timestamp;          // Unix timestamp in milliseconds
        public Identity ChangedBy;       // Identity of the user who made the change
    }
}
