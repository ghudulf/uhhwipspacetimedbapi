using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Microsoft.Extensions.Logging;
using TicketSalesApp.Services.Interfaces;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Text.Json;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allows both custom JWT (manual parsing) and ASP.NET Core auth (OpenIddict)
    public class BusesController : BaseController
    {
        private readonly IBusService _busService;
        private readonly IAdminActionLogger _adminLogger;
        private readonly ILogger<BusesController> _logger;
        private readonly IRealtimeEventBus _realtimeEventBus;

        /// <summary>
        /// Initializes a new instance of <see cref="BusesController"/> with its required services.
        /// </summary>
        /// <param name="busService">Service for bus data access and operations.</param>
        /// <param name="adminLogger">Service for recording administrative actions.</param>
        /// <param name="logger">Logger for controller diagnostics.</param>
        /// <param name="realtimeEventBus">Pub/sub bus for subscribing and publishing realtime events.</param>
        /// <exception cref="ArgumentNullException">Thrown if any provided dependency is null.</exception>
        public BusesController(
            IBusService busService,
            IAdminActionLogger adminLogger,
            ILogger<BusesController> logger,
            IRealtimeEventBus realtimeEventBus)
        {
            _busService = busService ?? throw new ArgumentNullException(nameof(busService));
            _adminLogger = adminLogger ?? throw new ArgumentNullException(nameof(adminLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
        }


        /// <summary>
        /// Streams real-time CRUD events for buses over a WebSocket connection.
        /// </summary>
        /// <remarks>
        /// If the caller is not authenticated, the method sets the response status to 401 Unauthorized and returns without starting a stream.
        /// </remarks>
        /// <param name="cancellationToken">Token used to cancel the streaming session and associated subscriptions.</param>
        [HttpGet("realtime/ws")]
        public async Task StreamRealtimeBusEvents(CancellationToken cancellationToken)
        {
            if (!IsAuthenticated())
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!IsAdmin() && !HasPermission("buses.view"))
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await WebSocketEventStreamWriter.StreamCrudSessionAsync(
                HttpContext,
                _realtimeEventBus.SubscribeAsync("buses", cancellationToken),
                HandleRealtimeCrudAsync,
                _logger,
                cancellationToken);
        }

        /// <summary>
        /// Dispatches a realtime CRUD request to the corresponding handler based on the request's Command.
        /// </summary>
        /// <param name="request">The realtime CRUD request containing the command, optional Id, and optional payload.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
        /// <returns>An object containing the command-specific response (e.g., snapshot, entity, operation result).</returns>
        /// <exception cref="InvalidOperationException">Thrown when request.Command is not one of: "read_all", "read", "create", "update", or "delete".</exception>
        private async Task<object> HandleRealtimeCrudAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
        {
            var command = (request.Command ?? string.Empty).Trim().ToLowerInvariant();

            return command switch
            {
                "read_all" => await HandleReadAllCommandAsync(),
                "read" => await HandleReadCommandAsync(request),
                "create" => await HandleCreateCommandAsync(request),
                "update" => await HandleUpdateCommandAsync(request),
                "delete" => await HandleDeleteCommandAsync(request),
                _ => throw new InvalidOperationException($"Unsupported command '{request.Command}'")
            };
        }

        /// <summary>
        /// Retrieve a snapshot of all buses for realtime "read_all" requests, enforcing admin or "buses.view" permission.
        /// </summary>
        /// <returns>An object with a `buses` property containing the collection of all buses.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin and does not have the "buses.view" permission.</exception>
        private async Task<object> HandleReadAllCommandAsync()
        {
            if (!IsAdmin() && !HasPermission("buses.view"))
            {
                throw new UnauthorizedAccessException("Not authorized for buses.view");
            }

            var buses = await _busService.GetAllBusesAsync();
            var mapped = buses.Select(b => new {
                b.BusId,
                b.Model,
                b.RegistrationNumber,
                b.Capacity,
                b.BusType,
                b.Year,
                b.Vin,
                b.LicensePlate,
                b.CurrentStatus,
                b.IsActive,
                b.SeatedCapacity,
                b.StandingCapacity,
                b.CurrentLocation,
                b.LastLocationUpdate,
                b.FuelConsumption,
                b.CurrentFuelLevel,
                b.FuelType,
                b.MileageTotal,
                b.MileageSinceService,
                b.HasAccessibility,
                b.HasAirConditioning,
                b.HasWifi,
                b.HasUsbCharging
            }).ToList();
            return new { buses = mapped };
        }

        /// <summary>
        /// Handle a realtime "read" CRUD request and return the bus matching the provided request Id.
        /// </summary>
        /// <param name="request">The realtime CRUD request; its <see cref="RealtimeCrudRequest.Id"/> must be provided.</param>
        /// <returns>An object with a single property `bus` containing the requested bus entity (or `null` if not found).</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks admin rights and the "buses.view" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request does not include an Id.</exception>
        private async Task<object> HandleReadCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("buses.view"))
            {
                throw new UnauthorizedAccessException("Not authorized for buses.view");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for read");
            return new { bus = await _busService.GetBusByIdAsync(id) };
        }

        /// <summary>
        /// Handles a realtime "create" CRUD request by creating a new bus from the request payload and returning the operation result with a full snapshot of buses.
        /// </summary>
        /// <param name="request">The realtime CRUD request whose Payload must deserialize to a CreateBusModel.</param>
        /// <returns>
        /// An object containing:
        /// - operation: the string "create",
        /// - success: `true` if the bus was created, `false` otherwise,
        /// - entity: the created bus entity (or null on failure),
        /// - snapshot: the current list of all buses.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin and lacks the "buses.create" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request Payload is missing or cannot be deserialized into CreateBusModel.</exception>
        private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("buses.create"))
            {
                throw new UnauthorizedAccessException("Not authorized for buses.create");
            }

            var model = request.Payload?.Deserialize<CreateBusModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for create");

            var bus = await _busService.CreateBusAsync(model.Model);
            var mappedEntity = bus != null ? new {
                bus.BusId,
                bus.Model,
                bus.RegistrationNumber,
                bus.Capacity,
                bus.BusType,
                bus.Year,
                bus.Vin,
                bus.LicensePlate,
                bus.CurrentStatus,
                bus.IsActive,
                bus.SeatedCapacity,
                bus.StandingCapacity,
                bus.CurrentLocation,
                bus.LastLocationUpdate,
                bus.FuelConsumption,
                bus.CurrentFuelLevel,
                bus.FuelType,
                bus.MileageTotal,
                bus.MileageSinceService,
                bus.HasAccessibility,
                bus.HasAirConditioning,
                bus.HasWifi,
                bus.HasUsbCharging
            } : null;
            var result = new { operation = "create", success = bus is not null, entity = mappedEntity };

            if (bus is not null)
            {
                // Log admin action
                var userId = GetUserId();
                if (userId != null)
                {
                    await _adminLogger.LogActionAsync(
                        userId,
                        "CreateBus",
                        $"Created bus {model.Model} with ID {bus.BusId}");
                }

                await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                    EventName: "bus.created",
                    Resource: "buses",
                    HttpMethod: "POST",
                    StatusCode: 201,
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: Guid.NewGuid().ToString(),
                    UserId: null,
                    UserName: null,
                    Tenant: null,
                    SourceIp: "internal",
                    Metadata: new Dictionary<string, string> { ["operation"] = "create", ["success"] = "true" }
                ));
            }

            return result;
        }

        /// <summary>
        /// Handle an incoming realtime "update" CRUD request for buses.
        /// </summary>
        /// <param name="request">Realtime CRUD request containing the target <c>Id</c> and a JSON <c>Payload</c> deserializable to <see cref="UpdateBusModel"/>.</param>
        /// <returns>An object with keys: <c>operation</c> (string "update"), <c>success</c> (bool), <c>entity</c> (the updated bus or null), and <c>snapshot</c> (the current list of all buses).</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin and lacks the "buses.edit" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request is missing <c>Id</c> or <c>Payload</c>.</exception>
        private async Task<object> HandleUpdateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("buses.edit"))
            {
                throw new UnauthorizedAccessException("Not authorized for buses.edit");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for update");
            var model = request.Payload?.Deserialize<UpdateBusModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for update");

            var success = await _busService.UpdateBusAsync(id, model.Model);
            var entity = await _busService.GetBusByIdAsync(id);
            var mappedEntity = entity != null ? new {
                entity.BusId,
                entity.Model,
                entity.RegistrationNumber,
                entity.Capacity,
                entity.BusType,
                entity.Year,
                entity.Vin,
                entity.LicensePlate,
                entity.CurrentStatus,
                entity.IsActive,
                entity.SeatedCapacity,
                entity.StandingCapacity,
                entity.CurrentLocation,
                entity.LastLocationUpdate,
                entity.FuelConsumption,
                entity.CurrentFuelLevel,
                entity.FuelType,
                entity.MileageTotal,
                entity.MileageSinceService,
                entity.HasAccessibility,
                entity.HasAirConditioning,
                entity.HasWifi,
                entity.HasUsbCharging
            } : null;
            var result = new { operation = "update", success, entity = mappedEntity };

            if (success)
            {
                // Log admin action
                var userId = GetUserId();
                if (userId != null && entity != null)
                {
                    await _adminLogger.LogActionAsync(
                        userId,
                        "UpdateBus",
                        $"Updated bus {model.Model} with ID {id}");
                }

                await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                    EventName: "bus.updated",
                    Resource: "buses",
                    HttpMethod: "PUT",
                    StatusCode: 200,
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: Guid.NewGuid().ToString(),
                    UserId: null,
                    UserName: null,
                    Tenant: null,
                    SourceIp: "internal",
                    Metadata: new Dictionary<string, string> { ["operation"] = "update", ["success"] = success.ToString() }
                ));
            }

            return result;
        }

        /// <summary>
        /// Handle a realtime "delete" CRUD request for buses, performing authorization, deletion, and returning a post-operation snapshot.
        /// </summary>
        /// <param name="request">The realtime CRUD request. Must contain an <c>Id</c> of the bus to delete.</param>
        /// <returns>
        /// An object with the result of the operation:
        /// - <c>operation</c>: the string "delete";
        /// - <c>success</c>: a boolean indicating whether deletion succeeded;
        /// - <c>deletedId</c>: the id of the deleted entity;
        /// - <c>snapshot</c>: the current collection of buses after the operation.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin and lacks the "buses.delete" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request does not include a required <c>Id</c>.</exception>
        private async Task<object> HandleDeleteCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("buses.delete"))
            {
                throw new UnauthorizedAccessException("Not authorized for buses.delete");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for delete");
            var success = await _busService.DeleteBusAsync(id);
            var result = new { operation = "delete", success, deletedId = id };

            if (success)
            {
                // Log admin action
                var userId = GetUserId();
                if (userId != null)
                {
                    await _adminLogger.LogActionAsync(
                        userId,
                        "DeleteBus",
                        $"Deleted bus with ID {id}");
                }

                await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                    EventName: "bus.deleted",
                    Resource: "buses",
                    HttpMethod: "DELETE",
                    StatusCode: 200,
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: Guid.NewGuid().ToString(),
                    UserId: null,
                    UserName: null,
                    Tenant: null,
                    SourceIp: "internal",
                    Metadata: new Dictionary<string, string> { ["operation"] = "delete", ["success"] = success.ToString(), ["deletedId"] = id.ToString() }
                ));
            }

            return result;
        }

        /// <summary>
        /// Retrieves all buses and returns them mapped to a client-facing JSON-friendly representation.
        /// </summary>
        /// <returns>
        /// An ActionResult containing a list of bus representations (objects with fields such as BusId, Model, RegistrationNumber, Capacity, BusType, Year, Vin, LicensePlate, CurrentStatus, IsActive, SeatedCapacity, StandingCapacity, CurrentLocation, LastLocationUpdate, FuelConsumption, CurrentFuelLevel, FuelType, MileageTotal, MileageSinceService, HasAccessibility, HasAirConditioning, HasWifi, and HasUsbCharging).
        /// Returns 200 OK with the list on success, 403 Forbidden if the caller lacks permission, or 500 Internal Server Error if an unexpected error occurs.
        /// </returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetBuses()
        {
            _logger.LogInformation("REQUEST RECEIVED: GetBuses");
            
            try
            {
                if (!IsAdmin() && !HasPermission("buses.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view buses");
                    return Forbid();
                }

                _logger.LogInformation("DATABASE OPERATION: GetAllBuses");
                var buses = await _busService.GetAllBusesAsync();
                
                _logger.LogInformation("DATABASE RESULT: GetAllBuses - Retrieved {BusCount} buses", buses.Count());
                
                // Map to anonymous type - CRITICAL: This converts SpacetimeDB structure to valid JSON
                // Include ALL fields that the client needs
                var result = buses.Select(b => new {
                    b.BusId,
                    b.Model,
                    b.RegistrationNumber,
                    b.Capacity,
                    b.BusType,
                    b.Year,
                    b.Vin,
                    b.LicensePlate,
                    b.CurrentStatus,
                    b.IsActive,
                    b.SeatedCapacity,
                    b.StandingCapacity,
                    b.CurrentLocation,
                    b.LastLocationUpdate,
                    b.FuelConsumption,
                    b.CurrentFuelLevel,
                    b.FuelType,
                    b.MileageTotal,
                    b.MileageSinceService,
                    b.HasAccessibility,
                    b.HasAirConditioning,
                    b.HasWifi,
                    b.HasUsbCharging
                }).ToList();

                _logger.LogInformation("FULL BUS DATA: {BusData}", JsonSerializer.Serialize(result));
                
                foreach (var bus in result)
                {
                    _logger.LogDebug("Bus ID: {BusId}, Model: {Model}, Year: {Year}, Type: {BusType}", 
                        bus.BusId, bus.Model, bus.Year, bus.BusType);
                }
                
                _logger.LogInformation("RESPONSE SENT: Returning {BusCount} buses to client", result.Count());
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving buses");
                return StatusCode(500, "An error occurred while retrieving buses");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetBus(uint id)
        {
            _logger.LogInformation("REQUEST RECEIVED: GetBus with ID {BusId}", id);
            
            try
            {
                if (!IsAdmin() && !HasPermission("buses.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view bus {BusId}", id);
                    return Forbid();
                }

                _logger.LogInformation("DATABASE OPERATION: Fetching bus with ID {BusId}", id);
                var bus = await _busService.GetBusByIdAsync(id);
                
                if (bus == null)
                {
                    _logger.LogWarning("DATABASE RESULT: Bus with ID {BusId} not found", id);
                    return NotFound();
                }

                // Map to anonymous type
                var result = new {
                    bus.BusId,
                    bus.Model,
                    bus.RegistrationNumber,
                    bus.Capacity,
                 
                    bus.IsActive
                };

                _logger.LogInformation("DATABASE RESULT: Successfully retrieved bus with ID {BusId}", id);
                _logger.LogInformation("FULL BUS DATA: {BusData}", JsonSerializer.Serialize(result));
                _logger.LogInformation("RESPONSE SENT: Bus details for ID {BusId}, Model: {Model}", 
                    result.BusId, result.Model);
                
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving bus with ID {BusId}", id);
                return StatusCode(500, $"An error occurred while retrieving bus with ID {id}");
            }
        }

        [HttpPost]
        public async Task<ActionResult<dynamic>> CreateBus([FromBody] CreateBusModel model)
        {
            _logger.LogInformation("REQUEST RECEIVED: CreateBus with data: {RequestData}", JsonSerializer.Serialize(model));
            
            if (!IsAdmin() && !HasPermission("buses.create"))
            {
                _logger.LogWarning("Unauthorized attempt to create bus");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("DATABASE OPERATION: Creating new bus with model {Model}", model.Model);
                
                var bus = await _busService.CreateBusAsync(model.Model);
                if (bus == null)
                {
                    _logger.LogWarning("DATABASE RESULT: Failed to create bus with model {Model}", model.Model);
                    return StatusCode(500, "Failed to create bus");
                }

                _logger.LogInformation("DATABASE RESULT: Successfully created bus with ID {BusId}", bus.BusId);
                _logger.LogInformation("FULL BUS DATA CREATED: {BusData}", JsonSerializer.Serialize(bus));

                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "CreateBus",
                    $"Created bus with model {model.Model}, ID: {bus.BusId}"
                );

                _logger.LogInformation("RESPONSE SENT: Created bus with ID {BusId}, Model: {Model}", 
                    bus.BusId, bus.Model);
                
                // Return the created bus as JSON with 201 status
                return StatusCode(201, new {
                    bus.BusId,
                    bus.Model,
                    bus.RegistrationNumber,
                    bus.Capacity,
                    bus.IsActive
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating bus with model {Model}", model.Model);
                return StatusCode(500, $"An error occurred while creating bus: {ex.Message}");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateBus(uint id, [FromBody] UpdateBusModel model)
        {
            _logger.LogInformation("REQUEST RECEIVED: UpdateBus ID {BusId} with data: {RequestData}", 
                id, JsonSerializer.Serialize(model));
            
            if (!IsAdmin() && !HasPermission("buses.edit"))
            {
                _logger.LogWarning("Unauthorized attempt to update bus");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("DATABASE OPERATION: Updating bus with ID {BusId}, New Model: {Model}", 
                    id, model.Model ?? "unchanged");
                
                var success = await _busService.UpdateBusAsync(id, model.Model);
                if (!success)
                {
                    _logger.LogWarning("DATABASE RESULT: Bus with ID {BusId} not found for update", id);
                    return NotFound();
                }

                _logger.LogInformation("DATABASE RESULT: Successfully updated bus with ID {BusId}", id);

                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "UpdateBus",
                    $"Updated bus with ID {id}, Model: {model.Model ?? "unchanged"}"
                );

                _logger.LogInformation("RESPONSE SENT: Updated bus with ID {BusId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating bus with ID {BusId}", id);
                return StatusCode(500, $"An error occurred while updating bus: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBus(uint id)
        {
            _logger.LogInformation("REQUEST RECEIVED: DeleteBus ID {BusId}", id);
            
            if (!IsAdmin() && !HasPermission("buses.delete"))
            {
                _logger.LogWarning("Unauthorized attempt to delete bus");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("DATABASE OPERATION: Deleting bus with ID {BusId}", id);
                
                // Get bus data before deletion for logging
                var busBeforeDeletion = await _busService.GetBusByIdAsync(id);
                if (busBeforeDeletion != null)
                {
                    _logger.LogInformation("BUS TO BE DELETED: {BusData}", JsonSerializer.Serialize(busBeforeDeletion));
                }
                
                var success = await _busService.DeleteBusAsync(id);
                if (!success)
                {
                    _logger.LogWarning("DATABASE RESULT: Bus with ID {BusId} not found for deletion", id);
                    return NotFound();
                }

                _logger.LogInformation("DATABASE RESULT: Successfully deleted bus with ID {BusId}", id);

                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "DeleteBus",
                    $"Deleted bus with ID {id}"
                );

                _logger.LogInformation("RESPONSE SENT: Deleted bus with ID {BusId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting bus with ID {BusId}", id);
                return StatusCode(500, $"An error occurred while deleting bus: {ex.Message}");
            }
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<dynamic>>> SearchBuses(
            [FromQuery] string? model = null,
            [FromQuery] string? serviceStatus = null)
        {
            _logger.LogInformation("REQUEST RECEIVED: SearchBuses with parameters - Model: {Model}, ServiceStatus: {Status}", 
                model ?? "any", serviceStatus ?? "any");
            
            try
            {
                if (!IsAdmin() && !HasPermission("buses.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to search buses");
                    return Forbid();
                }

                _logger.LogInformation("DATABASE OPERATION: Searching buses with model: {Model}, service status: {ServiceStatus}", 
                    model ?? "any", serviceStatus ?? "any");
                
                var buses = await _busService.SearchBusesAsync(model, serviceStatus);
                
                // Map to anonymous type
                var result = buses.Select(b => new {
                    b.BusId,
                    b.Model,
                    b.RegistrationNumber,
                    b.Capacity,
             
                    b.IsActive
                }).ToList();

                _logger.LogInformation("DATABASE RESULT: Found {ResultCount} buses matching search criteria", result.Count());
                _logger.LogInformation("FULL SEARCH RESULTS: {BusData}", JsonSerializer.Serialize(result));
                
                foreach (var bus in result)
                {
                    _logger.LogDebug("Search Result - Bus ID: {BusId}, Model: {Model}", 
                        bus.BusId, bus.Model);
                }
                
                _logger.LogInformation("RESPONSE SENT: Returning {ResultCount} buses matching search criteria", result.Count());
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching buses");
                return StatusCode(500, "An error occurred while searching buses");
            }
        }

        [HttpPost("{id}/activate")]
        public async Task<IActionResult> ActivateBus(uint id)
        {
            _logger.LogInformation("REQUEST RECEIVED: ActivateBus ID {BusId}", id);
            
            if (!IsAdmin() && !HasPermission("buses.edit"))
            {
                _logger.LogWarning("Unauthorized attempt to activate bus");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("DATABASE OPERATION: Activating bus with ID {BusId}", id);
                
                // Get bus data before activation for logging
                var busBeforeActivation = await _busService.GetBusByIdAsync(id);
                if (busBeforeActivation != null)
                {
                    _logger.LogInformation("BUS BEFORE ACTIVATION: {BusData}", JsonSerializer.Serialize(busBeforeActivation));
                }
                
                var success = await _busService.ActivateBusAsync(id);
                if (!success)
                {
                    _logger.LogWarning("DATABASE RESULT: Bus with ID {BusId} not found for activation", id);
                    return NotFound();
                }

                // Get bus data after activation for logging
                var busAfterActivation = await _busService.GetBusByIdAsync(id);
                if (busAfterActivation != null)
                {
                    _logger.LogInformation("BUS AFTER ACTIVATION: {BusData}", JsonSerializer.Serialize(busAfterActivation));
                }

                _logger.LogInformation("DATABASE RESULT: Successfully activated bus with ID {BusId}", id);

                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "ActivateBus",
                    $"Activated bus with ID {id}"
                );

                _logger.LogInformation("RESPONSE SENT: Activated bus with ID {BusId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating bus with ID {BusId}", id);
                return StatusCode(500, $"An error occurred while activating bus: {ex.Message}");
            }
        }

        [HttpPost("{id}/deactivate")]
        public async Task<IActionResult> DeactivateBus(uint id)
        {
            _logger.LogInformation("REQUEST RECEIVED: DeactivateBus ID {BusId}", id);
            
            if (!IsAdmin() && !HasPermission("buses.edit"))
            {
                _logger.LogWarning("Unauthorized attempt to deactivate bus");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("DATABASE OPERATION: Deactivating bus with ID {BusId}", id);
                
                // Get bus data before deactivation for logging
                var busBeforeDeactivation = await _busService.GetBusByIdAsync(id);
                if (busBeforeDeactivation != null)
                {
                    _logger.LogInformation("BUS BEFORE DEACTIVATION: {BusData}", JsonSerializer.Serialize(busBeforeDeactivation));
                }
                
                var success = await _busService.DeactivateBusAsync(id);
                if (!success)
                {
                    _logger.LogWarning("DATABASE RESULT: Bus with ID {BusId} not found for deactivation", id);
                    return NotFound();
                }

                // Get bus data after deactivation for logging
                var busAfterDeactivation = await _busService.GetBusByIdAsync(id);
                if (busAfterDeactivation != null)
                {
                    _logger.LogInformation("BUS AFTER DEACTIVATION: {BusData}", JsonSerializer.Serialize(busAfterDeactivation));
                }

                _logger.LogInformation("DATABASE RESULT: Successfully deactivated bus with ID {BusId}", id);

                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "DeactivateBus",
                    $"Deactivated bus with ID {id}"
                );

                _logger.LogInformation("RESPONSE SENT: Deactivated bus with ID {BusId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating bus with ID {BusId}", id);
                return StatusCode(500, $"An error occurred while deactivating bus: {ex.Message}");
            }
        }
    }

    public class CreateBusModel
    {
        public required string Model { get; set; }
    }

    public class UpdateBusModel
    {
        public string? Model { get; set; }
    }
} 