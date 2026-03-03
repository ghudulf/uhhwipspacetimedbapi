using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BRU_AVTOPARK_AspireAPI.ApiService.Models.Auth;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    public partial class AuthController
    {
        // ── GET /api/auth/magic-link ────────────────────────────────────

        [HttpGet("magic-link")]
        [AllowAnonymous]
        public IActionResult MagicLinkPage([FromQuery] string? error, [FromQuery] string? message)
        {
            return HtmlContent(_htmlRenderer.RenderMagicLinkForm(error, message));
        }

        // ── POST /api/auth/magic-link/send ──────────────────────────────

        [HttpPost("magic-link/send")]
        [AllowAnonymous]
        public async Task<IActionResult> SendMagicLink([FromBody] MagicLinkRequest? json = null)
        {
            try
            {
                var email = json?.Email ?? Request.Form["email"].ToString();

                if (string.IsNullOrWhiteSpace(email))
                {
                    if (IsBrowserRequest())
                        return Redirect("/api/auth/magic-link?error=Email+is+required");
                    return BadRequest(new ApiResponse<object> { Success = false, Message = "Email is required" });
                }

                var (success, errorMessage) = await _magicLinkService.SendMagicLinkAsync(
                    email,
                    Request.Headers["User-Agent"].ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                if (!success)
                {
                    if (IsBrowserRequest())
                        return Redirect($"/api/auth/magic-link?error={Uri.EscapeDataString(errorMessage ?? "Failed to send magic link")}");
                    return BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Failed to send" });
                }

                if (IsBrowserRequest())
                    return Redirect($"/api/auth/magic-link?message={Uri.EscapeDataString("Magic link sent! Check your email.")}");

                return Ok(new ApiResponse<MagicLinkResponse>
                {
                    Success = true,
                    Message = "Magic link sent",
                    Data = new MagicLinkResponse { Sent = true, Email = email }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending magic link");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error sending magic link" });
            }
        }

        // ── GET /api/auth/validate-magic-link ───────────────────────────

        [HttpGet("validate-magic-link")]
        [AllowAnonymous]
        public async Task<IActionResult> ValidateMagicLinkGet([FromQuery] string token)
        {
            try
            {
                var (success, user, errorMessage) = await _magicLinkService.ValidateMagicLinkAsync(token);

                if (!success || user == null)
                {
                    if (IsBrowserRequest())
                        return Redirect($"/api/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Invalid magic link")}");
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Invalid magic link" });
                }

                var jwtToken = _tokenService.GenerateJwtToken(user);
                await _magicLinkService.MarkMagicLinkAsUsedAsync(token);

                if (IsBrowserRequest())
                    return Redirect($"/api/auth/success?token={Uri.EscapeDataString(jwtToken)}");

                return Ok(new ApiResponse<ValidateMagicLinkResponse>
                {
                    Success = true,
                    Message = "Magic link validated",
                    Data = new ValidateMagicLinkResponse { Token = jwtToken, User = ToUserDto(user) }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating magic link");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error validating magic link" });
            }
        }

        // ── POST /api/auth/validate-magic-link ──────────────────────────

        [HttpPost("validate-magic-link")]
        [AllowAnonymous]
        public async Task<IActionResult> ValidateMagicLinkPost([FromBody] ValidateMagicLinkRequest request)
        {
            return await ValidateMagicLinkGet(request.Token);
        }
    }
}
