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


        [HttpGet("realtime/ws")]
        public async Task StreamRealtimeBusEvents(CancellationToken cancellationToken)
        {
            if (!IsAuthenticated())
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await WebSocketEventStreamWriter.StreamCrudSessionAsync(
                HttpContext,
                _realtimeEventBus.SubscribeAsync("buses", cancellationToken),
                HandleRealtimeCrudAsync,
                _logger,
                cancellationToken);
        }

        private async Task<object> HandleRealtimeCrudAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
        {
            var command = (request.Command ?? string.Empty).Trim().ToLowerInvariant();

            return command switch
            {
                "read_all" => new { buses = await _busService.GetAllBusesAsync() },
                "read" => new { bus = await _busService.GetBusByIdAsync(request.Id ?? throw new InvalidOperationException("id is required for read")) },
                "create" => await HandleCreateCommandAsync(request),
                "update" => await HandleUpdateCommandAsync(request),
                "delete" => await HandleDeleteCommandAsync(request),
                _ => throw new InvalidOperationException($"Unsupported command '{request.Command}'")
            };
        }

        private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
        {
            var model = request.Payload?.Deserialize<CreateBusModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for create");

            var actionResult = await CreateBus(model);
            var snapshot = await _busService.GetAllBusesAsync();
            return new { operation = "create", actionResult = ControllerActionResultMapper.Map(actionResult.Result ?? new OkObjectResult(actionResult.Value)), snapshot };
        }

        private async Task<object> HandleUpdateCommandAsync(RealtimeCrudRequest request)
        {
            var id = request.Id ?? throw new InvalidOperationException("id is required for update");
            var model = request.Payload?.Deserialize<UpdateBusModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for update");

            var actionResult = await UpdateBus(id, model);
            var snapshot = await _busService.GetAllBusesAsync();
            return new { operation = "update", actionResult = ControllerActionResultMapper.Map(actionResult), snapshot };
        }

        private async Task<object> HandleDeleteCommandAsync(RealtimeCrudRequest request)
        {
            var id = request.Id ?? throw new InvalidOperationException("id is required for delete");
            var actionResult = await DeleteBus(id);
            var snapshot = await _busService.GetAllBusesAsync();
            return new { operation = "delete", actionResult = ControllerActionResultMapper.Map(actionResult), snapshot };
        }
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
