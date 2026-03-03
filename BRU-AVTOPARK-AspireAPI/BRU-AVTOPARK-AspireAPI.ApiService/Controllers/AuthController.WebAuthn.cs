using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Fido2NetLib;
using BRU_AVTOPARK_AspireAPI.ApiService.Models.Auth;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    public partial class AuthController
    {
        // ── POST /api/auth/webauthn/register/options ────────────────────

        [HttpPost("webauthn/register/options")]
        [AllowAnonymous]
        public async Task<IActionResult> WebAuthnRegisterOptions()
        {
            try
            {
                var identity = GetUserIdentity();
                if (identity == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Authentication required" });

                var user = GetUserByIdentity(identity);
                if (user == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "User not found" });

                var (success, options, errorMessage) =
                    await _webAuthnService.GetCredentialCreateOptionsAsync(identity, user.Login);

                if (!success || options == null)
                    return BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Failed to create options" });

                if (IsBrowserRequest())
                {
                    var optionsJson = JsonSerializer.Serialize(options);
                    return HtmlContent(_htmlRenderer.RenderWebAuthnRegistration(
                        System.Net.WebUtility.HtmlEncode(optionsJson)));
                }

                return Ok(new ApiResponse<WebAuthnRegisterOptionsResponse>
                {
                    Success = true,
                    Data = new WebAuthnRegisterOptionsResponse { Options = options }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating WebAuthn register options");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error creating options" });
            }
        }

        // ── POST /api/auth/webauthn/register/complete ───────────────────

        [HttpPost("webauthn/register/complete")]
        [AllowAnonymous]
        public async Task<IActionResult> WebAuthnRegisterComplete([FromBody] WebAuthnRegisterCompleteRequest request)
        {
            try
            {
                var identity = GetUserIdentity();
                if (identity == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Authentication required" });

                var user = GetUserByIdentity(identity);
                if (user == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "User not found" });

                var (success, errorMessage) =
                    await _webAuthnService.CompleteRegistrationAsync(identity, user.Login, request.AttestationResponse);

                if (!success)
                    return BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Registration failed" });

                return Ok(new ApiResponse<WebAuthnRegisterCompleteResponse>
                {
                    Success = true,
                    Message = "Security key registered",
                    Data = new WebAuthnRegisterCompleteResponse { Registered = true }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing WebAuthn registration");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error completing registration" });
            }
        }

        // ── POST /api/auth/webauthn/login/options ───────────────────────

        [HttpPost("webauthn/login/options")]
        [AllowAnonymous]
        public async Task<IActionResult> WebAuthnLoginOptions([FromBody] WebAuthnLoginOptionsRequest request)
        {
            try
            {
                var (success, options, errorMessage) =
                    await _webAuthnService.GetAssertionOptionsAsync(request.Username);

                if (!success || options == null)
                    return BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Failed to create options" });

                return Ok(new ApiResponse<WebAuthnLoginOptionsResponse>
                {
                    Success = true,
                    Data = new WebAuthnLoginOptionsResponse { Options = options }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating WebAuthn login options");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error creating login options" });
            }
        }

        // ── POST /api/auth/webauthn/login/complete ──────────────────────

        [HttpPost("webauthn/login/complete")]
        [AllowAnonymous]
        public async Task<IActionResult> WebAuthnLoginComplete([FromBody] WebAuthnLoginCompleteRequest request)
        {
            try
            {
                var (success, user, errorMessage) =
                    await _webAuthnService.CompleteAssertionAsync(request.Username, request.AssertionResponse);

                if (!success || user == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Authentication failed" });

                var token = _tokenService.GenerateJwtToken(user);

                return Ok(new ApiResponse<WebAuthnLoginCompleteResponse>
                {
                    Success = true,
                    Message = "WebAuthn login successful",
                    Data = new WebAuthnLoginCompleteResponse { Token = token, User = ToUserDto(user) }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing WebAuthn login");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error during WebAuthn login" });
            }
        }

        // ── POST /api/auth/webauthn/validate ────────────────────────────

        [HttpPost("webauthn/validate")]
        [AllowAnonymous]
        public async Task<IActionResult> WebAuthnValidate([FromBody] WebAuthnValidateRequest request)
        {
            try
            {
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

                var (success, validatedUser, errorMessage) =
                    await _webAuthnService.CompleteAssertionAsync(user.Login, request.AssertionResponse);

                if (!success || validatedUser == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = errorMessage ?? "WebAuthn validation failed" });

                var token = _tokenService.GenerateJwtToken(validatedUser);

                return Ok(new ApiResponse<WebAuthnValidateResponse>
                {
                    Success = true,
                    Message = "WebAuthn validation successful",
                    Data = new WebAuthnValidateResponse { Token = token, User = ToUserDto(validatedUser) }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating WebAuthn");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error validating WebAuthn" });
            }
        }

        // ── GET /api/auth/webauthn/credentials ──────────────────────────

        [HttpGet("webauthn/credentials")]
        [AllowAnonymous]
        public async Task<IActionResult> WebAuthnCredentials()
        {
            try
            {
                var identity = GetUserIdentity();
                if (identity == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Authentication required" });

                var credentials = await _webAuthnService.GetUserCredentialsAsync(identity);

                return Ok(new ApiResponse<WebAuthnCredentialsResponse>
                {
                    Success = true,
                    Data = new WebAuthnCredentialsResponse
                    {
                        Credentials = credentials.Select(c => new WebAuthnCredentialDto
                        {
                            Id = c.CredentialId,
                            CreatedAt = DateTime.UnixEpoch.AddMilliseconds(c.CreatedAt)
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn credentials");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error getting credentials" });
            }
        }

        // ── DELETE /api/auth/webauthn/credentials/{id} ──────────────────

        [HttpDelete("webauthn/credentials/{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> WebAuthnRemoveCredential(string id)
        {
            try
            {
                var identity = GetUserIdentity();
                if (identity == null)
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Authentication required" });

                var (success, errorMessage) = await _webAuthnService.RemoveCredentialAsync(identity, id);

                if (!success)
                    return BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Failed to remove credential" });

                return Ok(new ApiResponse<WebAuthnRemoveCredentialResponse>
                {
                    Success = true,
                    Message = "Credential removed",
                    Data = new WebAuthnRemoveCredentialResponse { Removed = true }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing WebAuthn credential");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error removing credential" });
            }
        }

        // ── POST /api/auth/webauthn/credentials/{id} (browser form) ────

        [HttpPost("webauthn/credentials/{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> WebAuthnRemoveCredentialForm(string id)
        {
            var result = await WebAuthnRemoveCredential(id);
            if (IsBrowserRequest())
                return Redirect("/api/auth/profile?message=Credential+removed");
            return result;
        }

        // ── GET /api/auth/webauthn/login (HTML page) ────────────────────

        [HttpGet("webauthn/login")]
        [AllowAnonymous]
        public IActionResult WebAuthnLoginPage()
        {
            // Renders a page with empty options; client JS will POST to login/options
            return HtmlContent(_htmlRenderer.RenderWebAuthnLogin("{}"));
        }
    }
}
