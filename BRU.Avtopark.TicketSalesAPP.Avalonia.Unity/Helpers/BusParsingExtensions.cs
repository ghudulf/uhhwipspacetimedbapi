using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SpacetimeDB.Types;
using Serilog;
using System.Linq;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers
{
    /// <summary>
    /// Extension methods for parsing SpacetimeDB types from JSON with all fields.
    /// This ensures no fields are missing when deserializing API responses.
    /// </summary>
    public static class BusParsingExtensions
    {
        // Note: Helper methods (GetValue, GetNullableValue, GetStringValue, GetStringArray) 
        // are defined in JsonReferenceHelper.cs to avoid duplication

        // ===== ENTITY PARSING METHODS =====
        /// <summary>
        /// Parses a Bus object from a JsonObject, mapping ALL fields from the Bus model.
        /// </summary>
        public static Bus? ParseBus(this JsonObject busObj)
        {
            try
            {
                // Required fields
                uint busId = busObj.GetValue<uint>("busId", 0);
                if (busId == 0)
                {
                    Log.Warning("Bus object has BusId 0, skipping");
                    return null;
                }

                string model = busObj.GetStringValue("model") ?? "N/A";
                string busType = busObj.GetStringValue("busType") ?? string.Empty;
                uint capacity = busObj.GetValue<uint>("capacity", 0);
                uint year = busObj.GetValue<uint>("year", 0);
                bool isActive = busObj.GetValue<bool>("isActive", false);

                // Optional fields
                string? registrationNumber = busObj.GetStringValue("registrationNumber");
                uint? seatedCapacity = busObj.GetNullableValue<uint>("seatedCapacity");
                uint? standingCapacity = busObj.GetNullableValue<uint>("standingCapacity");
                string? vin = busObj.GetStringValue("vin");
                string? licensePlate = busObj.GetStringValue("licensePlate");
                string? currentStatus = busObj.GetStringValue("currentStatus");
                string? currentLocation = busObj.GetStringValue("currentLocation");
                ulong? lastLocationUpdate = busObj.GetNullableValue<ulong>("lastLocationUpdate");
                double? fuelConsumption = busObj.GetNullableValue<double>("fuelConsumption");
                double? currentFuelLevel = busObj.GetNullableValue<double>("currentFuelLevel");
                string? fuelType = busObj.GetStringValue("fuelType");
                uint? mileageTotal = busObj.GetNullableValue<uint>("mileageTotal");
                uint? mileageSinceService = busObj.GetNullableValue<uint>("mileageSinceService");
                bool? hasAccessibility = busObj.GetNullableValue<bool>("hasAccessibility");
                bool? hasAirConditioning = busObj.GetNullableValue<bool>("hasAirConditioning");
                bool? hasWifi = busObj.GetNullableValue<bool>("hasWifi");
                bool? hasUsbCharging = busObj.GetNullableValue<bool>("hasUsbCharging");

                Log.Verbose("Parsed Bus: Id={BusId}, Model='{Model}', Type='{Type}', Year={Year}, " +
                           "Capacity={Capacity}, Seated={Seated}, Standing={Standing}, Active={Active}, " +
                           "Reg='{Reg}', VIN='{VIN}', Plate='{Plate}', Status='{Status}', " +
                           "Location='{Location}', Fuel={Fuel}L, Mileage={Mileage}km",
                    busId, model, busType, year, capacity, seatedCapacity, standingCapacity, isActive,
                    registrationNumber, vin, licensePlate, currentStatus, currentLocation, 
                    currentFuelLevel, mileageTotal);

                return new Bus
                {
                    BusId = busId,
                    Model = model,
                    RegistrationNumber = registrationNumber,
                    IsActive = isActive,
                    BusType = busType,
                    Capacity = capacity,
                    SeatedCapacity = seatedCapacity,
                    StandingCapacity = standingCapacity,
                    Year = year,
                    Vin = vin,
                    LicensePlate = licensePlate,
                    CurrentStatus = currentStatus,
                    CurrentLocation = currentLocation,
                    LastLocationUpdate = lastLocationUpdate,
                    FuelConsumption = fuelConsumption,
                    CurrentFuelLevel = currentFuelLevel,
                    FuelType = fuelType,
                    MileageTotal = mileageTotal,
                    MileageSinceService = mileageSinceService,
                    HasAccessibility = hasAccessibility,
                    HasAirConditioning = hasAirConditioning,
                    HasWifi = hasWifi,
                    HasUsbCharging = hasUsbCharging
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse Bus object from JSON: {Json}", busObj.ToJsonString());
                return null;
            }
        }

        /// <summary>
        /// Parses a Route object from a JsonObject, mapping ALL fields.
        /// </summary>
        public static Route? ParseRoute(this JsonObject routeObj)
        {
            try
            {
                uint routeId = routeObj.GetValue<uint>("routeId", 0);
                if (routeId == 0)
                {
                    Log.Warning("Route object has RouteId 0, skipping");
                    return null;
                }

                string routeNumber = routeObj.GetStringValue("routeNumber") ?? string.Empty;
                string startPoint = routeObj.GetStringValue("startPoint") ?? string.Empty;
                string endPoint = routeObj.GetStringValue("endPoint") ?? string.Empty;
                uint driverId = routeObj.GetValue<uint>("driverId", 0);
                uint busId = routeObj.GetValue<uint>("busId", 0);
                uint stopCount = routeObj.GetValue<uint>("stopCount", 0);
                double routeLength = routeObj.GetValue<double>("routeLength", 0.0);
                bool isActive = routeObj.GetValue<bool>("isActive", false);
                ulong createdAt = routeObj.GetValue<ulong>("createdAt", 0);

                // Optional fields
                string? travelTime = routeObj.GetStringValue("travelTime");
                string? routeDescription = routeObj.GetStringValue("routeDescription");
                string? routeType = routeObj.GetStringValue("routeType");
                List<string>? alternativeRoutes = routeObj.GetStringArray("alternativeRoutes")?.ToList();
                List<string>? peakHours = routeObj.GetStringArray("peakHours")?.ToList();
                uint? frequencyPeak = routeObj.GetNullableValue<uint>("frequencyPeak");
                uint? frequencyOffPeak = routeObj.GetNullableValue<uint>("frequencyOffPeak");
                List<string>? specialInstructions = routeObj.GetStringArray("specialInstructions")?.ToList();
                bool? isAccessible = routeObj.GetNullableValue<bool>("isAccessible");
                List<string>? routeFeatures = routeObj.GetStringArray("routeFeatures")?.ToList();
                ulong? updatedAt = routeObj.GetNullableValue<ulong>("updatedAt");
                string? updatedBy = routeObj.GetStringValue("updatedBy");

                return new Route
                {
                    RouteId = routeId,
                    RouteNumber = routeNumber,
                    StartPoint = startPoint,
                    EndPoint = endPoint,
                    DriverId = driverId,
                    BusId = busId,
                    TravelTime = travelTime,
                    StopCount = stopCount,
                    RouteDescription = routeDescription,
                    RouteLength = routeLength,
                    IsActive = isActive,
                    RouteType = routeType,
                    AlternativeRoutes = alternativeRoutes,
                    PeakHours = peakHours,
                    FrequencyPeak = frequencyPeak,
                    FrequencyOffPeak = frequencyOffPeak,
                    SpecialInstructions = specialInstructions,
                    IsAccessible = isAccessible,
                    RouteFeatures = routeFeatures,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    UpdatedBy = updatedBy
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse Route object from JSON: {Json}", routeObj.ToJsonString());
                return null;
            }
        }

        /// <summary>
        /// Parses a Maintenance object from a JsonObject, mapping ALL fields.
        /// </summary>
        public static Maintenance? ParseMaintenance(this JsonObject maintObj)
        {
            try
            {
                uint maintenanceId = maintObj.GetValue<uint>("maintenanceId", 0);
                if (maintenanceId == 0)
                {
                    Log.Warning("Maintenance object has MaintenanceId 0, skipping");
                    return null;
                }

                uint busId = maintObj.GetValue<uint>("busId", 0);
                ulong lastServiceDate = maintObj.GetValue<ulong>("lastServiceDate", 0);
                ulong nextServiceDate = maintObj.GetValue<ulong>("nextServiceDate", 0);
                double maintenanceCost = maintObj.GetValue<double>("maintenanceCost", 0.0);
                ulong maintenanceDuration = maintObj.GetValue<ulong>("maintenanceDuration", 0);
                bool isScheduled = maintObj.GetValue<bool>("isScheduled", false);

                // Optional fields
                string? mileageThreshold = maintObj.GetStringValue("mileageThreshold");
                string? maintenanceType = maintObj.GetStringValue("maintenanceType");
                string? serviceEngineer = maintObj.GetStringValue("serviceEngineer");
                string? foundIssues = maintObj.GetStringValue("foundIssues");
                string? roadworthiness = maintObj.GetStringValue("roadworthiness");
                string? partsReplaced = maintObj.GetStringValue("partsReplaced");
                string? maintenanceLocation = maintObj.GetStringValue("maintenanceLocation");
                uint? scheduledByEmployeeId = maintObj.GetNullableValue<uint>("scheduledByEmployeeId");
                uint? completedByEmployeeId = maintObj.GetNullableValue<uint>("completedByEmployeeId");
                string? maintenanceNotes = maintObj.GetStringValue("maintenanceNotes");
                string? maintenanceStatus = maintObj.GetStringValue("maintenanceStatus");
                List<string>? diagnosticCodes = maintObj.GetStringArray("diagnosticCodes")?.ToList();
                double? laborCost = maintObj.GetNullableValue<double>("laborCost");
                double? partsCost = maintObj.GetNullableValue<double>("partsCost");

                return new Maintenance
                {
                    MaintenanceId = maintenanceId,
                    BusId = busId,
                    LastServiceDate = lastServiceDate,
                    MileageThreshold = mileageThreshold,
                    MaintenanceType = maintenanceType,
                    ServiceEngineer = serviceEngineer,
                    FoundIssues = foundIssues,
                    NextServiceDate = nextServiceDate,
                    Roadworthiness = roadworthiness,
                    MaintenanceCost = maintenanceCost,
                    PartsReplaced = partsReplaced,
                    MaintenanceDuration = maintenanceDuration,
                    IsScheduled = isScheduled,
                    MaintenanceLocation = maintenanceLocation,
                    ScheduledByEmployeeId = scheduledByEmployeeId,
                    CompletedByEmployeeId = completedByEmployeeId,
                    MaintenanceNotes = maintenanceNotes,
                    MaintenanceStatus = maintenanceStatus,
                    DiagnosticCodes = diagnosticCodes,
                    LaborCost = laborCost,
                    PartsCost = partsCost
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse Maintenance object from JSON: {Json}", maintObj.ToJsonString());
                return null;
            }
        }

        /// <summary>
        /// Parses an Employee object from a JsonObject, mapping ALL fields.
        /// </summary>
        public static Employee? ParseEmployee(this JsonObject empObj)
        {
            try
            {
                uint employeeId = empObj.GetValue<uint>("employeeId", 0);
                if (employeeId == 0)
                {
                    Log.Warning("Employee object has EmployeeId 0, skipping");
                    return null;
                }

                string name = empObj.GetStringValue("name") ?? string.Empty;
                string surname = empObj.GetStringValue("surname") ?? string.Empty;
                uint jobId = empObj.GetValue<uint>("jobId", 0);
                ulong employedSince = empObj.GetValue<ulong>("employedSince", 0);

                // Optional fields
                string? patronym = empObj.GetStringValue("patronym");
                string? badgeNumber = empObj.GetStringValue("badgeNumber");
                string? contactPhone = empObj.GetStringValue("contactPhone");
                string? contactEmail = empObj.GetStringValue("contactEmail");
                ulong? dateOfBirth = empObj.GetNullableValue<ulong>("dateOfBirth");
                string? passportNumber = empObj.GetStringValue("passportNumber");
                string? passportIssuedBy = empObj.GetStringValue("passportIssuedBy");
                ulong? passportIssuedDate = empObj.GetNullableValue<ulong>("passportIssuedDate");
                string? photoUrl = empObj.GetStringValue("photoUrl");
                string? address = empObj.GetStringValue("address");
                string? emergencyContact = empObj.GetStringValue("emergencyContact");
                ulong? lastTrainingDate = empObj.GetNullableValue<ulong>("lastTrainingDate");
                string? trainingStatus = empObj.GetStringValue("trainingStatus");
                string? currentStatus = empObj.GetStringValue("currentStatus");
                List<string>? certifications = empObj.GetStringArray("certifications")?.ToList();
                ulong? certificationExpiry = empObj.GetNullableValue<ulong>("certificationExpiry");
                string? medicalCertificate = empObj.GetStringValue("medicalCertificate");
                ulong? medicalCertificateExpiry = empObj.GetNullableValue<ulong>("medicalCertificateExpiry");
                string? driverLicenseNumber = empObj.GetStringValue("driverLicenseNumber");
                string? driverLicenseCategory = empObj.GetStringValue("driverLicenseCategory");
                ulong? driverLicenseExpiry = empObj.GetNullableValue<ulong>("driverLicenseExpiry");
                uint? yearsOfExperience = empObj.GetNullableValue<uint>("yearsOfExperience");
                List<string>? languagesSpoken = empObj.GetStringArray("languagesSpoken")?.ToList();
                string? preferredShiftType = empObj.GetStringValue("preferredShiftType");
                List<string>? skillsAndQualifications = empObj.GetStringArray("skillsAndQualifications")?.ToList();
                string? performanceRating = empObj.GetStringValue("performanceRating");
                uint? vacationDaysRemaining = empObj.GetNullableValue<uint>("vacationDaysRemaining");
                uint? sickDaysUsed = empObj.GetNullableValue<uint>("sickDaysUsed");

                return new Employee
                {
                    EmployeeId = employeeId,
                    Name = name,
                    Surname = surname,
                    Patronym = patronym,
                    EmployedSince = employedSince,
                    JobId = jobId,
                    BadgeNumber = badgeNumber,
                    ContactPhone = contactPhone,
                    ContactEmail = contactEmail,
                    DateOfBirth = dateOfBirth,
                    PassportNumber = passportNumber,
                    PassportIssuedBy = passportIssuedBy,
                    PassportIssuedDate = passportIssuedDate,
                    PhotoUrl = photoUrl,
                    Address = address,
                    EmergencyContact = emergencyContact,
                    LastTrainingDate = lastTrainingDate,
                    TrainingStatus = trainingStatus,
                    CurrentStatus = currentStatus,
                    Certifications = certifications,
                    CertificationExpiry = certificationExpiry,
                    MedicalCertificate = medicalCertificate,
                    MedicalCertificateExpiry = medicalCertificateExpiry,
                    DriverLicenseNumber = driverLicenseNumber,
                    DriverLicenseCategory = driverLicenseCategory,
                    DriverLicenseExpiry = driverLicenseExpiry,
                    YearsOfExperience = yearsOfExperience,
                    LanguagesSpoken = languagesSpoken,
                    PreferredShiftType = preferredShiftType,
                    SkillsAndQualifications = skillsAndQualifications,
                    PerformanceRating = performanceRating,
                    VacationDaysRemaining = vacationDaysRemaining,
                    SickDaysUsed = sickDaysUsed
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse Employee object from JSON: {Json}", empObj.ToJsonString());
                return null;
            }
        }

        /// <summary>
        /// Parses a Ticket object from a JsonObject, mapping ALL fields.
        /// Handles complex DateTime to ulong conversions for timestamps.
        /// </summary>
        public static Ticket? ParseTicket(this JsonObject ticketObj)
        {
            try
            {
                uint ticketId = ticketObj.GetValue<uint>("ticketId", 0);
                if (ticketId == 0)
                {
                    Log.Warning("Ticket object has TicketId 0, skipping");
                    return null;
                }

                uint routeId = ticketObj.GetValue<uint>("routeId", 0);
                
                // Handle ticketPrice with robust parsing
                double ticketPrice = 0.0;
                if (ticketObj["ticketPrice"] != null)
                {
                    try
                    {
                        if (ticketObj["ticketPrice"].AsValue().TryGetValue<double>(out double priceVal))
                        {
                            ticketPrice = priceVal;
                        }
                        else
                        {
                            Log.Warning("ticketPrice for TicketId {TicketId} was not a valid number: {Value}", ticketId, ticketObj["ticketPrice"].ToJsonString());
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to parse ticketPrice for TicketId {TicketId}", ticketId);
                    }
                }
                
                uint seatNumber = ticketObj.GetValue<uint>("seatNumber", 0);
                string paymentMethod = ticketObj.GetStringValue("paymentMethod") ?? string.Empty;
                bool isActive = ticketObj.GetValue<bool>("isActive", false);
                bool isReserved = ticketObj.GetValue<bool>("isReserved", false);

                // Handle PurchaseTime (DateTime to ulong) with comprehensive error handling
                ulong purchaseTime = 0;
                if (ticketObj["purchaseTime"] != null)
                {
                    try
                    {
                        // Try to parse as DateTime first
                        if (ticketObj["purchaseTime"].AsValue().TryGetValue<string>(out _))
                        {
                            DateTime parsedPurchaseTime = ticketObj["purchaseTime"].GetValue<DateTime>();
                            TimeZoneInfo localZone = TimeZoneInfo.Local;
                            DateTimeOffset dto = new DateTimeOffset(parsedPurchaseTime, localZone.GetUtcOffset(parsedPurchaseTime));
                            purchaseTime = (ulong)dto.ToUnixTimeMilliseconds();
                            Log.Verbose("Converted purchaseTime DateTime {DateTime} to Unix MS {Timestamp} for ticket {TicketId}", 
                                parsedPurchaseTime, purchaseTime, ticketId);
                        }
                        // Try to parse as ulong directly (if already in Unix timestamp format)
                        else if (ticketObj["purchaseTime"].AsValue().TryGetValue<ulong>(out ulong directTimestamp))
                        {
                            purchaseTime = directTimestamp;
                        }
                    }
                    catch (ArgumentOutOfRangeException argEx)
                    {
                        Log.Error(argEx, "DateTime value for PurchaseTime for ticket {TicketId} is out of range for DateTimeOffset", ticketId);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to parse PurchaseTime for ticket {TicketId}. Raw value: {RawValue}", 
                            ticketId, ticketObj["purchaseTime"]?.ToJsonString());
                    }
                }

                // Handle CreatedAt (DateTime to ulong) with comprehensive error handling
                ulong createdAt = 0;
                if (ticketObj["createdAt"] != null)
                {
                    try
                    {
                        // Try to parse as DateTime first
                        if (ticketObj["createdAt"].AsValue().TryGetValue<string>(out _))
                        {
                            DateTime parsedCreatedAt = ticketObj["createdAt"].GetValue<DateTime>();
                            TimeZoneInfo localZone = TimeZoneInfo.Local;
                            DateTimeOffset dto = new DateTimeOffset(parsedCreatedAt, localZone.GetUtcOffset(parsedCreatedAt));
                            createdAt = (ulong)dto.ToUnixTimeMilliseconds();
                            Log.Verbose("Converted createdAt DateTime {DateTime} to Unix MS {Timestamp} for ticket {TicketId}", 
                                parsedCreatedAt, createdAt, ticketId);
                        }
                        // Try to parse as ulong directly
                        else if (ticketObj["createdAt"].AsValue().TryGetValue<ulong>(out ulong directTimestamp))
                        {
                            createdAt = directTimestamp;
                        }
                    }
                    catch (ArgumentOutOfRangeException argEx)
                    {
                        Log.Error(argEx, "DateTime value for CreatedAt for ticket {TicketId} is out of range for DateTimeOffset", ticketId);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to parse CreatedAt for ticket {TicketId}. Raw value: {RawValue}", 
                            ticketId, ticketObj["createdAt"]?.ToJsonString());
                    }
                }

                // Handle UpdatedAt (DateTime to ulong?) with comprehensive error handling
                ulong? updatedAt = null;
                if (ticketObj["updatedAt"] != null)
                {
                    try
                    {
                        // Try to parse as DateTime first
                        if (ticketObj["updatedAt"].AsValue().TryGetValue<string>(out _))
                        {
                            DateTime parsedUpdatedAt = ticketObj["updatedAt"].GetValue<DateTime>();
                            TimeZoneInfo localZone = TimeZoneInfo.Local;
                            DateTimeOffset dto = new DateTimeOffset(parsedUpdatedAt, localZone.GetUtcOffset(parsedUpdatedAt));
                            updatedAt = (ulong)dto.ToUnixTimeMilliseconds();
                            Log.Verbose("Converted updatedAt DateTime {DateTime} to Unix MS {Timestamp} for ticket {TicketId}", 
                                parsedUpdatedAt, updatedAt.Value, ticketId);
                        }
                        // Try to parse as ulong directly
                        else if (ticketObj["updatedAt"].AsValue().TryGetValue<ulong>(out ulong directTimestamp))
                        {
                            updatedAt = directTimestamp;
                        }
                    }
                    catch (ArgumentOutOfRangeException argEx)
                    {
                        Log.Error(argEx, "DateTime value for UpdatedAt for ticket {TicketId} is out of range for DateTimeOffset", ticketId);
                    }
                    catch (Exception ex)
                    {
                        Log.Verbose(ex, "Failed to parse UpdatedAt for ticket {TicketId}. Raw value: {RawValue}", 
                            ticketId, ticketObj["updatedAt"]?.ToJsonString());
                    }
                }

                // Optional fields
                string? updatedBy = ticketObj.GetStringValue("updatedBy");
                string? ticketType = ticketObj.GetStringValue("ticketType");
                string? ticketStatus = ticketObj.GetStringValue("ticketStatus");
                string? validationMethod = ticketObj.GetStringValue("validationMethod");
                ulong? validationTime = ticketObj.GetNullableValue<ulong>("validationTime");
                string? validationLocation = ticketObj.GetStringValue("validationLocation");
                uint? validatedByEmployeeId = ticketObj.GetNullableValue<uint>("validatedByEmployeeId");
                bool? isReturn = ticketObj.GetNullableValue<bool>("isReturn");
                uint? returnTicketId = ticketObj.GetNullableValue<uint>("returnTicketId");
                string? discountType = ticketObj.GetStringValue("discountType");
                double? discountAmount = ticketObj.GetNullableValue<double>("discountAmount");
                string? discountReason = ticketObj.GetStringValue("discountReason");
                string? refundStatus = ticketObj.GetStringValue("refundStatus");
                double? refundAmount = ticketObj.GetNullableValue<double>("refundAmount");
                ulong? refundTime = ticketObj.GetNullableValue<ulong>("refundTime");
                string? refundReason = ticketObj.GetStringValue("refundReason");
                uint? discountId = ticketObj.GetNullableValue<uint>("discountId");
                string? seatType = ticketObj.GetStringValue("seatType");
                string? reservationStatus = ticketObj.GetStringValue("reservationStatus");
                ulong? reservationExpiry = ticketObj.GetNullableValue<ulong>("reservationExpiry");

                return new Ticket
                {
                    TicketId = ticketId,
                    RouteId = routeId,
                    TicketPrice = ticketPrice,
                    SeatNumber = seatNumber,
                    PaymentMethod = paymentMethod,
                    IsActive = isActive,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    UpdatedBy = updatedBy,
                    PurchaseTime = purchaseTime,
                    TicketType = ticketType,
                    TicketStatus = ticketStatus,
                    ValidationMethod = validationMethod,
                    ValidationTime = validationTime,
                    ValidationLocation = validationLocation,
                    ValidatedByEmployeeId = validatedByEmployeeId,
                    IsReturn = isReturn,
                    ReturnTicketId = returnTicketId,
                    DiscountType = discountType,
                    DiscountAmount = discountAmount,
                    DiscountReason = discountReason,
                    RefundStatus = refundStatus,
                    RefundAmount = refundAmount,
                    RefundTime = refundTime,
                    RefundReason = refundReason,
                    DiscountId = discountId,
                    SeatType = seatType,
                    IsReserved = isReserved,
                    ReservationStatus = reservationStatus,
                    ReservationExpiry = reservationExpiry
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse Ticket object from JSON: {Json}", ticketObj.ToJsonString());
                return null;
            }
        }

        /// <summary>
        /// Parses a Sale object from a JsonObject, mapping ALL fields.
        /// </summary>
        public static Sale? ParseSale(this JsonObject saleObj)
        {
            try
            {
                uint saleId = saleObj.GetValue<uint>("saleId", 0);
                if (saleId == 0)
                {
                    Log.Warning("Sale object has SaleId 0, skipping");
                    return null;
                }

                ulong saleDate = saleObj.GetValue<ulong>("saleDate", 0);
                uint ticketId = saleObj.GetValue<uint>("ticketId", 0);
                string ticketSoldToUser = saleObj.GetStringValue("ticketSoldToUser") ?? string.Empty;
                string ticketSoldToUserPhone = saleObj.GetStringValue("ticketSoldToUserPhone") ?? string.Empty;
                double totalAmount = saleObj.GetValue<double>("totalAmount", 0.0);

                // Optional fields
                string? saleLocation = saleObj.GetStringValue("saleLocation");
                string? saleNotes = saleObj.GetStringValue("saleNotes");
                string? paymentMethod = saleObj.GetStringValue("paymentMethod");
                string? paymentStatus = saleObj.GetStringValue("paymentStatus");
                string? transactionId = saleObj.GetStringValue("transactionId");
                double? taxAmount = saleObj.GetNullableValue<double>("taxAmount");
                string? invoiceNumber = saleObj.GetStringValue("invoiceNumber");
                bool? isSubscription = saleObj.GetNullableValue<bool>("isSubscription");
                string? subscriptionType = saleObj.GetStringValue("subscriptionType");
                ulong? subscriptionStartDate = saleObj.GetNullableValue<ulong>("subscriptionStartDate");
                ulong? subscriptionEndDate = saleObj.GetNullableValue<ulong>("subscriptionEndDate");
                bool? isGift = saleObj.GetNullableValue<bool>("isGift");
                string? giftRecipient = saleObj.GetStringValue("giftRecipient");
                string? promotionCode = saleObj.GetStringValue("promotionCode");
                double? discountAmount = saleObj.GetNullableValue<double>("discountAmount");
                string? paymentTransactionId = saleObj.GetStringValue("paymentTransactionId");
                double? changeAmount = saleObj.GetNullableValue<double>("changeAmount");
                string? paymentProvider = saleObj.GetStringValue("paymentProvider");
                string? paymentReference = saleObj.GetStringValue("paymentReference");

                return new Sale
                {
                    SaleId = saleId,
                    SaleDate = saleDate,
                    TicketId = ticketId,
                    TicketSoldToUser = ticketSoldToUser,
                    TicketSoldToUserPhone = ticketSoldToUserPhone,
                    SaleLocation = saleLocation,
                    SaleNotes = saleNotes,
                    PaymentMethod = paymentMethod,
                    PaymentStatus = paymentStatus,
                    TransactionId = transactionId,
                    TaxAmount = taxAmount,
                    InvoiceNumber = invoiceNumber,
                    IsSubscription = isSubscription,
                    SubscriptionType = subscriptionType,
                    SubscriptionStartDate = subscriptionStartDate,
                    SubscriptionEndDate = subscriptionEndDate,
                    IsGift = isGift,
                    GiftRecipient = giftRecipient,
                    PromotionCode = promotionCode,
                    DiscountAmount = discountAmount,
                    TotalAmount = totalAmount,
                    PaymentTransactionId = paymentTransactionId,
                    ChangeAmount = changeAmount,
                    PaymentProvider = paymentProvider,
                    PaymentReference = paymentReference
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse Sale object from JSON: {Json}", saleObj.ToJsonString());
                return null;
            }
        }

        /// <summary>
        /// Parses a RouteSchedule object from a JsonObject, mapping ALL fields.
        /// </summary>
        public static RouteSchedule? ParseRouteSchedule(this JsonObject schedObj)
        {
            try
            {
                uint scheduleId = schedObj.GetValue<uint>("scheduleId", 0);
                if (scheduleId == 0)
                {
                    Log.Warning("RouteSchedule object has ScheduleId 0, skipping");
                    return null;
                }

                uint routeId = schedObj.GetValue<uint>("routeId", 0);
                ulong departureTime = schedObj.GetValue<ulong>("departureTime", 0);
                ulong arrivalTime = schedObj.GetValue<ulong>("arrivalTime", 0);
                double price = schedObj.GetValue<double>("price", 0.0);
                uint availableSeats = schedObj.GetValue<uint>("availableSeats", 0);
                bool isActive = schedObj.GetValue<bool>("isActive", false);
                ulong validFrom = schedObj.GetValue<ulong>("validFrom", 0);
                bool isRecurring = schedObj.GetValue<bool>("isRecurring", false);
                ulong createdAt = schedObj.GetValue<ulong>("createdAt", 0);

                // Optional fields
                string? startPoint = schedObj.GetStringValue("startPoint");
                List<string>? routeStops = schedObj.GetStringArray("routeStops")?.ToList();
                string? endPoint = schedObj.GetStringValue("endPoint");
                uint? seatedCapacity = schedObj.GetNullableValue<uint>("seatedCapacity");
                uint? standingCapacity = schedObj.GetNullableValue<uint>("standingCapacity");
                List<string>? daysOfWeek = schedObj.GetStringArray("daysOfWeek")?.ToList();
                List<string>? busTypes = schedObj.GetStringArray("busTypes")?.ToList();
                ulong? validUntil = schedObj.GetNullableValue<ulong>("validUntil");
                uint? stopDurationMinutes = schedObj.GetNullableValue<uint>("stopDurationMinutes");
                List<string>? estimatedStopTimes = schedObj.GetStringArray("estimatedStopTimes")?.ToList();
                List<double>? stopDistances = schedObj.GetDoubleArray("stopDistances")?.ToList();
                string? notes = schedObj.GetStringValue("notes");
                ulong? updatedAt = schedObj.GetNullableValue<ulong>("updatedAt");
                string? updatedBy = schedObj.GetStringValue("updatedBy");
                double? peakHourLoad = schedObj.GetNullableValue<double>("peakHourLoad");
                double? offPeakHourLoad = schedObj.GetNullableValue<double>("offPeakHourLoad");
                bool? isSpecialEvent = schedObj.GetNullableValue<bool>("isSpecialEvent");
                string? specialEventName = schedObj.GetStringValue("specialEventName");
                bool? isHoliday = schedObj.GetNullableValue<bool>("isHoliday");
                string? holidayName = schedObj.GetStringValue("holidayName");
                bool? isWeekend = schedObj.GetNullableValue<bool>("isWeekend");
                uint? seatConfigurationId = schedObj.GetNullableValue<uint>("seatConfigurationId");
                bool? requiresSeatReservation = schedObj.GetNullableValue<bool>("requiresSeatReservation");
                string? routeType = schedObj.GetStringValue("routeType");

                return new RouteSchedule
                {
                    ScheduleId = scheduleId,
                    RouteId = routeId,
                    StartPoint = startPoint,
                    RouteStops = routeStops,
                    EndPoint = endPoint,
                    DepartureTime = departureTime,
                    ArrivalTime = arrivalTime,
                    Price = price,
                    AvailableSeats = availableSeats,
                    SeatedCapacity = seatedCapacity,
                    StandingCapacity = standingCapacity,
                    DaysOfWeek = daysOfWeek,
                    BusTypes = busTypes,
                    IsActive = isActive,
                    ValidFrom = validFrom,
                    ValidUntil = validUntil,
                    StopDurationMinutes = stopDurationMinutes,
                    IsRecurring = isRecurring,
                    EstimatedStopTimes = estimatedStopTimes,
                    StopDistances = stopDistances,
                    Notes = notes,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    UpdatedBy = updatedBy,
                    PeakHourLoad = peakHourLoad,
                    OffPeakHourLoad = offPeakHourLoad,
                    IsSpecialEvent = isSpecialEvent,
                    SpecialEventName = specialEventName,
                    IsHoliday = isHoliday,
                    HolidayName = holidayName,
                    IsWeekend = isWeekend,
                    SeatConfigurationId = seatConfigurationId,
                    RequiresSeatReservation = requiresSeatReservation,
                    RouteType = routeType
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse RouteSchedule object from JSON: {Json}", schedObj.ToJsonString());
                return null;
            }
        }

        /// <summary>
        /// Parses a Job object from a JsonObject, mapping ALL fields.
        /// </summary>
        public static Job? ParseJob(this JsonObject jobObj)
        {
            try
            {
                uint jobId = jobObj.GetValue<uint>("jobId", 0);
                if (jobId == 0)
                {
                    Log.Warning("Job object has JobId 0, skipping");
                    return null;
                }

                string jobTitle = jobObj.GetStringValue("jobTitle") ?? string.Empty;

                // Optional fields
                string? internship = jobObj.GetStringValue("internship");
                double? baseSalary = jobObj.GetNullableValue<double>("baseSalary");
                string? department = jobObj.GetStringValue("department");
                string? jobDescription = jobObj.GetStringValue("jobDescription");
                uint? requiredExperience = jobObj.GetNullableValue<uint>("requiredExperience");
                List<string>? requiredSkills = jobObj.GetStringArray("requiredSkills")?.ToList();
                List<string>? requiredCertifications = jobObj.GetStringArray("requiredCertifications")?.ToList();
                string? educationRequirements = jobObj.GetStringValue("educationRequirements");
                string? workSchedule = jobObj.GetStringValue("workSchedule");
                bool? isFullTime = jobObj.GetNullableValue<bool>("isFullTime");
                bool? isPartTime = jobObj.GetNullableValue<bool>("isPartTime");
                bool? isShiftWork = jobObj.GetNullableValue<bool>("isShiftWork");
                List<string>? benefits = jobObj.GetStringArray("benefits")?.ToList();
                string? reportingTo = jobObj.GetStringValue("reportingTo");
                uint? vacationDays = jobObj.GetNullableValue<uint>("vacationDays");
                uint? sickDays = jobObj.GetNullableValue<uint>("sickDays");
                string? performanceMetrics = jobObj.GetStringValue("performanceMetrics");

                return new Job
                {
                    JobId = jobId,
                    JobTitle = jobTitle,
                    Internship = internship,
                    BaseSalary = baseSalary,
                    Department = department,
                    JobDescription = jobDescription,
                    RequiredExperience = requiredExperience,
                    RequiredSkills = requiredSkills,
                    RequiredCertifications = requiredCertifications,
                    EducationRequirements = educationRequirements,
                    WorkSchedule = workSchedule,
                    IsFullTime = isFullTime,
                    IsPartTime = isPartTime,
                    IsShiftWork = isShiftWork,
                    Benefits = benefits,
                    ReportingTo = reportingTo,
                    VacationDays = vacationDays,
                    SickDays = sickDays,
                    PerformanceMetrics = performanceMetrics
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse Job object from JSON: {Json}", jobObj.ToJsonString());
                return null;
            }
        }

        /// <summary>
        /// Parses a UserProfile object from a JsonObject, mapping ALL fields.
        /// </summary>
        public static UserProfile? ParseUserProfile(this JsonObject userObj)
        {
            try
            {
                // UserProfile uses Identity as primary key, which is a special type
                // Server sends it as hex string in "UserId" field (uppercase U)
                string? userIdStr = userObj.GetStringValue("UserId");
                if (string.IsNullOrEmpty(userIdStr))
                {
                    Log.Warning("UserProfile object has null/empty UserId, skipping. JSON: {Json}", userObj.ToJsonString());
                    return null;
                }

                // Parse Identity from hex string
                SpacetimeDB.Identity userId;
                try
                {
                    // Convert hex string to byte array
                    byte[] bytes = Convert.FromHexString(userIdStr);
                    userId = new SpacetimeDB.Identity(bytes);
                    Log.Verbose("Parsed UserId from hex string: {UserIdHex}", userIdStr);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to parse UserId hex string '{UserIdHex}' for user", userIdStr);
                    return null;
                }

                uint legacyUserId = userObj.GetValue<uint>("LegacyUserId", 0);
                string login = userObj.GetStringValue("Login") ?? string.Empty;
                bool isActive = userObj.GetValue<bool>("IsActive", false);
                ulong createdAt = userObj.GetValue<ulong>("CreatedAt", 0);

                // Optional fields - match server's PascalCase naming
                string? passwordHash = userObj.GetStringValue("PasswordHash");
                string? email = userObj.GetStringValue("Email");
                string? phoneNumber = userObj.GetStringValue("PhoneNumber");
                ulong? lastLoginAt = userObj.GetNullableValue<ulong>("LastLoginAt");
                string? legacyGuid = userObj.GetStringValue("LegacyGuid");
                bool? emailConfirmed = userObj.GetNullableValue<bool>("EmailConfirmed");
                double? xuid = userObj.GetNullableValue<double>("Xuid");
                bool? phoneNumberConfirmed = userObj.GetNullableValue<bool>("PhoneNumberConfirmed");

                Log.Verbose("Parsed UserProfile: UserId={UserId}, LegacyUserId={LegacyUserId}, Login='{Login}', Email='{Email}', Active={Active}",
                    userIdStr, legacyUserId, login, email, isActive);

                return new UserProfile
                {
                    UserId = userId,
                    LegacyUserId = legacyUserId,
                    Xuid = xuid,
                    Login = login,
                    PasswordHash = passwordHash,
                    Email = email,
                    PhoneNumber = phoneNumber,
                    IsActive = isActive,
                    CreatedAt = createdAt,
                    LastLoginAt = lastLoginAt,
                    LegacyGuid = legacyGuid,
                    EmailConfirmed = emailConfirmed,
                    PhoneNumberConfirmed = phoneNumberConfirmed
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse UserProfile object from JSON: {Json}", userObj.ToJsonString());
                return null;
            }
        }

        /// <summary>
        /// Parses a Role object from a JsonObject, mapping ALL fields.
        /// </summary>
        public static Role? ParseRole(this JsonObject roleObj)
        {
            try
            {
                uint roleId = roleObj.GetValue<uint>("roleId", 0);
                if (roleId == 0)
                {
                    Log.Warning("Role object has RoleId 0, skipping");
                    return null;
                }

                int legacyRoleId = roleObj.GetValue<int>("legacyRoleId", 0);
                string name = roleObj.GetStringValue("name") ?? string.Empty;
                string description = roleObj.GetStringValue("description") ?? string.Empty;
                bool isSystem = roleObj.GetValue<bool>("isSystem", false);
                uint priority = roleObj.GetValue<uint>("priority", 0);
                bool isActive = roleObj.GetValue<bool>("isActive", false);
                ulong createdAt = roleObj.GetValue<ulong>("createdAt", 0);
                ulong updatedAt = roleObj.GetValue<ulong>("updatedAt", 0);

                // Optional fields
                string? createdBy = roleObj.GetStringValue("createdBy");
                string? updatedBy = roleObj.GetStringValue("updatedBy");
                string? normalizedName = roleObj.GetStringValue("normalizedName");

                return new Role
                {
                    RoleId = roleId,
                    LegacyRoleId = legacyRoleId,
                    Name = name,
                    Description = description,
                    IsSystem = isSystem,
                    Priority = priority,
                    IsActive = isActive,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    CreatedBy = createdBy,
                    UpdatedBy = updatedBy,
                    NormalizedName = normalizedName
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse Role object from JSON: {Json}", roleObj.ToJsonString());
                return null;
            }
        }

        /// <summary>
        /// Parses a Permission object from a JsonObject, mapping ALL fields.
        /// </summary>
        public static Permission? ParsePermission(this JsonObject permObj)
        {
            try
            {
                uint permissionId = permObj.GetValue<uint>("permissionId", 0);
                if (permissionId == 0)
                {
                    Log.Warning("Permission object has PermissionId 0, skipping");
                    return null;
                }

                string name = permObj.GetStringValue("name") ?? string.Empty;
                string description = permObj.GetStringValue("description") ?? string.Empty;
                string category = permObj.GetStringValue("category") ?? string.Empty;
                bool isActive = permObj.GetValue<bool>("isActive", false);
                ulong createdAt = permObj.GetValue<ulong>("createdAt", 0);

                return new Permission
                {
                    PermissionId = permissionId,
                    Name = name,
                    Description = description,
                    Category = category,
                    IsActive = isActive,
                    CreatedAt = createdAt
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse Permission object from JSON: {Json}", permObj.ToJsonString());
                return null;
            }
        }
    }
}