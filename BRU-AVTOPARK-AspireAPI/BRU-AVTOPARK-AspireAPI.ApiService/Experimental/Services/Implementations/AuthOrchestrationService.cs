using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Serilog;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Orchestrates authentication flows. All business logic that was embedded in
/// the original AuthController action methods now lives here, making it
/// independently testable and reusable across controllers.
/// </summary>
public sealed class AuthOrchestrationService : IAuthOrchestrationService
{
    private readonly TicketSalesApp.Services.Interfaces.IAuthenticationService _authService;
    private readonly IUserService _userService;
    private readonly ISpacetimeDBService _spacetimeService;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthOrchestrationService> _logger;

    public AuthOrchestrationService(
        TicketSalesApp.Services.Interfaces.IAuthenticationService authService,
        IUserService userService,
        ISpacetimeDBService spacetimeService,
        ITokenService tokenService,
        ILogger<AuthOrchestrationService> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AuthenticationResult?> AuthenticateAsync(string username, string password)
    {
        _logger.LogInformation("Authentication attempt for user: {Username}", username);

        var user = await _authService.AuthenticateAsync(username, password);
        if (user is null)
        {
            _logger.LogWarning("Authentication failed for user: {Username}", username);
            return null;
        }

        var conn = _spacetimeService.GetConnection();
        var settings = conn.Db.UserSettings.Iter()
            .FirstOrDefault(s => s.UserId.Equals(user.UserId));

        // Lazily create default settings if missing
        if (settings is null)
        {
            _logger.LogWarning("User settings not found for {Username}, creating defaults", username);
            conn.Reducers.CreateUserSettings(user.UserId);
            await Task.Delay(100);
            settings = conn.Db.UserSettings.Iter()
                .FirstOrDefault(s => s.UserId.Equals(user.UserId));
        }

        var roles = conn.Db.UserRole.Iter()
            .Where(ur => ur.UserId.Equals(user.UserId))
            .Join(conn.Db.Role.Iter(), ur => ur.RoleId, r => r.RoleId, (ur, r) => r)
            .ToList();

        var primaryRole = roles.FirstOrDefault()?.LegacyRoleId ?? 0;

        return new AuthenticationResult
        {
            UserId = user.UserId.ToString(),
            Username = user.Login,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            TotpEnabled = settings?.TotpEnabled ?? false,
            WebAuthnEnabled = settings?.WebAuthnEnabled ?? false,
            PrimaryRole = primaryRole,
            Roles = roles.Select(r => r.Name).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<RegisterResult> RegisterAsync(
        string username, string password, int role,
        string? email, string? phoneNumber, string? adminIdentity)
    {
        _logger.LogInformation("Registration attempt for user: {Username}", username);

        try
        {
            var success = await _authService.RegisterAsync(
                username, password, role, email, phoneNumber,
                adminIdentity != null ? SpacetimeDB.Identity.From(Convert.FromBase64String(adminIdentity)) : null,
                null);

            if (!success)
                return new RegisterResult(false, "Failed to register user");

            var newUser = await _userService.GetUserByLoginAsync(username);
            if (newUser is null)
                return new RegisterResult(false, "User was created but could not be retrieved");

            return new RegisterResult(true, User: new UserDto
            {
                Id = newUser.LegacyUserId,
                Username = newUser.Login,
                Email = newUser.Email,
                PhoneNumber = newUser.PhoneNumber,
                Role = _authService.GetUserRole(newUser.UserId)
            });
        }
        catch (Exception ex) when (ex.Message?.Contains("Unauthorized") == true)
        {
            _logger.LogWarning("Role assignment failed: {Error}", ex.Message);
            var fallback = await _userService.GetUserByLoginAsync(username);
            if (fallback is not null)
            {
                return new RegisterResult(true,
                    "User created with default role (requested role assignment failed).",
                    new UserDto
                    {
                        Id = fallback.LegacyUserId,
                        Username = fallback.Login,
                        Email = fallback.Email,
                        PhoneNumber = fallback.PhoneNumber,
                        Role = _authService.GetUserRole(fallback.UserId)
                    });
            }
            return new RegisterResult(false, "Registration failed during role assignment");
        }
    }

    /// <inheritdoc />
    public async Task<ClaimResult> ClaimAccountAsync(
        string username, string password, bool generateNewIdentity)
    {
        _logger.LogInformation("Account claim attempt for: {Username}", username);

        var conn = _spacetimeService.GetConnection();
        var user = conn.Db.UserProfile.Iter()
            .FirstOrDefault(u => u.Login == username);

        if (user is null)
            return new ClaimResult(false, "Account not found. Please check your username.");

        string? newIdentity = null;
        if (generateNewIdentity)
        {
            // Identity generation would be delegated to the SpacetimeDB service
            newIdentity = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        }

        conn.Reducers.ClaimUserAccount(username, password, newIdentity);
        _logger.LogInformation("Account claim processed for: {Username}", username);

        return new ClaimResult(true);
    }

    /// <inheritdoc />
    public bool IsAdmin(ClaimsPrincipal? user, string? bearerToken)
    {
        // Check ASP.NET Core identity first
        if (user?.Identity?.IsAuthenticated == true)
        {
            if (user.IsInRole("Administrator")) return true;

            var primaryRole = user.FindFirst("primary_role");
            if (primaryRole?.Value == "1") return true;

            var roleClaims = user.Claims.Where(c =>
                c.Type == ClaimTypes.Role || c.Type == "role");
            if (roleClaims.Any(c => c.Value is "Administrator" or "1"))
                return true;
        }

        // Fallback: parse bearer token directly
        if (string.IsNullOrEmpty(bearerToken)) return false;

        var payload = _tokenService.ReadTokenPayload(bearerToken);
        if (payload is null) return false;

        return payload.PrimaryRole == 1
               || payload.Roles.Any(r => r is "Administrator" or "1");
    }

    /// <inheritdoc />
    public bool HasPermission(ClaimsPrincipal? user, string? bearerToken, string permissionName)
    {
        if (user?.Identity?.IsAuthenticated == true)
        {
            if (user.Claims.Any(c => c.Type == "permission" && c.Value == permissionName))
                return true;
        }

        if (string.IsNullOrEmpty(bearerToken)) return false;

        var payload = _tokenService.ReadTokenPayload(bearerToken);
        return payload?.Permissions.Contains(permissionName) == true;
    }
}
