using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using TicketSalesApp.AdminServer.Experimental.Services.Interfaces;

namespace TicketSalesApp.AdminServer.Controllers
{
    /// <summary>
    /// Admin-only controller for managing feature flags and other administrative tasks.
    /// All endpoints require Admin role authorization.
    /// </summary>
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly IFeatureFlagService _featureFlagService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IFeatureFlagService featureFlagService,
            ILogger<AdminController> logger)
        {
            _featureFlagService = featureFlagService ?? throw new ArgumentNullException(nameof(featureFlagService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Render the feature flags management UI.
        /// </summary>
        /// <returns>HTML view for feature flags management</returns>
        [HttpGet("feature-flags-ui")]
        public IActionResult FeatureFlagsUI()
        {
            // Additional manual admin check (defense in depth)
            if (!IsAdmin())
            {
                _logger.LogWarning("Unauthorized access attempt to FeatureFlagsUI by non-admin user");
                return Forbid();
            }

            return View("~/Experimental/Views/Admin/FeatureFlags.cshtml");
        }

        /// <summary>
        /// Get all feature flags and their current state.
        /// </summary>
        /// <returns>Dictionary of flag names and their enabled state</returns>
        [HttpGet("feature-flags")]
        public async Task<IActionResult> GetFeatureFlags()
        {
            try
            {
                // Additional manual admin check (defense in depth)
                if (!IsAdmin())
                {
                    _logger.LogWarning("Unauthorized access attempt to GetFeatureFlags by non-admin user");
                    return Forbid();
                }

                var flags = await _featureFlagService.GetAllFlagsAsync();

                _logger.LogInformation("Feature flags retrieved by admin user. Count: {Count}", flags.Count);

                return Ok(new
                {
                    success = true,
                    flags = flags,
                    count = flags.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving feature flags");
                return StatusCode(500, new
                {
                    success = false,
                    error = "Failed to retrieve feature flags",
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Update a specific feature flag.
        /// </summary>
        /// <param name="flagName">Name of the feature flag</param>
        /// <param name="request">Request containing the new enabled state</param>
        /// <returns>Success response</returns>
        [HttpPut("feature-flags/{flagName}")]
        public async Task<IActionResult> UpdateFeatureFlag(string flagName, [FromBody] UpdateFeatureFlagRequest request)
        {
            try
            {
                // Additional manual admin check (defense in depth)
                if (!IsAdmin())
                {
                    _logger.LogWarning("Unauthorized access attempt to UpdateFeatureFlag by non-admin user");
                    return Forbid();
                }

                if (string.IsNullOrWhiteSpace(flagName))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Flag name cannot be empty"
                    });
                }

                // Get the current user's identity for audit logging
                var userIdentity = GetCurrentUserIdentity();
                if (userIdentity == null)
                {
                    _logger.LogWarning("Could not determine user identity for feature flag update");
                    return Unauthorized(new
                    {
                        success = false,
                        error = "Could not determine user identity"
                    });
                }

                await _featureFlagService.UpdateFlagAsync(flagName, request.Enabled, userIdentity.Value);

                _logger.LogInformation(
                    "Feature flag {FlagName} updated to {Enabled} by {UserId}",
                    flagName,
                    request.Enabled,
                    userIdentity.Value);

                return Ok(new
                {
                    success = true,
                    message = "Feature flag updated successfully",
                    flagName = flagName,
                    enabled = request.Enabled
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid feature flag name: {FlagName}", flagName);
                return BadRequest(new
                {
                    success = false,
                    error = "Invalid feature flag name",
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating feature flag {FlagName}", flagName);
                return StatusCode(500, new
                {
                    success = false,
                    error = "Failed to update feature flag",
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Update multiple feature flags at once.
        /// </summary>
        /// <param name="request">Request containing multiple flag updates</param>
        /// <returns>Success response</returns>
        [HttpPost("feature-flags/bulk")]
        public async Task<IActionResult> BulkUpdateFeatureFlags([FromBody] BulkUpdateFeatureFlagsRequest request)
        {
            try
            {
                // Additional manual admin check (defense in depth)
                if (!IsAdmin())
                {
                    _logger.LogWarning("Unauthorized access attempt to BulkUpdateFeatureFlags by non-admin user");
                    return Forbid();
                }

                if (request?.Flags == null || request.Flags.Count == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Flags dictionary cannot be empty"
                    });
                }

                // Get the current user's identity for audit logging
                var userIdentity = GetCurrentUserIdentity();
                if (userIdentity == null)
                {
                    _logger.LogWarning("Could not determine user identity for bulk feature flag update");
                    return Unauthorized(new
                    {
                        success = false,
                        error = "Could not determine user identity"
                    });
                }

                await _featureFlagService.BulkUpdateFlagsAsync(request.Flags, userIdentity.Value);

                _logger.LogInformation(
                    "Bulk feature flag update completed by {UserId}. Count: {Count}",
                    userIdentity.Value,
                    request.Flags.Count);

                return Ok(new
                {
                    success = true,
                    message = "Feature flags updated successfully",
                    count = request.Flags.Count
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid bulk feature flag update request");
                return BadRequest(new
                {
                    success = false,
                    error = "Invalid request",
                    message = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Bulk feature flag update partially failed");
                return StatusCode(207, new // 207 Multi-Status
                {
                    success = false,
                    error = "Bulk update partially failed",
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk feature flag update");
                return StatusCode(500, new
                {
                    success = false,
                    error = "Failed to update feature flags",
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Reset all feature flags to appsettings.json defaults.
        /// Clears all runtime overrides.
        /// </summary>
        /// <returns>Success response</returns>
        [HttpPost("feature-flags/reset")]
        public async Task<IActionResult> ResetFeatureFlags()
        {
            try
            {
                // Additional manual admin check (defense in depth)
                if (!IsAdmin())
                {
                    _logger.LogWarning("Unauthorized access attempt to ResetFeatureFlags by non-admin user");
                    return Forbid();
                }

                // Get the current user's identity for audit logging
                var userIdentity = GetCurrentUserIdentity();
                if (userIdentity == null)
                {
                    _logger.LogWarning("Could not determine user identity for feature flag reset");
                    return Unauthorized(new
                    {
                        success = false,
                        error = "Could not determine user identity"
                    });
                }

                await _featureFlagService.ResetToDefaultsAsync(userIdentity.Value);

                _logger.LogInformation("All feature flag overrides reset to defaults by {UserId}", userIdentity.Value);

                return Ok(new
                {
                    success = true,
                    message = "All feature flags reset to appsettings.json defaults"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting feature flags");
                return StatusCode(500, new
                {
                    success = false,
                    error = "Failed to reset feature flags",
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Get audit log of feature flag changes.
        /// </summary>
        /// <param name="limit">Maximum number of log entries to return (default: 100)</param>
        /// <returns>List of audit log entries</returns>
        [HttpGet("feature-flags/audit-log")]
        public async Task<IActionResult> GetFeatureFlagAuditLog([FromQuery] int limit = 100)
        {
            try
            {
                // Additional manual admin check (defense in depth)
                if (!IsAdmin())
                {
                    _logger.LogWarning("Unauthorized access attempt to GetFeatureFlagAuditLog by non-admin user");
                    return Forbid();
                }

                if (limit < 1 || limit > 1000)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Limit must be between 1 and 1000"
                    });
                }

                var auditLog = await _featureFlagService.GetAuditLogAsync(limit);

                return Ok(new
                {
                    success = true,
                    auditLog = auditLog,
                    count = auditLog.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving feature flag audit log");
                return StatusCode(500, new
                {
                    success = false,
                    error = "Failed to retrieve audit log",
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Helper method to check if the current user has administrator role.
        /// </summary>
        private bool IsAdmin()
        {
            try
            {
                if (User?.Identity?.IsAuthenticated == true)
                {
                    // Check primary role first
                    var primaryRole = User.FindFirst("primary_role");
                    if (primaryRole?.Value == "1")
                    {
                        return true;
                    }

                    // Check role claims
                    var roleClaims = User.FindAll("role");
                    if (roleClaims.Any(c => c.Value == "1" || c.Value == "Administrator"))
                    {
                        return true;
                    }

                    // Check standard role claims
                    var standardRoleClaims = User.FindAll(System.Security.Claims.ClaimTypes.Role);
                    if (standardRoleClaims.Any(c => c.Value == "1" || c.Value == "Administrator"))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking admin status");
                return false;
            }
        }

        /// <summary>
        /// Helper method to get the current user's SpacetimeDB Identity.
        /// </summary>
        private Identity? GetCurrentUserIdentity()
        {
            try
            {
                // Try to get identity from claims
                var identityClaim = User.FindFirst("identity");
                if (identityClaim != null && !string.IsNullOrEmpty(identityClaim.Value))
                {
                    return Identity.FromHexString(identityClaim.Value);
                }

                // Try to get from sub claim
                var subClaim = User.FindFirst("sub");
                if (subClaim != null && !string.IsNullOrEmpty(subClaim.Value))
                {
                    return Identity.FromHexString(subClaim.Value);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse user identity from claims");
                return null;
            }
        }
    }

    /// <summary>
    /// Request model for updating a single feature flag.
    /// </summary>
    public class UpdateFeatureFlagRequest
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Request model for bulk updating feature flags.
    /// </summary>
    public class BulkUpdateFeatureFlagsRequest
    {
        public Dictionary<string, bool> Flags { get; set; }
    }
}
