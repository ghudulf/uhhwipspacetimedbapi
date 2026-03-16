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
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Collections.Concurrent;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    /// <summary>
    /// Refactored AuthController that uses the orchestration service pattern.
    /// This controller is used when feature flags are enabled.
    /// Routes are conditionally registered based on feature flags via [RefactoredAction] attribute.
    /// CRITICAL: Route must match legacy AuthController ("api/Auth") for feature flag routing to work.
    /// </summary>
    [ApiController]
    [Route("api/Auth")]
    public class AuthControllerRefactored : BaseController
    {
        private static readonly JsonSerializerOptions WsJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly IAuthOrchestrationService _authOrchestrationService;
        private readonly IHtmlRenderingService _htmlRenderingService;
        private readonly IRequestDetector _requestDetector;
        private readonly IOptions<FeatureFlagOptions> _featureFlags;
        private readonly ILogger<AuthControllerRefactored> _logger;
        private readonly IMemoryCache _cache;
        private readonly IOidcHelperService _oidcHelperService;
        private readonly IOpenIdConnectService _openIdConnectService;
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IRealtimeEventBus _realtimeEventBus;

        public AuthControllerRefactored(
            IAuthOrchestrationService authOrchestrationService,
            IHtmlRenderingService htmlRenderingService,
            IRequestDetector requestDetector,
            IOptions<FeatureFlagOptions> featureFlags,
            ILogger<AuthControllerRefactored> logger,
            IMemoryCache cache,
            IOidcHelperService oidcHelperService,
            IOpenIdConnectService openIdConnectService,
            ISpacetimeDBService spacetimeService,
            IRealtimeEventBus realtimeEventBus)
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
        }

        #region Traditional Authentication (2 endpoints)

        /// <summary>
        /// GET /api/auth/login - Show login page (HTML) or return login info (JSON)
        /// Enabled by: EnableLoginRefactoring feature flag
        /// </summary>
        [HttpGet("login")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableLoginRefactoring))]
        public IActionResult LoginPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
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
        [HttpGet("register")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableRegisterRefactoring))]
        public IActionResult RegisterPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
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
        [HttpPost("totp/verify")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableTotpVerifyRefactoring))]
        public async Task<IActionResult> TotpVerify([FromBody] VerifyTotpRequest request)
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
        [HttpPost("totp/validate")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableTotpValidateRefactoring))]
        public async Task<IActionResult> TotpValidate([FromBody] ValidateTotpRequest request)
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
        [HttpPost("webauthn/register/complete")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableWebAuthnRegisterCompleteRefactoring))]
        public async Task<IActionResult> WebAuthnRegisterComplete([FromBody] WebAuthnRegisterCompleteRequest request)
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

            var identity = new SpacetimeDB.Identity(Convert.FromHexString(userId));
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
        [HttpGet("magic-link")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableMagicLinkPageRefactoring))]
        public Task<IActionResult> MagicLinkPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
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
        [HttpGet("qr-login")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableQRLoginPageRefactoring))]
        public Task<IActionResult> QRLoginPage()
        {
            _logger.LogInformation("Refactored QR Login Page endpoint called");

            // Generate a placeholder QR code for the page
            var html = _htmlRenderingService.RenderQrLogin("placeholder-qr-code");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
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
        [HttpGet("~/connect/authorize")]
        [HttpPost("~/connect/authorize")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthAuthorizeRefactoring))]
        public async Task<IActionResult> Authorize()
        {
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

                if (_requestDetector.IsBrowserRequest())
                {
                    // Get client display name
                    var clientResult = await _openIdConnectService.GetApplicationByClientIdAsync(request.ClientId ?? "");
                    var clientName = request.ClientId ?? "Unknown";
                    if (clientResult.success && clientResult.application != null)
                    {
                        clientName = await _oidcHelperService.GetDisplayNameAsync(clientResult.application) ?? clientName;
                    }
                    
                    var scopes = request.Scope?.Split(' ') ?? Array.Empty<string>();
                    return Content(_htmlRenderingService.RenderOAuthLoginForm(requestId, clientName, scopes), "text/html");
                }

                return Ok(new { login_url = $"/api/auth/oauth/login?request_id={requestId}" });
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
        /// </summary>
        [HttpPost("~/connect/token")]
        [AllowAnonymous]
        [Produces("application/json")]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthTokenRefactoring))]
        public async Task<IActionResult> Exchange()
        {
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

            return Ok(result.Claims);
        }

        #endregion

        #region OAuth Client Management API Endpoints (7 endpoints)

        /// <summary>
        /// POST /api/oauth/clients - Register new OAuth client
        /// Enabled by: EnableOAuthClientRegisterRefactoring feature flag
        /// </summary>
        [HttpPost("connect/clients")]
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
                Data = new OAuthClientDto { ClientId = result.ClientId!, DisplayName = result.DisplayName! }
            });
        }

        /// <summary>
        /// GET /connect/clients - List all OAuth clients (API) or OAuth clients list page (Browser)
        /// Enabled by: EnableOAuthClientListRefactoring OR EnableOAuthClientsPageRefactoring feature flags
        /// Returns JSON for API requests, HTML for browser requests
        /// </summary>
        [HttpGet("connect/clients")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientListRefactoring))]
        public async Task<IActionResult> OAuthClientList([FromQuery] string? token = null)
        {
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
        /// GET /connect/clients/{id} - Get OAuth client details (API) or OAuth client details page (Browser)
        /// Enabled by: EnableOAuthClientDetailsRefactoring OR EnableOAuthClientDetailsPageRefactoring feature flags
        /// Returns JSON for API requests, HTML for browser requests
        /// </summary>
        [HttpGet("connect/clients/{id}")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientDetailsRefactoring))]
        public async Task<IActionResult> OAuthClientDetails(string id, [FromQuery] string? token = null)
        {
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
        /// PUT /api/oauth/clients/{id} - Update OAuth client
        /// Enabled by: EnableOAuthClientUpdateRefactoring feature flag
        /// </summary>
        [HttpPut("connect/clients/{id}")]
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
                Data = new OAuthClientDto { ClientId = result.ClientId!, DisplayName = result.DisplayName! }
            });
        }

        /// <summary>
        /// DELETE /api/oauth/clients/{id} - Delete OAuth client
        /// Enabled by: EnableOAuthClientDeleteRefactoring feature flag
        /// </summary>
        [HttpDelete("connect/clients/{id}")]
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
        /// GET /api/connect/scopes - List available OAuth scopes
        /// Enabled by: EnableOAuthScopesRefactoring feature flag
        /// </summary>
        [HttpGet("connect/scopes")]
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
                Data = result.Scopes.Select(s => new OAuthScopeDto { Name = s }).ToList()
            });
        }

        /// <summary>
        /// POST /api/oauth/clients/{id}/regenerate-secret - Regenerate client secret
        /// Enabled by: EnableOAuthClientRegenerateSecretRefactoring feature flag
        /// </summary>
        [HttpPost("connect/clients/{id}/regenerate-secret")]
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
        [HttpGet("connect/clients/new")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientNewPageRefactoring))]
        public Task<IActionResult> OAuthClientNewPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Client New Page endpoint called");

            var html = _htmlRenderingService.RenderOidcClientForm(null, null, token);
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/clients/{id}/edit - Edit OAuth client form page
        /// Enabled by: EnableOAuthClientEditPageRefactoring feature flag
        /// </summary>
        [HttpGet("connect/clients/{id}/edit")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientEditPageRefactoring))]
        public async Task<IActionResult> OAuthClientEditPage(string id, [FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Client Edit Page endpoint called for client: {ClientId}", id);

            var result = await _authOrchestrationService.GetOAuthClientAsync(id);

            if (!result.Success || result.Client == null)
            {
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

            var html = _htmlRenderingService.RenderOidcClientForm(id, clientResponse, token);
            return Content(html, "text/html");
        }

        /// <summary>
        /// GET /connect/scopes - OAuth scopes list page
        /// Enabled by: EnableOAuthScopesPageRefactoring feature flag
        /// </summary>
        [HttpGet("connect/scopes")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthScopesPageRefactoring))]
        public async Task<IActionResult> OAuthScopesPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Scopes Page endpoint called");

            var result = await _authOrchestrationService.GetOAuthScopesAsync();

            if (!result.Success)
            {
                return BadRequest(result.ErrorMessage ?? "Failed to retrieve OAuth scopes");
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
        [HttpGet("oauth/authorizations")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthAuthorizationsPageRefactoring))]
        public Task<IActionResult> OAuthAuthorizationsPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Authorizations Page endpoint called");

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth authorizations page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/tokens - OAuth tokens list page
        /// Enabled by: EnableOAuthTokensPageRefactoring feature flag
        /// </summary>
        [HttpGet("connect/tokens")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthTokensPageRefactoring))]
        public Task<IActionResult> OAuthTokensPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Tokens Page endpoint called");

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth tokens page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/dashboard - OAuth admin dashboard page
        /// Enabled by: EnableOAuthDashboardPageRefactoring feature flag
        /// </summary>
        [HttpGet("oauth/dashboard")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthDashboardPageRefactoring))]
        public Task<IActionResult> OAuthDashboardPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Dashboard Page endpoint called");

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth dashboard page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/settings - OAuth settings page
        /// Enabled by: EnableOAuthSettingsPageRefactoring feature flag
        /// </summary>
        [HttpGet("connect/settings")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthSettingsPageRefactoring))]
        public Task<IActionResult> OAuthSettingsPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Settings Page endpoint called");

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth settings page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/logs - OAuth audit logs page
        /// Enabled by: EnableOAuthLogsPageRefactoring feature flag
        /// </summary>
        [HttpGet("connect/logs")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthLogsPageRefactoring))]
        public Task<IActionResult> OAuthLogsPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Logs Page endpoint called");

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth logs page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/help - OAuth help/documentation page
        /// Enabled by: EnableOAuthHelpPageRefactoring feature flag
        /// </summary>
        [HttpGet("connect/help")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthHelpPageRefactoring))]
        public Task<IActionResult> OAuthHelpPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Help Page endpoint called");

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth help page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/test - OAuth test/playground page
        /// Enabled by: EnableOAuthTestPageRefactoring feature flag
        /// </summary>
        [HttpGet("connect/test")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthTestPageRefactoring))]
        public Task<IActionResult> OAuthTestPage([FromQuery] string? token = null)
        {
            _logger.LogInformation("Refactored OAuth Test Page endpoint called");

            // Placeholder: This page is not yet implemented in IHtmlRenderingService
            var html = _htmlRenderingService.RenderErrorPage("OAuth test page not yet implemented");
            return Task.FromResult<IActionResult>(Content(html, "text/html"));
        }

        /// <summary>
        /// GET /oauth/callback - OAuth callback page
        /// Enabled by: EnableOAuthCallbackPageRefactoring feature flag
        /// </summary>
        [HttpGet("connect/callback")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthCallbackPageRefactoring))]
        public Task<IActionResult> OAuthCallbackPage([FromQuery] string? code = null, [FromQuery] string? error = null)
        {
            _logger.LogInformation("Refactored OAuth Callback Page endpoint called");

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
        [HttpGet("profile")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableProfileRefactoring))]
        public async Task<IActionResult> Profile([FromQuery] string? token = null)
        {
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
        /// Enabled by: EnableLoginRefactoring feature flag (same as login)
        /// </summary>
        [HttpGet("logout")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableLoginRefactoring))]
        public IActionResult LogoutPage()
        {
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
                    ExpiresAt = result.ExpiresAt ?? DateTime.UtcNow.AddHours(1)
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

        #region UTILITY HTML PAGES (Feature Flag Controlled)

        // ============================================
        // UTILITY HTML PAGES (Feature Flag Controlled)
        // ============================================

        /// <summary>
        /// GET /api/auth/success - Show success page after authentication
        /// Enabled by: EnableSuccessPageRefactoring feature flag
        /// </summary>
        [HttpGet("success")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableSuccessPageRefactoring))]
        public IActionResult SuccessPage([FromQuery] string? token = null)
        {
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
        [HttpGet("error")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableErrorPageRefactoring))]
        public IActionResult ErrorPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
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
        [HttpGet("claim-account")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableClaimAccountPageRefactoring))]
        public IActionResult ClaimAccountPage([FromQuery] string? error = null, [FromQuery] string? message = null)
        {
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
        [HttpGet("webauthn/register")]
        [Authorize]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableWebAuthnRegisterPageRefactoring))]
        public async Task<IActionResult> WebAuthnRegisterPage()
        {
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
        /// GET ~/connect/tokeninfo - Token validation endpoint used by BaseController for OAuth token validation
        /// </summary>
        [HttpGet("~/connect/tokeninfo")]
        [Produces("application/json")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthTokenInfoRefactoring))]
        public async Task<IActionResult> TokenInfo()
        {
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
        /// </summary>
        [HttpPost("~/connect/authorize/callback")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthAuthorizeCallbackRefactoring))]
        public async Task<IActionResult> AuthorizeCallback([FromForm] AuthorizeCallbackRequest request)
        {
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
        /// </summary>
        [HttpPost("connect/register-client")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientRegisterRefactoring))]
        public async Task<IActionResult> RegisterClientSubmit([FromForm] RegisterClientFormRequest request)
        {
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
        /// </summary>
        [HttpPost("connect/update-client/{clientId}")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientUpdateRefactoring))]
        public async Task<IActionResult> UpdateClientSubmit(string clientId, [FromForm] UpdateClientFormRequest request)
        {
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
        /// </summary>
        [HttpPost("connect/clients/{clientId}/delete")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableOAuthClientDeleteRefactoring))]
        public async Task<IActionResult> DeleteClientSubmit(string clientId, [FromForm] string? token)
        {
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
        [HttpGet("ws")]
        [AllowAnonymous]
        [RefactoredAction(nameof(FeatureFlagOptions.EnableWebSocketAuthRefactoring))]
        public async Task AuthWebSocket()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await HttpContext.Response.WriteAsync("WebSocket connection required");
                return;
            }

            // Accept the connection first – auth is validated per-message for auth:validate,
            // or pre-validated here for all other message types.
            // This mirrors the pattern used in RealtimeController.StreamEvents but is more
            // permissive: unauthenticated clients can still call auth:validate.
            var preValidatedClaims = await ValidateOAuthTokenAsync();
            var connectionId = Guid.NewGuid().ToString("N")[..12];

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            _logger.LogInformation("[AuthWS:{ConnId}] Connection established. Pre-authenticated: {Auth}",
                connectionId, preValidatedClaims != null);

            // Send welcome frame
            var sendLock = new SemaphoreSlim(1, 1);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);

            await WsSendAsync(webSocket, new
            {
                type = "auth:connected",
                connectionId,
                authenticated = preValidatedClaims != null,
                serverTimeUtc = DateTimeOffset.UtcNow
            }, sendLock, cts.Token);

            // Track QR-status subscriptions: deviceId → polling task
            var qrSubscriptions = new ConcurrentDictionary<string, CancellationTokenSource>();
            // Track the actual poller Task objects so we can await them before disposing sendLock.
            var qrPollerTasks = new ConcurrentDictionary<string, Task>();

            // Mutable holder so async handlers can upgrade the connection's auth state
            var claimsHolder = new ClaimsHolder { Claims = preValidatedClaims };

            var buffer = new byte[4096];
            const int MaxMessageBytes = 64 * 1024; // 64 KB per message

            try
            {
                while (webSocket.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await webSocket.ReceiveAsync(buffer, cts.Token);
                        if (ms.Length + result.Count > MaxMessageBytes)
                        {
                            _logger.LogWarning("[AuthWS:{ConnId}] Message exceeds max size ({Max} bytes), closing", connectionId, MaxMessageBytes);
                            await webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None);
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                        continue;

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    await HandleAuthWsMessageAsync(webSocket, json, sendLock, connectionId,
                        claimsHolder, qrSubscriptions, qrPollerTasks, cts);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("[AuthWS:{ConnId}] Cancelled", connectionId);
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                _logger.LogWarning("[AuthWS:{ConnId}] Connection closed prematurely", connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthWS:{ConnId}] Unexpected error", connectionId);
            }
            finally
            {
                cts.Cancel();
                // Cancel all QR polling tasks – only cancel here; each poller's finally block
                // is responsible for disposing its own subCts to avoid double-dispose races.
                foreach (var sub in qrSubscriptions.Values)
                {
                    sub.Cancel();
                }
                // Await all pollers so they finish using sendLock before we dispose it.
                // Capture a snapshot of all current tasks to await every tracked poller.
                if (qrPollerTasks.Count > 0)
                {
                    var allPollerTasks = qrPollerTasks.Values.ToArray();
                    try
                    {
                        // Give pollers up to 5 s to finish; abandon any that are still running after that.
                        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await Task.WhenAny(Task.WhenAll(allPollerTasks), Task.Delay(Timeout.Infinite, shutdownCts.Token));
                        if (allPollerTasks.Any(t => !t.IsCompleted))
                            _logger.LogWarning("[AuthWS:{ConnId}] {Count} QR poller task(s) did not finish within shutdown timeout; abandoning.",
                                connectionId, allPollerTasks.Count(t => !t.IsCompleted));
                    }
                    catch { /* individual poller exceptions are already logged */ }
                }
                sendLock.Dispose();
                _logger.LogInformation("[AuthWS:{ConnId}] Connection closed", connectionId);
            }
        }

        /// <summary>
        /// Dispatches an incoming WebSocket message to the appropriate auth handler.
        /// </summary>
        private async Task HandleAuthWsMessageAsync(
            WebSocket webSocket,
            string json,
            SemaphoreSlim sendLock,
            string connectionId,
            ClaimsHolder claimsHolder,
            ConcurrentDictionary<string, CancellationTokenSource> qrSubscriptions,
            ConcurrentDictionary<string, Task> qrPollerTasks,
            CancellationTokenSource cts)
        {
            string? requestId = null;
            string? messageType = null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                requestId = root.TryGetProperty("requestId", out var rid) ? rid.GetString() : null;
                messageType = root.TryGetProperty("type", out var t) ? t.GetString() : null;

                // Sanitize client-controlled values before logging to prevent log forging
                var safeRequestId = SanitizeLogValue(requestId);
                var safeMessageType = SanitizeLogValue(messageType);

                _logger.LogDebug("[AuthWS:{ConnId}] Received: {Type} (requestId={ReqId})",
                    connectionId, safeMessageType, safeRequestId);

                switch (messageType?.ToLowerInvariant())
                {
                    case "auth:validate":
                        await HandleWsValidateAsync(webSocket, root, requestId, sendLock,
                            connectionId, claimsHolder, cts.Token);
                        break;

                    case "auth:refresh":
                        if (claimsHolder.Claims == null)
                        {
                            await WsSendAsync(webSocket, new
                            {
                                type = "auth:error",
                                requestId,
                                error = "Authentication required for auth:refresh"
                            }, sendLock, cts.Token);
                            break;
                        }
                        await HandleWsRefreshAsync(webSocket, root, requestId, sendLock,
                            connectionId, claimsHolder.Claims, cts.Token);
                        break;

                    case "auth:qr-status":
                        if (claimsHolder.Claims == null)
                        {
                            await WsSendAsync(webSocket, new
                            {
                                type = "auth:error",
                                requestId,
                                error = "Authentication required for auth:qr-status"
                            }, sendLock, cts.Token);
                            break;
                        }
                        await HandleWsQrStatusAsync(webSocket, root, requestId, sendLock,
                            connectionId, claimsHolder.Claims, qrSubscriptions, qrPollerTasks, cts);
                        break;

                    case "auth:ping":
                        await WsSendAsync(webSocket, new
                        {
                            type = "auth:pong",
                            requestId,
                            serverTimeUtc = DateTimeOffset.UtcNow
                        }, sendLock, cts.Token);
                        break;

                    default:
                        await WsSendAsync(webSocket, new
                        {
                            type = "auth:error",
                            requestId,
                            error = $"Unknown message type: {messageType}"
                        }, sendLock, cts.Token);
                        break;
                }
            }
            catch (JsonException)
            {
                await WsSendAsync(webSocket, new
                {
                    type = "auth:error",
                    requestId,
                    error = "Invalid JSON"
                }, sendLock, cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthWS:{ConnId}] Error handling message type {Type}", connectionId, messageType);
                await WsSendAsync(webSocket, new
                {
                    type = "auth:error",
                    requestId,
                    error = "Internal server error"
                }, sendLock, cts.Token);
            }
        }

        /// <summary>
        /// Handles auth:validate – validates a bearer token supplied in the message payload
        /// (or falls back to the connection-level token) and returns the claims.
        /// On success, upgrades the connection's validated claims so subsequent messages
        /// can use the authenticated context.
        /// </summary>
        private async Task HandleWsValidateAsync(
            WebSocket webSocket,
            JsonElement root,
            string? requestId,
            SemaphoreSlim sendLock,
            string connectionId,
            ClaimsHolder claimsHolder,
            CancellationToken cancellationToken)
        {
            // Token can be supplied inline in the message or fall back to connection-level claims
            string? inlineToken = root.TryGetProperty("token", out var tok) ? tok.GetString() : null;

            Dictionary<string, object>? claims = null;

            if (!string.IsNullOrWhiteSpace(inlineToken))
            {
                // Validate the inline token directly, supporting both JWE (5-segment encrypted
                // OpenIddict tokens) and plain JWT (3-segment signed tokens).
                // ITokenService.ValidateToken only handles JWTs and cannot decrypt JWEs, which
                // would cause runtime failures when clients supply OpenIddict-issued access tokens.
                // ValidateTokenDirectAsync routes JWEs to /connect/tokeninfo and JWTs to local
                // signature validation, matching the behaviour of ValidateOAuthTokenAsync without
                // relying on HttpContext headers.
                claims = await ValidateTokenDirectAsync(inlineToken, cancellationToken);
            }
            else
            {
                // Use connection-level claims (already validated at connection time)
                claims = claimsHolder.Claims;
            }

            if (claims == null || claims.Count == 0)
            {
                _logger.LogWarning("[AuthWS:{ConnId}] auth:validate failed – invalid or missing token", connectionId);
                await WsSendAsync(webSocket, new
                {
                    type = "auth:validated",
                    requestId,
                    success = false,
                    error = "Invalid or expired token"
                }, sendLock, cancellationToken);
                return;
            }

            // Upgrade connection-level claims if inline token was provided and valid
            if (!string.IsNullOrWhiteSpace(inlineToken))
            {
                claimsHolder.Claims = claims;
                _logger.LogInformation("[AuthWS:{ConnId}] Connection upgraded to authenticated via auth:validate", connectionId);
            }

            // Publish auth event to the realtime bus so SignalR clients also see it
            await PublishAuthEventAsync("auth.token.validated",
                claims.TryGetValue("sub", out var sub) ? sub?.ToString() : null,
                claims.TryGetValue("unique_name", out var name) ? name?.ToString() : null,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                new Dictionary<string, string> { ["source"] = "websocket" });

            await WsSendAsync(webSocket, new
            {
                type = "auth:validated",
                requestId,
                success = true,
                claims = BuildSafeClaims(claims)
            }, sendLock, cancellationToken);
        }

        /// <summary>
        /// Handles auth:refresh – exchanges a refresh token for a new access token
        /// using the orchestration service, then pushes the new token back.
        /// Requires an authenticated connection (or inline refreshToken in the message).
        /// </summary>
        private async Task HandleWsRefreshAsync(
            WebSocket webSocket,
            JsonElement root,
            string? requestId,
            SemaphoreSlim sendLock,
            string connectionId,
            Dictionary<string, object>? validatedClaims,
            CancellationToken cancellationToken)
        {
            var refreshToken = root.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                await WsSendAsync(webSocket, new
                {
                    type = "auth:refreshed",
                    requestId,
                    success = false,
                    error = "refreshToken is required"
                }, sendLock, cancellationToken);
                return;
            }

            var result = await _authOrchestrationService.RefreshTokenAsync(refreshToken);

            if (!result.Success)
            {
                _logger.LogWarning("[AuthWS:{ConnId}] auth:refresh failed: {Error}", connectionId, result.ErrorMessage);
                await WsSendAsync(webSocket, new
                {
                    type = "auth:refreshed",
                    requestId,
                    success = false,
                    error = result.ErrorMessage ?? "Token refresh failed"
                }, sendLock, cancellationToken);
                return;
            }

            await PublishAuthEventAsync("auth.token.refreshed",
                validatedClaims?.TryGetValue("sub", out var sub) == true ? sub?.ToString() : null,
                validatedClaims?.TryGetValue("unique_name", out var name) == true ? name?.ToString() : null,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                new Dictionary<string, string> { ["source"] = "websocket" });

            await WsSendAsync(webSocket, new
            {
                type = "auth:refreshed",
                requestId,
                success = true,
                token = result.Token,
                refreshToken = result.RefreshToken,
                expiresAt = result.ExpiresAt
            }, sendLock, cancellationToken);
        }

        /// <summary>
        /// Handles auth:qr-status – subscribes to real-time QR login completion events
        /// for a given deviceId. Polls the orchestration service and pushes a single
        /// auth:qr-completed or auth:qr-failed message when the status resolves.
        /// Multiple subscriptions for different deviceIds are supported concurrently.
        /// </summary>
        private async Task HandleWsQrStatusAsync(
            WebSocket webSocket,
            JsonElement root,
            string? requestId,
            SemaphoreSlim sendLock,
            string connectionId,
            Dictionary<string, object>? validatedClaims,
            ConcurrentDictionary<string, CancellationTokenSource> qrSubscriptions,
            ConcurrentDictionary<string, Task> qrPollerTasks,
            CancellationTokenSource connectionCts)
        {
            var deviceId = root.TryGetProperty("deviceId", out var did) ? did.GetString() : null;

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                await WsSendAsync(webSocket, new
                {
                    type = "auth:error",
                    requestId,
                    error = "deviceId is required for auth:qr-status"
                }, sendLock, connectionCts.Token);
                return;
            }

            // Cancel any existing subscription for this deviceId
            if (qrSubscriptions.TryRemove(deviceId, out var existing))
            {
                existing.Cancel();
            }

            // Cap concurrent QR subscriptions to prevent task amplification
            const int MaxQrSubscriptions = 50;
            if (qrPollerTasks.Count >= MaxQrSubscriptions)
            {
                await WsSendAsync(webSocket, new
                {
                    type = "auth:qr-error",
                    requestId,
                    deviceId,
                    reason = "Too many concurrent QR subscriptions. Please try again later."
                }, sendLock, connectionCts.Token);
                return;
            }

            var subCts = CancellationTokenSource.CreateLinkedTokenSource(connectionCts.Token);
            qrSubscriptions[deviceId] = subCts;

            // Acknowledge subscription immediately
            await WsSendAsync(webSocket, new
            {
                type = "auth:qr-subscribed",
                requestId,
                deviceId
            }, sendLock, connectionCts.Token);

            // Capture IP address before background task to avoid accessing HttpContext from background thread
            var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Start background polling task – does not block the message loop
            // Use unique key (deviceId + Guid) to track multiple concurrent pollers for the same device
            var pollerKey = $"{deviceId}_{Guid.NewGuid():N}";

            // Pre-create a placeholder to avoid race where the task completes before assignment
            var tcs = new TaskCompletionSource<Task>();
            qrPollerTasks[pollerKey] = tcs.Task.Unwrap();

            var pollerTask = Task.Run(async () =>
            {
                const int pollIntervalMs = 1500;
                const int jitterMs = 300; // +/- 300ms jitter to avoid synchronized storms
                const int maxPollSeconds = 300; // 5-minute QR expiry
                var deadline = DateTimeOffset.UtcNow.AddSeconds(maxPollSeconds);

                try
                {
                    while (!subCts.Token.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
                    {
                        string? pollStatus;
                        string? pollToken;
                        try
                        {
                           // using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(subCts.Token);
                           // pollCts.CancelAfter(TimeSpan.FromSeconds(10));
                            var status = await _authOrchestrationService.CheckQRLoginStatusAsync(deviceId);
                            if (status == null)
                            {
                                if (qrSubscriptions.TryGetValue(deviceId, out var cur1) && ReferenceEquals(cur1, subCts))
                                {
                                    await WsSendAsync(webSocket, new
                                    {
                                        type = "auth:qr-error",
                                        deviceId,
                                        reason = "QR status check returned null"
                                    }, sendLock, subCts.Token);
                                    await PublishAuthEventAsync("auth.qr.error", null, null, sourceIp,
                                        new Dictionary<string, string> { ["deviceId"] = deviceId, ["reason"] = "null_status" });
                                }
                                break;
                            }
                            if (!status.Success)
                            {
                                if (qrSubscriptions.TryGetValue(deviceId, out var cur2) && ReferenceEquals(cur2, subCts))
                                {
                                    await WsSendAsync(webSocket, new
                                    {
                                        type = "auth:qr-error",
                                        deviceId,
                                        reason = "QR status check indicated failure"
                                    }, sendLock, subCts.Token);
                                    await PublishAuthEventAsync("auth.qr.error", null, null, sourceIp,
                                        new Dictionary<string, string> { ["deviceId"] = deviceId, ["reason"] = "status_not_success" });
                                }
                                break;
                            }
                            pollStatus = status.Status;
                            pollToken = status.Token;
                        }
                        catch (Exception pollEx)
                        {
                            _logger.LogError(pollEx, "[AuthWS:{ConnId}] QR poll exception for device {DeviceId}", connectionId, deviceId);
                            if (qrSubscriptions.TryGetValue(deviceId, out var cur3) && ReferenceEquals(cur3, subCts))
                            {
                                await WsSendAsync(webSocket, new
                                {
                                    type = "auth:qr-error",
                                    deviceId,
                                    reason = "Internal error during QR status check"
                                }, sendLock, subCts.Token);
                                await PublishAuthEventAsync("auth.qr.error", null, null, sourceIp,
                                    new Dictionary<string, string> { ["deviceId"] = deviceId, ["reason"] = "poll_exception" });
                            }
                            break;
                        }

                        if (pollStatus == "completed" && !string.IsNullOrEmpty(pollToken))
                        {
                            if (qrSubscriptions.TryGetValue(deviceId, out var cur4) && ReferenceEquals(cur4, subCts))
                            {
                                await WsSendAsync(webSocket, new
                                {
                                    type = "auth:qr-completed",
                                    deviceId,
                                    token = pollToken
                                }, sendLock, subCts.Token);

                                await PublishAuthEventAsync("auth.qr.completed", null, null, sourceIp,
                                    new Dictionary<string, string> { ["deviceId"] = deviceId, ["source"] = "websocket" });
                            }
                            break;
                        }

                        if (pollStatus == "failed" || pollStatus == "cancelled" || pollStatus == "expired")
                        {
                            if (qrSubscriptions.TryGetValue(deviceId, out var cur5) && ReferenceEquals(cur5, subCts))
                            {
                                await WsSendAsync(webSocket, new
                                {
                                    type = "auth:qr-failed",
                                    deviceId,
                                    reason = pollStatus
                                }, sendLock, subCts.Token);
                            }
                            break;
                        }

                        // Add jitter to prevent synchronized polling storms
                        var jitter = Random.Shared.Next(-jitterMs, jitterMs);
                        var delayMs = pollIntervalMs + jitter;
                        await Task.Delay(delayMs, subCts.Token);
                    }

                    // Timed out without resolution
                    if (!subCts.Token.IsCancellationRequested && DateTimeOffset.UtcNow >= deadline)
                    {
                        if (qrSubscriptions.TryGetValue(deviceId, out var cur6) && ReferenceEquals(cur6, subCts))
                        {
                            await WsSendAsync(webSocket, new
                            {
                                type = "auth:qr-failed",
                                deviceId,
                                reason = "timeout"
                            }, sendLock, subCts.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Connection closed or subscription cancelled – normal
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AuthWS:{ConnId}] QR status polling error for device {DeviceId}",
                        connectionId, deviceId);
                }
                finally
                {
                    if (qrSubscriptions.TryGetValue(deviceId, out var current) && ReferenceEquals(current, subCts))
                        qrSubscriptions.TryRemove(deviceId, out _);
                    subCts?.Dispose();
                    // Remove this poller from the tracked tasks
                    qrPollerTasks.TryRemove(pollerKey, out _);
                }
            });

            // Complete the placeholder with the actual task
            tcs.SetResult(pollerTask);
        }

        /// <summary>
        /// Publishes an auth domain event to the realtime event bus so that
        /// SignalR subscribers and WebSocket stream clients also receive auth events.
        /// </summary>
        /// <param name="eventName">The name of the event.</param>
        /// <param name="userId">Optional user ID associated with the event.</param>
        /// <param name="userName">Optional user name associated with the event.</param>
        /// <param name="sourceIp">The source IP address. Must be captured before background task execution.</param>
        /// <param name="metadata">Optional metadata dictionary.</param>
        private async Task PublishAuthEventAsync(
            string eventName,
            string? userId,
            string? userName,
            string? sourceIp,
            Dictionary<string, string>? metadata = null)
        {
            try
            {
                var domainEvent = new ApiDomainEvent(
                    EventName: eventName,
                    Resource: "auth",
                    HttpMethod: "WS",
                    StatusCode: 200,
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: Guid.NewGuid().ToString("N"),
                    UserId: userId,
                    UserName: userName,
                    Tenant: null,
                    SourceIp: sourceIp ?? "unknown",
                    Metadata: (metadata ?? new Dictionary<string, string>()).AsReadOnly());

                await _realtimeEventBus.PublishAsync(domainEvent);
            }
            catch (Exception ex)
            {
                // Non-critical – don't let event bus failures break auth flows
                _logger.LogWarning(ex, "Failed to publish auth event {EventName} to realtime bus", eventName);
            }
        }

        /// <summary>
        /// Builds a safe claims dictionary for sending to the client,
        /// stripping any internal/sensitive claim types.
        /// </summary>
        private static Dictionary<string, object> BuildSafeClaims(Dictionary<string, object> claims)
        {
            var sensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "oi_tkn_id", "oi_au_id", "oi_app_id"
            };

            return claims
                .Where(kv => !sensitive.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        /// <summary>
        /// Sanitizes a client-controlled string value before use in log messages to prevent log forging.
        /// Trims whitespace, strips control characters and newlines, enforces max length, and returns a
        /// safe placeholder for null/empty values.
        /// </summary>
        private static string SanitizeLogValue(string? value, int maxLength = 100)
        {
            if (string.IsNullOrWhiteSpace(value)) return "(none)";
            // Strip control characters (including CR/LF) to prevent log injection
            var sanitized = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (sanitized.Length == 0) return "(none)";
            return sanitized.Length > maxLength ? sanitized[..maxLength] + "…" : sanitized;
        }

        /// <summary>
        /// Thread-safe JSON serialization and send over WebSocket.
        /// sendLock serializes all SendAsync calls on this connection so only one
        /// send is in-flight at a time (WebSocket.SendAsync is not thread-safe).
        /// </summary>
        private async Task WsSendAsync(WebSocket webSocket, object payload, SemaphoreSlim sendLock, CancellationToken cancellationToken)
        {
            if (webSocket.State != WebSocketState.Open)
                return;

            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, WsJsonOptions);

            await sendLock.WaitAsync(cancellationToken);
            try
            {
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }
            finally
            {
                sendLock.Release();
            }
        }

        #endregion
    }

    /// <summary>
    /// Mutable wrapper for validated claims, allowing async WebSocket handlers
    /// to upgrade the connection's authentication state without ref parameters.
    /// 
    /// Single-writer assumption: Claims is only mutated by HandleWsValidateAsync
    /// when a valid inline token is provided. The WebSocket message loop is
    /// sequential (one message processed at a time), so concurrent writes are
    /// not possible and no synchronization is needed on this property.
    /// </summary>
    internal sealed class ClaimsHolder
    {
        public Dictionary<string, object>? Claims { get; set; }
    }
} 