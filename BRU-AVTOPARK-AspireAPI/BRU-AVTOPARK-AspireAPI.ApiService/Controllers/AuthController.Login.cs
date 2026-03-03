using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BRU_AVTOPARK_AspireAPI.ApiService.Models.Auth;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    public partial class AuthController
    {
        // ── GET /api/auth/login ─────────────────────────────────────────

        [HttpGet("login")]
        [AllowAnonymous]
        public IActionResult LoginPage([FromQuery] string? error, [FromQuery] string? message)
        {
            return HtmlContent(_htmlRenderer.RenderLoginForm(error, message));
        }

        // ── POST /api/auth/login ────────────────────────────────────────

        [Route("login")]
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest? jsonRequest = null)
        {
            _logger.LogInformation("=== LOGIN ATTEMPT STARTED ===");

            try
            {
                // Accept form or JSON
                var request = jsonRequest ?? new LoginRequest
                {
                    Username = Request.Form["username"].ToString(),
                    Password = Request.Form["password"].ToString()
                };

                var result = await ProcessLoginRequest(request);

                // Set cookie when we have a successful token
                if (result is OkObjectResult okResult &&
                    okResult.Value is ApiResponse<LoginResponse> response &&
                    response.Data != null)
                {
                    await SetAuthCookieAsync(response.Data.Token);
                }

                if (jsonRequest != null) return result;

                if (IsBrowserRequest())
                {
                    if (result is OkObjectResult ok2 && ok2.Value is ApiResponse<LoginResponse> r2)
                        return Redirect($"/api/auth/success?token={Uri.EscapeDataString(r2.Data!.Token)}");

                    var errorMessage = "Invalid credentials";
                    if (result is UnauthorizedObjectResult unauth &&
                        unauth.Value is ApiResponse<object> errResp)
                        errorMessage = errResp.Message ?? errorMessage;

                    return Redirect($"/api/auth/login?error={Uri.EscapeDataString(errorMessage)}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"An error occurred during login: {ex.Message}"
                });
            }
        }

        // ── POST /api/auth/register ─────────────────────────────────────

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request)
        {
            _logger.LogInformation("Registration attempt for user: {Username}", request.Username);

            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid request data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                // Determine admin status from bearer token if present
                bool isAdmin = IsAdmin();
                int role = isAdmin ? request.Role : 0;

                var success = await _authService.RegisterAsync(
                    request.Username, request.Password, role,
                    request.Email, request.PhoneNumber);

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Registration failed. Username may already exist."
                    });
                }

                var user = await _userService.GetUserByLoginAsync(request.Username);
                if (user == null)
                {
                    return StatusCode(500, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User was created but could not be retrieved."
                    });
                }

                return Ok(new ApiResponse<RegisterResponse>
                {
                    Success = true,
                    Message = "Registration successful",
                    Data = new RegisterResponse { User = ToUserDto(user) }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for user: {Username}", request.Username);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during registration"
                });
            }
        }

        // ── GET /api/auth/register (HTML form) ──────────────────────────

        [HttpGet("register")]
        [AllowAnonymous]
        public IActionResult RegisterPage([FromQuery] string? error, [FromQuery] string? message)
        {
            return HtmlContent(_htmlRenderer.RenderRegisterForm(error, message));
        }

        // ── GET /api/auth/claim-account ─────────────────────────────────

        [HttpGet("claim-account")]
        [AllowAnonymous]
        public IActionResult ClaimAccountPage([FromQuery] string? error, [FromQuery] string? message)
        {
            return HtmlContent(_htmlRenderer.RenderClaimAccountForm(error, message));
        }

        // ── POST /api/auth/claim-account ────────────────────────────────

        [HttpPost("claim-account")]
        [AllowAnonymous]
        public async Task<IActionResult> ClaimAccount([FromForm] ClaimAccountRequest request)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                var existingUser = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == request.Username);

                if (existingUser == null)
                {
                    if (IsBrowserRequest())
                        return Redirect($"/api/auth/claim-account?error={Uri.EscapeDataString("Account not found.")}");

                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Account not found."
                    });
                }

                string? newIdentityString = null;
                if (request.GenerateNewIdentity)
                    newIdentityString = Guid.NewGuid().ToString();

                conn.Reducers.ClaimUserAccount(request.Username, request.Password, newIdentityString);

                if (IsBrowserRequest())
                    return Redirect($"/api/auth/login?message={Uri.EscapeDataString("Account claimed successfully. You can now log in.")}");

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "Account claimed successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during account claim for user: {Username}", request.Username);
                if (IsBrowserRequest())
                    return Redirect($"/api/auth/claim-account?error={Uri.EscapeDataString("Error: " + ex.Message)}");

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error claiming account: " + ex.Message
                });
            }
        }

        // ── GET /api/auth/success ───────────────────────────────────────

        [HttpGet("success")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginSuccess([FromQuery] string? token)
        {
            if (string.IsNullOrEmpty(token))
                return Redirect("/api/auth/login?error=No+token+provided");

            try
            {
                await SetAuthCookieAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not set auth cookie on success page");
            }

            return HtmlContent(_htmlRenderer.RenderSuccess(token));
        }

        // ── GET /api/auth/error ─────────────────────────────────────────

        [HttpGet("error")]
        [AllowAnonymous]
        public IActionResult ErrorPage([FromQuery] string? message)
        {
            return HtmlContent(_htmlRenderer.RenderError(message ?? "An unknown error occurred."));
        }

        // ── Internal login orchestration ────────────────────────────────

        private async Task<IActionResult> ProcessLoginRequest(LoginRequest request)
        {
            try
            {
                _logger.LogInformation("Login attempt for user: {Username}", request.Username);

                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid request data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                var user = await _authService.AuthenticateAsync(request.Username, request.Password);
                if (user == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid username or password"
                    });
                }

                var conn = _spacetimeService.GetConnection();

                // Check 2FA settings
                var userSettings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(user.UserId));

                if (userSettings == null)
                {
                    conn.Reducers.CreateUserSettings(user.UserId);
                    await Task.Delay(100);
                    userSettings = conn.Db.UserSettings.Iter()
                        .FirstOrDefault(s => s.UserId.Equals(user.UserId));
                }

                // TOTP 2FA
                if (userSettings?.TotpEnabled == true && !request.SkipTwoFactor)
                {
                    var tempToken = _tokenService.GenerateRandomToken();
                    var expiresAt = (ulong)DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
                    conn.Reducers.CreateTwoFactorToken(
                        user.UserId, tempToken, false, expiresAt,
                        Request.Headers["User-Agent"].ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString());

                    return Ok(new ApiResponse<TwoFactorResponse>
                    {
                        Success = true,
                        Message = "Two-factor authentication required",
                        Data = new TwoFactorResponse
                        {
                            RequiresTwoFactor = true,
                            TwoFactorType = "totp",
                            TempToken = tempToken
                        }
                    });
                }

                // WebAuthn 2FA
                if (userSettings?.WebAuthnEnabled == true && !request.SkipTwoFactor)
                {
                    var tempToken = _tokenService.GenerateRandomToken();
                    var expiresAt = (ulong)DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
                    conn.Reducers.CreateTwoFactorToken(
                        user.UserId, tempToken, false, expiresAt,
                        Request.Headers["User-Agent"].ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString());

                    var (success, options, _) = await _webAuthnService.GetAssertionOptionsAsync(user.Login);
                    if (!success || options == null)
                    {
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "Failed to create WebAuthn assertion options"
                        });
                    }

                    return Ok(new ApiResponse<WebAuthnTwoFactorResponse>
                    {
                        Success = true,
                        Message = "WebAuthn authentication required",
                        Data = new WebAuthnTwoFactorResponse
                        {
                            RequiresTwoFactor = true,
                            TwoFactorType = "webauthn",
                            TempToken = tempToken,
                            Options = options
                        }
                    });
                }

                // Standard login
                var token = _tokenService.GenerateJwtToken(user);
                var claims = _tokenService.ExtractTokenClaims(token);

                return Ok(new ApiResponse<LoginResponse>
                {
                    Success = true,
                    Message = "Authentication successful",
                    Data = new LoginResponse
                    {
                        Token = token,
                        Claims = claims,
                        User = ToUserDto(user)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user: {Username}", request.Username);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during login"
                });
            }
        }
    }
}
