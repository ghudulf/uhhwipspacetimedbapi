using BRU_AVTOPARK.Models.ViewModels;
using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.Extensions.Logging;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Aggregates profile data from multiple SpacetimeDB tables.
/// Replaces profile-building logic from the original AuthController.
/// </summary>
public class ProfileService : IProfileService
{
    private readonly ISpacetimeDBService _spacetimeService;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(
        ISpacetimeDBService spacetimeService,
        ILogger<ProfileService> logger)
    {
        _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ProfileViewModel?> GetProfileAsync(string userId, string? token)
    {
        try
        {
            var conn = _spacetimeService.GetConnection();

            // Get user profile
            var user = conn.Db.UserProfile.Iter()
                .FirstOrDefault(u => u.UserId.ToString() == userId);

            if (user is null)
            {
                _logger.LogWarning("User profile not found for userId: {UserId}", userId);
                return null;
            }

            // Get user settings
            var settings = conn.Db.UserSettings.Iter()
                .FirstOrDefault(s => s.UserId.Equals(user.UserId));

            // Get WebAuthn credentials
            var webAuthnCreds = conn.Db.WebAuthnCredential.Iter()
                .Where(c => c.UserId.Equals(user.UserId))
                .Select(c => new WebAuthnCredentialViewModel
                {
                    Id = Convert.ToBase64String(c.CredentialId.ToArray()),
                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)c.CreatedAt).DateTime,
                    IsActive = c.IsActive
                })
                .ToList();

            // Get user roles
            var userRoleIds = conn.Db.UserRole.Iter()
                .Where(ur => ur.UserId.Equals(user.UserId))
                .Select(ur => ur.RoleId)
                .ToList();

            var roles = conn.Db.Role.Iter()
                .Where(r => userRoleIds.Contains(r.RoleId) && r.IsActive)
                .Select(r => new RoleViewModel
                {
                    LegacyRoleId = (int)r.LegacyRoleId,
                    Name = r.Name,
                    Priority = (int)r.Priority,
                    IsActive = r.IsActive
                })
                .ToList();

            // Get permissions through roles
            var permissionIds = conn.Db.RolePermission.Iter()
                .Where(rp => userRoleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToList();

            var permissions = conn.Db.Permission.Iter()
                .Where(p => permissionIds.Contains(p.PermissionId) && p.IsActive)
                .Select(p => new PermissionViewModel
                {
                    Name = p.Name,
                    IsActive = p.IsActive
                })
                .ToList();

            return new ProfileViewModel
            {
                User = new UserProfileViewModel
                {
                    UserId = user.UserId.ToString(),
                    LegacyUserId = user.LegacyUserId,
                    Login = user.Login,
                    Email = user.Email,
                    PhoneNumber = user.PhoneNumber,
                    EmailConfirmed = user.EmailConfirmed,
                    PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                    IsActive = user.IsActive,
                    Xuid = user.Xuid?.ToString()
                },
                TotpEnabled = settings?.TotpEnabled ?? false,
                WebAuthnEnabled = settings?.WebAuthnEnabled ?? false,
                WebAuthnCredentials = webAuthnCreds,
                Roles = roles,
                Permissions = permissions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving profile for userId: {UserId}", userId);
            return null;
        }
    }
}

