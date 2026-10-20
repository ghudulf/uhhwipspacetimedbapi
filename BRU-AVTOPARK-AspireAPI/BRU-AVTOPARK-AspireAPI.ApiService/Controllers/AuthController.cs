using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Security.Claims;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using TicketSalesApp.AdminServer.Configuration;
using TicketSalesApp.AdminServer.Controllers;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK.Models.Requests;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Models.ViewModels;
using Microsoft.Extensions.Logging;
using BRU_AVTOPARK_AspireAPI.ApiService.Routing;
using Fido2NetLib;
using Fido2NetLib.Objects;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using static OpenIddict.Abstractions.OpenIddictConstants;
using SpacetimeDB.Types;
using SpacetimeDB;
using Identity = SpacetimeDB.Identity;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Caching.Memory;
using TicketSalesApp.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    /// <summary>
    /// AuthController that uses the orchestration service pattern.
    /// Implements the Controller → Orchestration → Services → Database architecture.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : BaseController
    {
        private readonly IAuthOrchestrationService _authOrchestrationService;
        private readonly IHtmlRenderingService _htmlRenderingService;
        private readonly IRequestDetector _requestDetector;
        private readonly IOptions<FeatureFlagOptions> _featureFlags;
        private readonly ILogger<AuthController> _logger;
        private readonly IMemoryCache _cache;
        private readonly IOidcHelperService _oidcHelperService;
        private readonly IOpenIdConnectService _openIdConnectService;
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IRealtimeEventBus _realtimeEventBus;
        private readonly IAuthWebSocketService _authWebSocketService;

        public AuthController(
            IAuthOrchestrationService authOrchestrationService,
            IHtmlRenderingService htmlRenderingService,
            IRequestDetector requestDetector,
            IOptions<FeatureFlagOptions> featureFlags,
            ILogger<AuthController> logger,
            IMemoryCache cache,
            IOidcHelperService oidcHelperService,
            IOpenIdConnectService openIdConnectService,
            ISpacetimeDBService spacetimeService,
            IRealtimeEventBus realtimeEventBus,
            IAuthWebSocketService authWebSocketService)
        {
            _authOrchestrationService = authOrchestrationService ?? throw new ArgumentNullException(nameof(authOrchestrationService));
            _htmlRenderingService = htmlRenderingService ?? throw new ArgumentNullException(nameof(htmlRenderingService));
            _requestDetector = requestDetector ?? throw new ArgumentNullException(nameof(requestDetector));
            _featureFlags = featureFlags ?? throw new ArgumentNullException(nameof(featureFlags));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _oidcHelperService = oidcHelperService ?? throw new ArgumentNullException(nameof(oidcHelperService));
            _openIdConnectService = openIdConnectService ?? throw new ArgumentNullException(nameof(openIdConnectService));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
            _authWebSocketService = authWebSocketService ?? throw new ArgumentNullException(nameof(authWebSocketService));
        }

        #region Traditional Authentication (2 endpoints)

        /// <summary>
        /// GET /api/auth/login - Show login page (HTML) or return login info (JSON)
        /// Enabled by: EnableLoginRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableLoginRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("login")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public IActionResult LoginPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
            if (!_featureFlags.Value.EnableLoginRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/login", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/login" });
            }
            _logger.LogInformation("Refactored Login Page endpoint called");

            if (_requestDetector.IsBrowserRequest())
            {
                var html = _htmlRenderingService.RenderLoginForm(error, message);
                return Content(html, "text/html");
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Login endpoint available. Use POST method to authenticate.",
                Data = new
                {
                    Method = "POST",
                    Endpoint = "/api/auth/login",
                    RequiredFields = new[] { "username", "password" }
                }
            });
        }

        /// <summary>
        /// POST /api/auth/login - Login with username/password
        /// Enabled by: EnableLoginRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableLoginRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("login")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<TwoFactorResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!_featureFlags.Value.EnableLoginRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/login", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/login" });
            }
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
                        TempToken = result.TempToken
                    }
                });
            }

            var loginResponse = new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "Authentication successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = new UserDto
                    {
                        Id = result.User!.Id,
                        Username = result.User.Username,
                        Email = result.User.Email,
                        PhoneNumber = result.User.PhoneNumber,
                        Role = result.User.Role
                    }
                }
            };

            // For browser requests, set cookie authentication
            if (_requestDetector.IsBrowserRequest())
            {
                _logger.LogInformation("Browser request detected, setting cookie authentication for user: {Username}", request.Username);
                
                // Parse JWT token to extract claims
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(result.Token);
                
                // Create claims identity from JWT token
                var claims = jwtToken.Claims.ToList();
                _logger.LogInformation("Setting cookie with {ClaimCount} claims", claims.Count);
                
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                
                // Sign in with cookie
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                    });
                
                _logger.LogInformation("Cookie authentication set successfully");
            }

            return Ok(loginResponse);
        }

        /// <summary>
        /// GET /api/auth/register - Show registration page (HTML) or return registration info (JSON)
        /// Enabled by: EnableRegisterRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableRegisterRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("register")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public IActionResult RegisterPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
            if (!_featureFlags.Value.EnableRegisterRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/register", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/register" });
            }
            _logger.LogInformation("Refactored Register Page endpoint called");

            if (_requestDetector.IsBrowserRequest())
            {
                var html = _htmlRenderingService.RenderRegisterForm(error, message);
                return Content(html, "text/html");
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Register endpoint available. Use POST method to create account.",
                Data = new
                {
                    Method = "POST",
                    Endpoint = "/api/auth/register",
                    RequiredFields = new[] { "username", "password", "email" }
                }
            });
        }

        /// <summary>
        /// POST /api/auth/register - Register new user account
        /// Enabled by: EnableRegisterRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableRegisterRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("register")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!_featureFlags.Value.EnableRegisterRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/register", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/register" });
            }
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
                    User = new UserDto
                    {
                        Id = result.User!.Id,
                        Username = result.User.Username,
                        Email = result.User.Email,
                        PhoneNumber = result.User.PhoneNumber,
                        Role = result.User.Role
                    }
                }
            });
        }
        #endregion

        #region TOTP Endpoints (4 endpoints)

        /// <summary>
        /// GET /api/auth/totp/setup - Setup TOTP for user
        /// Enabled by: EnableTotpSetupRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableTotpSetupRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("totp/setup")]
        [Authorize]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<TotpSetupResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> TotpSetup()
        {
            if (!_featureFlags.Value.EnableTotpSetupRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/totp/setup", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/totp/setup" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableTotpVerifyRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("totp/verify")]
        [Authorize]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> TotpVerify([FromBody] VerifyTotpRequest request)
        {
            if (!_featureFlags.Value.EnableTotpVerifyRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/totp/verify", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/totp/verify" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableTotpDisableRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("totp/disable")]
        [Authorize]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> TotpDisable()
        {
            if (!_featureFlags.Value.EnableTotpDisableRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/totp/disable", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/totp/disable" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableTotpValidateRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("totp/validate")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> TotpValidate([FromBody] ValidateTotpRequest request)
        {
            if (!_featureFlags.Value.EnableTotpValidateRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/totp/validate", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/totp/validate" });
            }
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

            var loginResponse = new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "TOTP validation successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = new UserDto
                    {
                        Id = result.User!.Id,
                        Username = result.User.Username,
                        Email = result.User.Email,
                        PhoneNumber = result.User.PhoneNumber,
                        Role = result.User.Role
                    }
                }
            };

            // For browser requests, set cookie authentication
            if (_requestDetector.IsBrowserRequest())
            {
                _logger.LogInformation("Browser request detected, setting cookie authentication after TOTP validation");
                
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(result.Token);
                var claims = jwtToken.Claims.ToList();
                
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                    });
                
                _logger.LogInformation("Cookie authentication set successfully after TOTP validation");
            }

            return Ok(loginResponse);
        }

        #endregion

        #region WebAuthn Endpoints (7 endpoints)

        /// <summary>
        /// POST /api/auth/webauthn/register/options - Get WebAuthn registration options
        /// Enabled by: EnableWebAuthnRegisterOptionsRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableWebAuthnRegisterOptionsRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("webauthn/register/options")]
        [Authorize]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> WebAuthnRegisterOptions()
        {
            if (!_featureFlags.Value.EnableWebAuthnRegisterOptionsRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/webauthn/register/options", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/webauthn/register/options" });
            }
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

            return Ok(new ApiResponse<Fido2NetLib.CredentialCreateOptions>
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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableWebAuthnRegisterCompleteRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("webauthn/register/complete")]
        [Authorize]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> WebAuthnRegisterComplete([FromBody] WebAuthnRegisterCompleteRequest request)
        {
            if (!_featureFlags.Value.EnableWebAuthnRegisterCompleteRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/webauthn/register/complete", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/webauthn/register/complete" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
            var attestationResponse = System.Text.Json.JsonSerializer.Deserialize<Fido2NetLib.AuthenticatorAttestationRawResponse>(request.AttestationResponse)!;
            var result = await _authOrchestrationService.RegisterWebAuthnAsync(identity, username, attestationResponse);

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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableWebAuthnLoginOptionsRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("webauthn/login/options")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> WebAuthnLoginOptions([FromBody] WebAuthnLoginOptionsRequest request)
        {
            if (!_featureFlags.Value.EnableWebAuthnLoginOptionsRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/webauthn/login/options", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/webauthn/login/options" });
            }
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

            return Ok(new ApiResponse<Fido2NetLib.AssertionOptions>
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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableWebAuthnLoginCompleteRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("webauthn/login/complete")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> WebAuthnLoginComplete([FromBody] WebAuthnLoginCompleteRequest request)
        {
            if (!_featureFlags.Value.EnableWebAuthnLoginCompleteRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/webauthn/login/complete", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/webauthn/login/complete" });
            }
            _logger.LogInformation("Refactored WebAuthn Login Complete endpoint called");

            var result = await _authOrchestrationService.CompleteWebAuthnLoginAsync(request.Username, System.Text.Json.JsonSerializer.Deserialize<Fido2NetLib.AuthenticatorAssertionRawResponse>(request.AssertionResponse)!);

            if (!result.Success)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "WebAuthn login failed"
                });
            }

            var loginResponse = new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "WebAuthn login successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = new UserDto
                    {
                        Id = result.User!.Id,
                        Username = result.User.Username,
                        Email = result.User.Email,
                        PhoneNumber = result.User.PhoneNumber,
                        Role = result.User.Role
                    }
                }
            };

            // For browser requests, set cookie authentication
            if (_requestDetector.IsBrowserRequest())
            {
                _logger.LogInformation("Browser request detected, setting cookie authentication after WebAuthn login");
                
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(result.Token);
                var claims = jwtToken.Claims.ToList();
                
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                    });
                
                _logger.LogInformation("Cookie authentication set successfully after WebAuthn login");
            }

            return Ok(loginResponse);
        }

        /// <summary>
        /// POST /api/auth/webauthn/validate - Validate WebAuthn assertion during 2FA
        /// Enabled by: EnableWebAuthnValidateRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableWebAuthnValidateRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("webauthn/validate")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> WebAuthnValidate([FromBody] WebAuthnValidateRequest request)
        {
            if (!_featureFlags.Value.EnableWebAuthnValidateRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/webauthn/validate", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/webauthn/validate" });
            }
            _logger.LogInformation("Refactored WebAuthn Validate endpoint called");

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid request data"
                });
            }

            var result = await _authOrchestrationService.ValidateWebAuthnAsync(request.TempToken, System.Text.Json.JsonSerializer.Deserialize<Fido2NetLib.AuthenticatorAssertionRawResponse>(request.AssertionResponse)!);

            if (!result.Success)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "WebAuthn validation failed"
                });
            }

            var loginResponse = new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "WebAuthn validation successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = new UserDto
                    {
                        Id = result.User!.Id,
                        Username = result.User.Username,
                        Email = result.User.Email,
                        PhoneNumber = result.User.PhoneNumber,
                        Role = result.User.Role
                    }
                }
            };

            // For browser requests, set cookie authentication
            if (_requestDetector.IsBrowserRequest())
            {
                _logger.LogInformation("Browser request detected, setting cookie authentication after WebAuthn validation");
                
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(result.Token);
                var claims = jwtToken.Claims.ToList();
                
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                    });
                
                _logger.LogInformation("Cookie authentication set successfully after WebAuthn validation");
            }

            return Ok(loginResponse);
        }

        /// <summary>
        /// GET /api/auth/webauthn/credentials - Get user's WebAuthn credentials
        /// Enabled by: EnableWebAuthnCredentialsRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableWebAuthnCredentialsRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("webauthn/credentials")]
        [Authorize]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<List<object>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> WebAuthnCredentials()
        {
            if (!_featureFlags.Value.EnableWebAuthnCredentialsRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/webauthn/credentials", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/webauthn/credentials" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
            var result = await _authOrchestrationService.GetWebAuthnCredentialsAsync(identity);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to retrieve credentials"
                });
            }

            return Ok(new ApiResponse<BRU_AVTOPARK.Models.Responses.WebAuthnCredentialsResponse>
            {
                Success = true,
                Message = "Credentials retrieved successfully",
                Data = new BRU_AVTOPARK.Models.Responses.WebAuthnCredentialsResponse
                {
                    Credentials = result.Credentials
                }
            });
        }

        /// <summary>
        /// DELETE /api/auth/webauthn/credentials/{id} - Remove WebAuthn credential
        /// Enabled by: EnableWebAuthnCredentialDeleteRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableWebAuthnCredentialDeleteRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpDelete("webauthn/credentials/{id}")]
        [Authorize]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> WebAuthnCredentialDelete(string id)
        {
            if (!_featureFlags.Value.EnableWebAuthnCredentialDeleteRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "DELETE /api/auth/webauthn/credentials/{id}", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "DELETE /api/auth/webauthn/credentials/{id}" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableMagicLinkSendRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("magic-link/send")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<MagicLinkResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> MagicLinkSend([FromBody] MagicLinkRequest request)
        {
            if (!_featureFlags.Value.EnableMagicLinkSendRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/magic-link/send", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/magic-link/send" });
            }
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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableMagicLinkValidateRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("validate-magic-link")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> MagicLinkValidate([FromBody] MagicLinkValidateRequest request)
        {
            if (!_featureFlags.Value.EnableMagicLinkValidateRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/validate-magic-link", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/validate-magic-link" });
            }
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

            var loginResponse = new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "Magic link validation successful",
                Data = new LoginResponse
                {
                    Token = result.Token!,
                    Claims = result.Claims,
                    User = new UserDto
                    {
                        Id = result.User!.Id,
                        Username = result.User.Username,
                        Email = result.User.Email,
                        PhoneNumber = result.User.PhoneNumber,
                        Role = result.User.Role
                    }
                }
            };

            // For browser requests, set cookie authentication
            if (_requestDetector.IsBrowserRequest())
            {
                _logger.LogInformation("Browser request detected, setting cookie authentication after Magic Link validation");
                
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(result.Token);
                var claims = jwtToken.Claims.ToList();
                
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                    });
                
                _logger.LogInformation("Cookie authentication set successfully after Magic Link validation");
            }

            return Ok(loginResponse);
        }

        /// <summary>
        /// GET /api/auth/magic-link - Show magic link login page
        /// Enabled by: EnableMagicLinkPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableMagicLinkPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("magic-link")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public Task<IActionResult> MagicLinkPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
            if (!_featureFlags.Value.EnableMagicLinkPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/magic-link", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/magic-link" }));
            }
            _logger.LogInformation("Refactored Magic Link Page endpoint called");

            var html = _htmlRenderingService.RenderMagicLinkForm(error, message);
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        #endregion

        #region QR Authentication Endpoints (7 endpoints)

        /// <summary>
        /// GET /api/auth/qr-login - Show QR login page
        /// Enabled by: EnableQRLoginPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableQRLoginPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("qr-login")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public Task<IActionResult> QRLoginPage()
        {
            if (!_featureFlags.Value.EnableQRLoginPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/qr-login", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/qr-login" }));
            }
            _logger.LogInformation("Refactored QR Login Page endpoint called");

            // Generate a placeholder QR code for the page
            var html = _htmlRenderingService.RenderQrLogin("placeholder-qr-code");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// POST /api/auth/qr-login/generate - Generate QR login token
        /// Enabled by: EnableQRLoginGenerateRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableQRLoginGenerateRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("qr-login/generate")]
        [Authorize]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<QRLoginResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> QRLoginGenerate()
        {
            if (!_featureFlags.Value.EnableQRLoginGenerateRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/qr-login/generate", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/qr-login/generate" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
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
                    Token = result.QrCode!,
                    QrCodeData = result.RawData!,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                }
            });
        }

        /// <summary>
        /// POST /api/auth/qr-login/validate - Validate QR login token
        /// Enabled by: EnableQRLoginValidateRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableQRLoginValidateRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("qr-login/validate")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> QRLoginValidate([FromBody] QRLoginValidateRequest request)
        {
            if (!_featureFlags.Value.EnableQRLoginValidateRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/qr-login/validate", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/qr-login/validate" });
            }
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

            return Ok(new ApiResponse<BRU_AVTOPARK.Models.Responses.LoginResponse>
            {
                Success = true,
                Message = "QR login successful",
                Data = new BRU_AVTOPARK.Models.Responses.LoginResponse
                {
                    Token = result.Token!,
                    Claims = new Dictionary<string, object>(),
                    User = new BRU_AVTOPARK.Models.Responses.UserDto
                    {
                        Id = result.User!.Id,
                        Username = result.User.Username,
                        Email = result.User.Email,
                        PhoneNumber = result.User.PhoneNumber,
                        Role = result.User.Role
                    }
                }
            });
        }

        /// <summary>
        /// POST /api/auth/qr-login/direct - Direct QR login (no 2FA)
        /// Enabled by: EnableQRLoginDirectRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableQRLoginDirectRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("qr-login/direct")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> QRLoginDirect([FromBody] QRLoginDirectRequest request)
        {
            if (!_featureFlags.Value.EnableQRLoginDirectRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/qr-login/direct", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/qr-login/direct" });
            }
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
                    Token = result.QrCode!,
                    QrCodeData = result.RawData!,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                }
            });
        }

        /// <summary>
        /// GET /api/auth/qr-login/status - Check QR login status
        /// Enabled by: EnableQRLoginStatusRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableQRLoginStatusRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("qr-login/status")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<QRLoginStatusResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> QRLoginStatus([FromQuery] string deviceId)
        {
            if (!_featureFlags.Value.EnableQRLoginStatusRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/qr-login/status", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/qr-login/status" });
            }
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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableQRLoginCancelRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("qr-login/cancel")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> QRLoginCancel([FromBody] QRLoginCancelRequest request)
        {
            if (!_featureFlags.Value.EnableQRLoginCancelRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/qr-login/cancel", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/qr-login/cancel" });
            }
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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableQRLoginNotifyRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("qr-login/notify")]
        [Authorize]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> QRLoginNotify([FromBody] QRLoginNotifyRequest request)
        {
            if (!_featureFlags.Value.EnableQRLoginNotifyRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/qr-login/notify", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/qr-login/notify" });
            }
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
        /// 
        /// CRITICAL: This endpoint follows OpenIddict's architecture requirements.
        /// - HttpContext.GetOpenIddictServerRequest() MUST stay in controller
        /// - HttpContext.AuthenticateAsync() MUST stay in controller
        /// - SignIn() MUST stay in controller
        /// - Forbid() MUST stay in controller
        /// - Only validation and claims building are delegated to service layer
        /// 
        /// Reference: CRITICAL_OIDC_CONTROLLER_REQUIREMENTS.md
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthAuthorizeRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("~/connect/authorize")]
        [HttpPost("~/connect/authorize")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status302Found)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Authorize()
        {
            if (!_featureFlags.Value.EnableOAuthAuthorizeRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET|POST ~/connect/authorize", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "~/connect/authorize" });
            }
            _logger.LogInformation("Refactored OAuth Authorize endpoint called");

            // STEP 1: GET OPENIDDICT REQUEST (MUST BE IN CONTROLLER)
            // This retrieves the OAuth request from HttpContext with all parameters
            var request = HttpContext.GetOpenIddictServerRequest();
            if (request == null)
            {
                _logger.LogError("OpenIddict request not found in HttpContext");
                return BadRequest(new { error = Errors.InvalidRequest, error_description = "The OpenID Connect request cannot be retrieved." });
            }

            // STEP 2: DELEGATE CLIENT VALIDATION TO SERVICE
            // Service layer validates client_id, redirect_uri, and scope
            var validationResult = await _authOrchestrationService.ValidateOAuthRequestAsync(
                request.ClientId ?? "",
                request.RedirectUri ?? "",
                request.Scope ?? ""
            );

            if (!validationResult.Success)
            {
                _logger.LogWarning("OAuth client validation failed: {Error}", validationResult.ErrorMessage);
                
                // STEP 3: RETURN OAUTH ERROR (MUST BE IN CONTROLLER)
                // Forbid() generates proper OAuth error response
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = validationResult.ErrorMessage
                    }));
            }

            // STEP 4: CHECK AUTHENTICATION (MUST BE IN CONTROLLER)
            // Verify user is logged in via cookie authentication
            var authenticateResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            
            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                _logger.LogInformation("User not authenticated, showing OIDC login form");
                
                // Store request for later - CRITICAL: Store the ENTIRE OpenIddict request context
                // This preserves ALL parameters including PKCE (code_challenge, code_challenge_method)
                var requestId = Guid.NewGuid().ToString();
                
                // Store ALL request parameters to preserve OpenIddict context
                var requestParams = new Dictionary<string, string>();
                foreach (var param in request.GetParameters())
                {
                    // OpenIddictParameter is a struct that wraps the actual value
                    // Convert to string, handling null values
                    var stringValue = param.Value.Value?.ToString();
                    if (!string.IsNullOrEmpty(stringValue))
                    {
                        requestParams[param.Key] = stringValue;
                    }
                }
                
                _cache.Set($"oidc_request_params_{requestId}", requestParams, TimeSpan.FromMinutes(10));
                
                _logger.LogInformation("Stored OIDC request with {Count} parameters including PKCE data", requestParams.Count);

                // Get client display name (used by both HTML and JSON responses)
                var clientResult = await _openIdConnectService.GetApplicationByClientIdAsync(request.ClientId ?? "");
                var clientName = request.ClientId ?? "Unknown";
                if (clientResult.success && clientResult.application != null)
                {
                    clientName = await _oidcHelperService.GetDisplayNameAsync(clientResult.application) ?? clientName;
                }

                var scopes = request.Scope?.Split(' ') ?? Array.Empty<string>();

                if (_requestDetector.IsBrowserRequest())
                {
                    return Content(_htmlRenderingService.RenderOAuthLoginForm(requestId, clientName, scopes), "text/html");
                }

                // JSON response for headless clients: return all info needed to render their own login UI
                return Ok(new
                {
                    requestId,
                    clientName,
                    scopes,
                    redirectUri = request.RedirectUri,
                    state = request.State
                });
            }

            var username = authenticateResult.Principal.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("Authenticated user has no username");
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidRequest,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "User identity not found"
                    }));
            }

            // STEP 5: DELEGATE CLAIMS BUILDING TO SERVICE
            // Service layer builds ClaimsIdentity with user claims, roles, and permissions
            var claimsResult = await _authOrchestrationService.BuildOAuthClaimsIdentityAsync(
                username,
                request.GetScopes().ToArray()
            );

            if (!claimsResult.Success || claimsResult.Identity == null)
            {
                _logger.LogWarning("Failed to build claims identity: {Error}", claimsResult.ErrorMessage);
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ServerError,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = claimsResult.ErrorMessage ?? "Failed to build user identity"
                    }));
            }

            // STEP 6: SIGN IN WITH OPENIDDICT (MUST BE IN CONTROLLER)
            // SignIn() generates authorization code and redirects to client
            _logger.LogInformation("OAuth authorization successful for user: {Username}, client: {ClientId}", username, request.ClientId);
            return SignIn(
                new ClaimsPrincipal(claimsResult.Identity),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme
            );
        }

        /// <summary>
        /// POST ~/connect/token - OAuth token exchange endpoint
        /// Enabled by: EnableOAuthTokenRefactoring feature flag
        ///
        /// CRITICAL: This endpoint follows OpenIddict's architecture requirements.
        /// - HttpContext.GetOpenIddictServerRequest() MUST stay in controller
        /// - HttpContext.AuthenticateAsync() MUST stay in controller
        /// - SignIn() MUST stay in controller
        /// - Forbid() MUST stay in controller
        /// - Only user validation and claims building are delegated to service layer
        ///
        /// Reference: CRITICAL_OIDC_CONTROLLER_REQUIREMENTS.md
        ///
        /// Returns standard OAuth 2.0 JSON token response:
        /// {
        ///   "access_token": "...",
        ///   "token_type": "Bearer",
        ///   "expires_in": 3600,
        ///   "refresh_token": "...",   // present when offline_access scope was granted
        ///   "id_token": "..."         // present when openid scope was granted
        /// }
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthTokenRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("~/connect/token")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Exchange()
        {
            if (!_featureFlags.Value.EnableOAuthTokenRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST ~/connect/token", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "~/connect/token" });
            }
            _logger.LogInformation("Refactored OAuth Token endpoint called");

            // STEP 1: GET OPENIDDICT REQUEST (MUST BE IN CONTROLLER)
            // This retrieves the OAuth token request from HttpContext
            var request = HttpContext.GetOpenIddictServerRequest();
            if (request == null)
            {
                _logger.LogError("OpenIddict request not found in HttpContext");
                return BadRequest(new { error = Errors.InvalidRequest, error_description = "The OpenID Connect request cannot be retrieved." });
            }

            // STEP 2: HANDLE AUTHORIZATION CODE GRANT TYPE
            if (request.IsAuthorizationCodeGrantType())
            {
                // STEP 3: AUTHENTICATE AUTHORIZATION CODE (MUST BE IN CONTROLLER)
                // This validates the authorization code and returns the principal with original claims
                var authenticateResult = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                
                if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
                {
                    _logger.LogWarning("Authorization code authentication failed");
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The authorization code is invalid or expired"
                        }));
                }

                var principal = authenticateResult.Principal;
                var userId = principal.FindFirst(Claims.Subject)?.Value;
                
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("No subject claim found in authorization code principal");
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "User identity not found in authorization code"
                        }));
                }

                // STEP 4: DELEGATE USER VALIDATION TO SERVICE
                // Service layer validates user still exists and is active
                var userResult = await _authOrchestrationService.ValidateUserForTokenExchangeAsync(userId);
                
                if (!userResult.Success || userResult.UserId == null)
                {
                    _logger.LogWarning("User validation failed for token exchange: {Error}", userResult.ErrorMessage);
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = userResult.ErrorMessage ?? "User not found or inactive"
                        }));
                }

                // STEP 5: DELEGATE CLAIMS BUILDING TO SERVICE
                // Service layer builds fresh ClaimsIdentity with current user claims, roles, and permissions
                var identity = await _authOrchestrationService.BuildOAuthTokenIdentityAsync(
                    userResult.UserId.Value,
                    principal.GetScopes(),
                    principal.GetResources()
                );

                if (identity == null)
                {
                    _logger.LogWarning("Failed to build token identity for user: {UserId}", userId);
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ServerError,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Failed to build token identity"
                        }));
                }

                // STEP 6: SIGN IN WITH OPENIDDICT (MUST BE IN CONTROLLER)
                // SignIn() generates access token, refresh token, and id_token
                _logger.LogInformation("OAuth token exchange successful for user: {UserId}", userId);
                return SignIn(
                    new ClaimsPrincipal(identity),
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme
                );
            }

            // STEP 7: HANDLE REFRESH TOKEN GRANT TYPE
            if (request.IsRefreshTokenGrantType())
            {
                // STEP 8: AUTHENTICATE REFRESH TOKEN (MUST BE IN CONTROLLER)
                var authenticateResult = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                
                if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
                {
                    _logger.LogWarning("Refresh token authentication failed");
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The refresh token is invalid or expired"
                        }));
                }

                var principal = authenticateResult.Principal;
                var userId = principal.FindFirst(Claims.Subject)?.Value;
                
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("No subject claim found in refresh token principal");
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "User identity not found in refresh token"
                        }));
                }

                // STEP 9: DELEGATE USER VALIDATION TO SERVICE
                var userResult = await _authOrchestrationService.ValidateUserForTokenExchangeAsync(userId);
                
                if (!userResult.Success || userResult.UserId == null)
                {
                    _logger.LogWarning("User validation failed for refresh token: {Error}", userResult.ErrorMessage);
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = userResult.ErrorMessage ?? "User not found or inactive"
                        }));
                }

                // STEP 10: DELEGATE CLAIMS BUILDING TO SERVICE
                var identity = await _authOrchestrationService.BuildOAuthTokenIdentityAsync(
                    userResult.UserId.Value,
                    principal.GetScopes(),
                    principal.GetResources()
                );

                if (identity == null)
                {
                    _logger.LogWarning("Failed to build token identity for refresh token: {UserId}", userId);
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ServerError,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Failed to build token identity"
                        }));
                }

                // STEP 11: SIGN IN WITH OPENIDDICT (MUST BE IN CONTROLLER)
                _logger.LogInformation("OAuth refresh token exchange successful for user: {UserId}", userId);
                return SignIn(
                    new ClaimsPrincipal(identity),
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme
                );
            }

            // Unsupported grant type
            _logger.LogWarning("Unsupported grant type: {GrantType}", request.GrantType);
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnsupportedGrantType,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The specified grant type is not supported"
                }));
        }

        /// <summary>
        /// GET ~/connect/userinfo - OAuth user info endpoint
        /// Enabled by: EnableOAuthUserInfoRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthUserInfoRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("~/connect/userinfo")]
        [Authorize]
        [Produces("application/json")]
        [ProducesResponseType(typeof(UserInfoResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> OAuthUserInfo()
        {
            if (!_featureFlags.Value.EnableOAuthUserInfoRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET ~/connect/userinfo", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "~/connect/userinfo" });
            }
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

            return Ok(result.Claims);
        }

        /// <summary>
        /// POST /api/auth/oauth/authorize - Backchannel OAuth authorize endpoint for headless/native clients.
        /// Enabled by: EnableOAuthBackchannelAuthorizeRefactoring feature flag
        ///
        /// Allows non-browser clients (desktop apps, mobile apps, CLI tools) to complete the full
        /// OAuth 2.0 authorization code flow without browser redirects. The client authenticates
        /// the user directly (username/password or existing token), and receives an authorization
        /// code that can be exchanged at ~/connect/token.
        ///
        /// Security: Only allowed for confidential/native client types. Public browser clients
        /// must use the standard ~/connect/authorize browser redirect flow.
        ///
        /// 2FA handling: If the user requires two-factor authentication, the endpoint returns
        /// { requiresTwoFactor: true, tempToken, twoFactorType } and the client must call
        /// /api/auth/totp/validate or /api/auth/webauthn/validate before retrying.
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthBackchannelAuthorizeRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("oauth/authorize")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> OAuthBackchannelAuthorize([FromBody] OAuthBackchannelAuthorizeRequest request)
        {
            if (!_featureFlags.Value.EnableOAuthBackchannelAuthorizeRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/oauth/authorize", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/oauth/authorize" });
            }

            if (!ModelState.IsValid)
                return BadRequest(new { error = "invalid_request", error_description = "Invalid request parameters" });

            // STEP 1: Validate the OAuth client (same logic as ~/connect/authorize)
            var validationResult = await _authOrchestrationService.ValidateOAuthRequestAsync(
                request.ClientId,
                request.RedirectUri,
                request.Scope
            );

            if (!validationResult.Success)
            {
                _logger.LogWarning("OAuthBackchannelAuthorize: Client validation failed for {ClientId}: {Error}", request.ClientId, validationResult.ErrorMessage);
                return BadRequest(new { error = "invalid_client", error_description = validationResult.ErrorMessage });
            }

            // STEP 2: Security check — only confidential/native clients may use this endpoint.
            // Public browser clients must use the standard ~/connect/authorize redirect flow.
            var appResult = await _openIdConnectService.GetApplicationByClientIdAsync(request.ClientId);
            if (!appResult.success || appResult.application == null)
            {
                return BadRequest(new { error = "invalid_client", error_description = "Client application not found" });
            }

            var appManager = _openIdConnectService.GetApplicationManager();
            var clientType = await appManager.GetClientTypeAsync(appResult.application);
            if (clientType == OpenIddictConstants.ClientTypes.Public)
            {
                _logger.LogWarning("OAuthBackchannelAuthorize: Rejected public client {ClientId} — must use browser redirect flow", request.ClientId);
                return BadRequest(new
                {
                    error = "unauthorized_client",
                    error_description = "Public browser clients must use the standard ~/connect/authorize redirect flow. This endpoint is only available to confidential and native clients."
                });
            }

            // STEP 3: Authenticate the user (username/password or existing token)
            string? authenticatedUsername = null;

            if (!string.IsNullOrEmpty(request.Token))
            {
                // Token-based authentication: validate the existing JWT
                var principal = await Task.Run(() =>
                {
                    try
                    {
                        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                        if (!handler.CanReadToken(request.Token)) return null;
                        var jwt = handler.ReadJwtToken(request.Token);
                        if (jwt.ValidTo < DateTime.UtcNow) return null;
                        return jwt.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value
                            ?? jwt.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Name)?.Value;
                    }
                    catch { return null; }
                });

                authenticatedUsername = principal;
                if (string.IsNullOrEmpty(authenticatedUsername))
                {
                    return Unauthorized(new { error = "invalid_token", error_description = "The provided token is invalid or expired" });
                }
            }
            else if (!string.IsNullOrEmpty(request.Username) && !string.IsNullOrEmpty(request.Password))
            {
                // Username/password authentication
                var loginResult = await _authOrchestrationService.LoginAsync(request.Username, request.Password);

                if (!loginResult.Success)
                {
                    return Unauthorized(new { error = "invalid_credentials", error_description = loginResult.ErrorMessage ?? "Invalid username or password" });
                }

                // STEP 4: Check if 2FA is required
                if (loginResult.RequiresTwoFactor)
                {
                    _logger.LogInformation("OAuthBackchannelAuthorize: 2FA required for user {Username}", request.Username);
                    return Ok(new
                    {
                        requiresTwoFactor = true,
                        tempToken = loginResult.TempToken,
                        twoFactorType = loginResult.TwoFactorType
                    });
                }

                authenticatedUsername = request.Username;
            }
            else
            {
                return BadRequest(new { error = "invalid_request", error_description = "Either username/password or a valid token must be provided" });
            }

            // STEP 5: Build the OAuth claims identity for the authenticated user
            var scopes = request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var claimsResult = await _authOrchestrationService.BuildOAuthClaimsIdentityAsync(authenticatedUsername, scopes);

            if (!claimsResult.Success || claimsResult.Identity == null)
            {
                _logger.LogWarning("OAuthBackchannelAuthorize: Failed to build claims identity for {Username}: {Error}", authenticatedUsername, claimsResult.ErrorMessage);
                return StatusCode(500, new { error = "server_error", error_description = claimsResult.ErrorMessage ?? "Failed to build user identity" });
            }

            // STEP 6: Store the authorization request parameters in cache so ~/connect/authorize
            // can complete the OpenIddict flow when the client follows the returned redirectUri.
            // This mirrors the same pattern used by AuthorizeCallback.
            var requestId = Guid.NewGuid().ToString();
            var requestParams = new Dictionary<string, string>
            {
                ["client_id"] = request.ClientId,
                ["redirect_uri"] = request.RedirectUri,
                ["scope"] = request.Scope,
                ["response_type"] = "code"
            };
            if (!string.IsNullOrEmpty(request.State)) requestParams["state"] = request.State;
            if (!string.IsNullOrEmpty(request.CodeChallenge)) requestParams["code_challenge"] = request.CodeChallenge;
            if (!string.IsNullOrEmpty(request.CodeChallengeMethod)) requestParams["code_challenge_method"] = request.CodeChallengeMethod;
            if (!string.IsNullOrEmpty(request.Nonce)) requestParams["nonce"] = request.Nonce;

            _cache.Set($"oidc_request_params_{requestId}", requestParams, TimeSpan.FromMinutes(10));

            // Create a cookie session for the user so ~/connect/authorize can authenticate them
            var conn = _spacetimeService.GetConnection();
            var userProfile = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == authenticatedUsername);
            if (userProfile == null)
            {
                _logger.LogError("OAuthBackchannelAuthorize: User profile not found for {Username}", authenticatedUsername);
                return StatusCode(500, new { error = "server_error", error_description = "User profile not found" });
            }

            var cookieIdentity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, authenticatedUsername),
                    new Claim(ClaimTypes.NameIdentifier, userProfile.UserId.ToString()),
                    new Claim(OpenIddictConstants.Claims.Subject, userProfile.UserId.ToString())
                },
                CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(cookieIdentity),
                new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) });

            // STEP 7: Build the authorize redirect URL with all parameters.
            // The client follows this URL to complete the OpenIddict authorization code flow.
            var queryParts = new List<string>();
            foreach (var param in requestParams)
                queryParts.Add($"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}");
            var authorizeUrl = "/connect/authorize?" + string.Join("&", queryParts);

            _logger.LogInformation("OAuthBackchannelAuthorize: Authorization prepared for user {Username}, client {ClientId}", authenticatedUsername, request.ClientId);

            // Return the authorize URL for the client to follow (with their session cookie).
            // The client GETs this URL and will receive the authorization code redirect.
            return Ok(new
            {
                redirectUri = authorizeUrl,
                requestId,
                state = request.State
            });
        }

        #endregion

        #region OAuth Client Management API Endpoints (7 endpoints)

        /// <summary>
        /// POST /api/auth/connect/clients - Register new OAuth client.
        /// Enabled by: EnableOAuthClientRegisterRefactoring feature flag.
        ///
        /// Returns JSON: <c>{ clientId, clientSecret, clientName }</c>
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientRegisterRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("connect/clients")]
        [Authorize(Roles = "Administrator")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<OAuthClientDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> OAuthClientRegister([FromBody] RegisterClientRequest request)
        {
            if (!_featureFlags.Value.EnableOAuthClientRegisterRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/connect/clients", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/connect/clients" });
            }
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
                Data = new OAuthClientDto { ClientId = result.ClientId!, DisplayName = result.DisplayName! }
            });
        }

        /// <summary>
        /// GET /api/auth/connect/clients - List all OAuth clients.
        /// Enabled by: EnableOAuthClientListRefactoring OR EnableOAuthClientsPageRefactoring feature flags.
        ///
        /// Returns JSON array for API requests: <c>[{ clientId, clientName, redirectUris, scopes, createdAt }]</c>
        /// Returns HTML page for browser requests.
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientListRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/clients")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        [ProducesResponseType(typeof(ApiResponse<List<ClientDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> OAuthClientList([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthClientListRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/clients", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/clients" });
            }
            _logger.LogInformation("Refactored OAuth Client List endpoint called");

            var result = await _authOrchestrationService.GetOAuthClientsAsync();

            if (!result.Success)
            {
                // Check if browser request
                if (_requestDetector.IsBrowserRequest())
                {
                    return BadRequest(result.ErrorMessage ?? "Failed to retrieve OAuth clients");
                }
                
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to retrieve OAuth clients"
                });
            }

            // Check if browser request - return HTML
            if (_requestDetector.IsBrowserRequest())
            {
                var html = _htmlRenderingService.RenderOidcClientsList(result.Clients.Select(c => new BRU_AVTOPARK.Models.Responses.ClientDto
                {
                    ClientId = c.ClientId,
                    DisplayName = c.DisplayName
                }).ToList(), token);

                return Content(html, "text/html");
            }

            // API request - return JSON
            return Ok(new ApiResponse<List<OAuthClientDto>>
            {
                Success = true,
                Message = "OAuth clients retrieved successfully",
                Data = result.Clients!
            });
        }

        /// <summary>
        /// GET /api/auth/connect/clients/{id} - Get OAuth client details.
        /// Enabled by: EnableOAuthClientDetailsRefactoring OR EnableOAuthClientDetailsPageRefactoring feature flags.
        ///
        /// Returns JSON for API requests: <c>{ clientId, clientName, redirectUris, scopes }</c>
        /// Returns HTML page for browser requests.
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientDetailsRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/clients/{id}")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        [ProducesResponseType(typeof(ApiResponse<OAuthClientDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> OAuthClientDetails(string id, [FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthClientDetailsRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/clients/{id}", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/clients/{id}" });
            }
            _logger.LogInformation("Refactored OAuth Client Details endpoint called for client: {ClientId}", id);

            var result = await _authOrchestrationService.GetOAuthClientAsync(id);

            if (!result.Success || result.Client == null)
            {
                // Check if browser request
                if (_requestDetector.IsBrowserRequest())
                {
                    return NotFound(result.ErrorMessage ?? "OAuth client not found");
                }
                
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "OAuth client not found"
                });
            }

            // Check if browser request - return HTML
            if (_requestDetector.IsBrowserRequest())
            {
                var clientResponse = new BRU_AVTOPARK.Models.Responses.GetClientResponse
                {
                    ClientId = result.Client.ClientId,
                    DisplayName = result.Client.DisplayName,
                    RedirectUris = result.Client.RedirectUris.ToArray(),
                    PostLogoutRedirectUris = result.Client.PostLogoutRedirectUris.ToArray(),
                    AllowedScopes = result.Client.AllowedScopes.ToArray(),
                    RequireConsent = result.Client.RequireConsent
                };

                var html = _htmlRenderingService.RenderOidcClientDetails(clientResponse, token);
                return Content(html, "text/html");
            }

            // API request - return JSON
            return Ok(new ApiResponse<OAuthClientDto>
            {
                Success = true,
                Message = "OAuth client retrieved successfully",
                Data = new OAuthClientDto
                {
                    ClientId = result.Client!.ClientId,
                    DisplayName = result.Client!.DisplayName,
                    RedirectUris = result.Client!.RedirectUris,
                    AllowedScopes = result.Client!.AllowedScopes,
                    RequireConsent = result.Client!.RequireConsent
                }
            });
        }
               

        /// <summary>
        /// PUT /api/auth/connect/clients/{id} - Update OAuth client.
        /// Enabled by: EnableOAuthClientUpdateRefactoring feature flag.
        ///
        /// Returns JSON: <c>{ success, message }</c>
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientUpdateRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPut("connect/clients/{id}")]
        [Authorize(Roles = "Administrator")]
        [Produces("application/json")]
        public async Task<IActionResult> OAuthClientUpdate(string id, [FromBody] UpdateClientRequest request)
        {
            if (!_featureFlags.Value.EnableOAuthClientUpdateRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "PUT /api/auth/connect/clients/{id}", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "PUT /api/auth/connect/clients/{id}" });
            }
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
                Data = new OAuthClientDto { ClientId = result.ClientId!, DisplayName = result.DisplayName! }
            });
        }

        /// <summary>
        /// DELETE /api/auth/connect/clients/{id} - Delete OAuth client.
        /// Enabled by: EnableOAuthClientDeleteRefactoring feature flag.
        ///
        /// Returns JSON: <c>{ success, message }</c>
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientDeleteRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpDelete("connect/clients/{id}")]
        [Authorize(Roles = "Administrator")]
        [Produces("application/json")]
        public async Task<IActionResult> OAuthClientDelete(string id)
        {
            if (!_featureFlags.Value.EnableOAuthClientDeleteRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "DELETE /api/auth/connect/clients/{id}", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "DELETE /api/auth/connect/clients/{id}" });
            }
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
        /// GET /api/connect/scopes - List available OAuth scopes
        /// Enabled by: EnableOAuthScopesRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthScopesRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/scopes")]
        [Authorize(Roles = "Administrator")]
        [Produces("application/json")]
        public async Task<IActionResult> OAuthScopes()
        {
            if (!_featureFlags.Value.EnableOAuthScopesRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/scopes", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/scopes" });
            }
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
                Data = result.Scopes.Select(s => new OAuthScopeDto { Name = s }).ToList()
            });
        }

        /// <summary>
        /// POST /api/oauth/clients/{id}/regenerate-secret - Regenerate client secret
        /// Enabled by: EnableOAuthClientRegenerateSecretRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientRegenerateSecretRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("connect/clients/{id}/regenerate-secret")]
        [Authorize(Roles = "Administrator")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiResponse<OAuthClientSecretDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> OAuthClientRegenerateSecret(string id)
        {
            if (!_featureFlags.Value.EnableOAuthClientRegenerateSecretRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/connect/clients/{id}/regenerate-secret", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/connect/clients/{id}/regenerate-secret" });
            }
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
                    ClientSecret = result.ClientSecret!
                }
            });
        }

        #endregion

        #region OAuth Admin HTML Pages Endpoints (13 endpoints)

        /// <summary>
        /// GET /oauth/clients/new - New OAuth client form page
        /// Enabled by: EnableOAuthClientNewPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientNewPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/clients/new")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public Task<IActionResult> OAuthClientNewPage([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthClientNewPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/clients/new", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/clients/new" }));
            }
            _logger.LogInformation("Refactored OAuth Client New Page endpoint called");

            if (!_requestDetector.IsBrowserRequest())
            {
                return Task.FromResult<IActionResult>(Ok(new { form = "new_client", fields = new[] { "clientId", "clientSecret", "displayName", "redirectUris", "allowedScopes", "requireConsent" } }));
            }

            var html = _htmlRenderingService.RenderOidcClientForm(null, null, token);
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/clients/{id}/edit - Edit OAuth client form page
        /// Enabled by: EnableOAuthClientEditPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientEditPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/clients/{id}/edit")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public async Task<IActionResult> OAuthClientEditPage(string id, [FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthClientEditPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/clients/{id}/edit", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/clients/{id}/edit" });
            }
            _logger.LogInformation("Refactored OAuth Client Edit Page endpoint called for client: {ClientId}", id);

            var result = await _authOrchestrationService.GetOAuthClientAsync(id);

            if (!result.Success || result.Client == null)
            {
                if (!_requestDetector.IsBrowserRequest())
                    return NotFound(new { error = "not_found", error_description = result.ErrorMessage ?? "OAuth client not found" });
                return NotFound(result.ErrorMessage ?? "OAuth client not found");
            }

            var clientResponse = new BRU_AVTOPARK.Models.Responses.GetClientResponse
            {
                ClientId = result.Client.ClientId,
                DisplayName = result.Client.DisplayName,
                RedirectUris = result.Client.RedirectUris.ToArray(),
                PostLogoutRedirectUris = result.Client.PostLogoutRedirectUris.ToArray(),
                AllowedScopes = result.Client.AllowedScopes.ToArray(),
                RequireConsent = result.Client.RequireConsent
            };

            if (!_requestDetector.IsBrowserRequest())
            {
                return Ok(new
                {
                    clientId = result.Client.ClientId,
                    clientName = result.Client.DisplayName,
                    redirectUris = result.Client.RedirectUris,
                    postLogoutRedirectUris = result.Client.PostLogoutRedirectUris,
                    allowedScopes = result.Client.AllowedScopes,
                    requireConsent = result.Client.RequireConsent
                });
            }

            var html = _htmlRenderingService.RenderOidcClientForm(id, clientResponse, token);
            return Content(html, "text/html");
        }

        /// <summary>
        /// GET /connect/scopes - OAuth scopes list page
        /// Enabled by: EnableOAuthScopesPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthScopesPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/scopes")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public async Task<IActionResult> OAuthScopesPage([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthScopesPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/scopes (page)", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/scopes (page)" });
            }
            _logger.LogInformation("Refactored OAuth Scopes Page endpoint called");

            var result = await _authOrchestrationService.GetOAuthScopesAsync();

            if (!result.Success)
            {
                if (!_requestDetector.IsBrowserRequest())
                    return BadRequest(new { error = "server_error", error_description = result.ErrorMessage ?? "Failed to retrieve OAuth scopes" });
                return BadRequest(result.ErrorMessage ?? "Failed to retrieve OAuth scopes");
            }

            if (!_requestDetector.IsBrowserRequest())
            {
                return Ok(new
                {
                    scopes = result.Scopes.Select(s => new { name = s, description = (string?)null, resources = Array.Empty<string>() }).ToList()
                });
            }

            var scopeDtos = result.Scopes.Select(s => new BRU_AVTOPARK.Models.Responses.ScopeDto
            {
                Name = s,
                DisplayName = s,
                Description = null,
                OidcId = s
            }).ToList();

            var html = _htmlRenderingService.RenderOidcScopesList(scopeDtos, token);
            return Content(html, "text/html");
        }

        /// <summary>
        /// GET /oauth/authorizations - OAuth authorizations list page
        /// Enabled by: EnableOAuthAuthorizationsPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthAuthorizationsPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("oauth/authorizations")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public Task<IActionResult> OAuthAuthorizationsPage([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthAuthorizationsPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/oauth/authorizations", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/oauth/authorizations" }));
            }
            _logger.LogInformation("Refactored OAuth Authorizations Page endpoint called");

            if (!_requestDetector.IsBrowserRequest())
            {
                return Task.FromResult<IActionResult>(Ok(new { authorizations = Array.Empty<object>(), message = "OAuth authorizations page not yet fully implemented" }));
            }

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth authorizations page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/tokens - OAuth tokens list page
        /// Enabled by: EnableOAuthTokensPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthTokensPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/tokens")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public Task<IActionResult> OAuthTokensPage([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthTokensPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/tokens", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/tokens" }));
            }
            _logger.LogInformation("Refactored OAuth Tokens Page endpoint called");

            if (!_requestDetector.IsBrowserRequest())
            {
                return Task.FromResult<IActionResult>(Ok(new { tokens = Array.Empty<object>(), message = "OAuth tokens page not yet fully implemented" }));
            }

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth tokens page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/dashboard - OAuth admin dashboard page
        /// Enabled by: EnableOAuthDashboardPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthDashboardPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("oauth/dashboard")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public Task<IActionResult> OAuthDashboardPage([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthDashboardPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/oauth/dashboard", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/oauth/dashboard" }));
            }
            _logger.LogInformation("Refactored OAuth Dashboard Page endpoint called");

            if (!_requestDetector.IsBrowserRequest())
            {
                return Task.FromResult<IActionResult>(Ok(new { dashboard = "oauth_admin", message = "OAuth dashboard page not yet fully implemented" }));
            }

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth dashboard page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/settings - OAuth settings page
        /// Enabled by: EnableOAuthSettingsPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthSettingsPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/settings")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public Task<IActionResult> OAuthSettingsPage([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthSettingsPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/settings", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/settings" }));
            }
            _logger.LogInformation("Refactored OAuth Settings Page endpoint called");

            if (!_requestDetector.IsBrowserRequest())
            {
                return Task.FromResult<IActionResult>(Ok(new { settings = new object(), message = "OAuth settings page not yet fully implemented" }));
            }

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth settings page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/logs - OAuth audit logs page
        /// Enabled by: EnableOAuthLogsPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthLogsPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/logs")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public Task<IActionResult> OAuthLogsPage([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthLogsPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/logs", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/logs" }));
            }
            _logger.LogInformation("Refactored OAuth Logs Page endpoint called");

            if (!_requestDetector.IsBrowserRequest())
            {
                return Task.FromResult<IActionResult>(Ok(new { logs = Array.Empty<object>(), message = "OAuth logs page not yet fully implemented" }));
            }

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth logs page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/help - OAuth help/documentation page
        /// Enabled by: EnableOAuthHelpPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthHelpPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/help")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public Task<IActionResult> OAuthHelpPage([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthHelpPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/help", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/help" }));
            }
            _logger.LogInformation("Refactored OAuth Help Page endpoint called");

            if (!_requestDetector.IsBrowserRequest())
            {
                return Task.FromResult<IActionResult>(Ok(new { help = "oauth_help", message = "OAuth help page not yet fully implemented" }));
            }

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth help page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/test - OAuth test/playground page
        /// Enabled by: EnableOAuthTestPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthTestPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/test")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public Task<IActionResult> OAuthTestPage([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableOAuthTestPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/test", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/test" }));
            }
            _logger.LogInformation("Refactored OAuth Test Page endpoint called");

            if (!_requestDetector.IsBrowserRequest())
            {
                return Task.FromResult<IActionResult>(Ok(new { test = "oauth_playground", message = "OAuth test page not yet fully implemented" }));
            }

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth test page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/callback - OAuth callback page
        /// Enabled by: EnableOAuthCallbackPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthCallbackPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("connect/callback")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public Task<IActionResult> OAuthCallbackPage([FromQuery] string? code = null, [FromQuery] string? error = null)
        {
            if (!_featureFlags.Value.EnableOAuthCallbackPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/connect/callback", userIdentity, DateTime.UtcNow);
                return Task.FromResult<IActionResult>(StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/connect/callback" }));
            }
            _logger.LogInformation("Refactored OAuth Callback Page endpoint called");

            if (!_requestDetector.IsBrowserRequest())
            {
                if (!string.IsNullOrEmpty(error))
                    return Task.FromResult<IActionResult>(BadRequest(new { error, code = (string?)null }));
                return Task.FromResult<IActionResult>(Ok(new { code, error = (string?)null }));
            }

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage(error ?? "OAuth callback page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        #endregion

        #region Profile & Utility Endpoints (8 endpoints)

        /// <summary>
        /// GET /api/auth/profile - Get user profile
        /// Enabled by: EnableProfileRefactoring feature flag
        /// Accepts token from Authorization header OR query string parameter
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableProfileRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("profile")]
        [AllowAnonymous]
        [Produces("application/json", "text/html")]
        public async Task<IActionResult> Profile([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableProfileRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/profile", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/profile" });
            }
            _logger.LogInformation("Refactored Profile endpoint called");

            var isBrowserRequest = _requestDetector.IsBrowserRequest();

            // Check Authorization header first
            if (string.IsNullOrEmpty(token) && Request.Headers.Authorization.Count > 0)
            {
                var authHeader = Request.Headers.Authorization.ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = authHeader.Substring("Bearer ".Length).Trim();
                }
            }

            // If no auth header, check query string
            if (string.IsNullOrEmpty(token) && Request.Query.ContainsKey("token"))
            {
                token = Request.Query["token"];
            }

            // If still no token and browser request, check localStorage via JavaScript
            if (string.IsNullOrEmpty(token) && isBrowserRequest)
            {
                return Content($@"
                    <script>
                        const storedToken = localStorage.getItem('auth_token');
                        if (storedToken) {{
                            window.location.href = '/api/auth/profile?token=' + encodeURIComponent(storedToken);
                        }} else {{
                            window.location.href = '/api/auth/login?error=Please log in to view your profile';
                        }}
                    </script>
                ", "text/html");
            }

            // If no token and API request, return error
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Token is required (provide via Authorization header or ?token= query parameter)"
                });
            }

            // Validate token format
            if (!token.Contains('.') || token.Count(c => c == '.') != 2)
            {
                if (isBrowserRequest)
                {
                    return Redirect("/api/auth/login?error=Invalid token format");
                }
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid token format"
                });
            }

            // For browser requests, get SpacetimeDB data for HTML rendering
            if (isBrowserRequest)
            {
                var profileRenderData = await _authOrchestrationService.GetProfileWithSpacetimeDataAsync(token);

                if (profileRenderData == null)
                {
                    // Clear localStorage and redirect to login
                    return Content(@"
                        <script>
                            localStorage.removeItem('auth_token');
                            window.location.href = '/api/auth/login?error=' + encodeURIComponent('Invalid token. Please log in again.');
                        </script>
                    ", "text/html");
                }

                // Render HTML using HtmlRenderingService
                var html = _htmlRenderingService.RenderProfilePage(
                    profileRenderData.User,
                    profileRenderData.TotpEnabled,
                    profileRenderData.WebAuthnCredentials,
                    profileRenderData.Roles,
                    profileRenderData.Permissions
                );

                return Content(html, "text/html");
            }

            // For API requests, return JSON
            var profile = await _authOrchestrationService.GetProfileAsync(token);

            if (profile == null)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Profile not found or invalid token"
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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableProfileUpdateRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPut("profile")]
        [Authorize]
        public async Task<IActionResult> ProfileUpdate([FromBody] UpdateProfileRequest request)
        {
            if (!_featureFlags.Value.EnableProfileUpdateRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "PUT /api/auth/profile", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "PUT /api/auth/profile" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
            var result = await _authOrchestrationService.UpdateProfileAsync(
                identity, 
                request.Email, 
                request.PhoneNumber, 
                request.DisplayName
            );

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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableChangePasswordRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!_featureFlags.Value.EnableChangePasswordRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/change-password", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/change-password" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
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
        /// GET /api/auth/logout - Show logout confirmation page
        /// Enabled by: EnableLogoutRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableLogoutRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("logout")]
        [AllowAnonymous]
        public IActionResult LogoutPage()
        {
            if (!_featureFlags.Value.EnableLogoutRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/logout", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/logout" });
            }
            _logger.LogInformation("Refactored Logout page requested");

            if (_requestDetector.IsBrowserRequest())
            {
                return Content(_htmlRenderingService.RenderLogoutPage(), "text/html");
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Logged out successfully"
            });
        }

        /// <summary>
        /// POST /api/auth/logout - Logout user
        /// Enabled by: EnableLogoutRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableLogoutRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            if (!_featureFlags.Value.EnableLogoutRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/logout", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/logout" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
            var result = await _authOrchestrationService.LogoutAsync(identity);

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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableRefreshTokenRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            if (!_featureFlags.Value.EnableRefreshTokenRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/refresh", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/refresh" });
            }
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
                    ExpiresAt = result.ExpiresAt ?? DateTime.UtcNow.AddHours(1)
                }
            });
        }

        /// <summary>
        /// GET /api/auth/settings - Get user authentication settings
        /// Enabled by: EnableSettingsRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableSettingsRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("settings")]
        [Authorize]
        public async Task<IActionResult> Settings()
        {
            if (!_featureFlags.Value.EnableSettingsRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/settings", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/settings" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
            var result = await _authOrchestrationService.GetSettingsAsync(identity);

            if (!result.Success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Failed to retrieve settings"
                });
            }

            return Ok(new ApiResponse<BRU_AVTOPARK.Models.Responses.UserSettingsDto>
            {
                Success = true,
                Message = "Settings retrieved successfully",
                Data = new BRU_AVTOPARK.Models.Responses.UserSettingsDto
                {
                    TotpEnabled = result.Settings!.TotpEnabled,
                    WebAuthnEnabled = result.Settings.WebAuthnEnabled,
                    EmailNotifications = result.Settings.EmailNotifications,
                    SmsNotifications = false // Default value as the service DTO doesn't have this
                }
            });
        }

        /// <summary>
        /// PUT /api/auth/settings - Update user authentication settings
        /// Enabled by: EnableSettingsUpdateRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableSettingsUpdateRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPut("settings")]
        [Authorize]
        public async Task<IActionResult> SettingsUpdate([FromBody] UpdateSettingsRequest request)
        {
            if (!_featureFlags.Value.EnableSettingsUpdateRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "PUT /api/auth/settings", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "PUT /api/auth/settings" });
            }
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
            var result = await _authOrchestrationService.UpdateSettingsAsync(
                identity, 
                request.TotpEnabled, 
                request.WebAuthnEnabled, 
                request.EmailNotifications
            );

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
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableStatusRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("status")]
        [AllowAnonymous]
        public async Task<IActionResult> Status()
        {
            if (!_featureFlags.Value.EnableStatusRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/status", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/status" });
            }
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

        #region UTILITY HTML PAGES (Feature Flag Controlled)

        // ============================================
        // UTILITY HTML PAGES (Feature Flag Controlled)
        // ============================================

        /// <summary>
        /// GET /api/auth/success - Show success page after authentication
        /// Enabled by: EnableSuccessPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableSuccessPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("success")]
        [AllowAnonymous]
        public IActionResult SuccessPage([FromQuery] string? token = null)
        {
            if (!_featureFlags.Value.EnableSuccessPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/success", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/success" });
            }
            _logger.LogInformation("Refactored Success page requested");

            if (_requestDetector.IsBrowserRequest())
            {
                return Content(_htmlRenderingService.RenderSuccessPage(token ?? ""), "text/html");
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Authentication successful",
                Data = new { token }
            });
        }

        /// <summary>
        /// GET /api/auth/error - Show error page
        /// Enabled by: EnableErrorPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableErrorPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("error")]
        [AllowAnonymous]
        public IActionResult ErrorPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
            if (!_featureFlags.Value.EnableErrorPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/error", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/error" });
            }
            _logger.LogInformation("Refactored Error page requested: {Error}", error);

            var errorMessage = message ?? error ?? "An error occurred";

            if (_requestDetector.IsBrowserRequest())
            {
                return Content(_htmlRenderingService.RenderErrorPage(errorMessage), "text/html");
            }

            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = errorMessage
            });
        }

        /// <summary>
        /// GET /api/auth/claim-account - Show claim account page
        /// Enabled by: EnableClaimAccountPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableClaimAccountPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("claim-account")]
        [AllowAnonymous]
        public IActionResult ClaimAccountPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
            if (!_featureFlags.Value.EnableClaimAccountPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/claim-account", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/claim-account" });
            }
            _logger.LogInformation("Refactored Claim account page requested");

            if (_requestDetector.IsBrowserRequest())
            {
                return Content(_htmlRenderingService.RenderClaimAccountForm(error, message), "text/html");
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Claim account page"
            });
        }

        /// <summary>
        /// GET /api/auth/webauthn/register - Show WebAuthn registration page
        /// Enabled by: EnableWebAuthnRegisterPageRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableWebAuthnRegisterPageRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("webauthn/register")]
        [Authorize]
        public async Task<IActionResult> WebAuthnRegisterPage()
        {
            if (!_featureFlags.Value.EnableWebAuthnRegisterPageRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/webauthn/register", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/webauthn/register" });
            }
            _logger.LogInformation("Refactored WebAuthn register page requested");

            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized();
            }

            if (_requestDetector.IsBrowserRequest())
            {
                // Get registration options to pass to the page
                var result = await _authOrchestrationService.GetWebAuthnRegisterOptionsAsync(username);
                
                if (!result.Success)
                {
                    return Content(_htmlRenderingService.RenderErrorPage(result.ErrorMessage ?? "Failed to generate WebAuthn options"), "text/html");
                }

                var optionsJson = System.Text.Json.JsonSerializer.Serialize(result.Options);
                return Content(_htmlRenderingService.RenderWebAuthnRegistration(optionsJson), "text/html");
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "WebAuthn registration page"
            });
        }

        #endregion

        #region Missing Endpoints - OAuth Callback and Form Submissions

        /// <summary>
        /// GET ~/debug/tokentest - Debug token test endpoint
        /// Enabled by: EnableDebugTokenTestRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableDebugTokenTestRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("~/debug/tokentest")]
        [AllowAnonymous]
        public async Task<IActionResult> TokenTest()
        {
            if (!_featureFlags.Value.EnableDebugTokenTestRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET ~/debug/tokentest", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET ~/debug/tokentest" });
            }
            try
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                _logger.LogInformation("TokenTest - Authorization header present: {Present}, length: {Len}",
                    !string.IsNullOrEmpty(authHeader), authHeader?.Length ?? 0);

                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                    return Ok(new { error = "No Bearer token found", hint = "Send Authorization: Bearer <token>" });

                var token = authHeader.Substring("Bearer ".Length).Trim();
                _logger.LogInformation("TokenTest - Token length: {Length}, starts with: {Start}",
                    token.Length, token.Substring(0, Math.Min(20, token.Length)));

                // Detect JWE (5 parts) vs JWT (3 parts)
                var parts = token.Split('.');
                bool isJwe = parts.Length == 5 ||
                             (parts.Length == 3 && TryDecodeBase64Json(parts[0], out var hdr) && hdr.Contains("\"enc\""));

                _logger.LogInformation("TokenTest - Parts: {Parts}, IsJWE: {IsJwe}", parts.Length, isJwe);

                var tokenHandler = new JwtSecurityTokenHandler();
                var canRead = tokenHandler.CanReadToken(token);

                if (canRead && !isJwe)
                {
                    var jwtToken = tokenHandler.ReadJwtToken(token);
                    var claims = jwtToken.Claims.Select(c => new { c.Type, c.Value }).ToList();
                    return Ok(new { token_type = "JWT", can_read = true, is_encrypted = false, claims, claim_count = claims.Count });
                }

                // JWE or unreadable — validate via OpenIddict
                var authResult = await HttpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                if (authResult.Succeeded && authResult.Principal != null)
                {
                    var claims = authResult.Principal.Claims.Select(c => new { c.Type, c.Value }).ToList();
                    _logger.LogInformation("TokenTest - OpenIddict validation SUCCESS, {Count} claims", claims.Count);
                    return Ok(new { token_type = isJwe ? "JWE" : "JWT", can_read = canRead, is_encrypted = isJwe,
                        openiddict_validation = "SUCCESS", claims, claim_count = claims.Count });
                }

                _logger.LogWarning("TokenTest - OpenIddict validation FAILED: {Msg}", authResult.Failure?.Message);
                return Ok(new { token_type = isJwe ? "JWE" : "JWT", can_read = canRead, is_encrypted = isJwe,
                    openiddict_validation = "FAILED", error = authResult.Failure?.Message, claims = Array.Empty<object>(), claim_count = 0 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TokenTest error");
                return Ok(new { error = ex.Message });
            }
        }

        private static bool TryDecodeBase64Json(string base64, out string json)
        {
            json = string.Empty;
            try
            {
                var padded = base64.Replace('-', '+').Replace('_', '/').PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
                json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                return true;
            }
            catch { return false; }
        }


        /// <summary>
        /// GET ~/connect/tokeninfo - Token validation endpoint used by BaseController for OAuth token validation
        /// Enabled by: EnableOAuthTokenInfoRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthTokenInfoRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("~/connect/tokeninfo")]
        [Produces("application/json")]
        [AllowAnonymous]
        public async Task<IActionResult> TokenInfo()
        {
            if (!_featureFlags.Value.EnableOAuthTokenInfoRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET ~/connect/tokeninfo", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "GET ~/connect/tokeninfo" });
            }
            try
            {
                _logger.LogInformation("TokenInfo endpoint called");
                
                // Manually authenticate the request using OpenIddict validation
                var authenticateResult = await HttpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                
                if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
                {
                    _logger.LogWarning("TokenInfo - Authentication failed");
                    return Unauthorized(new { error = "invalid_token", error_description = "The access token is invalid or expired" });
                }
                
                // Extract all claims from the authenticated principal
                var claimsDict = new Dictionary<string, object>();
                
                foreach (var claim in authenticateResult.Principal.Claims)
                {
                    // Group multiple claims with the same type into arrays
                    if (claimsDict.ContainsKey(claim.Type))
                    {
                        // Convert to list if not already
                        if (claimsDict[claim.Type] is List<string> list)
                        {
                            list.Add(claim.Value);
                        }
                        else
                        {
                            // Convert single value to list
                            var existingValue = claimsDict[claim.Type].ToString();
                            claimsDict[claim.Type] = new List<string> { existingValue!, claim.Value };
                        }
                    }
                    else
                    {
                        claimsDict[claim.Type] = claim.Value;
                    }
                }
                
                _logger.LogInformation("TokenInfo - Returning {ClaimCount} claims from token", claimsDict.Count);
                _logger.LogDebug("TokenInfo - Claims: {Claims}", string.Join(", ", claimsDict.Keys));
                
                return Ok(new
                {
                    claims = claimsDict,
                    token_type = "Bearer",
                    authenticated = authenticateResult.Principal.Identity?.IsAuthenticated ?? false,
                    authentication_type = authenticateResult.Principal.Identity?.AuthenticationType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tokeninfo request");
                return StatusCode(500, "An error occurred while processing the tokeninfo request");
            }
        }

        /// <summary>
        /// POST ~/connect/authorize/callback - OAuth authorization callback form handler
        /// Processes user login during OAuth authorization flow
        /// Enabled by: EnableOAuthAuthorizeCallbackRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthAuthorizeCallbackRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("~/connect/authorize/callback")]
        [AllowAnonymous]
        public async Task<IActionResult> AuthorizeCallback([FromForm] AuthorizeCallbackRequest request)
        {
            if (!_featureFlags.Value.EnableOAuthAuthorizeCallbackRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST ~/connect/authorize/callback", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST ~/connect/authorize/callback" });
            }
            try
            {
                _logger.LogInformation("OIDC Authorize Callback: Processing login for RequestId: {RequestId}, Username: {Username}", 
                    request.RequestId, request.Username);

                // Get the original OpenIddict request parameters from cache
                var requestParams = _cache.Get<Dictionary<string, string>>($"oidc_request_params_{request.RequestId}");
                if (requestParams == null)
                {
                    _logger.LogWarning("Invalid or expired request ID: {RequestId}", request.RequestId);
                    if (_requestDetector.IsBrowserRequest())
                    {
                        return Content(_htmlRenderingService.RenderOAuthLoginForm(request.RequestId, "Unknown", Array.Empty<string>(), 
                            "Invalid or expired request. Please try again."), "text/html");
                    }
                    return BadRequest(new { error = "invalid_request", error_description = "Invalid or expired request ID" });
                }
                
                // Extract key parameters for validation
                var clientId = requestParams.GetValueOrDefault("client_id", "");
                var redirectUri = requestParams.GetValueOrDefault("redirect_uri", "");
                var scope = requestParams.GetValueOrDefault("scope", "");

                // Authenticate user using LoginAsync (not deprecated AuthenticateAsync)
                var loginResult = await _authOrchestrationService.LoginAsync(request.Username, request.Password);
                if (!loginResult.Success || loginResult.User == null)
                {
                    _logger.LogWarning("Authentication failed for user: {Username}", request.Username);
                    
                    // Get client info for re-rendering the form
                    var clientResult = await _openIdConnectService.GetApplicationByClientIdAsync(clientId);
                    var clientName = clientResult.success && clientResult.application != null 
                        ? await _oidcHelperService.GetDisplayNameAsync(clientResult.application) ?? clientId
                        : clientId;
                    var scopes = scope?.Split(' ') ?? Array.Empty<string>();
                    
                    if (_requestDetector.IsBrowserRequest())
                    {
                        return Content(_htmlRenderingService.RenderOAuthLoginForm(request.RequestId, clientName, scopes, 
                            "Invalid username or password. Please try again."), "text/html");
                    }
                    return Unauthorized(new { error = "invalid_credentials", error_description = "Invalid username or password" });
                }

                // Get the actual SpacetimeDB Identity from the database
                var conn = _spacetimeService.GetConnection();
                var userProfile = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == request.Username);
                if (userProfile == null)
                {
                    _logger.LogError("User profile not found for authenticated user: {Username}", request.Username);
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ServerError,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "User profile not found."
                        }));
                }

                var userId = userProfile.UserId;
                _logger.LogInformation("User authenticated successfully: {Username}, UserId: {UserId}", request.Username, userId);

                // Get the application for authorization
                var appResult = await _openIdConnectService.GetApplicationByClientIdAsync(clientId);
                if (!appResult.success || appResult.application == null)
                {
                    _logger.LogError("Failed to get application: {ClientId}, Error: {Error}", 
                        clientId, appResult.errorMessage);
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidClient,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = appResult.errorMessage ?? "Invalid client application."
                        }));
                }

                // Build OAuth claims identity using service
                var identityResult = await _authOrchestrationService.BuildOAuthTokenIdentityAsync(
                    userId, 
                    scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
                    Array.Empty<string>());

                if (identityResult == null)
                {
                    _logger.LogError("Failed to build OAuth identity for user: {Username}", request.Username);
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ServerError,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Failed to build authorization identity."
                        }));
                }

                // Create authentication cookie for the user session
                var cookieIdentity = new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.Name, request.Username),
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                        new Claim(OpenIddictConstants.Claims.Subject, userId.ToString())
                    },
                    CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(cookieIdentity),
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                    });

                _logger.LogInformation("Cookie authentication successful for user: {Username}", request.Username);

                // Reconstruct the authorization URL with ALL original parameters
                var authUrl = "/connect/authorize?";
                var queryParams = new List<string>();
                
                foreach (var param in requestParams)
                {
                    queryParams.Add($"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}");
                }
                
                authUrl += string.Join("&", queryParams);
                
                _logger.LogInformation("Redirecting to authorization endpoint with {Count} parameters (including PKCE)", requestParams.Count);
                
                // Remove the cached request as it's been processed
                _cache.Remove($"oidc_request_params_{request.RequestId}");
                
                return Redirect(authUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing authorization callback for RequestId: {RequestId}", request.RequestId);
                if (_requestDetector.IsBrowserRequest())
                {
                    return Content(_htmlRenderingService.RenderOAuthLoginForm(request.RequestId ?? "", "Unknown", Array.Empty<string>(), 
                        "An error occurred while processing your request. Please try again."), "text/html");
                }
                return StatusCode(500, new { error = "server_error", 
                    error_description = "An error occurred while processing the authorization callback" });
            }
        }

        /// <summary>
        /// POST /connect/register-client - Form-based client registration
        /// Enabled by: EnableOAuthClientRegisterRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientRegisterRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("connect/register-client")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterClientSubmit([FromForm] RegisterClientFormRequest request)
        {
            if (!_featureFlags.Value.EnableOAuthClientRegisterRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/connect/register-client", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/connect/register-client" });
            }
            // Validate JWT token from form
            if (string.IsNullOrEmpty(request.Token))
            {
                _logger.LogWarning("No token provided to RegisterClientSubmit");
                return Redirect("/api/auth/login");
            }

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(request.Token);
                
                // Validate token is not expired
                if (jwtToken.ValidTo < DateTime.UtcNow)
                {
                    _logger.LogWarning("Expired token provided to RegisterClientSubmit");
                    return Redirect("/api/auth/login");
                }

                // Check if user is an administrator
                var roleClaims = jwtToken.Claims.Where(c => c.Type == "role" || c.Type == ClaimTypes.Role);
                bool isAdmin = roleClaims.Any(c => c.Value == "Administrator" || c.Value == "1");
                
                if (!isAdmin)
                {
                    _logger.LogWarning("User is not an administrator");
                    return Redirect("/api/auth/error?message=" + Uri.EscapeDataString("Administrator access required"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invalid token provided to RegisterClientSubmit");
                return Redirect("/api/auth/login");
            }
            
            try
            {
                if (!ModelState.IsValid)
                {
                    if (_requestDetector.IsBrowserRequest())
                    {
                        var clientData = new BRU_AVTOPARK.Models.Responses.GetClientResponse
                        {
                            ClientId = request.ClientId,
                            DisplayName = request.DisplayName,
                            RedirectUris = _oidcHelperService.SplitTextareaInput(request.RedirectUris),
                            PostLogoutRedirectUris = _oidcHelperService.SplitTextareaInput(request.PostLogoutRedirectUris),
                            AllowedScopes = _oidcHelperService.SplitTextareaInput(request.AllowedScopes),
                            RequireConsent = request.RequireConsent
                        };
                        return Content(_htmlRenderingService.RenderOidcClientForm(null, clientData, request.Token), "text/html");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid form data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                // Register client application
                var result = await _authOrchestrationService.RegisterOAuthClientAsync(
                    request.ClientId,
                    request.ClientSecret,
                    request.DisplayName,
                    _oidcHelperService.SplitTextareaInput(request.RedirectUris),
                    _oidcHelperService.SplitTextareaInput(request.PostLogoutRedirectUris),
                    _oidcHelperService.SplitTextareaInput(request.AllowedScopes),
                    request.RequireConsent
                );

                if (!result.Success)
                {
                    if (_requestDetector.IsBrowserRequest())
                    {
                        return Redirect($"/api/auth/error?message={Uri.EscapeDataString(result.ErrorMessage ?? "Failed to register client application")}");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = result.ErrorMessage ?? "Failed to register client application"
                    });
                }

                if (_requestDetector.IsBrowserRequest())
                {
                    var tokenParam = !string.IsNullOrEmpty(request.Token) ? $"?token={Uri.EscapeDataString(request.Token)}" : "";
                    return Redirect($"/api/auth/connect/clients{tokenParam}");
                }

                return Ok(new ApiResponse<OAuthClientDto>
                {
                    Success = true,
                    Message = "Client application registered successfully",
                    Data = new OAuthClientDto
                    {
                        ClientId = result.ClientId,
                        DisplayName = result.DisplayName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering client application");
                if (_requestDetector.IsBrowserRequest())
                {
                    return Redirect($"/api/auth/error?message={Uri.EscapeDataString("An error occurred while registering client application")}");
                }

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while registering client application"
                });
            }
        }

        /// <summary>
        /// POST /connect/update-client/{clientId} - Form-based client update
        /// Enabled by: EnableOAuthClientUpdateRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientUpdateRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("connect/update-client/{clientId}")]
        [AllowAnonymous]
        public async Task<IActionResult> UpdateClientSubmit(string clientId, [FromForm] UpdateClientFormRequest request)
        {
            if (!_featureFlags.Value.EnableOAuthClientUpdateRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/connect/update-client/{clientId}", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/connect/update-client/{clientId}" });
            }
            // Validate JWT token from form
            if (string.IsNullOrEmpty(request.Token))
            {
                _logger.LogWarning("No token provided to UpdateClientSubmit");
                return Redirect("/api/auth/login");
            }

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(request.Token);
                
                // Validate token is not expired
                if (jwtToken.ValidTo < DateTime.UtcNow)
                {
                    _logger.LogWarning("Expired token provided to UpdateClientSubmit");
                    return Redirect("/api/auth/login");
                }

                // Check if user is an administrator
                var roleClaims = jwtToken.Claims.Where(c => c.Type == "role" || c.Type == ClaimTypes.Role);
                bool isAdmin = roleClaims.Any(c => c.Value == "Administrator" || c.Value == "1");
                
                if (!isAdmin)
                {
                    _logger.LogWarning("User is not an administrator");
                    return Redirect("/api/auth/error?message=" + Uri.EscapeDataString("Administrator access required"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invalid token provided to UpdateClientSubmit");
                return Redirect("/api/auth/login");
            }
            
            try
            {
                if (!ModelState.IsValid)
                {
                    if (_requestDetector.IsBrowserRequest())
                    {
                        var clientData = new BRU_AVTOPARK.Models.Responses.GetClientResponse
                        {
                            ClientId = clientId,
                            DisplayName = request.DisplayName,
                            RedirectUris = _oidcHelperService.SplitTextareaInput(request.RedirectUris),
                            PostLogoutRedirectUris = _oidcHelperService.SplitTextareaInput(request.PostLogoutRedirectUris),
                            AllowedScopes = _oidcHelperService.SplitTextareaInput(request.AllowedScopes),
                            RequireConsent = request.RequireConsent
                        };
                        return Content(_htmlRenderingService.RenderOidcClientForm(clientId, clientData, request.Token), "text/html");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid form data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                // Update client application
                var result = await _authOrchestrationService.UpdateOAuthClientAsync(
                    clientId,
                    request.ClientSecret,
                    request.DisplayName,
                    _oidcHelperService.SplitTextareaInput(request.RedirectUris),
                    _oidcHelperService.SplitTextareaInput(request.PostLogoutRedirectUris),
                    _oidcHelperService.SplitTextareaInput(request.AllowedScopes),
                    request.RequireConsent
                );

                if (!result.Success)
                {
                    if (_requestDetector.IsBrowserRequest())
                    {
                        return Redirect($"/api/auth/error?message={Uri.EscapeDataString(result.ErrorMessage ?? "Failed to update client application")}");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = result.ErrorMessage ?? "Failed to update client application"
                    });
                }

                if (_requestDetector.IsBrowserRequest())
                {
                    var tokenParam = !string.IsNullOrEmpty(request.Token) ? $"?token={Uri.EscapeDataString(request.Token)}" : "";
                    return Redirect($"/api/auth/connect/clients{tokenParam}");
                }

                return Ok(new ApiResponse<OAuthClientDto>
                {
                    Success = true,
                    Message = "Client application updated successfully",
                    Data = new OAuthClientDto
                    {
                        ClientId = result.ClientId,
                        DisplayName = result.DisplayName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating client application");
                if (_requestDetector.IsBrowserRequest())
                {
                    return Redirect($"/api/auth/error?message={Uri.EscapeDataString("An error occurred while updating client application")}");
                }

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while updating client application"
                });
            }
        }

        /// <summary>
        /// POST /connect/clients/{clientId}/delete - Form-based client deletion
        /// Enabled by: EnableOAuthClientDeleteRefactoring feature flag
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthClientDeleteRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("connect/clients/{clientId}/delete")]
        [AllowAnonymous]
        public async Task<IActionResult> DeleteClientSubmit(string clientId, [FromForm] string? token)
        {
            if (!_featureFlags.Value.EnableOAuthClientDeleteRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/connect/clients/{clientId}/delete", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/connect/clients/{clientId}/delete" });
            }
            // Validate JWT token from form
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("No token provided to DeleteClientSubmit");
                return Redirect("/api/auth/login");
            }

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(token);
                
                // Validate token is not expired
                if (jwtToken.ValidTo < DateTime.UtcNow)
                {
                    _logger.LogWarning("Expired token provided to DeleteClientSubmit");
                    return Redirect("/api/auth/login");
                }

                // Check if user is an administrator
                var roleClaims = jwtToken.Claims.Where(c => c.Type == "role" || c.Type == ClaimTypes.Role);
                bool isAdmin = roleClaims.Any(c => c.Value == "Administrator" || c.Value == "1");
                
                if (!isAdmin)
                {
                    _logger.LogWarning("User is not an administrator");
                    return Redirect("/api/auth/error?message=" + Uri.EscapeDataString("Administrator access required"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invalid token provided to DeleteClientSubmit");
                return Redirect("/api/auth/login");
            }
            
            try
            {
                var result = await _authOrchestrationService.DeleteOAuthClientAsync(clientId);

                if (!result.Success)
                {
                    if (_requestDetector.IsBrowserRequest())
                    {
                        return Redirect($"/api/auth/error?message={Uri.EscapeDataString(result.ErrorMessage ?? "Failed to delete client application")}");
                    }

                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = result.ErrorMessage ?? "Failed to delete client application"
                    });
                }

                if (_requestDetector.IsBrowserRequest())
                {
                    var tokenParam = !string.IsNullOrEmpty(token) ? $"?token={Uri.EscapeDataString(token)}" : "";
                    return Redirect($"/api/auth/connect/clients{tokenParam}");
                }

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "Client application deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting client application");
                if (_requestDetector.IsBrowserRequest())
                {
                    return Redirect($"/api/auth/error?message={Uri.EscapeDataString("An error occurred while deleting client application")}");
                }

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while deleting client application"
                });
            }
        }

        #endregion

        #region WebSocket Authentication (1 endpoint)

        /// <summary>
        /// GET /api/auth/ws - Real-time authentication over WebSocket.
        /// Enabled by: EnableWebSocketAuthRefactoring feature flag.
        ///
        /// Supported client→server message types:
        ///   auth:validate   – validate a bearer token and receive claims back
        ///   auth:refresh    – exchange a refresh token for a new access token
        ///   auth:qr-status  – subscribe to QR-login completion events for a deviceId
        ///   auth:ping       – keep-alive / latency check
        ///
        /// Server→client push message types:
        ///   auth:connected      – connection acknowledged / client connected
        ///   auth:validated      – result of auth:validate
        ///   auth:refreshed      – result of auth:refresh
        ///   auth:qr-completed   – QR login succeeded (pushed when status changes)
        ///   auth:qr-failed      – QR login failed / expired
        ///   auth:qr-subscribed  – QR polling subscription acknowledged
        ///   auth:event          – auth domain event (login, logout, token-refresh)
        ///   auth:pong           – response to auth:ping
        ///   auth:error          – error response (general / message-level errors)
        ///   auth:qr-error       – error specific to QR login polling (null status, poll exception)
        ///
        /// Authentication: token may be supplied as Authorization: Bearer header
        /// OR as ?access_token= query parameter (for WebSocket upgrade compatibility).
        /// An unauthenticated connection is accepted but only auth:validate and auth:ping are allowed
        /// until a valid token is confirmed.
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableWebSocketAuthRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpGet("ws")]
        [AllowAnonymous]
        public async Task AuthWebSocket()
        {
            if (!_featureFlags.Value.EnableWebSocketAuthRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "GET /api/auth/ws", userIdentity, DateTime.UtcNow);
                HttpContext.Response.StatusCode = 503;
                await HttpContext.Response.WriteAsJsonAsync(new { error = "This endpoint is temporarily unavailable", endpoint = "GET /api/auth/ws" });
                return;
            }

            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await HttpContext.Response.WriteAsync("WebSocket connection required");
                return;
            }

            var preValidatedClaims = await ValidateOAuthTokenAsync();
            var connectionId = Guid.NewGuid().ToString("N")[..12];
            var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            await _authWebSocketService.RunSessionAsync(
                webSocket,
                connectionId,
                preValidatedClaims,
                sourceIp,
                HttpContext.RequestAborted);
        }

        #endregion

        #region OAuth Consent JSON Endpoint (1 endpoint)

        /// <summary>
        /// POST /api/auth/oauth/consent - JSON endpoint for headless OAuth consent
        /// Enabled by: EnableOAuthConsentRefactoring feature flag
        ///
        /// Allows non-browser (headless) clients to programmatically complete the OAuth consent step.
        /// The browser HTML form POST flow via ~/connect/authorize remains unchanged.
        /// </summary>
        /// <remarks>
        /// This endpoint's availability is controlled by the <c>EnableOAuthConsentRefactoring</c> feature flag in <c>FeatureFlagOptions</c>.
        /// When the flag is false, the endpoint returns 503 Service Unavailable.
        /// Use the admin UI at /admin/feature-flags or the API at /api/admin/feature-flags to toggle availability at runtime without a deployment.
        /// </remarks>
        [HttpPost("oauth/consent")]
        [Authorize]
        [Produces("application/json")]
        public async Task<IActionResult> OAuthConsent([FromBody] OAuthConsentRequest request)
        {
            if (!_featureFlags.Value.EnableOAuthConsentRefactoring)
            {
                var userIdentity = User.FindFirst("unique_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
                _logger.LogWarning("Endpoint {EndpointName} is disabled via feature flag. Request from {UserIdentity} at {Timestamp}",
                    "POST /api/auth/oauth/consent", userIdentity, DateTime.UtcNow);
                return StatusCode(503, new { error = "This endpoint is temporarily unavailable", endpoint = "POST /api/auth/oauth/consent" });
            }

            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.RequestId))
            {
                return BadRequest(new { error = "invalid_request", error_description = "requestId is required" });
            }

            // Look up the cached OpenIddict request parameters stored during ~/connect/authorize
            var requestParams = _cache.Get<Dictionary<string, string>>($"oidc_request_params_{request.RequestId}");
            if (requestParams == null)
            {
                _logger.LogWarning("OAuthConsent: Invalid or expired requestId: {RequestId}", request.RequestId);
                return BadRequest(new { error = "invalid_request", error_description = "Invalid or expired requestId" });
            }

            var redirectUri = requestParams.GetValueOrDefault("redirect_uri", "");
            var state = requestParams.GetValueOrDefault("state", "");

            if (!request.Grant)
            {
                // User denied consent — return the error redirect URI with access_denied
                _logger.LogInformation("OAuthConsent: User denied consent for requestId: {RequestId}", request.RequestId);
                _cache.Remove($"oidc_request_params_{request.RequestId}");

                var errorUri = redirectUri;
                if (!string.IsNullOrEmpty(errorUri))
                {
                    var separator = errorUri.Contains('?') ? "&" : "?";
                    errorUri += $"{separator}error=access_denied&error_description={Uri.EscapeDataString("The resource owner denied the request.")}";
                    if (!string.IsNullOrEmpty(state))
                        errorUri += $"&state={Uri.EscapeDataString(state)}";
                }

                return Ok(new { redirectUri = errorUri });
            }

            // Grant == true: build claims identity and reconstruct the authorize URL for the client to follow
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("OAuthConsent: Authenticated user has no username");
                return Unauthorized(new { error = "invalid_token", error_description = "User identity not found" });
            }

            var scope = requestParams.GetValueOrDefault("scope", "");
            var scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var claimsResult = await _authOrchestrationService.BuildOAuthClaimsIdentityAsync(username, scopes);
            if (!claimsResult.Success || claimsResult.Identity == null)
            {
                _logger.LogWarning("OAuthConsent: Failed to build claims identity for user: {Username}, Error: {Error}", username, claimsResult.ErrorMessage);
                return StatusCode(500, new { error = "server_error", error_description = claimsResult.ErrorMessage ?? "Failed to build user identity" });
            }

            // Reconstruct the /connect/authorize URL with all original parameters.
            // The client follows this URL (with their session cookie) to complete the OpenIddict flow.
            var queryParams = new List<string>();
            foreach (var param in requestParams)
            {
                queryParams.Add($"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}");
            }
            var authorizeUrl = "/connect/authorize?" + string.Join("&", queryParams);

            _logger.LogInformation("OAuthConsent: Consent granted for user: {Username}, requestId: {RequestId}", username, request.RequestId);
            _cache.Remove($"oidc_request_params_{request.RequestId}");

            return Ok(new { redirectUri = authorizeUrl });
        }

        #endregion
    }
}
