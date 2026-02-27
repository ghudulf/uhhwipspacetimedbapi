// API/Controllers/UsersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using Serilog;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.Extensions.Configuration;
using SpacetimeDB.Types;
using System.Linq;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UsersController : BaseController
    {
        private readonly IUserService _userService;
        private readonly IAuthenticationService _authService;
        private readonly IRoleService _roleService;
        private readonly IConfiguration _configuration;

        public UsersController(IUserService userService, IAuthenticationService authService, IRoleService roleService, IConfiguration configuration)
        {
            _userService = userService;
            _authService = authService;
            _roleService = roleService;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserProfile>>> GetUsers()
        {
            if (!IsAdmin() && !HasPermission("users.view"))
            {
                Log.Warning("Unauthorized attempt to access users list");
                return Forbid();
            }
            Log.Information("Fetching all users");
            var users = await _userService.GetAllUsersAsync();
            Log.Debug("Retrieved {UserCount} users", users.Count());
            return Ok(users);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<UserProfile>> GetUser(uint id)
        {
            if (!IsAdmin() && !HasPermission("users.view"))
            {
                Log.Warning("Unauthorized attempt to access user {UserId}", id);
                return Forbid();
            }
            Log.Information("Fetching user with ID {UserId}", id);
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                Log.Warning("User with ID {UserId} not found", id);
                return NotFound();
            }
            Log.Debug("Successfully retrieved user with ID {UserId}", id);
            return user;
        }

        [HttpPost]
        public async Task<ActionResult<UserProfile>> CreateUser([FromBody] CreateUserModel model)
        {
            if (!IsAdmin() && !HasPermission("users.create"))
            {
                Log.Warning("Unauthorized attempt to create user");
                return Forbid();
            }

            Log.Information("Attempting to create new user with login {Login}", model.Login);
            
            // Check if user already exists
            var existingUser = await _userService.GetUserByLoginAsync(model.Login);
            if (existingUser != null)
            {
                Log.Warning("User creation failed - login {Login} already exists", model.Login);
                return BadRequest("Login already exists");
            }

            var createdUser = await _userService.CreateUserAsync(
                model.Login, 
                model.Password, 
                model.Role, 
                model.Email, 
                model.PhoneNumber
            );

            if (createdUser == null)
            {
                Log.Error("Failed to create user with login {Login}", model.Login);
                return BadRequest("Failed to create user");
            }

            Log.Information("Successfully created user with ID {UserId}", createdUser.LegacyUserId);
            return CreatedAtAction(nameof(GetUser), new { id = createdUser.LegacyUserId }, createdUser);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(uint id, [FromBody] UpdateUserModel model)
        {
            if (!IsAdmin() && !HasPermission("users.edit"))
            {
                Log.Warning("Unauthorized attempt to update user {UserId}", id);
                return Forbid();
            }

            Log.Information("Attempting to update user with ID {UserId}", id);
            
            // Check if user exists
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                Log.Warning("User with ID {UserId} not found for update", id);
                return NotFound();
            }

            // Check if login is already taken
            if (!string.IsNullOrEmpty(model.Login) && model.Login != user.Login)
            {
                var existingUser = await _userService.GetUserByLoginAsync(model.Login);
                if (existingUser != null)
                {
                    Log.Warning("Update failed - login {Login} already exists", model.Login);
                    return BadRequest("Login already exists");
                }
            }

            // Update user
            var success = await _userService.UpdateUserAsync(
                id,
                model.Login,
                model.Password,
                model.Role,
                model.Email,
                model.PhoneNumber,
                model.IsActive
            );

            if (!success)
            {
                Log.Error("Failed to update user with ID {UserId}", id);
                return BadRequest("Failed to update user");
            }

            Log.Information("Successfully updated user with ID {UserId}", id);
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(uint id)
        {
            if (!IsAdmin() && !HasPermission("users.delete"))
            {
                Log.Warning("Unauthorized attempt to delete user {UserId}", id);
                return Forbid();
            }

            Log.Information("Attempting to delete user with ID {UserId}", id);
            
            // Get current user ID from token
            var currentUserId = GetUserId();
            if (currentUserId == null)
            {
                Log.Warning("Failed to get current user ID from token");
                return Unauthorized();
            }
            
            // Prevent deleting yourself
            if (id.ToString() == currentUserId)
            {
                Log.Warning("User {UserId} attempted to delete their own account", id);
                return BadRequest("You cannot delete your own account");
            }

            // Check if user exists
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                Log.Warning("User with ID {UserId} not found for deletion", id);
                return NotFound();
            }

            // Delete user
            var success = await _userService.DeleteUserAsync(id);
            if (!success)
            {
                Log.Error("Failed to delete user with ID {UserId}", id);
                return BadRequest("Failed to delete user");
            }

            Log.Information("Successfully deleted user with ID {UserId}", id);
            return NoContent();
        }

        [HttpGet("{id}/roles")]
        public async Task<ActionResult<IEnumerable<Role>>> GetUserRoles(uint id)
        {
            if (!IsAdmin() && !HasPermission("users.view.roles"))
            {
                Log.Warning("Unauthorized attempt to access user roles for user {UserId}", id);
                return Forbid();
            }

            Log.Information("Fetching roles for user {UserId}", id);
            
            // Check if user exists
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                Log.Warning("User {UserId} not found while fetching roles", id);
                return NotFound();
            }

            var roles = await _userService.GetUserRolesAsync(id);

            Log.Information("Retrieved {RoleCount} roles for user {UserId}", roles.Count(), id);
            return Ok(roles);
        }

        [HttpGet("{id}/permissions")]
        public async Task<ActionResult<IEnumerable<Permission>>> GetUserPermissions(uint id)
        {
            if (!IsAdmin() && !HasPermission("users.view.permissions"))
            {
                Log.Warning("Unauthorized attempt to access user permissions for user {UserId}", id);
                return Forbid();
            }

            Log.Information("Fetching permissions for user {UserId}", id);
            
            // Check if user exists
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                Log.Warning("User {UserId} not found while fetching permissions", id);
                return NotFound();
            }

            var permissions = await _userService.GetUserPermissionsAsync(id);

            Log.Information("Retrieved {PermissionCount} permissions for user {UserId}", permissions.Count(), id);
            return Ok(permissions);
        }

        [HttpPost("{id}/roles")]
        public async Task<IActionResult> AssignRoleToUser(uint id, [FromBody] AssignRoleModel model)
        {
            if (!IsAdmin() && !HasPermission("users.assign.roles"))
            {
                Log.Warning("Unauthorized attempt to assign role to user {UserId}", id);
                return Forbid();
            }

            Log.Information("Assigning role {RoleId} to user {UserId}", model.RoleId, id);
            var success = await _roleService.AssignRoleToUserAsync(id, model.RoleId);
            
            if (!success)
            {
                Log.Warning("Failed to assign role {RoleId} to user {UserId}", model.RoleId, id);
                return BadRequest("Failed to assign role to user");
            }

            Log.Information("Successfully assigned role {RoleId} to user {UserId}", model.RoleId, id);
            return NoContent();
        }

        [HttpDelete("{id}/roles/{roleId}")]
        public async Task<IActionResult> RemoveRoleFromUser(uint id, uint roleId)
        {
            if (!IsAdmin() && !HasPermission("users.remove.roles"))
            {
                Log.Warning("Unauthorized attempt to remove role from user {UserId}", id);
                return Forbid();
            }

            Log.Information("Removing role {RoleId} from user {UserId}", roleId, id);
            var success = await _roleService.RemoveRoleFromUserAsync(id, roleId);
            
            if (!success)
            {
                Log.Warning("Failed to remove role {RoleId} from user {UserId}", roleId, id);
                return BadRequest("Failed to remove role from user");
            }

            Log.Information("Successfully removed role {RoleId} from user {UserId}", roleId, id);
            return NoContent();
        }

        [HttpGet("current")]
        public async Task<ActionResult<UserProfile>> GetCurrentUser()
        {
            try
            {
                var userLogin = User.Identity?.Name;
                if (string.IsNullOrEmpty(userLogin))
                {
                    Log.Warning("No username claim found in token");
                    return Unauthorized(new { message = "Invalid token: no username claim found" });
                }

                Log.Debug("Looking up user with login: {Login}", userLogin);
                var user = await _userService.GetCurrentUserAsync(userLogin);

                if (user == null)
                {
                    Log.Warning("User from token not found in database: {Username}", userLogin);
                    return NotFound(new { message = $"User '{userLogin}' not found" });
                }

                Log.Information("Successfully retrieved current user information for {Username}", user.Login);
                return user;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving current user");
                return StatusCode(500, new { message = "Internal server error while retrieving user information" });
            }
        }
    }

    public class CreateUserModel
    {
        public required string Login { get; set; }
        public required string Password { get; set; }
        public int Role { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
    }

    public class UpdateUserModel
    {
        public string? Login { get; set; }
        public string? Password { get; set; }
        public int? Role { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public bool? IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    public class AssignRoleModel
    {
        public required uint RoleId { get; set; }
    }
}
