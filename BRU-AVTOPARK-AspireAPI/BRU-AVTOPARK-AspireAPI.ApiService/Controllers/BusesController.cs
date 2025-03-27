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

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Allow all authenticated users to read
    public class BusesController : BaseController
    {
        private readonly IBusService _busService;
        private readonly IAdminActionLogger _adminLogger;
        private readonly ILogger<BusesController> _logger;

        public BusesController(
            IBusService busService,
            IAdminActionLogger adminLogger,
            ILogger<BusesController> logger)
        {
            _busService = busService ?? throw new ArgumentNullException(nameof(busService));
            _adminLogger = adminLogger ?? throw new ArgumentNullException(nameof(adminLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Bus>>> GetBuses()
        {
            try
            {
                if (!IsAdmin() && !HasPermission("buses.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view buses");
                    return Forbid();
                }

                _logger.LogInformation("Fetching all buses");
                var buses = await _busService.GetAllBusesAsync();
                _logger.LogDebug("Retrieved {BusCount} buses", buses.Count());
                return Ok(buses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving buses");
                return StatusCode(500, "An error occurred while retrieving buses");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Bus>> GetBus(uint id)
        {
            try
            {
                if (!IsAdmin() && !HasPermission("buses.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view bus {BusId}", id);
                    return Forbid();
                }

                _logger.LogInformation("Fetching bus with ID {BusId}", id);
                var bus = await _busService.GetBusByIdAsync(id);
                if (bus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found", id);
                    return NotFound();
                }

                _logger.LogDebug("Successfully retrieved bus with ID {BusId}", id);
                return Ok(bus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving bus with ID {BusId}", id);
                return StatusCode(500, $"An error occurred while retrieving bus with ID {id}");
            }
        }

        [HttpPost]
        public async Task<ActionResult<Bus>> CreateBus([FromBody] CreateBusModel model)
        {
            if (!IsAdmin() && !HasPermission("buses.create"))
            {
                _logger.LogWarning("Unauthorized attempt to create bus");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Creating new bus with model {Model}", model.Model);
                
                var bus = await _busService.CreateBusAsync(model.Model);
                if (bus == null)
                {
                    _logger.LogWarning("Failed to create bus");
                    return StatusCode(500, "Failed to create bus");
                }

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

                _logger.LogInformation("Successfully created bus with ID {BusId}", bus.BusId);
                return CreatedAtAction(nameof(GetBus), new { id = bus.BusId }, bus);
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
            if (!IsAdmin() && !HasPermission("buses.edit"))
            {
                _logger.LogWarning("Unauthorized attempt to update bus");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Updating bus with ID {BusId}", id);
                
                var success = await _busService.UpdateBusAsync(id, model.Model);
                if (!success)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for update", id);
                    return NotFound();
                }

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

                _logger.LogInformation("Successfully updated bus with ID {BusId}", id);
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
            if (!IsAdmin() && !HasPermission("buses.delete"))
            {
                _logger.LogWarning("Unauthorized attempt to delete bus");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Deleting bus with ID {BusId}", id);
                
                var success = await _busService.DeleteBusAsync(id);
                if (!success)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for deletion", id);
                    return NotFound();
                }

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

                _logger.LogInformation("Successfully deleted bus with ID {BusId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting bus with ID {BusId}", id);
                return StatusCode(500, $"An error occurred while deleting bus: {ex.Message}");
            }
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<Bus>>> SearchBuses(
            [FromQuery] string? model = null,
            [FromQuery] string? serviceStatus = null)
        {
            try
            {
                if (!IsAdmin() && !HasPermission("buses.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to search buses");
                    return Forbid();
                }

                _logger.LogInformation("Searching buses with model: {Model}, service status: {ServiceStatus}", 
                    model ?? "any", serviceStatus ?? "any");
                
                var results = await _busService.SearchBusesAsync(model, serviceStatus);
                
                _logger.LogDebug("Found {ResultCount} buses matching search criteria", results.Count());
                return Ok(results);
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
            if (!IsAdmin() && !HasPermission("buses.edit"))
            {
                _logger.LogWarning("Unauthorized attempt to activate bus");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Activating bus with ID {BusId}", id);
                
                var success = await _busService.ActivateBusAsync(id);
                if (!success)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for activation", id);
                    return NotFound();
                }

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

                _logger.LogInformation("Successfully activated bus with ID {BusId}", id);
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
            if (!IsAdmin() && !HasPermission("buses.edit"))
            {
                _logger.LogWarning("Unauthorized attempt to deactivate bus");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Deactivating bus with ID {BusId}", id);
                
                var success = await _busService.DeactivateBusAsync(id);
                if (!success)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for deactivation", id);
                    return NotFound();
                }

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

                _logger.LogInformation("Successfully deactivated bus with ID {BusId}", id);
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