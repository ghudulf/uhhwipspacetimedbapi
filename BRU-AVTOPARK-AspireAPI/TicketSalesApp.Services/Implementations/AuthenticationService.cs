using Microsoft.Extensions.Logging;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Linq;

namespace TicketSalesApp.Services.Implementations
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IRoleService _roleService;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            ISpacetimeDBService spacetimeService, 
            IRoleService roleService,
            ILogger<AuthenticationService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UserProfile?> AuthenticateAsync(string login, string password)
        {
            try
            {
                _logger.LogInformation("Attempting to authenticate user: {Login}", login);

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("Authentication attempt with empty login or password");
                    return null;
                }

                var conn = _spacetimeService.GetConnection();
                
                // Find user by login
                var userProfile = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == login && u.IsActive);

                if (userProfile == null)
                {
                    _logger.LogWarning("Authentication failed: User not found or inactive for login: {Login}", login);
                    return null;
                }

                if (VerifyPassword(password, userProfile.PasswordHash))
                {
                    _logger.LogInformation("User {Login} authenticated successfully", login);
                    
                    // Call the AuthenticateUser reducer to update last login time
                    conn.Reducers.AuthenticateUser(login, password);
                    
                    return userProfile;
                }

                _logger.LogWarning("Authentication failed: Invalid password for user: {Login}", login);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while authenticating user: {Login}", login);
                throw;
            }
        }

        public async Task<bool> RegisterAsync(string login, string password, int role, string? email = null, string? phoneNumber = null)
        {
            try
            {
                _logger.LogInformation("Attempting to register new user with login: {Login}", login);

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("Registration attempt with empty login or password");
                    return false;
                }

                var conn = _spacetimeService.GetConnection();
                
                // Check if user already exists
                var existingUser = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == login);

                if (existingUser != null)
                {
                    _logger.LogWarning("Registration failed: User already exists with login: {Login}", login);
                    return false;
                }

                // Call the RegisterUser reducer
                conn.Reducers.RegisterUser(login, password, email, phoneNumber, (uint?)role, null);
                
                // Wait a moment for the reducer to complete and the subscription to update
                await Task.Delay(100);
                
                // Get the newly created user
                var newUser = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == login);
                
                if (newUser == null)
                {
                    _logger.LogError("Registration failed: User was not created properly");
                    return false;
                }
                
                _logger.LogInformation("Successfully registered new user with login: {Login}", login);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while registering user: {Login}", login);
                throw;
            }
        }

        public async Task<UserProfile?> AuthenticateDirectQRAsync(string login, string validationToken)
        {
            try
            {
                _logger.LogInformation("Attempting direct QR authentication for user: {Login}", login);

                if (string.IsNullOrEmpty(login))
                {
                    _logger.LogWarning("Direct QR authentication attempt with empty login");
                    return null;
                }

                var conn = _spacetimeService.GetConnection();
                
                // Find user by login
                var userProfile = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == login && u.IsActive);

                if (userProfile == null)
                {
                    _logger.LogWarning("Direct QR authentication failed: User not found or inactive for login: {Login}", login);
                    return null;
                }

                // In a real implementation, you would validate the QR token here
                // For now, we'll just update the last login time
                conn.Reducers.AuthenticateUser(login, "");

                _logger.LogInformation("User {Login} authenticated successfully via direct QR", login);
                return userProfile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while authenticating user via direct QR: {Login}", login);
                throw;
            }
        }
        
        public int GetUserRole(Identity userId)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                
                // Find user roles
                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.UserId.Equals(userId))
                    .ToList();
                
                if (userRoles.Count == 0)
                    return 0; // Default role
                
                // Get the highest priority role
                uint highestPriorityRoleId = userRoles.First().RoleId;
                uint highestPriority = 0;
                
                foreach (var userRole in userRoles)
                {
                    var role = conn.Db.Role.RoleId.Find(userRole.RoleId);
                    if (role != null && role.Priority > highestPriority)
                    {
                        highestPriority = role.Priority;
                        highestPriorityRoleId = role.RoleId;
                    }
                }
                
                // Get the legacy role ID for compatibility
                var highestRole = conn.Db.Role.RoleId.Find(highestPriorityRoleId);
                return highestRole?.LegacyRoleId ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user role for identity {Identity}", userId);
                return 0; // Default role on error
            }
        }

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                // Convert the input string to a byte array and compute the hash
                byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));

                // Convert the byte array to a string
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < hashedBytes.Length; i++)
                {
                    builder.Append(hashedBytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private bool VerifyPassword(string password, string? hash)
        {
            if (string.IsNullOrEmpty(hash))
                return false;
                
            try
            {
                return HashPassword(password) == hash;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while verifying password");
                throw;
            }
        }
    }
}
