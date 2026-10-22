using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Implementations
{
    public class UserService : IUserService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IAuthenticationService _authService;
        private readonly IRoleService _roleService;
        private readonly ILogger<UserService> _logger;

        public UserService(
            ISpacetimeDBService spacetimeService, 
            IAuthenticationService authService,
            IRoleService roleService,
            ILogger<UserService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IEnumerable<UserProfile>> GetAllUsersAsync()
        {
            try
            {
                _logger.LogInformation("Getting all users");
                
                // Get all users from SpacetimeDB
                var users = await _spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter().ToList());
                
                _logger.LogInformation("Retrieved {Count} users", users.Count);
                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all users");
                throw;
            }
        }

        public async Task<UserProfile?> GetUserByIdAsync(uint userId)
        {
            try
            {
                _logger.LogInformation("Getting user by ID: {UserId}", userId);
                
                // Find user by legacy ID
                var userProfile = await _spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.LegacyUserId == userId));
                
                if (userProfile == null)
                {
                    _logger.LogWarning("User not found with ID: {UserId}", userId);
                    return null;
                }
                
                return userProfile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by ID: {UserId}", userId);
                throw;
            }
        }

        public async Task<UserProfile?> GetUserByLoginAsync(string login)
        {
            try
            {
                _logger.LogInformation("Getting user by login: {Login}", login);
                
                // Find user by login
                var userProfile = await _spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == login));
                
                if (userProfile == null)
                {
                    _logger.LogWarning("User not found with login: {Login}", login);
                    return null;
                }
                
                return userProfile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by login: {Login}", login);
                throw;
            }
        }

        public async Task<bool> UpdateUserAsync(uint userId, string? login, string? password, int? role, string? email, string? phoneNumber, bool? isActive, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Updating user with ID: {UserId}", userId);
                
                // Find user by legacy ID
                var userProfile = await _spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.LegacyUserId == userId));
                
                if (userProfile == null)
                {
                    _logger.LogWarning("User not found with ID: {UserId}", userId);
                    return false;
                }
                
                // Call the UpdateUser reducer
                try
                {
                    await _spacetimeService.ExecuteReducerAsync(new Reducer.UpdateUser(
                        userProfile.UserId, 
                        login, 
                        password, // Will be hashed in the reducer
                        role,
                        phoneNumber,
                        email,
                        isActive,
                        actingUser
                    ), _ => true);
                    
                    _logger.LogInformation("Successfully updated user with ID: {UserId}", userId);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error calling UpdateUser reducer for user ID: {UserId}", userId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user with ID: {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> DeleteUserAsync(uint userId, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Deleting user with ID: {UserId}", userId);
                
                // Find user by legacy ID
                var userProfile = await _spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.LegacyUserId == userId));
                
                if (userProfile == null)
                {
                    _logger.LogWarning("User not found with ID: {UserId}", userId);
                    return false;
                }
                
                // Call the DeleteUser reducer
                try
                {
                    await _spacetimeService.ExecuteReducerAsync(new Reducer.DeleteUser(userProfile.UserId, actingUser), _ => true);
                    
                    _logger.LogInformation("Successfully deleted user with ID: {UserId}", userId);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error calling DeleteUser reducer for user ID: {UserId}", userId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user with ID: {UserId}", userId);
                throw;
            }
        }

        public async Task<IEnumerable<Role>> GetUserRolesAsync(uint userId)
        {
            try
            {
                _logger.LogInformation("Getting roles for user with ID: {UserId}", userId);
                
                // Find user by legacy ID
                var userProfile = await _spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.LegacyUserId == userId));
                
                if (userProfile == null)
                {
                    _logger.LogWarning("User not found with ID: {UserId}", userId);
                    return new List<Role>();
                }
                
                // Get user roles
                var userRoles = await _spacetimeService.ReadAsync(c => c.Db.UserRole.UserId.Filter(userProfile.UserId)
                    .ToList());
                
                if (userRoles.Count == 0)
                {
                    _logger.LogInformation("No roles found for user with ID: {UserId}", userId);
                    return new List<Role>();
                }
                
                // Get role details
                var roles = new List<Role>();
                foreach (var ur in userRoles)
                {
                    var role = await _spacetimeService.ReadAsync(c => c.Db.Role.RoleId.Find(ur.RoleId));
                    if (role != null && role.IsActive)
                    {
                        roles.Add(role);
                    }
                }
                
                _logger.LogInformation("Retrieved {Count} roles for user with ID: {UserId}", roles.Count, userId);
                return roles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting roles for user with ID: {UserId}", userId);
                throw;
            }
        }

        public async Task<IEnumerable<Permission>> GetUserPermissionsAsync(uint userId)
        {
            try
            {
                _logger.LogInformation("Getting permissions for user with ID: {UserId}", userId);
                
                // Find user by legacy ID
                var userProfile = await _spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.LegacyUserId == userId));
                
                if (userProfile == null)
                {
                    _logger.LogWarning("User not found with ID: {UserId}", userId);
                    return new List<Permission>();
                }
                
                // Get user roles
                var userRoles = await _spacetimeService.ReadAsync(c => c.Db.UserRole.UserId.Filter(userProfile.UserId)
                    .Select(ur => ur.RoleId)
                    .ToList());
                
                if (userRoles.Count == 0)
                {
                    _logger.LogInformation("No roles found for user with ID: {UserId}", userId);
                    return new List<Permission>();
                }
                
                // Get role permissions
                var rolePermissions = await _spacetimeService.ReadAsync(c => userRoles.SelectMany(roleId => c.Db.RolePermission.RoleId.Filter(roleId))
                    .Select(rp => rp.PermissionId)
                    .Distinct()
                    .ToList());
                
                if (rolePermissions.Count == 0)
                {
                    _logger.LogInformation("No permissions found for user with ID: {UserId}", userId);
                    return new List<Permission>();
                }
                
                // Get permission details
                var permissions = new List<Permission>();
                foreach (var permissionId in rolePermissions)
                {
                    var permission = await _spacetimeService.ReadAsync(c => c.Db.Permission.PermissionId.Find(permissionId));
                    if (permission != null && permission.IsActive)
                    {
                        permissions.Add(permission);
                    }
                }
                
                _logger.LogInformation("Retrieved {Count} permissions for user with ID: {UserId}", permissions.Count, userId);
                return permissions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting permissions for user with ID: {UserId}", userId);
                throw;
            }
        }

        public async Task<UserProfile?> GetCurrentUserAsync(string login)
        {
            try
            {
                _logger.LogInformation("Getting current user with login: {Login}", login);
                
                // Use the existing method to get user by login
                var user = await GetUserByLoginAsync(login);
                
                if (user == null)
                {
                    _logger.LogWarning("Current user not found with login: {Login}", login);
                    return null;
                }
                
                _logger.LogInformation("Successfully retrieved current user with login: {Login}", login);
                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user with login: {Login}", login);
                throw;
            }
        }

        public async Task<UserProfile?> CreateUserAsync(string login, string password, int role, string? email = null, string? phoneNumber = null, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Creating new user with login: {Login}", login);
                
                // Check if user already exists
                var existingUser = await GetUserByLoginAsync(login);
                if (existingUser != null)
                {
                    _logger.LogWarning("User with login {Login} already exists", login);
                    return null;
                }
                
                // Call the RegisterUser reducer
                // The v2 Core-backed creator allocates a distinct account identity; legacy
                // RegisterUser without newUserIdentity would collide with the API sender.
                var actor = actingUser ?? _spacetimeService.GetLocalIdentity() ?? throw new DatabaseUnavailableException("Database identity unavailable.");
                await _spacetimeService.ExecuteReducerAsync(new Reducer.CreateUserV2(Guid.NewGuid().ToString("N"), actor,
                    login, password, email ?? "", phoneNumber ?? "", (uint?)role), _ => true);
                
                // Get the newly created user
                var newUser = await GetUserByLoginAsync(login);
                if (newUser == null)
                {
                    _logger.LogError("User was not created properly");
                    return null;
                }
                
                _logger.LogInformation("Successfully created user with login: {Login}", login);
                return newUser;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user with login: {Login}", login);
                throw;
            }
        }
    }
}
