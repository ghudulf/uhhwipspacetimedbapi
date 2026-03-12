namespace TicketSalesApp.AdminServer.Models
{
    /// <summary>
    /// Data transfer object for bus entities, providing a consistent JSON-serializable representation.
    /// </summary>
    public class BusDto
    {
        public required uint BusId { get; set; }
        public required string Model { get; set; }
        public required string RegistrationNumber { get; set; }
        public required uint Capacity { get; set; }
        public required string BusType { get; set; }
        public required uint Year { get; set; }
        public required string Vin { get; set; }
        public required string LicensePlate { get; set; }
        public required string CurrentStatus { get; set; }
        public required bool IsActive { get; set; }
        public required uint SeatedCapacity { get; set; }
        public required uint StandingCapacity { get; set; }
        public required string CurrentLocation { get; set; }
        public required ulong LastLocationUpdate { get; set; }
        public required double FuelConsumption { get; set; }
        public required double CurrentFuelLevel { get; set; }
        public required string FuelType { get; set; }
        public required uint MileageTotal { get; set; }
        public required uint MileageSinceService { get; set; }
        public required bool HasAccessibility { get; set; }
        public required bool HasAirConditioning { get; set; }
        public required bool HasWifi { get; set; }
        public required bool HasUsbCharging { get; set; }
    }
}
