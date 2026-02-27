using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using Serilog;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RolesController : BaseController
    {
        private readonly IRoleService _roleService;
        private readonly IAdminActionLogger _adminLogger;
        private readonly ILogger<RolesController> _logger;

        public RolesController(
            IRoleService roleService,
            IAdminActionLogger adminLogger,
            ILogger<RolesController> logger)
        {
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _adminLogger = adminLogger ?? throw new ArgumentNullException(nameof(adminLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private bool IsAdmin()
        {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return false;

            var token = authHeader.Substring("Bearer ".Length);
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(token);
            var roleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "role");
            return roleClaim?.Value == "1";
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Role>>> GetRoles()
        {
            try
            {
                if (!IsAdmin() && !HasPermission("roles.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view roles");
                    return Forbid();
                }

                _logger.LogInformation("Fetching all roles");
                var roles = await _roleService.GetAllRolesAsync();
                _logger.LogInformation("Retrieved {Count} roles", roles.Count());
                return Ok(roles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving roles");
                return StatusCode(500, new { message = "Error retrieving roles", error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Role>> GetRole(uint id)
        {
            try
            {
                if (!IsAdmin() && !HasPermission("roles.view"))
                {
                    _logger.LogWarning("Unauthorized attempt to view role {RoleId}", id);
                    return Forbid();
                }

                _logger.LogInformation("Fetching role with ID {RoleId}", id);
                var role = await _roleService.GetRoleByIdAsync(id);

                if (role == null)
                {
                    _logger.LogWarning("Role with ID {RoleId} not found", id);
                    return NotFound();
                }

                return Ok(role);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving role {RoleId}", id);
                return StatusCode(500, new { message = $"Error retrieving role {id}", error = ex.Message });
            }
        }

        [HttpGet("{id}/permissions")]
        public async Task<ActionResult<IEnumerable<Permission>>> GetRolePermissions(uint id)
        {
            try
            {
                if (!IsAdmin() && !HasPermission("roles.view.permissions"))
                {
                    _logger.LogWarning("Unauthorized attempt to view role permissions for role {RoleId}", id);
                    return Forbid();
                }

                _logger.LogInformation("Fetching permissions for role {RoleId}", id);
                var permissions = await _roleService.GetRolePermissionsAsync(id);
                return Ok(permissions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving permissions for role {RoleId}", id);
                return StatusCode(500, new { message = $"Error retrieving permissions for role {id}", error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<Role>> CreateRole([FromBody] CreateRoleModel model)
        {
            if (!IsAdmin() && !HasPermission("roles.create"))
            {
                _logger.LogWarning("Unauthorized attempt to create role");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Creating new role: {RoleName}", model.Name);
                
                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Call the CreateRole reducer through the service
                var role = await _roleService.CreateRoleAsync(
                    model.Name,
                    model.Description,
                    model.LegacyRoleId,
                    model.Priority,
                    model.PermissionIds,
                    userId
                );

                if (role == null)
                {
                    _logger.LogWarning("Failed to create role {RoleName}", model.Name);
                    return BadRequest("Failed to create role");
                }

                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "CreateRole",
                    $"Created role {role.Name} with ID {role.RoleId}"
                );

                _logger.LogInformation("Successfully created role {RoleName} with ID {RoleId}", role.Name, role.RoleId);
                return CreatedAtAction(nameof(GetRole), new { id = role.RoleId }, role);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating role {RoleName}", model.Name);
                return StatusCode(500, new { message = $"Error creating role {model.Name}", error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRole(uint id, [FromBody] UpdateRoleModel model)
        {
            if (!IsAdmin() && !HasPermission("roles.edit"))
            {
                _logger.LogWarning("Unauthorized attempt to update role");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Updating role {RoleId}", id);
                
                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Check if role exists
                var existingRole = await _roleService.GetRoleByIdAsync(id);
                if (existingRole == null)
                {
                    _logger.LogWarning("Role with ID {RoleId} not found for update", id);
                    return NotFound();
                }

                if (existingRole.IsSystem)
                {
                    _logger.LogWarning("Attempted to modify system role {RoleId}", id);
                    return BadRequest("System roles cannot be modified");
                }

                // Call the UpdateRole reducer through the service
                var success = await _roleService.UpdateRoleAsync(
                    id,
                    model.Name,
                    model.Description,
                    model.Priority,
                    model.PermissionIds,
                    userId
                );

                if (!success)
                {
                    _logger.LogWarning("Failed to update role {RoleId}", id);
                    return BadRequest("Failed to update role");
                }

                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "UpdateRole",
                    $"Updated role {existingRole.Name} with ID {id}"
                );

                _logger.LogInformation("Successfully updated role {RoleId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating role {RoleId}", id);
                return StatusCode(500, new { message = $"Error updating role {id}", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRole(uint id)
        {
            if (!IsAdmin() && !HasPermission("roles.delete"))
            {
                _logger.LogWarning("Unauthorized attempt to delete role");
                return Forbid();
            }

            try
            {
                _logger.LogInformation("Attempting to delete role {RoleId}", id);
                
                // Get the current user ID from token
                var userId = GetUserId();
                if (userId == null)
                {
                    _logger.LogWarning("Failed to get user ID from token");
                    return Unauthorized();
                }
                
                // Check if role exists
                var existingRole = await _roleService.GetRoleByIdAsync(id);
                if (existingRole == null)
                {
                    _logger.LogWarning("Role with ID {RoleId} not found for deletion", id);
                    return NotFound();
                }

                if (existingRole.IsSystem)
                {
                    _logger.LogWarning("Attempted to delete system role {RoleId}", id);
                    return BadRequest("System roles cannot be deleted");
                }

                // Call the DeleteRole reducer through the service
                var success = await _roleService.DeleteRoleAsync(id);

                if (!success)
                {
                    _logger.LogWarning("Failed to delete role {RoleId}", id);
                    return BadRequest("Failed to delete role");
                }

                // Log the admin action
                await _adminLogger.LogActionAsync(
                    userId,
                    "DeleteRole",
                    $"Deleted role {existingRole.Name} with ID {id}"
                );

                _logger.LogInformation("Successfully deleted role {RoleId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting role {RoleId}", id);
                return StatusCode(500, new { message = $"Error deleting role {id}", error = ex.Message });
            }
        }
    }

    public class CreateRoleModel
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public int LegacyRoleId { get; set; }
        public uint Priority { get; set; }
        public List<uint>? PermissionIds { get; set; }
    }

    public class UpdateRoleModel
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public uint? Priority { get; set; }
        public List<uint>? PermissionIds { get; set; }
    }
} 