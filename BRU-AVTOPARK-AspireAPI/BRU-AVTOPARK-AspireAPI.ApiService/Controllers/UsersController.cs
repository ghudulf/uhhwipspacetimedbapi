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
using SpacetimeDB;
using Log = Serilog.Log;
using System.Text.Json;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allows both custom JWT (manual parsing) and ASP.NET Core auth (OpenIddict)
    public class UsersController : BaseController
    {
        private readonly IUserService _userService;
        private readonly IAuthenticationService _authService;
        private readonly IRoleService _roleService;
        private readonly IConfiguration _configuration;
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IRealtimeEventBus _realtimeEventBus;
        private readonly ILogger<UsersController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="UsersController"/> with its required services and infrastructure.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown if <c>spacetimeService</c>, <c>realtimeEventBus</c>, or <c>logger</c> is null.</exception>
        public UsersController(IUserService userService, IAuthenticationService authService, IRoleService roleService, IConfiguration configuration, ISpacetimeDBService spacetimeService, IRealtimeEventBus realtimeEventBus, ILogger<UsersController> logger)
        {
            _userService = userService;
            _authService = authService;
            _roleService = roleService;
            _configuration = configuration;
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Streams realtime user CRUD events over a WebSocket to the authenticated caller.
        /// </summary>
        /// <param name="cancellationToken">Token used to cancel the streaming session.</param>
        [HttpGet("realtime/ws")]
        public async Task StreamRealtimeEvents(CancellationToken cancellationToken)
        {
            // Validate token and check permissions
            var claims = await ValidateOAuthTokenAsync();
            if (claims == null && !IsAuthenticated())
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Require admin or users.view permission to subscribe to users channel
            if (!await IsAdminAsync() && !HasPermission("users.view"))
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await WebSocketEventStreamWriter.StreamCrudSessionAsync(
                HttpContext,
                _realtimeEventBus.SubscribeAsync("users", cancellationToken),
                HandleRealtimeCrudAsync,
                _logger,
                cancellationToken);
        }

        /// <summary>
        /// Dispatches a realtime CRUD request to the corresponding handler based on the request's Command.
        /// </summary>
        /// <param name="request">The realtime CRUD request containing the command and optional payload.</param>
        /// <param name="cancellationToken">Token to observe for cancellation.</param>
        /// <returns>The handler's response object: for "read_all" or "read" commands an object containing user data; for "create", "update", and "delete" commands an operation result object indicating success and related data.</returns>
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
        /// Retrieves all users and returns a JSON-serializable snapshot containing a list of user summaries.
        /// </summary>
        /// <returns>An object with a `users` property containing an array of user summary objects (LegacyUserId, UserId, Login, Email, PhoneNumber, IsActive, CreatedAt, LastLoginAt, LegacyGuid, EmailConfirmed).</returns>
        /// <exception cref="System.UnauthorizedAccessException">Thrown when the caller is neither an administrator nor has the "users.view" permission.</exception>
        private async Task<object> HandleReadAllCommandAsync()
        {
            if (!IsAdmin() && !HasPermission("users.view"))
            {
                throw new UnauthorizedAccessException("Not authorized for users.view");
            }

            var users = await _userService.GetAllUsersAsync();
            var result = users.Select(u => new {
                u.LegacyUserId,
                UserId = u.UserId.ToString(),
                u.Login,
                u.Email,
                u.PhoneNumber,
                u.IsActive,
                u.CreatedAt,
                u.LastLoginAt,
                u.LegacyGuid,
                u.EmailConfirmed
            }).ToList();

            return new { users = result };
        }

        /// <summary>
        /// Retrieves a detailed user snapshot for the given realtime read request, including the user's roles and derived permissions.
        /// </summary>
        /// <param name="request">Realtime CRUD request whose <c>Id</c> must contain the target user's identifier.</param>
        /// <returns>
        /// An object with a single property <c>user</c> that contains the user's fields:
        /// LegacyUserId, UserId, Login, Email, PhoneNumber, IsActive, CreatedAt, LastLoginAt, Roles, and Permissions.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an administrator and lacks the "users.view" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when <c>request.Id</c> is missing or when no user is found for the provided id.</exception>
        private async Task<object> HandleReadCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("users.view"))
            {
                throw new UnauthorizedAccessException("Not authorized for users.view");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for read");
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                throw new InvalidOperationException($"User {id} not found");
            }

            var conn = _spacetimeService.GetConnection();
            var userRoles = conn.Db.UserRole.Iter().Where(ur => ur.UserId.Equals(user.UserId)).ToList();
            var roles = userRoles.Select(ur => {
                var role = conn.Db.Role.RoleId.Find(ur.RoleId);
                return role != null ? new { role.RoleId, role.Name, role.Description, role.IsSystem } : null;
            }).Where(r => r != null).ToList();

            var permissionIds = conn.Db.RolePermission.Iter()
                .Where(rp => roles.Select(r => r.RoleId).Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToList();
            var permissions = permissionIds.Select(pid => {
                var perm = conn.Db.Permission.PermissionId.Find(pid);
                return perm != null ? new { perm.PermissionId, perm.Name, perm.Description, perm.Category } : null;
            }).Where(p => p != null).ToList();

            var result = new {
                user.LegacyUserId,
                user.UserId,
                user.Login,
                user.Email,
                user.PhoneNumber,
                user.IsActive,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)user.CreatedAt).DateTime,
                LastLoginAt = user.LastLoginAt.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)user.LastLoginAt.Value).DateTime : (DateTime?)null,
                Roles = roles,
                Permissions = permissions
            };

            return new { user = result };
        }

        /// <summary>
        /// Processes a realtime "create" command by creating a new user from the request payload.
        /// </summary>
        /// <param name="request">The realtime CRUD request whose payload must deserialize to <see cref="CreateUserModel"/>.</param>
        /// <returns>
        /// An object with the operation result:
        /// - `operation`: the string "create".
        /// - `success`: `true` if a user was created, `false` otherwise.
        /// - `entity`: the created user object (or null if creation failed).
        /// - `snapshot`: current list of all users after the operation.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks admin rights or the "users.create" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request payload is missing or cannot be deserialized to <see cref="CreateUserModel"/>.</exception>
        private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("users.create"))
            {
                throw new UnauthorizedAccessException("Not authorized for users.create");
            }

            var model = request.Payload?.Deserialize<CreateUserModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for create");

            var created = await _userService.CreateUserAsync(model.Login, model.Password, model.Role, model.Email, model.PhoneNumber);
            
            // Map to safe projection (exclude PasswordHash)
            var safeEntity = created != null ? new {
                created.LegacyUserId,
                UserId = created.UserId.ToString(),
                created.Login,
                created.Email,
                created.PhoneNumber,
                created.IsActive,
                created.CreatedAt,
                created.LastLoginAt,
                created.LegacyGuid,
                created.EmailConfirmed
            } : null;

            // Only include snapshot if user has view permission
            object result;
            if (IsAdmin() || HasPermission("users.view"))
            {
                var snapshot = await _userService.GetAllUsersAsync();
                var safeSnapshot = snapshot.Select(u => new {
                    u.LegacyUserId,
                    UserId = u.UserId.ToString(),
                    u.Login,
                    u.Email,
                    u.PhoneNumber,
                    u.IsActive,
                    u.CreatedAt,
                    u.LastLoginAt,
                    u.LegacyGuid,
                    u.EmailConfirmed
                }).ToList();
                result = new { operation = "create", success = created is not null, entity = safeEntity, snapshot = safeSnapshot };
            }
            else
            {
                result = new { operation = "create", success = created is not null, entity = safeEntity };
            }

            // Publish realtime event (best-effort)
            if (created != null)
            {
                try
                {
                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "user.created",
                        Resource: "users",
                        HttpMethod: "POST",
                        StatusCode: 201,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: GetUserId(),
                        UserName: await GetUserNameAsync(),
                        Tenant: User?.FindFirst("tenant")?.Value,
                        SourceIp: GetClientIp(),
                        Metadata: new Dictionary<string, string> { ["operation"] = "create", ["success"] = "true", ["createdUserId"] = created.UserId.ToString() }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for user.created");
                }
            }

            return result;
        }

        /// <summary>
        /// Handle a realtime "update" CRUD request for a user.
        /// </summary>
        /// <param name="request">The realtime CRUD request containing the target Id and a payload deserializable as UpdateUserModel.</param>
        /// <returns>An object with properties: `operation` (string, value "update"), `success` (bool indicating whether the update succeeded), `entity` (the updated user or null), and `snapshot` (the current list of all users).</returns>
        private async Task<object> HandleUpdateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("users.edit"))
            {
                throw new UnauthorizedAccessException("Not authorized for users.edit");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for update");
            var model = request.Payload?.Deserialize<UpdateUserModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for update");

            var success = await _userService.UpdateUserAsync(id, model.Login, model.Password, model.Role, model.Email, model.PhoneNumber, model.IsActive);
            var entity = await _userService.GetUserByIdAsync(id);
            
            // Map to safe projection (exclude PasswordHash)
            var safeEntity = entity != null ? new {
                entity.LegacyUserId,
                UserId = entity.UserId.ToString(),
                entity.Login,
                entity.Email,
                entity.PhoneNumber,
                entity.IsActive,
                entity.CreatedAt,
                entity.LastLoginAt,
                entity.LegacyGuid,
                entity.EmailConfirmed
            } : null;

            // Only include snapshot if user has view permission
            object result;
            if (IsAdmin() || HasPermission("users.view"))
            {
                var snapshot = await _userService.GetAllUsersAsync();
                var safeSnapshot = snapshot.Select(u => new {
                    u.LegacyUserId,
                    UserId = u.UserId.ToString(),
                    u.Login,
                    u.Email,
                    u.PhoneNumber,
                    u.IsActive,
                    u.CreatedAt,
                    u.LastLoginAt,
                    u.LegacyGuid,
                    u.EmailConfirmed
                }).ToList();
                result = new { operation = "update", success, entity = safeEntity, snapshot = safeSnapshot };
            }
            else
            {
                result = new { operation = "update", success, entity = safeEntity };
            }

            // Publish realtime event (best-effort)
            if (success)
            {
                try
                {
                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "user.updated",
                        Resource: "users",
                        HttpMethod: "PUT",
                        StatusCode: 200,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: GetUserId(),
                        UserName: await GetUserNameAsync(),
                        Tenant: User?.FindFirst("tenant")?.Value,
                        SourceIp: GetClientIp(),
                        Metadata: new Dictionary<string, string> { ["operation"] = "update", ["success"] = "true", ["updatedUserId"] = id.ToString() }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for user.updated");
                }
            }

            return result;
        }

        /// <summary>
        /// Handle a realtime "delete" CRUD command for users.
        /// </summary>
        /// <param name="request">The realtime request which must include the target user's Id.</param>
        /// <returns>
        /// An object with the following properties:
        /// - operation: the string "delete"
        /// - success: `true` if the delete succeeded, `false` otherwise
        /// - deletedId: the id of the deleted user
        /// - snapshot: the current collection of users after the operation
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin and lacks the "users.delete" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request does not provide an id or when attempting to delete the current caller's own account.</exception>
        private async Task<object> HandleDeleteCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin() && !HasPermission("users.delete"))
            {
                throw new UnauthorizedAccessException("Not authorized for users.delete");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for delete");

            // Reject if caller identity cannot be resolved
            var currentUserId = GetUserId();
            if (currentUserId == null)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                throw new UnauthorizedAccessException("Unable to resolve caller identity");
            }

            if (id.ToString() == currentUserId)
            {
                throw new InvalidOperationException("You cannot delete your own account");
            }

            var success = await _userService.DeleteUserAsync(id);

            // Only include snapshot if user has view permission
            object result;
            if (IsAdmin() || HasPermission("users.view"))
            {
                var snapshot = await _userService.GetAllUsersAsync();
                var safeSnapshot = snapshot.Select(u => new {
                    u.LegacyUserId,
                    UserId = u.UserId.ToString(),
                    u.Login,
                    u.Email,
                    u.PhoneNumber,
                    u.IsActive,
                    u.CreatedAt,
                    u.LastLoginAt,
                    u.LegacyGuid,
                    u.EmailConfirmed
                }).ToList();
                result = new { operation = "delete", success, deletedId = id, snapshot = safeSnapshot };
            }
            else
            {
                result = new { operation = "delete", success, deletedId = id };
            }

            // Publish realtime event (best-effort)
            if (success)
            {
                try
                {
                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "user.deleted",
                        Resource: "users",
                        HttpMethod: "DELETE",
                        StatusCode: 200,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: currentUserId,
                        UserName: await GetUserNameAsync(),
                        Tenant: User?.FindFirst("tenant")?.Value,
                        SourceIp: GetClientIp(),
                        Metadata: new Dictionary<string, string> { ["operation"] = "delete", ["success"] = "true", ["deletedUserId"] = id.ToString() }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for user.deleted");
                }
            }

            return result;
        }

        /// <summary>
        /// Retrieves all users and maps their public data for JSON serialization.
        /// </summary>
        /// <remarks>
        /// Requires the caller to be an administrator or to have the "users.view" permission.
        /// The returned user objects include fields intended for client consumption and JSON transport.
        /// </remarks>
        /// <returns>
        /// An Ok result containing a list of user objects with the following fields: LegacyUserId, UserId, Login, PasswordHash, Email, PhoneNumber, IsActive, CreatedAt, LastLoginAt, LegacyGuid, EmailConfirmed; or a 403 Forbidden result when the caller is not authorized.
        /// </returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetUsers()
        {
            if (!IsAdmin() && !HasPermission("users.view"))
            {
                Log.Warning("Unauthorized attempt to access users list");
                return Forbid();
            }
            Log.Information("Fetching all users");
            var users = await _userService.GetAllUsersAsync();

            // Map to anonymous type - CRITICAL: This converts SpacetimeDB structure to valid JSON
            // Exclude PasswordHash for security
            var result = users.Select(u => new {
                u.LegacyUserId,
                UserId = u.UserId.ToString(), // Convert Identity to string for JSON
                u.Login,
                u.Email,
                u.PhoneNumber,
                u.IsActive,
                u.CreatedAt,
                u.LastLoginAt,
                u.LegacyGuid,
                u.EmailConfirmed
            }).ToList();

            Log.Debug("Retrieved {UserCount} users", result.Count);
            Log.Information("FULL USERS DATA: {UsersData}", JsonSerializer.Serialize(result));
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetUser(uint id)
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
            
            var conn = _spacetimeService.GetConnection();
            var userRoles = conn.Db.UserRole.Iter().Where(ur => ur.UserId.Equals(user.UserId)).ToList();
            var roles = userRoles.Select(ur => {
                var role = conn.Db.Role.RoleId.Find(ur.RoleId);
                return role != null ? new { role.RoleId, role.Name, role.Description, role.IsSystem } : null;
            }).Where(r => r != null).ToList();

            var permissionIds = conn.Db.RolePermission.Iter()
                .Where(rp => roles.Select(r => r.RoleId).Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToList();
            var permissions = permissionIds.Select(pid => {
                var perm = conn.Db.Permission.PermissionId.Find(pid);
                return perm != null ? new { perm.PermissionId, perm.Name, perm.Description, perm.Category } : null;
            }).Where(p => p != null).ToList();

            // Map to anonymous type including Roles and Permissions
            var result = new {
                user.LegacyUserId,
                user.UserId,
                user.Login,
                user.Email,
                user.PhoneNumber,
                user.IsActive,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)user.CreatedAt).DateTime,
                LastLoginAt = user.LastLoginAt.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)user.LastLoginAt.Value).DateTime : (DateTime?)null,
                Roles = roles,
                Permissions = permissions
            };

            Log.Debug("Successfully retrieved user with ID {UserId}", id);
            Log.Information("FULL USER DATA: {UserData}", JsonSerializer.Serialize(result));
            return Ok(result);
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
        public async Task<ActionResult<IEnumerable<dynamic>>> GetUserRoles(uint id)
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

            // Map to anonymous type
            var result = roles.Select(r => new {
                r.RoleId,
                r.LegacyRoleId,
                r.Name,
                r.Description,
                r.IsActive,
                r.Priority,
                r.IsSystem
            }).ToList();

            Log.Information("Retrieved {RoleCount} roles for user {UserId}", result.Count(), id);
            Log.Information("FULL USER ROLES DATA: {RolesData}", JsonSerializer.Serialize(result));
            return Ok(result);
        }

        [HttpGet("{id}/permissions")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetUserPermissions(uint id)
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

            // Map to anonymous type
            var result = permissions.Select(p => new {
                p.PermissionId,
                p.Name,
                p.Description,
                p.Category,
                p.IsActive
            }).ToList();

            Log.Information("Retrieved {PermissionCount} permissions for user {UserId}", result.Count(), id);
            Log.Information("FULL USER PERMISSIONS DATA: {PermissionsData}", JsonSerializer.Serialize(result));
            return Ok(result);
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
        public async Task<ActionResult<dynamic>> GetCurrentUser()
        {
            try
            {
                var userLogin = User.Identity?.Name;
                if (string.IsNullOrEmpty(userLogin))
                {
                    // Attempt to get login from other claims if Identity.Name is null
                    userLogin = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? 
                                User.Claims.FirstOrDefault(c => c.Type == "login" || c.Type == "preferred_username" || c.Type == "sub")?.Value;
                    if (string.IsNullOrEmpty(userLogin))
                    {
                        Log.Warning("No suitable username/login claim found in token. Claims: {@Claims}", User.Claims.Select(c => new { c.Type, c.Value }));
                        return Unauthorized(new { message = "Invalid token: no suitable username/login claim found" });
                    }
                }

                Log.Debug("Looking up user with login: {Login}", userLogin);
                var user = await _userService.GetCurrentUserAsync(userLogin);

                if (user == null)
                {
                    Log.Warning("User from token not found in database: {Username}", userLogin);
                    return NotFound(new { message = $"User '{userLogin}' not found" });
                }

                var conn = _spacetimeService.GetConnection();
                var userRoles = conn.Db.UserRole.Iter().Where(ur => ur.UserId.Equals(user.UserId)).ToList();
                var roles = userRoles.Select(ur => {
                    var role = conn.Db.Role.RoleId.Find(ur.RoleId);
                    return role != null ? new { role.RoleId, role.Name, role.Description, role.IsSystem } : null;
                }).Where(r => r != null).ToList();

                var permissionIds = conn.Db.RolePermission.Iter()
                    .Where(rp => roles.Select(r => r.RoleId).Contains(rp.RoleId))
                    .Select(rp => rp.PermissionId)
                    .Distinct()
                    .ToList();
                var permissions = permissionIds.Select(pid => {
                    var perm = conn.Db.Permission.PermissionId.Find(pid);
                    return perm != null ? new { perm.PermissionId, perm.Name, perm.Description, perm.Category } : null;
                }).Where(p => p != null).ToList();

                // Map to anonymous type including Roles and Permissions
                var result = new {
                    user.LegacyUserId,
                    user.UserId,
                    user.Login,
                    user.Email,
                    user.PhoneNumber,
                    user.IsActive,
                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)user.CreatedAt).DateTime,
                    LastLoginAt = user.LastLoginAt.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)user.LastLoginAt.Value).DateTime : (DateTime?)null,
                    Roles = roles,
                    Permissions = permissions
                };

                Log.Information("Successfully retrieved current user information for {Username}", user.Login);
                Log.Information("FULL CURRENT USER DATA: {UserData}", JsonSerializer.Serialize(result));
                return Ok(result);
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