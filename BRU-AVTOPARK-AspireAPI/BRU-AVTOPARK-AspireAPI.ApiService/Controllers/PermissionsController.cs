using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using Microsoft.Extensions.Logging;
using SpacetimeDB.Types;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allows both custom JWT (manual parsing) and ASP.NET Core auth (OpenIddict)
    public class PermissionsController : BaseController
    {
        private readonly IPermissionService _permissionService;
        private readonly IAdminActionLogger _adminLogger;
        private readonly ILogger<PermissionsController> _logger;

        private readonly IRealtimeEventBus _realtimeEventBus;

        public PermissionsController(
            IPermissionService permissionService,
            IAdminActionLogger adminLogger,
            ILogger<PermissionsController> logger,
            IRealtimeEventBus realtimeEventBus)
        {
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
            _adminLogger = adminLogger ?? throw new ArgumentNullException(nameof(adminLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
        }

       

        [HttpGet("realtime/ws")]
        public async Task StreamRealtimeEvents(CancellationToken cancellationToken)
        {
            if (!IsAuthenticated())
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await WebSocketEventStreamWriter.StreamCrudSessionAsync(
                HttpContext,
                _realtimeEventBus.SubscribeAsync("permissions", cancellationToken),
                HandleRealtimeCrudAsync,
                _logger,
                cancellationToken);
        }

        private async Task<object> HandleRealtimeCrudAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
        {
            var command = (request.Command ?? string.Empty).Trim().ToLowerInvariant();
            return command switch
            {
                "read_all" => new { permissions = await _permissionService.GetAllPermissionsAsync() },
                "read" => new { permission = await _permissionService.GetPermissionByIdAsync(request.Id ?? throw new InvalidOperationException("id is required for read")) },
                "create" => await HandleCreateCommandAsync(request),
                "update" => await HandleUpdateCommandAsync(request),
                "delete" => await HandleDeleteCommandAsync(request),
                _ => throw new InvalidOperationException($"Unsupported command '{request.Command}'")
            };
        }

        private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("permissions.create")) throw new UnauthorizedAccessException("Not authorized for permissions.create");
            var model = request.Payload?.Deserialize<CreatePermissionModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for create");
            var created = await _permissionService.CreatePermissionAsync(model.Name, model.Description, model.Category);
            var snapshot = await _permissionService.GetAllPermissionsAsync();
            return new { operation = "create", success = created is not null, entity = created, snapshot };
        }

        private async Task<object> HandleUpdateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("permissions.edit")) throw new UnauthorizedAccessException("Not authorized for permissions.edit");
            var id = request.Id ?? throw new InvalidOperationException("id is required for update");
            var model = request.Payload?.Deserialize<UpdatePermissionModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for update");
            var success = await _permissionService.UpdatePermissionAsync(id, model.Name, model.Description, model.Category, model.IsActive);
            var entity = await _permissionService.GetPermissionByIdAsync(id);
            var snapshot = await _permissionService.GetAllPermissionsAsync();
            return new { operation = "update", success, entity, snapshot };
        }

        private async Task<object> HandleDeleteCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("permissions.delete")) throw new UnauthorizedAccessException("Not authorized for permissions.delete");
            var id = request.Id ?? throw new InvalidOperationException("id is required for delete");
            var success = await _permissionService.DeletePermissionAsync(id);
            var snapshot = await _permissionService.GetAllPermissionsAsync();
            return new { operation = "delete", success, deletedId = id, snapshot };
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetPermissions()
        {
            try
            {
                if (!IsAdmin() && !HasPermission("permissions.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view permissions");
                    return Forbid();
                }

                _logger.LogInformation("Getting all permissions");
                var permissions = await _permissionService.GetAllPermissionsAsync();
                
                // Map to anonymous type
                var result = permissions.Select(p => new {
                    p.PermissionId,
                    p.Name,
                    p.Description,
                    p.Category,
                    p.IsActive
                }).ToList();

                _logger.LogInformation("Retrieved {Count} permissions", result.Count());
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving permissions");
                return StatusCode(500, new { message = "Error retrieving permissions", error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetPermission(uint id)
        {
            try
            {
                if (!IsAdmin() && !HasPermission("permissions.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view permission {PermissionId}", id);
                    return Forbid();
                }

                _logger.LogInformation("Getting permission by ID: {PermissionId}", id);
                var permission = await _permissionService.GetPermissionByIdAsync(id);

                if (permission == null)
                {
                    _logger.LogWarning("Permission with ID {PermissionId} not found", id);
                    return NotFound();
                }

                // Map to anonymous type
                var result = new {
                    permission.PermissionId,
                    permission.Name,
                    permission.Description,
                    permission.Category,
                    permission.IsActive
                };

                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving permission {PermissionId}", id);
                return StatusCode(500, new { message = $"Error retrieving permission {id}", error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<Permission>> CreatePermission([FromBody] CreatePermissionModel model)
        {
            if (!IsAdmin() && !HasPermission("permissions.create"))
            {
                _logger.LogWarning("Unauthorized attempt to create permission");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Creating new permission: {PermissionName}", model.Name);
                
                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Call the CreatePermission method
                var permission = await _permissionService.CreatePermissionAsync(
                    model.Name,
                    model.Description,
                    model.Category
                );

                if (permission == null)
                {
                    _logger.LogWarning("Failed to create permission {PermissionName}", model.Name);
                    return BadRequest("Failed to create permission. A permission with this name may already exist.");
                }

                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "CreatePermission",
                    $"Created permission {permission.Name} with ID {permission.PermissionId}"
                );

                _logger.LogInformation("Successfully created permission {PermissionName} with ID {PermissionId}", 
                    permission.Name, permission.PermissionId);
                return CreatedAtAction(nameof(GetPermission), new { id = permission.PermissionId }, permission);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating permission {PermissionName}", model.Name);
                return StatusCode(500, new { message = $"Error creating permission {model.Name}", error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePermission(uint id, [FromBody] UpdatePermissionModel model)
        {
            if (!IsAdmin() && !HasPermission("permissions.edit"))
            {
                _logger.LogWarning("Unauthorized attempt to update permission");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Updating permission {PermissionId}", id);
                
                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Check if permission exists
                var existingPermission = await _permissionService.GetPermissionByIdAsync(id);
                if (existingPermission == null)
                {
                    _logger.LogWarning("Permission with ID {PermissionId} not found for update", id);
                    return NotFound();
                }

                // Call the UpdatePermission method
                var success = await _permissionService.UpdatePermissionAsync(
                    id,
                    model.Name,
                    model.Description,
                    model.Category,
                    model.IsActive
                );

                if (!success)
                {
                    _logger.LogWarning("Failed to update permission {PermissionId}", id);
                    return BadRequest("Failed to update permission. A permission with this name may already exist.");
                }

                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "UpdatePermission",
                    $"Updated permission {existingPermission.Name} with ID {id}"
                );

                _logger.LogInformation("Successfully updated permission {PermissionId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating permission {PermissionId}", id);
                return StatusCode(500, new { message = $"Error updating permission {id}", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePermission(uint id)
        {
            if (!IsAdmin() && !HasPermission("permissions.delete"))
            {
                _logger.LogWarning("Unauthorized attempt to delete permission");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Attempting to delete permission {PermissionId}", id);
                
                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Check if permission exists
                var existingPermission = await _permissionService.GetPermissionByIdAsync(id);
                if (existingPermission == null)
                {
                    _logger.LogWarning("Permission with ID {PermissionId} not found for deletion", id);
                    return NotFound();
                }

                // Check if permission is in use
                var isInUse = await _permissionService.IsPermissionInUseAsync(id);
                if (isInUse)
                {
                    _logger.LogWarning("Cannot delete permission {PermissionId} as it is in use", id);
                    return BadRequest("Cannot delete permission as it is currently assigned to one or more roles");
                }

                // Call the DeletePermission method
                var success = await _permissionService.DeletePermissionAsync(id);
                if (!success)
                {
                    _logger.LogWarning("Failed to delete permission {PermissionId}", id);
                    return BadRequest("Failed to delete permission");
                }

                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "DeletePermission",
                    $"Deleted permission {existingPermission.Name} with ID {id}"
                );

                _logger.LogInformation("Successfully deleted permission {PermissionId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting permission {PermissionId}", id);
                return StatusCode(500, new { message = $"Error deleting permission {id}", error = ex.Message });
            }
        }

        [HttpGet("categories")]
        public async Task<ActionResult<IEnumerable<string>>> GetCategories()
        {
            try
            {
                if (!IsAdmin() && !HasPermission("permissions.view.categories"))
                {
                    _logger.LogWarning("Unauthorized attempt to view permission categories");
                    return Forbid();
                }

                _logger.LogInformation("Fetching all permission categories");
                var categories = await _permissionService.GetAllCategoriesAsync();
                _logger.LogInformation("Retrieved {Count} permission categories", categories.Count());
                return Ok(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving permission categories");
                return StatusCode(500, new { message = "Error retrieving permission categories", error = ex.Message });
            }
        }

        [HttpGet("category/{category}")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetPermissionsByCategory(string category)
        {
            try
            {
                if (!IsAdmin() && !HasPermission("permissions.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view permissions by category {Category}", category);
                    return Forbid();
                }

                _logger.LogInformation("Fetching permissions for category {Category}", category);
                var permissions = await _permissionService.GetPermissionsByCategoryAsync(category);
                
                // Map to anonymous type
                var result = permissions.Select(p => new {
                    p.PermissionId,
                    p.Name,
                    p.Description,
                    p.Category,
                    p.IsActive
                }).ToList();

                _logger.LogInformation("Retrieved {Count} permissions for category {Category}", result.Count(), category);
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving permissions for category {Category}", category);
                return StatusCode(500, new { message = $"Error retrieving permissions for category {category}", error = ex.Message });
            }
        }
    }

    public class CreatePermissionModel
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required string Category { get; set; }
    }

    public class UpdatePermissionModel
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public bool? IsActive { get; set; }
    }
} 
