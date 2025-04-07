using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

using TicketSalesApp.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using SpacetimeDB.Types;
using SpacetimeDB;
using Fido2NetLib;
using Fido2NetLib.Objects;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthenticationService _authService;
        private readonly IQRAuthenticationService _qrAuthService;
        private readonly IUserService _userService;
        private readonly ITotpService _totpService;
        private readonly IWebAuthnService _webAuthnService;
        private readonly IMagicLinkService _magicLinkService;
        private readonly IOpenIdConnectService _openIdConnectService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IMemoryCache _cache;
        private readonly ISpacetimeDBService _spacetimeService;

        public AuthController(
            IAuthenticationService authService, 
            IQRAuthenticationService qrAuthService,
            IUserService userService,
            ITotpService totpService,
            IWebAuthnService webAuthnService,
            IMagicLinkService magicLinkService,
            IOpenIdConnectService openIdConnectService,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            IMemoryCache cache,
            ISpacetimeDBService spacetimeService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _qrAuthService = qrAuthService ?? throw new ArgumentNullException(nameof(qrAuthService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _totpService = totpService ?? throw new ArgumentNullException(nameof(totpService));
            _webAuthnService = webAuthnService ?? throw new ArgumentNullException(nameof(webAuthnService));
            _magicLinkService = magicLinkService ?? throw new ArgumentNullException(nameof(magicLinkService));
            _openIdConnectService = openIdConnectService ?? throw new ArgumentNullException(nameof(openIdConnectService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        }

        #region Traditional Authentication

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
        {
            // this is a mess from the deepest pit of the seven hells and i doubt this shit wont crash and burn even if i fix all the errors
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

                // Authenticate user
                var user = await _authService.AuthenticateAsync(request.Username, request.Password);
                if (user == null)
                {
                    _logger.LogWarning("Authentication failed for user: {Username}", request.Username);
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid username or password"
                    });
                }

                var conn = _spacetimeService.GetConnection();
                
                // Check user settings for 2FA
                var userSettings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(user.UserId));
                
                if (userSettings == null)
                {
                    _logger.LogWarning("User settings not found for user: {Username}", request.Username);
                    return StatusCode(500, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User settings not found"
                    });
                }

                if (userSettings.TotpEnabled && !request.SkipTwoFactor)
                {
                    // Generate temporary token for 2FA
                    var tempToken = GenerateRandomToken();
                    var expiresAt = (ulong)DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
                    
                    // Store token in database with expiry
                    conn.Reducers.CreateTwoFactorToken(
                        user.UserId,
                        tempToken,
                        false,
                        expiresAt,
                        Request.Headers["User-Agent"].ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString()
                    );
                    
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
                
                if (userSettings.WebAuthnEnabled && !request.SkipTwoFactor)
                {
                    // Generate temporary token for WebAuthn
                    var tempToken = GenerateRandomToken();
                    var expiresAt = (ulong)DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
                    
                    // Store token in database with expiry
                    conn.Reducers.CreateTwoFactorToken(
                        user.UserId,
                        tempToken,
                        false,
                        expiresAt,
                        Request.Headers["User-Agent"].ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString()
                    );
                    
                    // Get WebAuthn credentials for the user
                    var credentials = conn.Db.WebAuthnCredential.Iter()
                        .Where(c => c.UserId.Equals(user.UserId) && c.IsActive)
                        .ToList();

                    if (!credentials.Any())
                    {
                        _logger.LogWarning("No WebAuthn credentials found for user: {Username}", request.Username);
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "No WebAuthn credentials found"
                        });
                    }

                    // Create assertion options
                    var (success, options, _) = await _webAuthnService.GetAssertionOptionsAsync(user.Login);
                    if (!success || options == null)
                    {
                        _logger.LogWarning("Failed to create assertion options for user: {Username}", request.Username);
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "Failed to create assertion options"
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

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<LoginResponse>
                {
                    Success = true,
                    Message = "Authentication successful",
                    Data = new LoginResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
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

        [HttpPost("register")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request)
        {
            try
            {
                _logger.LogInformation("Registration attempt for user: {Username}", request.Username);

                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid request data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                // Check if user already exists
                var existingUser = await _userService.GetUserByLoginAsync(request.Username);
                if (existingUser != null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Username already exists"
                    });
                }

                // Register user
                var success = await _authService.RegisterAsync(
                    request.Username,
                    request.Password,
                    request.Role,
                    request.Email,
                    request.PhoneNumber
                );

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Failed to register user"
                    });
                }

                // Get the newly created user
                var newUser = await _userService.GetUserByLoginAsync(request.Username);
                if (newUser == null)
                {
                    return StatusCode(500, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User was created but could not be retrieved"
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
                            Id = newUser.LegacyUserId,
                            Username = newUser.Login,
                            Email = newUser.Email,
                            PhoneNumber = newUser.PhoneNumber,
                            Role = _authService.GetUserRole(newUser.UserId)
                        }
                    }
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

        #endregion

        #region TOTP (Time-based One-Time Password)

        [HttpPost("totp/setup")]
        [Authorize]
        public async Task<ActionResult<TotpSetupResponse>> SetupTotp()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await GetUserByIdentityAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Generate TOTP setup
                var result = await _totpService.SetupTotpAsync(userId.Value, user.Login);
                bool success = result.success;
                string? secretKey = result.secretKey;
                string? qrCodeUri = result.qrCodeUri;
                string? errorMessage = result.errorMessage;
                if (!success || secretKey == null || qrCodeUri == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to set up TOTP"
                    });
                }

                return Ok(new ApiResponse<TotpSetupResponse>
                {
                    Success = true,
                    Message = "TOTP setup successful",
                    Data = new TotpSetupResponse
                    {
                        SecretKey = secretKey,
                        QrCodeUri = qrCodeUri
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while setting up TOTP"
                });
            }
        }

        [HttpPost("totp/verify")]
        [Authorize]
        public async Task<ActionResult<VerifyTotpResponse>> VerifyTotp([FromBody] VerifyTotpRequest request)
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Verify TOTP code
                var result = await _totpService.EnableTotpAsync(userId.Value, request.Code, request.SecretKey);
                bool success = result.success;
                string? errorMessage = result.errorMessage;
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to verify TOTP code"
                    });
                }

                return Ok(new ApiResponse<VerifyTotpResponse>
                {
                    Success = true,
                    Message = "TOTP verification successful",
                    Data = new VerifyTotpResponse
                    {
                        Enabled = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while verifying TOTP"
                });
            }
        }

        [HttpPost("totp/disable")]
        [Authorize]
        public async Task<ActionResult<DisableTotpResponse>> DisableTotp()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Disable TOTP
                var result = await _totpService.DisableTotpAsync(userId.Value);
                bool success = result.success;
                string? errorMessage = result.errorMessage;
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to disable TOTP"
                    });
                }

                return Ok(new ApiResponse<DisableTotpResponse>
                {
                    Success = true,
                    Message = "TOTP disabled successfully",
                    Data = new DisableTotpResponse
                    {
                        Disabled = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while disabling TOTP"
                });
            }
        }

        
        [HttpPost("totp/validate")]
        [AllowAnonymous]
        public async Task<ActionResult<ValidateTotpResponse>> ValidateTotp([FromBody] ValidateTotpRequest request)
        {
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

                var conn = _spacetimeService.GetConnection();
                
                // Find two-factor token
                var twoFactorToken = conn.Db.TwoFactorToken.Iter()
                    .FirstOrDefault(t => t.Token == request.TempToken && !t.IsUsed);
                
                if (twoFactorToken == null)
                {
                    _logger.LogWarning("Invalid or expired two-factor token: {Token}", request.TempToken);
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid or expired token"
                    });
                }

                // Get the user
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(twoFactorToken.UserId));
                if (user == null)
                {
                    _logger.LogWarning("User not found for two-factor token: {Token}", request.TempToken);
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Get TOTP secret
                var totpSecret = conn.Db.TotpSecret.Iter()
                    .FirstOrDefault(t => t.UserId.Equals(user.UserId) && t.IsActive);
                if (totpSecret == null)
                {
                    _logger.LogWarning("TOTP secret not found for user: {UserId}", user.UserId);
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "TOTP not set up"
                    });
                }

                // Verify TOTP code
                var isValid = _totpService.VerifyTotpCode(totpSecret.Secret, request.Code);
                if (!isValid)
                {
                    _logger.LogWarning("Invalid TOTP code for user: {UserId}", user.UserId);
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid code"
                    });
                }

                // Mark token as used
                var twoFactorTokenId = twoFactorToken.Id;
                var twoFactorUserId = twoFactorToken.UserId;
                var tokenValue = twoFactorToken.Token;
                var expiresAt = twoFactorToken.ExpiresAt;
                
                // We'll directly update the TwoFactorToken in the database
                // since UpdateTwoFactorToken doesn't exist in the reducers
                conn.Reducers.UpdateTwoFactorToken(
                    twoFactorTokenId,
                    twoFactorUserId,
                    tokenValue,
                    true, // Mark as used
                    expiresAt
                );

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<ValidateTotpResponse>
                {
                    Success = true,
                    Message = "TOTP validation successful",
                    Data = new ValidateTotpResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while validating TOTP"
                });
            }
        }

        #endregion

        #region WebAuthn (FIDO2)

        [HttpPost("webauthn/register/options")]
        [Authorize]
        public async Task<ActionResult<WebAuthnRegisterOptionsResponse>> GetWebAuthnRegisterOptions()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await GetUserByIdentityAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Get WebAuthn registration options
                var result = await _webAuthnService.GetCredentialCreateOptionsAsync(userId.Value, user.Login);
                bool success = result.success;
                CredentialCreateOptions? options = result.options;
                string? errorMessage = result.errorMessage;
                if (!success || options == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get WebAuthn registration options"
                    });
                }

                return Ok(new ApiResponse<WebAuthnRegisterOptionsResponse>
                {
                    Success = true,
                    Message = "WebAuthn registration options generated",
                    Data = new WebAuthnRegisterOptionsResponse
                    {
                        Options = options
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn registration options");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting WebAuthn registration options"
                });
            }
        }

        [HttpPost("webauthn/register/complete")]
        [Authorize]
        public async Task<ActionResult<WebAuthnRegisterCompleteResponse>> CompleteWebAuthnRegistration([FromBody] WebAuthnRegisterCompleteRequest request)
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await GetUserByIdentityAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Complete WebAuthn registration
                var result = await _webAuthnService.CompleteRegistrationAsync(userId.Value, user.Login, request.AttestationResponse);
                bool success = result.success;
                string? errorMessage = result.errorMessage;
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to complete WebAuthn registration"
                    });
                }

                return Ok(new ApiResponse<WebAuthnRegisterCompleteResponse>
                {
                    Success = true,
                    Message = "WebAuthn registration completed successfully",
                    Data = new WebAuthnRegisterCompleteResponse
                    {
                        Registered = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing WebAuthn registration");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while completing WebAuthn registration"
                });
            }
        }

        [HttpPost("webauthn/login/options")]
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnLoginOptionsResponse>> GetWebAuthnLoginOptions([FromBody] WebAuthnLoginOptionsRequest request)
        {
            try
            {
                // Get WebAuthn assertion options
                var (success, options, errorMessage) = await _webAuthnService.GetAssertionOptionsAsync(request.Username);
                if (!success || options == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get WebAuthn login options"
                    });
                }

                return Ok(new ApiResponse<WebAuthnLoginOptionsResponse>
                {
                    Success = true,
                    Message = "WebAuthn login options generated",
                    Data = new WebAuthnLoginOptionsResponse
                    {
                        Options = options
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn login options");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting WebAuthn login options"
                });
            }
        }

        [HttpPost("webauthn/login/complete")]
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnLoginCompleteResponse>> CompleteWebAuthnLogin([FromBody] WebAuthnLoginCompleteRequest request)
        {
            try
            {
                // Complete WebAuthn login
                var (success, user, errorMessage) = await _webAuthnService.CompleteAssertionAsync(request.Username, request.AssertionResponse);
                if (!success || user == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to complete WebAuthn login"
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<WebAuthnLoginCompleteResponse>
                {
                    Success = true,
                    Message = "WebAuthn login completed successfully",
                    Data = new WebAuthnLoginCompleteResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing WebAuthn login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while completing WebAuthn login"
                });
            }
        }

        [HttpPost("webauthn/validate")]
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnValidateResponse>> ValidateWebAuthn([FromBody] WebAuthnValidateRequest request)
        {
            try
            {
                // Get user from token
                var conn = _spacetimeService.GetConnection();
                var twoFactorToken = conn.Db.TwoFactorToken.Iter()
                    .FirstOrDefault(t => t.Token == request.TempToken && !t.IsUsed);
                
                if (twoFactorToken == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid or expired token"
                    });
                }

                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(twoFactorToken.UserId));
                
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Complete WebAuthn assertion
                var (success, _, errorMessage) = await _webAuthnService.CompleteAssertionAsync(user.Login, request.AssertionResponse);
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to validate WebAuthn"
                    });
                }

                // Mark token as used
                var twoFactorTokenId = twoFactorToken.Id;
                var twoFactorUserId = twoFactorToken.UserId;
                var tokenValue = twoFactorToken.Token;
                var expiresAt = twoFactorToken.ExpiresAt;
                
                // We'll directly update the TwoFactorToken in the database
                // since UpdateTwoFactorToken doesn't exist in the reducers
                conn.Reducers.UpdateTwoFactorToken(
                    twoFactorTokenId,
                    twoFactorUserId,
                    tokenValue,
                    true, // Mark as used
                    expiresAt
                );

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<WebAuthnValidateResponse>
                {
                    Success = true,
                    Message = "WebAuthn validation successful",
                    Data = new WebAuthnValidateResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating WebAuthn");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while validating WebAuthn"
                });
            }
        }

        [HttpGet("webauthn/credentials")]
        [Authorize]
        public async Task<ActionResult<WebAuthnCredentialsResponse>> GetWebAuthnCredentials()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Get WebAuthn credentials
                if (userId == null) throw new InvalidOperationException("User ID cannot be null");
                var credentials = await _webAuthnService.GetUserCredentialsAsync(userId.Value);

                return Ok(new ApiResponse<WebAuthnCredentialsResponse>
                {
                    Success = true,
                    Message = "WebAuthn credentials retrieved successfully",
                    Data = new WebAuthnCredentialsResponse
                    {
                        Credentials = credentials.Select(c => new WebAuthnCredentialDto
                        {
                            Id = Convert.ToBase64String(c.CredentialId.ToArray()),
                            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)c.CreatedAt).DateTime
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn credentials");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting WebAuthn credentials"
                });
            }
        }

        [HttpDelete("webauthn/credentials/{id}")]
        [Authorize]
        public async Task<ActionResult<WebAuthnRemoveCredentialResponse>> RemoveWebAuthnCredential(string id)
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Remove WebAuthn credential
                var result = await _webAuthnService.RemoveCredentialAsync(userId.Value, id);
                bool success = result.success;
                string? errorMessage = result.errorMessage;
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to remove WebAuthn credential"
                    });
                }

                return Ok(new ApiResponse<WebAuthnRemoveCredentialResponse>
                {
                    Success = true,
                    Message = "WebAuthn credential removed successfully",
                    Data = new WebAuthnRemoveCredentialResponse
                    {
                        Removed = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing WebAuthn credential");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while removing WebAuthn credential"
                });
            }
        }

        #endregion

        #region Magic Link

        [HttpPost("magic-link/send")]
        [AllowAnonymous]
        public async Task<ActionResult<MagicLinkResponse>> SendMagicLink([FromBody] MagicLinkRequest request)
        {
            try
            {
                // Get client info
                var userAgent = Request.Headers["User-Agent"].ToString();
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

                // Send magic link
                var (success, errorMessage) = await _magicLinkService.SendMagicLinkAsync(request.Email, userAgent, ipAddress);
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to send magic link"
                    });
                }

                return Ok(new ApiResponse<MagicLinkResponse>
                {   
                    Success = true,
                    Message = "Magic link sent successfully",
                    Data = new MagicLinkResponse
                    {
                        Sent = true,
                        Email = request.Email
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending magic link");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while sending magic link"
                });
            }
        }

        [HttpGet("validate-magic-link")]
        [AllowAnonymous]
        public async Task<ActionResult> ValidateMagicLink([FromQuery] string token)
        {
            try
            {
                // Validate magic link token
                var (success, user, errorMessage) = await _magicLinkService.ValidateMagicLinkAsync(token);
                if (!success || user == null)
                {
                    // Redirect to error page
                    return Redirect($"/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Invalid or expired magic link")}");
                }

                // Mark token as used
                await _magicLinkService.MarkMagicLinkAsUsedAsync(token);

                // Generate JWT token
                var jwtToken = GenerateJwtToken(user);

                // Redirect to success page with token
                return Redirect($"/auth/success?token={Uri.EscapeDataString(jwtToken)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating magic link");
                return Redirect($"/auth/error?message={Uri.EscapeDataString("An error occurred while validating magic link")}");
            }
        }

        [HttpPost("validate-magic-link")]
        [AllowAnonymous]
        public async Task<ActionResult<ValidateMagicLinkResponse>> ValidateMagicLinkApi([FromBody] ValidateMagicLinkRequest request)
        {
            try
            {
                // Validate magic link token
                var (success, user, errorMessage) = await _magicLinkService.ValidateMagicLinkAsync(request.Token);
                if (!success || user == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Invalid or expired magic link"
                    });
                }

                // Mark token as used
                await _magicLinkService.MarkMagicLinkAsUsedAsync(request.Token);

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<ValidateMagicLinkResponse>
                {
                    Success = true,
                    Message = "Magic link validated successfully",
                    Data = new ValidateMagicLinkResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating magic link");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while validating magic link"
                });
            }
        }

        #endregion

        #region QR Code Authentication

        [HttpGet("qr/generate")]
        [Authorize]
        public async Task<ActionResult<QrCodeResponse>> GenerateQRCode()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await GetUserByIdentityAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Generate QR code
                (string qrCodeBase64, string rawData) = await _qrAuthService.GenerateQRCodeWithDataAsync(user);

                return Ok(new ApiResponse<QrCodeResponse>
                {
                    Success = true,
                    Message = "QR code generated successfully",
                    Data = new QrCodeResponse
                    {
                        QrCode = qrCodeBase64,
                        RawData = rawData
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating QR code");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while generating QR code"
                });
            }
        }

        [HttpPost("qr/login")]
        [AllowAnonymous]
        public async Task<ActionResult<QrLoginResponse>> QRLogin([FromBody] QrLoginRequest request)
        {
            try
            {
                // Authenticate with QR code
                var user = await _authService.AuthenticateDirectQRAsync(request.Username, request.Token);
                if (user == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid QR login credentials"
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<QrLoginResponse>
                {
                    Success = true,
                    Message = "QR login successful",
                    Data = new QrLoginResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during QR login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during QR login"
                });
            }
        }

        [HttpGet("qr/direct/generate")]
        [AllowAnonymous]
        public async Task<ActionResult<DirectQrCodeResponse>> GenerateDirectLoginQRCode([FromQuery] string username, [FromQuery] string deviceType)
        {
            try
            {
                // Validate user exists
                var user = await _userService.GetUserByLoginAsync(username);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Generate direct login QR code
                var (qrCode, rawData) = await _qrAuthService.GenerateDirectLoginQRCodeAsync(username, deviceType);

                return Ok(new ApiResponse<DirectQrCodeResponse>
                {
                    Success = true,
                    Message = "Direct login QR code generated successfully",
                    Data = new DirectQrCodeResponse
                    {
                        QrCode = qrCode,
                        RawData = rawData
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating direct login QR code");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while generating direct login QR code"
                });
            }
        }

        [HttpPost("qr/direct/login")]
        [AllowAnonymous]
        public async Task<ActionResult<DirectQrLoginResponse>> DirectQRLogin([FromBody] DirectQrLoginRequest request)
        {
            try
            {
                // Validate direct login token
                var (success, user, deviceId) = await _qrAuthService.ValidateDirectLoginTokenAsync(request.Token, request.DeviceType);
                if (!success || user == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid QR login token"
                    });
                }

                // Authenticate user without password
                var authenticatedUser = await _authService.AuthenticateDirectQRAsync(user.Login, deviceId);
                if (authenticatedUser == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Authentication failed"
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(authenticatedUser);

                // If this is a mobile device scanning a desktop QR code, notify the desktop
                if (request.DeviceType == "mobile" && request.IsDesktopLogin)
                {
                    await _qrAuthService.NotifyDeviceLoginSuccessAsync(deviceId, token);
                }

                return Ok(new ApiResponse<DirectQrLoginResponse>
                {
                    Success = true,
                    Message = "Direct QR login successful",
                    Data = new DirectQrLoginResponse
                    {
                        Token = token,
                        DeviceId = deviceId,
                        User = new UserDto
                        {
                            Id = authenticatedUser.LegacyUserId,
                            Username = authenticatedUser.Login,
                            Email = authenticatedUser.Email,
                            PhoneNumber = authenticatedUser.PhoneNumber,
                            Role = _authService.GetUserRole(authenticatedUser.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during direct QR login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during direct QR login"
                });
            }
        }

        [HttpGet("qr/direct/check")]
        [AllowAnonymous]
        public async Task<ActionResult<CheckQrLoginResponse>> CheckDirectLoginStatus([FromQuery] string deviceId)
        {
            try
            {
                var loginSuccessKey = $"login_success_{deviceId}";
                if (_cache.TryGetValue(loginSuccessKey, out string token))
                {
                    _cache.Remove(loginSuccessKey); // One-time use
                    return Ok(new ApiResponse<CheckQrLoginResponse>
                    {
                        Success = true,
                        Message = "Login successful",
                        Data = new CheckQrLoginResponse
                        {
                            Success = true,
                            Token = token
                        }
                    });
                }

                return Ok(new ApiResponse<CheckQrLoginResponse>
                {
                    Success = true,
                    Message = "No login detected yet",
                    Data = new CheckQrLoginResponse
                    {
                        Success = false
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking direct login status");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while checking login status"
                });
            }
        }

        #endregion

        #region OpenID Connect

        [HttpGet("connect/authorize")]
        [AllowAnonymous]
        public IActionResult Authorize([FromQuery] string client_id, [FromQuery] string redirect_uri, [FromQuery] string response_type, [FromQuery] string scope, [FromQuery] string state, [FromQuery] string nonce)
        {
            try
            {
                // Store the request in cache for later retrieval
                var requestId = Guid.NewGuid().ToString();
                _cache.Set($"oidc_request_{requestId}", new OpenIdConnectRequest
                {
                    ClientId = client_id,
                    RedirectUri = redirect_uri,
                    ResponseType = response_type,
                    Scope = scope,
                    State = state,
                    Nonce = nonce
                }, TimeSpan.FromMinutes(10));

                // Redirect to login page with request ID
                return Redirect($"/oauth/login?request_id={requestId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OpenID Connect authorization request");
                return StatusCode(500, "An error occurred while processing the authorization request");
            }
        }

        [HttpPost("connect/token")]
        [AllowAnonymous]
        public async Task<ActionResult<TokenResponse>> Token([FromForm] TokenRequest request)
        {
            try
            {
                if (request.GrantType == "authorization_code")
                {
                    // Validate authorization code
                    var codeData = _cache.Get<AuthorizationCodeData>($"auth_code_{request.Code}");
                    if (codeData == null)
                    {
                        return BadRequest(new
                        {
                            error = "invalid_grant",
                            error_description = "The authorization code is invalid or has expired."
                        });
                    }

                    // Get the user's SpacetimeDB Identity from legacy ID
                    var conn = _spacetimeService.GetConnection();
                    var userProfile = conn.Db.UserProfile.Iter()
                        .FirstOrDefault(u => u.LegacyUserId == codeData.UserId);
                    
                    if (userProfile == null) {
                        return BadRequest(new {
                            error = "invalid_grant",
                            error_description = "User not found."
                        });
                    }

                    var user = await GetUserByIdentityAsync(userProfile.UserId);
                    if (user == null)
                    {
                        return BadRequest(new
                        {
                            error = "invalid_grant",
                            error_description = "The user associated with the authorization code no longer exists."
                        });
                    }

                    // Get the application
                    var (appSuccess, application, appError) = await _openIdConnectService.GetApplicationByClientIdAsync(request.ClientId);
                    if (!appSuccess || application == null)
                    {
                        return BadRequest(new
                        {
                            error = "invalid_client",
                            error_description = appError ?? "The client application is invalid."
                        });
                    }

                    // Validate client secret if provided
                    if (!string.IsNullOrEmpty(request.ClientSecret))
                    {
                        // Implement client secret validation here
                    }

                    // Create identity
                    var (identitySuccess, identity, identityError) = await _openIdConnectService.CreateIdentityFromUserAsync(user, codeData.Scopes);
                    if (!identitySuccess || identity == null)
                    {
                        return BadRequest(new
                        {
                            error = "server_error",
                            error_description = identityError ?? "Failed to create identity."
                        });
                    }

                    // Generate JWT token
                    var token = GenerateJwtToken(user);

                    // Remove the authorization code
                    _cache.Remove($"auth_code_{request.Code}");

                    return Ok(new TokenResponse
                    {
                        AccessToken = token,
                        TokenType = "Bearer",
                        ExpiresIn = int.Parse(_configuration["JwtSettings:ExpirationInMinutes"] ?? "120") * 60,
                        Scope = string.Join(" ", codeData.Scopes)
                    });
                }
                else if (request.GrantType == "refresh_token")
                {
                    // Implement refresh token flow
                    return BadRequest(new
                    {
                        error = "unsupported_grant_type",
                        error_description = "Refresh token flow is not implemented yet."
                        
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        error = "unsupported_grant_type",
                        error_description = "The specified grant type is not supported."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing token request");
                return StatusCode(500, new
                {
                    error = "server_error",
                    error_description = "An error occurred while processing the token request."
                });
            }
        }

        [HttpPost("connect/authorize/callback")]
        [AllowAnonymous]
        public async Task<IActionResult> AuthorizeCallback([FromForm] AuthorizeCallbackRequest request)
        {
            try
            {
                // Get the original request from cache
                var originalRequest = _cache.Get<OpenIdConnectRequest>($"oidc_request_{request.RequestId}");
                if (originalRequest == null)
                {
                    return BadRequest("Invalid or expired request ID");
                }

                // Authenticate user
                var user = await _authService.AuthenticateAsync(request.Username, request.Password);
                if (user == null)
                {
                    return Redirect($"/oauth/login?request_id={request.RequestId}&error=invalid_credentials");
                }

                // Get the application
                var (appSuccess, application, appError) = await _openIdConnectService.GetApplicationByClientIdAsync(originalRequest.ClientId);
                if (!appSuccess || application == null)
                {
                    return BadRequest($"Invalid client: {appError}");
                }

                // Parse scopes
                var scopes = originalRequest.Scope.Split(' ');

                // Create authorization code
                var code = GenerateRandomToken();
                
                // Store the code data
                _cache.Set($"auth_code_{code}", new AuthorizationCodeData
                {
                    UserId = user.LegacyUserId,
                    Scopes = scopes,
                    RedirectUri = originalRequest.RedirectUri
                }, TimeSpan.FromMinutes(5));

                // Build the redirect URL
                var redirectUrl = $"{originalRequest.RedirectUri}?code={code}&state={originalRequest.State}";
                
                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing authorization callback");
                return StatusCode(500, "An error occurred while processing the authorization callback");
            }
        }

        [HttpGet("connect/userinfo")]
        [Authorize]
        public async Task<ActionResult<UserInfoResponse>> UserInfo()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new
                    {
                        error = "invalid_token",
                        error_description = "The access token is invalid or expired."
                    });
                }

                var user = await GetUserByIdentityAsync(userId);
                if (user == null)
                {
                    return NotFound(new
                    {
                        error = "invalid_token",
                        error_description = "The user associated with the access token no longer exists."
                    });
                }

                // Get user roles
                var conn = _spacetimeService.GetConnection();
                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.UserId.Equals(userId))
                    .Join(conn.Db.Role.Iter(), ur => ur.RoleId, r => r.RoleId, (ur, r) => r.Name)
                    .ToList();

                return Ok(new UserInfoResponse
                {
                    Sub = user.LegacyUserId.ToString(),
                    Name = user.Login,
                    PreferredUsername = user.Login,
                    Email = user.Email,
                    EmailVerified = (bool)user.EmailConfirmed,
                    PhoneNumber = user.PhoneNumber,
                    
                    Roles = userRoles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user info");
                return StatusCode(500, new
                {
                    error = "server_error",
                    error_description = "An error occurred while getting user info."
                });
            }
        }

        [HttpPost("connect/register-client")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<RegisterClientResponse>> RegisterClient([FromBody] RegisterClientRequest request)
        {
            try
            {
                // Register client application
                var (success, errorMessage) = await _openIdConnectService.RegisterClientApplicationAsync(
                    request.ClientId,
                    request.ClientSecret,
                    request.DisplayName,
                    request.RedirectUris,
                    request.PostLogoutRedirectUris,
                    request.AllowedScopes,
                    request.RequireConsent
                );

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to register client application"
                    });
                }

                return Ok(new ApiResponse<RegisterClientResponse>
                {
                    Success = true,
                    Message = "Client application registered successfully",
                    Data = new RegisterClientResponse
                    {
                        ClientId = request.ClientId,
                        DisplayName = request.DisplayName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while registering client application"
                });
            }
        }

        [HttpPut("connect/update-client/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<UpdateClientResponse>> UpdateClient(string clientId, [FromBody] UpdateClientRequest request)
        {
            try
            {
                // Update client application
                var (success, errorMessage) = await _openIdConnectService.UpdateClientApplicationAsync(
                    clientId,
                    request.ClientSecret,
                    request.DisplayName,
                    request.RedirectUris,
                    request.PostLogoutRedirectUris,
                    request.AllowedScopes,
                    request.RequireConsent
                );

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to update client application"
                    });
                }

                return Ok(new ApiResponse<UpdateClientResponse>
                {
                    Success = true,
                    Message = "Client application updated successfully",
                    Data = new UpdateClientResponse
                    {
                        ClientId = clientId,
                        DisplayName = request.DisplayName ?? "Unknown"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while updating client application"
                });
            }
        }

        [HttpDelete("connect/delete-client/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<DeleteClientResponse>> DeleteClient(string clientId)
        {
            try
            {
                // Delete client application
                var (success, errorMessage) = await _openIdConnectService.DeleteClientApplicationAsync(clientId);

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to delete client application"
                    });
                }

                return Ok(new ApiResponse<DeleteClientResponse>
                {
                    Success = true,
                    Message = "Client application deleted successfully",
                    Data = new DeleteClientResponse
                    {
                        ClientId = clientId,
                        Deleted = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while deleting client application"
                });
            }
        }

        [HttpGet("connect/clients")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<GetClientsResponse>> GetClients()
        {
            try
            {
                // Get all client applications
                var (success, applications, errorMessage) = await _openIdConnectService.GetAllClientApplicationsAsync();

                if (!success || applications == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get client applications"
                    });
                }

                // Convert to DTO
                var clientDtos = new List<ClientDto>();
                foreach (var app in applications)
                {
                    var clientId = await GetClientIdAsync(app);
                    var displayName = await GetDisplayNameAsync(app);
                    
                    clientDtos.Add(new ClientDto
                    {
                        ClientId = clientId,
                        DisplayName = displayName
                    });
                }

                return Ok(new ApiResponse<GetClientsResponse>
                {
                    Success = true,
                    Message = "Client applications retrieved successfully",
                    Data = new GetClientsResponse
                    {
                        Clients = clientDtos
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client applications");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting client applications"
                });
            }
        }

        [HttpGet("connect/client/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<GetClientResponse>> GetClient(string clientId)
        {
            try
            {
                // Get client application
                var (success, application, errorMessage) = await _openIdConnectService.GetClientApplicationAsync(clientId);

                if (!success || application == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get client application"
                    });
                }

                // Get client details
                var displayName = await GetDisplayNameAsync(application);
                var redirectUris = await GetRedirectUrisAsync(application);
                var postLogoutRedirectUris = await GetPostLogoutRedirectUrisAsync(application);
                var permissions = await GetPermissionsAsync(application);
                var consentType = await GetConsentTypeAsync(application);

                return Ok(new ApiResponse<GetClientResponse>
                {
                    Success = true,
                    Message = "Client application retrieved successfully",
                    Data = new GetClientResponse
                    {
                        ClientId = clientId,
                        DisplayName = displayName,
                        RedirectUris = redirectUris.ToArray(),
                        PostLogoutRedirectUris = postLogoutRedirectUris.ToArray(),
                        AllowedScopes = permissions.ToArray(),
                        RequireConsent = consentType == "explicit"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting client application"
                });
            }
        }

        #endregion

        #region Helper Methods
        
        private string GenerateJwtToken(UserProfile userProfile)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var keyString = _configuration["JwtSettings:Secret"] ?? 
                throw new InvalidOperationException("JWT secret is not configured");

            // Ensure the key is at least 32 bytes
            var keyBytes = Encoding.UTF8.GetBytes(keyString);
            if (keyBytes.Length < 32)
            {
                Array.Resize(ref keyBytes, 32);
            }
            else if (keyBytes.Length > 64)
            {
                Array.Resize(ref keyBytes, 64);
            }

            var key = new SymmetricSecurityKey(keyBytes);
            var expirationMinutes = double.Parse(_configuration["JwtSettings:ExpirationInMinutes"] ?? "120");
            
            var conn = _spacetimeService.GetConnection();
            
            // Get user's roles
            var userRoles = conn.Db.UserRole.Iter()
                .Where(ur => ur.UserId.Equals(userProfile.UserId))
                .Select(ur => ur.RoleId)
                .ToList();
            
            // Get role details
            var roles = conn.Db.Role.Iter()
                .Where(r => userRoles.Contains(r.RoleId) && r.IsActive)
                .ToList();
            
            // Get role permissions
            var rolePermissions = conn.Db.RolePermission.Iter()
                .Where(rp => userRoles.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToList();
            
            // Get permission details
            var permissions = conn.Db.Permission.Iter()
                .Where(p => rolePermissions.Contains(p.PermissionId) && p.IsActive)
                .ToList();
            
            // Create claims
            var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, userProfile.Login),
                new Claim("sub", userProfile.LegacyUserId.ToString()),
                new Claim("identity", userProfile.UserId.ToString()),
                new Claim("xuid", userProfile.Xuid.ToString() ?? "0")
            };
            
            // Add role claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Name));
                claims.Add(new Claim("role", role.LegacyRoleId.ToString())); // Keep legacy role ID for backward compatibility
            }
            
            // Add permission claims
            foreach (var permission in permissions)
            {
                claims.Add(new Claim("permission", permission.Name));
            }
            
            // Add highest priority role for IsAdmin checks
            var highestPriorityRole = roles.OrderByDescending(r => r.Priority).FirstOrDefault();
            if (highestPriorityRole != null)
            {
                claims.Add(new Claim("primary_role", highestPriorityRole.LegacyRoleId.ToString()));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private Identity? GetUserIdentity()
        {
            var identityString = User.FindFirst("identity")?.Value;
            if (string.IsNullOrEmpty(identityString))
            {
                return null;
            }

            try
            {
                var conn = _spacetimeService.GetConnection();
                return conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.LegacyUserId.ToString() == identityString)?.UserId;
            }
            catch
            {
                return null;
            }
        }

        private string GenerateRandomToken()
        {
            var randomBytes = new byte[32];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(randomBytes);
            }
            return Convert.ToBase64String(randomBytes);
        }
//KEEP ASYNC stop trying to remove it
        private async Task<UserProfile?> GetUserByIdentityAsync(Identity? userId) // valid way to return any data -is RETURN THE FUCKING DATA - METHOD STILL GOTTA BE ASYNC
        {
            if (userId == null)
                return null;
                
            var conn = _spacetimeService.GetConnection();
            var user = conn.Db.UserProfile.Iter()
                .FirstOrDefault(u => u.UserId.Equals(userId));
                
            return user;
        }

        private async Task<string> GetClientIdAsync(object application)
        {
            // Here, we're assuming the application object has a property named ClientId
            // You may need to adjust this based on the actual implementation
            var propertyInfo = application.GetType().GetProperty("ClientId");
            if (propertyInfo != null)
            {
                var value = propertyInfo.GetValue(application)?.ToString();
                return value ?? string.Empty;
            }
            return string.Empty;
        }

        private async Task<string> GetDisplayNameAsync(object application)
        {
            // Here, we're assuming the application object has a property named DisplayName
            var propertyInfo = application.GetType().GetProperty("DisplayName");
            if (propertyInfo != null)
            {
                var value = propertyInfo.GetValue(application)?.ToString();
                return value ?? string.Empty;
            }
            return string.Empty;
        }

        private async Task<List<string>> GetRedirectUrisAsync(object application)
        {
            // Placeholder implementation
            // You may need to adjust this based on the actual implementation
            var result = new List<string>();
            try
            {
                var propertyInfo = application.GetType().GetProperty("RedirectUris");
                if (propertyInfo != null)
                {
                    var value = propertyInfo.GetValue(application);
                    if (value is IEnumerable<string> uris)
                    {
                        result.AddRange(uris);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting redirect URIs from application object");
            }
            return result;
        }

        private async Task<List<string>> GetPostLogoutRedirectUrisAsync(object application)
        {
            // Placeholder implementation
            // You may need to adjust this based on the actual implementation
            var result = new List<string>();
            try
            {
                var propertyInfo = application.GetType().GetProperty("PostLogoutRedirectUris");
                if (propertyInfo != null)
                {
                    var value = propertyInfo.GetValue(application);
                    if (value is IEnumerable<string> uris)
                    {
                        result.AddRange(uris);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting post-logout redirect URIs from application object");
            }
            return result;
        }

        private async Task<List<string>> GetPermissionsAsync(object application)
        {
            // Placeholder implementation
            // You may need to adjust this based on the actual implementation
            var result = new List<string>();
            try
            {
                var propertyInfo = application.GetType().GetProperty("Permissions");
                if (propertyInfo != null)
                {
                    var value = propertyInfo.GetValue(application);
                    if (value is IEnumerable<string> permissions)
                    {
                        result.AddRange(permissions);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting permissions from application object");
            }
            return result;
        }

        private async Task<string> GetConsentTypeAsync(object application)
        {
            // Placeholder implementation
            // You may need to adjust this based on the actual implementation
            try
            {
                var propertyInfo = application.GetType().GetProperty("ConsentType");
                if (propertyInfo != null)
                {
                    var value = propertyInfo.GetValue(application)?.ToString();
                    return value ?? "implicit";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting consent type from application object");
            }
            return "implicit";
        }

        #endregion
    }

    #region Request Models

    public class LoginRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public bool SkipTwoFactor { get; set; } = false;
    }

    public class RegisterRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public int Role { get; set; } = 0;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
    }

    public class VerifyTotpRequest
    {
        public required string Code { get; set; }
        public required string SecretKey { get; set; }
    }

    public class ValidateTotpRequest
    {
        public required string TempToken { get; set; }
        public required string Code { get; set; }
    }

    public class WebAuthnRegisterCompleteRequest
    {
        public required AuthenticatorAttestationRawResponse AttestationResponse { get; set; }
    }

    public class WebAuthnLoginOptionsRequest
    {
        public required string Username { get; set; }
    }

    public class WebAuthnLoginCompleteRequest
    {
        public required string Username { get; set; }
        public required AuthenticatorAssertionRawResponse AssertionResponse { get; set; }
    }

    public class WebAuthnValidateRequest
    {
        public required string TempToken { get; set; }
        public required AuthenticatorAssertionRawResponse AssertionResponse { get; set; }
    }

    public class MagicLinkRequest
    {
        public required string Email { get; set; }
    }

    public class ValidateMagicLinkRequest
    {
        public required string Token { get; set; }
    }

    public class QrLoginRequest
    {
        public required string Username { get; set; }
        public required string Token { get; set; }
    }

    public class DirectQrLoginRequest
    {
        public required string Token { get; set; }
        public required string DeviceType { get; set; }
        public bool IsDesktopLogin { get; set; }
    }

    public class TokenRequest
    {
        public required string GrantType { get; set; }
        public string? Code { get; set; }
        public string? RefreshToken { get; set; }
        public required string ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? RedirectUri { get; set; }
    }

    public class AuthorizeCallbackRequest
    {
        public required string RequestId { get; set; }
        public required string Username { get; set; }
        public required string Password { get; set; }
    }

    public class RegisterClientRequest
    {
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
        public required string DisplayName { get; set; }
        public required string[] RedirectUris { get; set; }
        public required string[] PostLogoutRedirectUris { get; set; }
        public required string[] AllowedScopes { get; set; }
        public bool RequireConsent { get; set; } = false;
    }

    public class UpdateClientRequest
    {
        public string? ClientSecret { get; set; }
        public string? DisplayName { get; set; }
        public string[]? RedirectUris { get; set; }
        public string[]? PostLogoutRedirectUris { get; set; }
        public string[]? AllowedScopes { get; set; }
        public bool? RequireConsent { get; set; }
    }

    #endregion

    #region Response Models

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string>? Errors { get; set; }
        public T? Data { get; set; }
    }

    public class UserDto
    {
        public uint Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public int Role { get; set; }
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class RegisterResponse
    {
        public UserDto User { get; set; } = new UserDto();
    }

    public class TwoFactorResponse
    {
        public bool RequiresTwoFactor { get; set; }
        public string TwoFactorType { get; set; } = string.Empty;
        public string TempToken { get; set; } = string.Empty;
    }

    public class WebAuthnTwoFactorResponse : TwoFactorResponse
    {
        public AssertionOptions? Options { get; set; }
    }

    public class TotpSetupResponse
    {
        public string SecretKey { get; set; } = string.Empty;
        public string QrCodeUri { get; set; } = string.Empty;
    }

    public class VerifyTotpResponse
    {
        public bool Enabled { get; set; }
    }

    public class DisableTotpResponse
    {
        public bool Disabled { get; set; }
    }

    public class ValidateTotpResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class WebAuthnRegisterOptionsResponse
    {
        public CredentialCreateOptions Options { get; set; } = new CredentialCreateOptions();
    }

    public class WebAuthnRegisterCompleteResponse
    {
        public bool Registered { get; set; }
    }

    public class WebAuthnLoginOptionsResponse
    {
        public AssertionOptions Options { get; set; } = new AssertionOptions();
    }

    public class WebAuthnLoginCompleteResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class WebAuthnValidateResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class WebAuthnCredentialDto
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class WebAuthnCredentialsResponse
    {
        public List<WebAuthnCredentialDto> Credentials { get; set; } = new List<WebAuthnCredentialDto>();
    }

    public class WebAuthnRemoveCredentialResponse
    {
        public bool Removed { get; set; }
    }

    public class MagicLinkResponse
    {
        public bool Sent { get; set; }
        public string Email { get; set; } = string.Empty;
    }

    public class ValidateMagicLinkResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class QrCodeResponse
    {
        public string QrCode { get; set; } = string.Empty;
        public string? RawData { get; set; }
    }

    public class QrLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class DirectQrCodeResponse
    {
        public string QrCode { get; set; } = string.Empty;
        public string? RawData { get; set; }
    }

    public class DirectQrLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class CheckQrLoginResponse
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
    }

    public class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "Bearer";
        public int ExpiresIn { get; set; }
        public string? RefreshToken { get; set; }
        public string? IdToken { get; set; }
        public string Scope { get; set; } = string.Empty;
    }

    public class UserInfoResponse
    {
        public string Sub { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PreferredUsername { get; set; } = string.Empty;
        public string? Email { get; set; }
        public bool EmailVerified { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneNumberVerified { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
    }

    public class RegisterClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class UpdateClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class DeleteClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public bool Deleted { get; set; }
    }

    public class ClientDto
    {
        public string? ClientId { get; set; }
        public string? DisplayName { get; set; }
    }

    public class GetClientsResponse
    {
        public List<ClientDto> Clients { get; set; } = new List<ClientDto>();
    }

    public class GetClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string[] RedirectUris { get; set; } = Array.Empty<string>();
        public string[] PostLogoutRedirectUris { get; set; } = Array.Empty<string>();
        public string[] AllowedScopes { get; set; } = Array.Empty<string>();
        public bool RequireConsent { get; set; }
    }

    #endregion

    #region Helper Classes

    public class OpenIdConnectRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string ResponseType { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? Nonce { get; set; }
    }

    public class AuthorizationCodeData
    {
        public uint UserId { get; set; }
        public string[] Scopes { get; set; } = Array.Empty<string>();
        public string RedirectUri { get; set; } = string.Empty;
    }

    #endregion
}












