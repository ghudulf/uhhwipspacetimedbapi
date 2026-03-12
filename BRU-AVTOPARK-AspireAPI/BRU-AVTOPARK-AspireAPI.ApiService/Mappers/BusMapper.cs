using System;
using Microsoft.CSharp.RuntimeBinder;
using TicketSalesApp.AdminServer.Models;

namespace TicketSalesApp.AdminServer.Mappers
{
    /// <summary>
    /// Provides mapping functionality to convert bus entities to DTOs.
    /// </summary>
    public static class BusMapper
    {
        /// <summary>
        /// Converts a bus entity to a BusDto for JSON serialization.
        /// </summary>
        /// <param name="bus">The bus entity to convert.</param>
        /// <returns>A BusDto containing all bus properties.</returns>
        /// <exception cref="ArgumentNullException">Thrown when bus parameter is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a required property cannot be accessed on the bus entity.</exception>
        public static BusDto ToDto(dynamic bus)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus), "Bus entity cannot be null");
            }

            try
            {
                return new BusDto
                {
                    BusId = bus.BusId,
                    Model = bus.Model,
                    RegistrationNumber = bus.RegistrationNumber,
                    Capacity = bus.Capacity,
                    BusType = bus.BusType,
                    Year = bus.Year,
                    Vin = bus.Vin,
                    LicensePlate = bus.LicensePlate,
                    CurrentStatus = bus.CurrentStatus,
                    IsActive = bus.IsActive,
                    SeatedCapacity = bus.SeatedCapacity,
                    StandingCapacity = bus.StandingCapacity,
                    CurrentLocation = bus.CurrentLocation,
                    LastLocationUpdate = bus.LastLocationUpdate,
                    FuelConsumption = bus.FuelConsumption,
                    CurrentFuelLevel = bus.CurrentFuelLevel,
                    FuelType = bus.FuelType,
                    MileageTotal = bus.MileageTotal,
                    MileageSinceService = bus.MileageSinceService,
                    HasAccessibility = bus.HasAccessibility,
                    HasAirConditioning = bus.HasAirConditioning,
                    HasWifi = bus.HasWifi,
                    HasUsbCharging = bus.HasUsbCharging
                };
            }
            catch (RuntimeBinderException ex)
            {
                throw new ArgumentException($"Failed to map bus entity to DTO. Missing or invalid property: {ex.Message}", nameof(bus), ex);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to map bus entity to DTO: {ex.Message}", nameof(bus), ex);
            }
        }
    }
}
