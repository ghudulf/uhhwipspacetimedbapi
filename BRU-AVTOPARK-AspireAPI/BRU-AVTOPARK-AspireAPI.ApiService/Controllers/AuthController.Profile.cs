using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BRU_AVTOPARK_AspireAPI.ApiService.Models.Auth;
using BRU_AVTOPARK_AspireAPI.ApiService.Services.Auth;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    public partial class AuthController
    {
        // ── GET /api/auth/profile ───────────────────────────────────────

        [HttpGet("profile")]
        [AllowAnonymous]
        public async Task<IActionResult> Profile([FromQuery] string? token)
        {
            try
            {
                // Try to resolve user from token query param or auth header
                SpacetimeDB.Types.UserProfile? user = null;
                var identity = GetUserIdentity();

                if (identity != null)
                {
                    user = GetUserByIdentity(identity);
                }
                else if (!string.IsNullOrEmpty(token))
                {
                    // Parse the JWT to find the user
                    var principal = _tokenService.ValidateToken(token);
                    var loginClaim = principal?.FindFirst("unique_name")?.Value;
                    if (loginClaim != null)
                        user = await _userService.GetUserByLoginAsync(loginClaim);
                }

                if (user == null)
                    return Redirect("/api/auth/login?error=Authentication+required");

                var conn = _spacetimeService.GetConnection();
                var userSettings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(user.UserId));

                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.UserId.Equals(user.UserId))
                    .Select(ur => ur.RoleId)
                    .ToList();

                var roles = conn.Db.Role.Iter()
                    .Where(r => userRoles.Contains(r.RoleId) && r.IsActive)
                    .Select(r => r.Name)
                    .ToList();

                var rolePermissionIds = conn.Db.RolePermission.Iter()
                    .Where(rp => userRoles.Contains(rp.RoleId))
                    .Select(rp => rp.PermissionId)
                    .Distinct()
                    .ToList();

                var permissions = conn.Db.Permission.Iter()
                    .Where(p => rolePermissionIds.Contains(p.PermissionId) && p.IsActive)
                    .Select(p => p.Name)
                    .ToList();

                if (IsBrowserRequest())
                {
                    var model = new ProfileViewModel
                    {
                        Username = user.Login,
                        Email = user.Email,
                        PhoneNumber = user.PhoneNumber,
                        Token = token ?? "",
                        TotpEnabled = userSettings?.TotpEnabled ?? false,
                        WebAuthnEnabled = userSettings?.WebAuthnEnabled ?? false,
                        Roles = roles,
                        Permissions = permissions
                    };

                    return HtmlContent(_htmlRenderer.RenderProfile(model));
                }

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new
                    {
                        user.Login,
                        user.Email,
                        user.PhoneNumber,
                        TotpEnabled = userSettings?.TotpEnabled ?? false,
                        WebAuthnEnabled = userSettings?.WebAuthnEnabled ?? false,
                        Roles = roles,
                        Permissions = permissions
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading profile");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error loading profile" });
            }
        }

        // ── GET /api/auth/logout ────────────────────────────────────────

        [HttpGet("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout()
        {
            try
            {
                await HttpContext.SignOutAsync(
                    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error clearing auth cookie during logout");
            }

            if (IsBrowserRequest())
            {
                return HtmlContent(@"
                    <script>
                        localStorage.removeItem('auth_token');
                        window.location.href = '/api/auth/login?message=You+have+been+logged+out';
                    </script>");
            }

            return Ok(new ApiResponse<object> { Success = true, Message = "Logged out" });
        }
    }
}
