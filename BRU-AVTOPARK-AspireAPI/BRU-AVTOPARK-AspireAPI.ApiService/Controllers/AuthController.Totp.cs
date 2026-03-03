using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BRU_AVTOPARK_AspireAPI.ApiService.Models.Auth;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    public partial class AuthController
    {
        // ── GET /api/auth/totp/setup ────────────────────────────────────

        [HttpGet("totp/setup")]
        [AllowAnonymous]
        public async Task<IActionResult> TotpSetup([FromQuery] string? token)
        {
            try
            {
                var identity = GetUserIdentity();
                if (identity == null)
                    return Redirect("/api/auth/login?error=Authentication+required");

                var user = GetUserByIdentity(identity);
                if (user == null)
                    return Redirect("/api/auth/login?error=User+not+found");

                var (success, secretKey, qrCodeUri, errorMessage) =
                    await _totpService.SetupTotpAsync(identity, user.Login);

                if (!success || secretKey == null || qrCodeUri == null)
                {
                    return IsBrowserRequest()
                        ? Redirect($"/api/auth/error?message={Uri.EscapeDataString(errorMessage ?? "TOTP setup failed")}")
                        : BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "TOTP setup failed" });
                }

                if (IsBrowserRequest())
                    return HtmlContent(_htmlRenderer.RenderTotpSetup(qrCodeUri, secretKey));

                return Ok(new ApiResponse<TotpSetupResponse>
                {
                    Success = true,
                    Data = new TotpSetupResponse { SecretKey = secretKey, QrCodeUri = qrCodeUri }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during TOTP setup");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "TOTP setup error" });
            }
        }

        // ── POST /api/auth/totp/verify ──────────────────────────────────

        [HttpPost("totp/verify")]
        [AllowAnonymous]
        public async Task<IActionResult> TotpVerify([FromBody] VerifyTotpRequest? json = null)
        {
            try
            {
                var code = json?.Code ?? Request.Form["code"].ToString();
                var secretKey = json?.SecretKey ?? Request.Form["secretKey"].ToString();

                var identity = GetUserIdentity();
                if (identity == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Authentication required" });

                var (success, errorMessage) = await _totpService.EnableTotpAsync(identity, code, secretKey);

                if (!success)
                {
                    return IsBrowserRequest()
                        ? Redirect($"/api/auth/totp/setup?error={Uri.EscapeDataString(errorMessage ?? "Verification failed")}")
                        : BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Verification failed" });
                }

                if (IsBrowserRequest())
                    return Redirect("/api/auth/profile?message=Two-factor+authentication+enabled");

                return Ok(new ApiResponse<VerifyTotpResponse>
                {
                    Success = true,
                    Message = "TOTP enabled",
                    Data = new VerifyTotpResponse { Enabled = true }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying TOTP");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error verifying TOTP" });
            }
        }

        // ── POST /api/auth/totp/disable ─────────────────────────────────

        [HttpPost("totp/disable")]
        [AllowAnonymous]
        public async Task<IActionResult> TotpDisable()
        {
            try
            {
                var identity = GetUserIdentity();
                if (identity == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Authentication required" });

                var (success, errorMessage) = await _totpService.DisableTotpAsync(identity);

                if (!success)
                    return BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Failed to disable TOTP" });

                return Ok(new ApiResponse<DisableTotpResponse>
                {
                    Success = true,
                    Message = "TOTP disabled",
                    Data = new DisableTotpResponse { Disabled = true }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling TOTP");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error disabling TOTP" });
            }
        }

        // ── POST /api/auth/totp/validate ────────────────────────────────

        [HttpPost("totp/validate")]
        [AllowAnonymous]
        public async Task<IActionResult> TotpValidate([FromBody] ValidateTotpRequest request)
        {
            try
            {
                var (success, errorMessage) = await _totpService.ValidateTotpWithTokenAsync(request.TempToken, request.Code);

                if (!success)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Invalid TOTP code" });

                // Resolve user from temp token
                var conn = _spacetimeService.GetConnection();
                var twoFaToken = conn.Db.TwoFactorToken.Iter()
                    .FirstOrDefault(t => t.Token == request.TempToken && !t.IsUsed);

                if (twoFaToken == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Invalid or expired token" });

                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(twoFaToken.UserId));

                if (user == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "User not found" });

                var token = _tokenService.GenerateJwtToken(user);

                return Ok(new ApiResponse<ValidateTotpResponse>
                {
                    Success = true,
                    Message = "TOTP validation successful",
                    Data = new ValidateTotpResponse { Token = token, User = ToUserDto(user) }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating TOTP");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error validating TOTP" });
            }
        }
    }
}
