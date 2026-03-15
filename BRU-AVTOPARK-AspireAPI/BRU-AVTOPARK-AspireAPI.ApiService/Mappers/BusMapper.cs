using System;
using System.Collections.Generic;
using Microsoft.CSharp.RuntimeBinder;
using Serilog;
using TicketSalesApp.AdminServer.Models;

namespace TicketSalesApp.AdminServer.Mappers
{
    /// <summary>
    /// Provides mapping functionality to convert bus entities to DTOs.
    /// </summary>
    public static class BusMapper
    {
        private static readonly Serilog.ILogger _log = Log.ForContext(typeof(BusMapper));

        /// <summary>
        /// Converts a bus entity to a BusDto for JSON serialization.
        /// Nullable SpacetimeDB fields are coerced to safe defaults; all applied defaults are logged at Debug level.
        /// </summary>
        /// <param name="bus">The bus entity to convert (dynamic SpacetimeDB type).</param>
        /// <returns>A BusDto with all fields populated.</returns>
        /// <exception cref="ArgumentNullException">Thrown when bus is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a required non-nullable field cannot be read.</exception>
        public static BusDto ToDto(dynamic bus)
        {
            if (bus == null)
                throw new ArgumentNullException(nameof(bus), "Bus entity cannot be null");

            // Track which nullable fields were missing so callers can see data quality at a glance.
            var defaults = new List<string>();

            try
            {
                // --- required (non-nullable in SpacetimeDB schema) ---
                uint  busId    = bus.BusId;
                string? modelRaw   = bus.Model;
                string? busTypeRaw = bus.BusType;

                // Fail-fast on required string fields that must never be null/empty.
                if (string.IsNullOrEmpty(modelRaw))
                {
                    _log.Warning("BusMapper.ToDto BusId={BusId}: required field 'Model' is null or empty", busId);
                    throw new ArgumentException($"Bus entity BusId={busId} has a null or empty 'Model' field.", nameof(bus));
                }
                if (string.IsNullOrEmpty(busTypeRaw))
                {
                    _log.Warning("BusMapper.ToDto BusId={BusId}: required field 'BusType' is null or empty", busId);
                    throw new ArgumentException($"Bus entity BusId={busId} has a null or empty 'BusType' field.", nameof(bus));
                }

                string model   = modelRaw;
                string busType = busTypeRaw;
                uint  capacity = bus.Capacity;
                uint  year     = bus.Year;
                bool  isActive = bus.IsActive;

                // --- optional (nullable in SpacetimeDB schema) ---
                string registrationNumber = Coerce(bus.RegistrationNumber, string.Empty, nameof(bus.RegistrationNumber), defaults);
                string vin                = Coerce(bus.Vin,                string.Empty, nameof(bus.Vin),                defaults);
                string licensePlate       = Coerce(bus.LicensePlate,       string.Empty, nameof(bus.LicensePlate),       defaults);
                string currentStatus      = Coerce(bus.CurrentStatus,      string.Empty, nameof(bus.CurrentStatus),      defaults);
                uint   seatedCapacity     = Coerce(bus.SeatedCapacity,     0u,           nameof(bus.SeatedCapacity),     defaults);
                uint   standingCapacity   = Coerce(bus.StandingCapacity,   0u,           nameof(bus.StandingCapacity),   defaults);
                string currentLocation    = Coerce(bus.CurrentLocation,    string.Empty, nameof(bus.CurrentLocation),    defaults);
                ulong  lastLocationUpdate = Coerce(bus.LastLocationUpdate, 0ul,          nameof(bus.LastLocationUpdate), defaults);
                double fuelConsumption    = Coerce(bus.FuelConsumption,    0.0,          nameof(bus.FuelConsumption),    defaults);
                double currentFuelLevel   = Coerce(bus.CurrentFuelLevel,   0.0,          nameof(bus.CurrentFuelLevel),   defaults);
                string fuelType           = Coerce(bus.FuelType,           string.Empty, nameof(bus.FuelType),           defaults);
                uint   mileageTotal       = Coerce(bus.MileageTotal,       0u,           nameof(bus.MileageTotal),       defaults);
                uint   mileageSinceService= Coerce(bus.MileageSinceService,0u,           nameof(bus.MileageSinceService),defaults);
                bool   hasAccessibility   = Coerce(bus.HasAccessibility,   false,        nameof(bus.HasAccessibility),   defaults);
                bool   hasAirConditioning = Coerce(bus.HasAirConditioning, false,        nameof(bus.HasAirConditioning), defaults);
                bool   hasWifi            = Coerce(bus.HasWifi,            false,        nameof(bus.HasWifi),            defaults);
                bool   hasUsbCharging     = Coerce(bus.HasUsbCharging,     false,        nameof(bus.HasUsbCharging),     defaults);

                if (defaults.Count > 0)
                    _log.Debug("BusMapper.ToDto BusId={BusId}: {Count} nullable field(s) defaulted: {Fields}",
                        busId, defaults.Count, string.Join(", ", defaults));
                else
                    _log.Debug("BusMapper.ToDto BusId={BusId}: all fields present", busId);

                return new BusDto
                {
                    BusId               = busId,
                    Model               = model,
                    RegistrationNumber  = registrationNumber,
                    Capacity            = capacity,
                    BusType             = busType,
                    Year                = year,
                    Vin                 = vin,
                    LicensePlate        = licensePlate,
                    CurrentStatus       = currentStatus,
                    IsActive            = isActive,
                    SeatedCapacity      = seatedCapacity,
                    StandingCapacity    = standingCapacity,
                    CurrentLocation     = currentLocation,
                    LastLocationUpdate  = lastLocationUpdate,
                    FuelConsumption     = fuelConsumption,
                    CurrentFuelLevel    = currentFuelLevel,
                    FuelType            = fuelType,
                    MileageTotal        = mileageTotal,
                    MileageSinceService = mileageSinceService,
                    HasAccessibility    = hasAccessibility,
                    HasAirConditioning  = hasAirConditioning,
                    HasWifi             = hasWifi,
                    HasUsbCharging      = hasUsbCharging
                };
            }
            catch (RuntimeBinderException ex)
            {
                _log.Error(ex, "BusMapper.ToDto RuntimeBinderException — bus entity type: {Type}, message: {Message}",
                    bus?.GetType()?.FullName ?? "null", ex.Message);
                throw new ArgumentException(
                    $"Failed to map bus entity to DTO. Missing or invalid property: {ex.Message}", nameof(bus), ex);
            }
            catch (Exception ex) when (ex is not ArgumentNullException && ex is not ArgumentException)
            {
                _log.Error(ex, "BusMapper.ToDto unexpected error — bus entity type: {Type}", bus?.GetType()?.FullName ?? "null");
                throw new InvalidOperationException("BusMapper.ToDto failed due to an unexpected internal error.", ex);
            }
        }

        // Helper: coerce a nullable dynamic value to a non-nullable default, recording the field name when defaulted.
        private static T Coerce<T>(dynamic? value, T fallback, string fieldName, List<string> defaults)
        {
            if (value is null)
            {
                defaults.Add(fieldName);
                return fallback;
            }
            try { return (T)value; }
            catch
            {
                defaults.Add($"{fieldName}(cast-failed)");
                return fallback;
            }
        }
    }
}
