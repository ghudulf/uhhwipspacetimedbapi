using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using System.Linq;
using System;
using TicketSalesApp.AdminServer.Configuration;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK.Models.Requests;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Models.ViewModels;
using Microsoft.Extensions.Logging;
using BRU_AVTOPARK_AspireAPI.ApiService.Routing;
using Fido2NetLib.Objects;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    /// <summary>
    /// Refactored AuthController that uses the orchestration service pattern.
    /// This controller is used when feature flags are enabled.
    /// Routes are conditionally registered based on feature flags via [RefactoredAction] attribute.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthControllerRefactored : ControllerBase
    {
        private readonly IAuthOrchestrationService _authOrchestrationService;
        private readonly IOptions<FeatureFlagOptions> _featureFlags;
        private readonly ILogger<AuthControllerRefactored> _logger;

        public AuthControllerRefactored(
            IAuthOrchestrationService authOrchestrationService,
            IOptions<FeatureFlagOptions> featureFlags,
            ILogger<AuthControllerRefactored> logger)
        {
            _authOrchestrationService = authOrchestrationService ?? throw new ArgumentNullException(nameof(authOrchestrationService));
            _featureFlags = featureFlags ?? throw new ArgumentNullException(nameof(featureFlags));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Traditional Authentication (2 endpoints)

        /// <summary>
        /// POST /api/auth/login - Login with username/password
        /// Enabled by: EnableLoginRefactoring feature flag
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableLoginRefactoring))]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            _logger.LogInformation("Refactored Login endpoint called for user: {Username}", request.Username);

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data",
                    Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                });
            }

            var result = await _authOrchestrationService.LoginAsync(request.Username, request.Password);

            if (!result.Success)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Authentication failed"
                });
            }

            if (result.RequiresTwoFactor)
            {
                return Ok(new ApiResponse<TwoFactorResponse>
                {
                    Success = true,
                    Message = "Two-factor authentication required",
                    Data = new TwoFactorResponse
                    {
                        RequiresTwoFactor = true,
                        TwoFactorType = result.TwoFactorType,
                        TempToken = result.TempToken,
                        TotpEnabled = result.TotpEnabled,
                        WebAuthnEnabled = result.WebAuthnEnabled,
                        WebAuthnOptions = result.WebAuthnAssertionOptions
                    }
                });
            }

            return Ok(new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "Authentication successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = result.User!
                }
            });
        }

        /// <summary>
        /// POST /api/auth/register - Register new user account
        /// Enabled by: EnableRegisterRefactoring feature flag
        /// </summary>
        [HttpPost("register")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableRegisterRefactoring))]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            _logger.LogInformation("Refactored Register endpoint called for user: {Username}", request.Username);

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data",
                    Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                });
            }

            var authHeader = Request.Headers["Authorization"].ToString();
            string? adminIdentity = null;
            
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring("Bearer ".Length);
                adminIdentity = token;
            }

            var result = await _authOrchestrationService.RegisterAsync(
                request.Username,
                request.Password,
                request.Role,
                request.Email,
                request.PhoneNumber,
                adminIdentity
            );

            if (!result.Success)
            {
                if (result.ErrorMessage?.Contains("Administrator privileges") == true)
                {
                    return StatusCode(403, new ApiResponse<object>
                    {
                        Success = false,
                        Message = result.ErrorMessage
                    });
                }

                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Registration failed"
                });
            }

            return Ok(new ApiResponse<RegisterResponse>
            {
                Success = true,
                Message = "User registered successfully",
                Data = new RegisterResponse
                {
                    User = result.User!
                }
            });
        }

        #endregion

        #region TOTP Endpoints (4 endpoints)

        /// <summary>
        /// GET /api/auth/totp/setup - Setup TOTP for user
        /// Enabled by: EnableTotpSetupRefactoring feature flag
        /// </summary>
        [HttpGet("totp/setup")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableTotpSetupRefactoring))]
        public async Task<IActionResult> TotpSetup()
        {
            _logger.LogInformation("Refactored TOTP Setup endpoint called");

            var userId = User.FindFirst("identity")?.Value;
            var username = User.FindFirst("unique_name")?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.SetupTotpAsync(identity, username);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "TOTP setup failed"
                });
            }

            return Ok(new ApiResponse<TotpSetupResponse>
            {
                Success = true,
                Message = "TOTP setup successful",
                Data = new TotpSetupResponse
                {
                    SecretKey = result.SecretKey!,
                    QrCodeUri = result.QrCodeUri!
                }
            });
        }

        /// <summary>
        /// POST /api/auth/totp/verify - Verify TOTP code during setup
        /// Enabled by: EnableTotpVerifyRefactoring feature flag
        /// </summary>
        [HttpPost("totp/verify")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableTotpVerifyRefactoring))]
        public async Task<IActionResult> TotpVerify([FromBody] TotpVerifyRequest request)
        {
            _logger.LogInformation("Refactored TOTP Verify endpoint called");

            var userId = User.FindFirst("identity")?.Value;
            var username = User.FindFirst("unique_name")?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.EnableTotpAsync(identity, username, request.Code, request.SecretKey);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "TOTP verification failed"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "TOTP enabled successfully"
            });
        }

        /// <summary>
        /// POST /api/auth/totp/disable - Disable TOTP for user
        /// Enabled by: EnableTotpDisableRefactoring feature flag
        /// </summary>
        [HttpPost("totp/disable")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableTotpDisableRefactoring))]
        public async Task<IActionResult> TotpDisable()
        {
            _logger.LogInformation("Refactored TOTP Disable endpoint called");

            var userId = User.FindFirst("identity")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.DisableTotpAsync(identity);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "TOTP disable failed"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "TOTP disabled successfully"
            });
        }

        /// <summary>
        /// POST /api/auth/totp/validate - Validate TOTP code during login
        /// Enabled by: EnableTotpValidateRefactoring feature flag
        /// </summary>
        [HttpPost("totp/validate")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableTotpValidateRefactoring))]
        public async Task<IActionResult> TotpValidate([FromBody] TotpValidateRequest request)
        {
            _logger.LogInformation("Refactored TOTP Validate endpoint called");

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.ValidateTotpAsync(request.TempToken, request.Code);

            if (!result.Success)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "TOTP validation failed"
                });
            }

            return Ok(new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "TOTP validation successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = result.User!
                }
            });
        }

        #endregion

        #region WebAuthn Endpoints (7 endpoints)

        /// <summary>
        /// POST /api/auth/webauthn/register/options - Get WebAuthn registration options
        /// Enabled by: EnableWebAuthnRegisterOptionsRefactoring feature flag
        /// </summary>
        [HttpPost("webauthn/register/options")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableWebAuthnRegisterOptionsRefactoring))]
        public async Task<IActionResult> WebAuthnRegisterOptions()
        {
            _logger.LogInformation("Refactored WebAuthn Register Options endpoint called");

            var username = User.FindFirst("unique_name")?.Value;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var result = await _authOrchestrationService.GetWebAuthnRegisterOptionsAsync(username);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to generate WebAuthn registration options"
                });
            }

            return Ok(new ApiResponse<CredentialCreateOptions>
            {
                Success = true,
                Message = "WebAuthn registration options generated",
                Data = result.Options!
            });
        }

        /// <summary>
        /// POST /api/auth/webauthn/register/complete - Complete WebAuthn registration
        /// Enabled by: EnableWebAuthnRegisterCompleteRefactoring feature flag
        /// </summary>
        [HttpPost("webauthn/register/complete")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableWebAuthnRegisterCompleteRefactoring))]
        public async Task<IActionResult> WebAuthnRegisterComplete([FromBody] WebAuthnRegisterRequest request)
        {
            _logger.LogInformation("Refactored WebAuthn Register Complete endpoint called");

            var userId = User.FindFirst("identity")?.Value;
            var username = User.FindFirst("unique_name")?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.RegisterWebAuthnAsync(identity, username, request.AttestationResponse);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "WebAuthn registration failed"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "WebAuthn credential registered successfully"
            });
        }

        /// <summary>
        /// POST /api/auth/webauthn/login/options - Get WebAuthn login options
        /// Enabled by: EnableWebAuthnLoginOptionsRefactoring feature flag
        /// </summary>
        [HttpPost("webauthn/login/options")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableWebAuthnLoginOptionsRefactoring))]
        public async Task<IActionResult> WebAuthnLoginOptions([FromBody] WebAuthnLoginOptionsRequest request)
        {
            _logger.LogInformation("Refactored WebAuthn Login Options endpoint called for user: {Username}", request.Username);

            var result = await _authOrchestrationService.GetWebAuthnLoginOptionsAsync(request.Username);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to generate WebAuthn login options"
                });
            }

            return Ok(new ApiResponse<AssertionOptions>
            {
                Success = true,
                Message = "WebAuthn login options generated",
                Data = result.Options!
            });
        }

        /// <summary>
        /// POST /api/auth/webauthn/login/complete - Complete WebAuthn login
        /// Enabled by: EnableWebAuthnLoginCompleteRefactoring feature flag
        /// </summary>
        [HttpPost("webauthn/login/complete")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableWebAuthnLoginCompleteRefactoring))]
        public async Task<IActionResult> WebAuthnLoginComplete([FromBody] WebAuthnLoginCompleteRequest request)
        {
            _logger.LogInformation("Refactored WebAuthn Login Complete endpoint called");

            var result = await _authOrchestrationService.CompleteWebAuthnLoginAsync(request.Username, request.AssertionResponse);

            if (!result.Success)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "WebAuthn login failed"
                });
            }

            return Ok(new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "WebAuthn login successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = result.User!
                }
            });
        }

        /// <summary>
        /// POST /api/auth/webauthn/validate - Validate WebAuthn assertion during 2FA
        /// Enabled by: EnableWebAuthnValidateRefactoring feature flag
        /// </summary>
        [HttpPost("webauthn/validate")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableWebAuthnValidateRefactoring))]
        public async Task<IActionResult> WebAuthnValidate([FromBody] WebAuthnValidateRequest request)
        {
            _logger.LogInformation("Refactored WebAuthn Validate endpoint called");

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.ValidateWebAuthnAsync(request.TempToken, request.AssertionResponse);

            if (!result.Success)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "WebAuthn validation failed"
                });
            }

            return Ok(new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "WebAuthn validation successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = result.User!
                }
            });
        }

        /// <summary>
        /// GET /api/auth/webauthn/credentials - Get user's WebAuthn credentials
        /// Enabled by: EnableWebAuthnCredentialsRefactoring feature flag
        /// </summary>
        [HttpGet("webauthn/credentials")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableWebAuthnCredentialsRefactoring))]
        public async Task<IActionResult> WebAuthnCredentials()
        {
            _logger.LogInformation("Refactored WebAuthn Credentials endpoint called");

            var userId = User.FindFirst("identity")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.GetWebAuthnCredentialsAsync(identity);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to retrieve credentials"
                });
            }

            return Ok(new ApiResponse<WebAuthnCredentialsResponse>
            {
                Success = true,
                Message = "Credentials retrieved successfully",
                Data = new WebAuthnCredentialsResponse
                {
                    Credentials = result.Credentials
                }
            });
        }

        /// <summary>
        /// DELETE /api/auth/webauthn/credentials/{id} - Remove WebAuthn credential
        /// Enabled by: EnableWebAuthnCredentialDeleteRefactoring feature flag
        /// </summary>
        [HttpDelete("webauthn/credentials/{id}")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableWebAuthnCredentialDeleteRefactoring))]
        public async Task<IActionResult> WebAuthnCredentialDelete(string id)
        {
            _logger.LogInformation("Refactored WebAuthn Credential Delete endpoint called for credential: {CredentialId}", id);

            var userId = User.FindFirst("identity")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.RemoveWebAuthnCredentialAsync(identity, id);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to remove credential"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Credential removed successfully"
            });
        }

        #endregion

        #region Magic Link Endpoints (3 endpoints)

        /// <summary>
        /// POST /api/auth/magic-link/send - Send magic link email
        /// Enabled by: EnableMagicLinkSendRefactoring feature flag
        /// </summary>
        [HttpPost("magic-link/send")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableMagicLinkSendRefactoring))]
        public async Task<IActionResult> MagicLinkSend([FromBody] MagicLinkRequest request)
        {
            _logger.LogInformation("Refactored Magic Link Send endpoint called for email: {Email}", request.Email);

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var userAgent = Request.Headers["User-Agent"].ToString();
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            var result = await _authOrchestrationService.SendMagicLinkAsync(request.Email, userAgent, ipAddress);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to send magic link"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Magic link sent successfully"
            });
        }

        /// <summary>
        /// POST /api/auth/validate-magic-link - Validate magic link token
        /// Enabled by: EnableMagicLinkValidateRefactoring feature flag
        /// </summary>
        [HttpPost("validate-magic-link")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableMagicLinkValidateRefactoring))]
        public async Task<IActionResult> MagicLinkValidate([FromBody] MagicLinkValidateRequest request)
        {
            _logger.LogInformation("Refactored Magic Link Validate endpoint called");

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.ValidateMagicLinkAsync(request.Token);

            if (!result.Success)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Magic link validation failed"
                });
            }

            return Ok(new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "Magic link validation successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = result.User!
                }
            });
        }

        /// <summary>
        /// GET /api/auth/magic-link - Show magic link login page
        /// Enabled by: EnableMagicLinkPageRefactoring feature flag
        /// </summary>
        [HttpGet("magic-link")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableMagicLinkPageRefactoring))]
        public async Task<IActionResult> MagicLinkPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
            _logger.LogInformation("Refactored Magic Link Page endpoint called");

            var result = await _authOrchestrationService.RenderMagicLinkPageAsync(error, message);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render magic link page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        #endregion

        #region QR Authentication Endpoints (7 endpoints)

        /// <summary>
        /// GET /api/auth/qr-login - Show QR login page
        /// Enabled by: EnableQRLoginPageRefactoring feature flag
        /// </summary>
        [HttpGet("qr-login")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableQRLoginPageRefactoring))]
        public async Task<IActionResult> QRLoginPage()
        {
            _logger.LogInformation("Refactored QR Login Page endpoint called");

            var result = await _authOrchestrationService.RenderQRLoginPageAsync();

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render QR login page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// POST /api/auth/qr-login/generate - Generate QR login token
        /// Enabled by: EnableQRLoginGenerateRefactoring feature flag
        /// </summary>
        [HttpPost("qr-login/generate")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableQRLoginGenerateRefactoring))]
        public async Task<IActionResult> QRLoginGenerate()
        {
            _logger.LogInformation("Refactored QR Login Generate endpoint called");

            var userId = User.FindFirst("identity")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.GenerateQRLoginAsync(identity);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to generate QR code"
                });
            }

            return Ok(new ApiResponse<QRLoginResponse>
            {
                Success = true,
                Message = "QR code generated successfully",
                Data = new QRLoginResponse
                {
                    Token = result.Token!,
                    QrCodeData = result.QrCodeData!,
                    ExpiresAt = result.ExpiresAt
                }
            });
        }

        /// <summary>
        /// POST /api/auth/qr-login/validate - Validate QR login token
        /// Enabled by: EnableQRLoginValidateRefactoring feature flag
        /// </summary>
        [HttpPost("qr-login/validate")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableQRLoginValidateRefactoring))]
        public async Task<IActionResult> QRLoginValidate([FromBody] QRLoginValidateRequest request)
        {
            _logger.LogInformation("Refactored QR Login Validate endpoint called");

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.ValidateQRLoginAsync(request.Token);

            if (!result.Success)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "QR login validation failed"
                });
            }

            return Ok(new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "QR login successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = result.User!
                }
            });
        }

        /// <summary>
        /// POST /api/auth/qr-login/direct - Direct QR login (no 2FA)
        /// Enabled by: EnableQRLoginDirectRefactoring feature flag
        /// </summary>
        [HttpPost("qr-login/direct")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableQRLoginDirectRefactoring))]
        public async Task<IActionResult> QRLoginDirect([FromBody] QRLoginDirectRequest request)
        {
            _logger.LogInformation("Refactored QR Login Direct endpoint called");

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.DirectQRLoginAsync(request.Username, request.DeviceType);

            if (!result.Success)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Direct QR login failed"
                });
            }

            return Ok(new ApiResponse<QRLoginResponse>
            {
                Success = true,
                Message = "Direct QR login successful",
                Data = new QRLoginResponse
                {
                    Token = result.Token!,
                    QrCodeData = result.QrCodeData!,
                    ExpiresAt = result.ExpiresAt
                }
            });
        }

        /// <summary>
        /// GET /api/auth/qr-login/status - Check QR login status
        /// Enabled by: EnableQRLoginStatusRefactoring feature flag
        /// </summary>
        [HttpGet("qr-login/status")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableQRLoginStatusRefactoring))]
        public async Task<IActionResult> QRLoginStatus([FromQuery] string deviceId)
        {
            _logger.LogInformation("Refactored QR Login Status endpoint called for device: {DeviceId}", deviceId);

            if (string.IsNullOrEmpty(deviceId))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Device ID is required"
                });
            }

            var result = await _authOrchestrationService.CheckQRLoginStatusAsync(deviceId);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to check QR login status"
                });
            }

            return Ok(new ApiResponse<QRLoginStatusResponse>
            {
                Success = true,
                Message = "QR login status retrieved",
                Data = new QRLoginStatusResponse
                {
                    Status = result.Status!,
                    Token = result.Token
                }
            });
        }

        /// <summary>
        /// POST /api/auth/qr-login/cancel - Cancel QR login attempt
        /// Enabled by: EnableQRLoginCancelRefactoring feature flag
        /// </summary>
        [HttpPost("qr-login/cancel")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableQRLoginCancelRefactoring))]
        public async Task<IActionResult> QRLoginCancel([FromBody] QRLoginCancelRequest request)
        {
            _logger.LogInformation("Refactored QR Login Cancel endpoint called");

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.CancelQRLoginAsync(request.Token);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to cancel QR login"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "QR login cancelled successfully"
            });
        }

        /// <summary>
        /// POST /api/auth/qr-login/notify - Notify device of successful login
        /// Enabled by: EnableQRLoginNotifyRefactoring feature flag
        /// </summary>
        [HttpPost("qr-login/notify")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableQRLoginNotifyRefactoring))]
        public async Task<IActionResult> QRLoginNotify([FromBody] QRLoginNotifyRequest request)
        {
            _logger.LogInformation("Refactored QR Login Notify endpoint called");

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.NotifyQRLoginAsync(request.DeviceId, request.Token);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to notify device"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Device notified successfully"
            });
        }

        #endregion

        #region OAuth/OIDC Core Flow Endpoints (3 endpoints)

        /// <summary>
        /// GET/POST ~/connect/authorize - OAuth authorization endpoint
        /// Enabled by: EnableOAuthAuthorizeRefactoring feature flag
        /// </summary>
        [HttpGet("~/connect/authorize")]
        [HttpPost("~/connect/authorize")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthAuthorizeRefactoring))]
        public async Task<IActionResult> OAuthAuthorize()
        {
            _logger.LogInformation("Refactored OAuth Authorize endpoint called");

            var userId = User.FindFirst("identity")?.Value;
            
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var clientId = Request.Query["client_id"].ToString();
            var redirectUri = Request.Query["redirect_uri"].ToString();
            var scope = Request.Query["scope"].ToString();

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.AuthorizeOAuthAsync(clientId, redirectUri, scope, identity);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "OAuth authorization failed"
                });
            }

            return Redirect(result.RedirectUri!);
        }

        /// <summary>
        /// POST ~/connect/token - OAuth token exchange endpoint
        /// Enabled by: EnableOAuthTokenRefactoring feature flag
        /// </summary>
        [HttpPost("~/connect/token")]
        [AllowAnonymous]
        [Produces("application/json")]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthTokenRefactoring))]
        public async Task<IActionResult> OAuthToken()
        {
            _logger.LogInformation("Refactored OAuth Token endpoint called");

            var code = Request.Form["code"].ToString();
            var clientId = Request.Form["client_id"].ToString();
            var clientSecret = Request.Form["client_secret"].ToString();

            var result = await _authOrchestrationService.ExchangeTokenAsync(code, clientId, clientSecret);

            if (!result.Success)
            {
                return BadRequest(new
                {
                    error = "invalid_grant",
                    error_description = result.ErrorMessage ?? "Token exchange failed"
                });
            }

            return Ok(new
            {
                access_token = result.AccessToken,
                token_type = "Bearer",
                expires_in = result.ExpiresIn,
                refresh_token = result.RefreshToken,
                id_token = result.IdToken
            });
        }

        /// <summary>
        /// GET ~/connect/userinfo - OAuth user info endpoint
        /// Enabled by: EnableOAuthUserInfoRefactoring feature flag
        /// </summary>
        [HttpGet("~/connect/userinfo")]
        [Authorize]
        [Produces("application/json")]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthUserInfoRefactoring))]
        public async Task<IActionResult> OAuthUserInfo()
        {
            _logger.LogInformation("Refactored OAuth UserInfo endpoint called");

            var username = User.FindFirst("unique_name")?.Value;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized(new
                {
                    error = "invalid_token",
                    error_description = "User not authenticated"
                });
            }

            var result = await _authOrchestrationService.GetUserInfoAsync(username);

            if (!result.Success)
            {
                return BadRequest(new
                {
                    error = "server_error",
                    error_description = result.ErrorMessage ?? "Failed to retrieve user info"
                });
            }

            return Ok(result.UserInfo);
        }

        #endregion

        #region OAuth Client Management API Endpoints (7 endpoints)

        /// <summary>
        /// POST /api/oauth/clients - Register new OAuth client
        /// Enabled by: EnableOAuthClientRegisterRefactoring feature flag
        /// </summary>
        [HttpPost("oauth/clients")]
        [Authorize(Roles = "Administrator")]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientRegisterRefactoring))]
        public async Task<IActionResult> OAuthClientRegister([FromBody] RegisterClientRequest request)
        {
            _logger.LogInformation("Refactored OAuth Client Register endpoint called");

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.RegisterOAuthClientAsync(
                request.ClientId,
                request.ClientSecret,
                request.DisplayName,
                request.RedirectUris,
                request.PostLogoutRedirectUris,
                request.AllowedScopes,
                request.RequireConsent
            );

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to register OAuth client"
                });
            }

            return Ok(new ApiResponse<OAuthClientDto>
            {
                Success = true,
                Message = "OAuth client registered successfully",
                Data = result.Client!
            });
        }

        /// <summary>
        /// GET /api/oauth/clients - List all OAuth clients
        /// Enabled by: EnableOAuthClientListRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/clients")]
        [Authorize(Roles = "Administrator")]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientListRefactoring))]
        public async Task<IActionResult> OAuthClientList()
        {
            _logger.LogInformation("Refactored OAuth Client List endpoint called");

            var result = await _authOrchestrationService.GetOAuthClientsAsync();

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to retrieve OAuth clients"
                });
            }

            return Ok(new ApiResponse<List<OAuthClientDto>>
            {
                Success = true,
                Message = "OAuth clients retrieved successfully",
                Data = result.Clients!
            });
        }

        /// <summary>
        /// GET /api/oauth/clients/{id} - Get OAuth client details
        /// Enabled by: EnableOAuthClientDetailsRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/clients/{id}")]
        [Authorize(Roles = "Administrator")]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientDetailsRefactoring))]
        public async Task<IActionResult> OAuthClientDetails(string id)
        {
            _logger.LogInformation("Refactored OAuth Client Details endpoint called for client: {ClientId}", id);

            var result = await _authOrchestrationService.GetOAuthClientAsync(id);

            if (!result.Success)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "OAuth client not found"
                });
            }

            return Ok(new ApiResponse<OAuthClientDto>
            {
                Success = true,
                Message = "OAuth client retrieved successfully",
                Data = result.Client!
            });
        }

        /// <summary>
        /// PUT /api/oauth/clients/{id} - Update OAuth client
        /// Enabled by: EnableOAuthClientUpdateRefactoring feature flag
        /// </summary>
        [HttpPut("oauth/clients/{id}")]
        [Authorize(Roles = "Administrator")]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientUpdateRefactoring))]
        public async Task<IActionResult> OAuthClientUpdate(string id, [FromBody] UpdateClientRequest request)
        {
            _logger.LogInformation("Refactored OAuth Client Update endpoint called for client: {ClientId}", id);

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.UpdateOAuthClientAsync(
                id,
                request.ClientSecret,
                request.DisplayName,
                request.RedirectUris,
                request.PostLogoutRedirectUris,
                request.AllowedScopes,
                request.RequireConsent
            );

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to update OAuth client"
                });
            }

            return Ok(new ApiResponse<OAuthClientDto>
            {
                Success = true,
                Message = "OAuth client updated successfully",
                Data = result.Client!
            });
        }

        /// <summary>
        /// DELETE /api/oauth/clients/{id} - Delete OAuth client
        /// Enabled by: EnableOAuthClientDeleteRefactoring feature flag
        /// </summary>
        [HttpDelete("oauth/clients/{id}")]
        [Authorize(Roles = "Administrator")]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientDeleteRefactoring))]
        public async Task<IActionResult> OAuthClientDelete(string id)
        {
            _logger.LogInformation("Refactored OAuth Client Delete endpoint called for client: {ClientId}", id);

            var result = await _authOrchestrationService.DeleteOAuthClientAsync(id);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to delete OAuth client"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "OAuth client deleted successfully"
            });
        }

        /// <summary>
        /// GET /api/oauth/scopes - List available OAuth scopes
        /// Enabled by: EnableOAuthScopesRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/scopes")]
        [Authorize(Roles = "Administrator")]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthScopesRefactoring))]
        public async Task<IActionResult> OAuthScopes()
        {
            _logger.LogInformation("Refactored OAuth Scopes endpoint called");

            var result = await _authOrchestrationService.GetOAuthScopesAsync();

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to retrieve OAuth scopes"
                });
            }

            return Ok(new ApiResponse<List<OAuthScopeDto>>
            {
                Success = true,
                Message = "OAuth scopes retrieved successfully",
                Data = result.Scopes!
            });
        }

        /// <summary>
        /// POST /api/oauth/clients/{id}/regenerate-secret - Regenerate client secret
        /// Enabled by: EnableOAuthClientRegenerateSecretRefactoring feature flag
        /// </summary>
        [HttpPost("oauth/clients/{id}/regenerate-secret")]
        [Authorize(Roles = "Administrator")]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientRegenerateSecretRefactoring))]
        public async Task<IActionResult> OAuthClientRegenerateSecret(string id)
        {
            _logger.LogInformation("Refactored OAuth Client Regenerate Secret endpoint called for client: {ClientId}", id);

            var result = await _authOrchestrationService.RegenerateOAuthClientSecretAsync(id);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to regenerate client secret"
                });
            }

            return Ok(new ApiResponse<OAuthClientSecretDto>
            {
                Success = true,
                Message = "Client secret regenerated successfully",
                Data = new OAuthClientSecretDto
                {
                    ClientId = id,
                    ClientSecret = result.NewSecret!
                }
            });
        }

        #endregion

        #region OAuth Admin HTML Pages Endpoints (13 endpoints)

        /// <summary>
        /// GET /oauth/clients - OAuth clients list page
        /// Enabled by: EnableOAuthClientsPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/clients")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientsPageRefactoring))]
        public async Task<IActionResult> OAuthClientsPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Clients Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthClientsPageAsync(token);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render OAuth clients page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/clients/new - New OAuth client form page
        /// Enabled by: EnableOAuthClientNewPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/clients/new")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientNewPageRefactoring))]
        public async Task<IActionResult> OAuthClientNewPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Client New Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthClientNewPageAsync(token);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render new client page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/clients/{id} - OAuth client details page
        /// Enabled by: EnableOAuthClientDetailsPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/clients/{id}")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientDetailsPageRefactoring))]
        public async Task<IActionResult> OAuthClientDetailsPage(string id, [FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Client Details Page endpoint called for client: {ClientId}", id);

            var result = await _authOrchestrationService.RenderOAuthClientDetailsPageAsync(id, token);

            if (!result.Success)
            {
                return NotFound(result.ErrorMessage ?? "OAuth client not found");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/clients/{id}/edit - Edit OAuth client form page
        /// Enabled by: EnableOAuthClientEditPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/clients/{id}/edit")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientEditPageRefactoring))]
        public async Task<IActionResult> OAuthClientEditPage(string id, [FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Client Edit Page endpoint called for client: {ClientId}", id);

            var result = await _authOrchestrationService.RenderOAuthClientEditPageAsync(id, token);

            if (!result.Success)
            {
                return NotFound(result.ErrorMessage ?? "OAuth client not found");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/scopes - OAuth scopes list page
        /// Enabled by: EnableOAuthScopesPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/scopes")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthScopesPageRefactoring))]
        public async Task<IActionResult> OAuthScopesPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Scopes Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthScopesPageAsync(token);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render OAuth scopes page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/authorizations - OAuth authorizations list page
        /// Enabled by: EnableOAuthAuthorizationsPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/authorizations")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthAuthorizationsPageRefactoring))]
        public async Task<IActionResult> OAuthAuthorizationsPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Authorizations Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthAuthorizationsPageAsync(token);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render OAuth authorizations page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/tokens - OAuth tokens list page
        /// Enabled by: EnableOAuthTokensPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/tokens")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthTokensPageRefactoring))]
        public async Task<IActionResult> OAuthTokensPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Tokens Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthTokensPageAsync(token);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render OAuth tokens page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/dashboard - OAuth admin dashboard page
        /// Enabled by: EnableOAuthDashboardPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/dashboard")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthDashboardPageRefactoring))]
        public async Task<IActionResult> OAuthDashboardPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Dashboard Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthDashboardPageAsync(token);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render OAuth dashboard page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/settings - OAuth settings page
        /// Enabled by: EnableOAuthSettingsPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/settings")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthSettingsPageRefactoring))]
        public async Task<IActionResult> OAuthSettingsPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Settings Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthSettingsPageAsync(token);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render OAuth settings page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/logs - OAuth audit logs page
        /// Enabled by: EnableOAuthLogsPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/logs")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthLogsPageRefactoring))]
        public async Task<IActionResult> OAuthLogsPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Logs Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthLogsPageAsync(token);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render OAuth logs page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/help - OAuth help/documentation page
        /// Enabled by: EnableOAuthHelpPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/help")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthHelpPageRefactoring))]
        public async Task<IActionResult> OAuthHelpPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Help Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthHelpPageAsync(token);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render OAuth help page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/test - OAuth test/playground page
        /// Enabled by: EnableOAuthTestPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/test")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthTestPageRefactoring))]
        public async Task<IActionResult> OAuthTestPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Test Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthTestPageAsync(token);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render OAuth test page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        /// <summary>
        /// GET /oauth/callback - OAuth callback page
        /// Enabled by: EnableOAuthCallbackPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/callback")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthCallbackPageRefactoring))]
        public async Task<IActionResult> OAuthCallbackPage([FromQuery] string? code = null, [FromQuery] string? error = null)
        {
            _logger.LogInformation("Refactored OAuth Callback Page endpoint called");

            var result = await _authOrchestrationService.RenderOAuthCallbackPageAsync(code, error);

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to render OAuth callback page");
            }

            return Content(result.HtmlContent!, "text/html");
        }

        #endregion

        #region Profile & Utility Endpoints (8 endpoints)

        /// <summary>
        /// GET /api/auth/profile - Get user profile
        /// Enabled by: EnableProfileRefactoring feature flag
        /// </summary>
        [HttpGet("profile")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableProfileRefactoring))]
        public async Task<IActionResult> Profile()
        {
            _logger.LogInformation("Refactored Profile endpoint called");

            var userId = User.FindFirst("identity")?.Value;
            var token = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var profile = await _authOrchestrationService.GetProfileAsync(userId, token);

            if (profile == null)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Profile not found"
                });
            }

            return Ok(new ApiResponse<ProfileViewModel>
            {
                Success = true,
                Message = "Profile retrieved successfully",
                Data = profile
            });
        }

        /// <summary>
        /// PUT /api/auth/profile - Update user profile
        /// Enabled by: EnableProfileUpdateRefactoring feature flag
        /// </summary>
        [HttpPut("profile")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableProfileUpdateRefactoring))]
        public async Task<IActionResult> ProfileUpdate([FromBody] UpdateProfileRequest request)
        {
            _logger.LogInformation("Refactored Profile Update endpoint called");

            var userId = User.FindFirst("identity")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.UpdateProfileAsync(identity, request);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to update profile"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Profile updated successfully"
            });
        }

        /// <summary>
        /// POST /api/auth/change-password - Change user password
        /// Enabled by: EnableChangePasswordRefactoring feature flag
        /// </summary>
        [HttpPost("change-password")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableChangePasswordRefactoring))]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            _logger.LogInformation("Refactored Change Password endpoint called");

            var userId = User.FindFirst("identity")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.ChangePasswordAsync(identity, request.CurrentPassword, request.NewPassword);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to change password"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Password changed successfully"
            });
        }

        /// <summary>
        /// POST /api/auth/logout - Logout user
        /// Enabled by: EnableLogoutRefactoring feature flag
        /// </summary>
        [HttpPost("logout")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableLogoutRefactoring))]
        public async Task<IActionResult> Logout()
        {
            _logger.LogInformation("Refactored Logout endpoint called");

            var userId = User.FindFirst("identity")?.Value;
            var token = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.LogoutAsync(identity, token);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to logout"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Logged out successfully"
            });
        }

        /// <summary>
        /// POST /api/auth/refresh - Refresh JWT token
        /// Enabled by: EnableRefreshTokenRefactoring feature flag
        /// </summary>
        [HttpPost("refresh")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableRefreshTokenRefactoring))]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            _logger.LogInformation("Refactored Refresh Token endpoint called");

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.RefreshTokenAsync(request.RefreshToken);

            if (!result.Success)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to refresh token"
                });
            }

            return Ok(new ApiResponse<RefreshTokenResponse>
            {
                Success = true,
                Message = "Token refreshed successfully",
                Data = new RefreshTokenResponse
                {
                    Token = result.Token!,
                    RefreshToken = result.RefreshToken!,
                    ExpiresAt = result.ExpiresAt
                }
            });
        }

        /// <summary>
        /// GET /api/auth/settings - Get user authentication settings
        /// Enabled by: EnableSettingsRefactoring feature flag
        /// </summary>
        [HttpGet("settings")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableSettingsRefactoring))]
        public async Task<IActionResult> Settings()
        {
            _logger.LogInformation("Refactored Settings endpoint called");

            var userId = User.FindFirst("identity")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.GetSettingsAsync(identity);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to retrieve settings"
                });
            }

            return Ok(new ApiResponse<UserSettingsDto>
            {
                Success = true,
                Message = "Settings retrieved successfully",
                Data = result.Settings!
            });
        }

        /// <summary>
        /// PUT /api/auth/settings - Update user authentication settings
        /// Enabled by: EnableSettingsUpdateRefactoring feature flag
        /// </summary>
        [HttpPut("settings")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableSettingsUpdateRefactoring))]
        public async Task<IActionResult> SettingsUpdate([FromBody] UpdateSettingsRequest request)
        {
            _logger.LogInformation("Refactored Settings Update endpoint called");

            var userId = User.FindFirst("identity")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not authenticated"
                });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var identity = SpacetimeDB.Identity.From(userId);
            var result = await _authOrchestrationService.UpdateSettingsAsync(identity, request);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to update settings"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Settings updated successfully"
            });
        }

        /// <summary>
        /// GET /api/auth/status - Check authentication status
        /// Enabled by: EnableStatusRefactoring feature flag
        /// </summary>
        [HttpGet("status")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableStatusRefactoring))]
        public async Task<IActionResult> Status()
        {
            _logger.LogInformation("Refactored Status endpoint called");

            var token = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

            if (string.IsNullOrEmpty(token))
            {
                return Ok(new ApiResponse<AuthStatusResponse>
                {
                    Success = true,
                    Message = "Not authenticated",
                    Data = new AuthStatusResponse
                    {
                        IsAuthenticated = false
                    }
                });
            }

            var result = await _authOrchestrationService.CheckAuthStatusAsync(token);

            return Ok(new ApiResponse<AuthStatusResponse>
            {
                Success = true,
                Message = result.IsAuthenticated ? "Authenticated" : "Not authenticated",
                Data = new AuthStatusResponse
                {
                    IsAuthenticated = result.IsAuthenticated,
                    Username = result.Username,
                    ExpiresAt = result.ExpiresAt
                }
            });
        }

        #endregion
    }
}
