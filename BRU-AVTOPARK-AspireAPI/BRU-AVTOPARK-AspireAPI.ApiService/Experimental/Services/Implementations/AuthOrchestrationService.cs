using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Models.ViewModels;
using BRU_AVTOPARK.Services.Interfaces;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Serilog;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Orchestrates authentication flows. All business logic that was embedded in
/// the original AuthController action methods now lives here, making it
/// independently testable and reusable across controllers.
/// </summary>
public class AuthOrchestrationService : IAuthOrchestrationService
{
    private readonly IAuthenticationService _authService;
    private readonly IUserService _userService;
    private readonly ISpacetimeDBService _spacetimeService;
    private readonly ITokenService _tokenService;
    private readonly ITwoFactorService _twoFactorService;
    private readonly ISettingsService _settingsService;
    private readonly ITotpService _totpService;
    private readonly IWebAuthnService _webAuthnService;
    private readonly IMagicLinkService _magicLinkService;
    private readonly IQRAuthenticationService _qrAuthService;
    private readonly IProfileService _profileService;
    private readonly IOpenIdConnectService _openIdConnectService;
    private readonly ILogger<AuthOrchestrationService> _logger;

    public AuthOrchestrationService(
        IAuthenticationService authService,
        IUserService userService,
        ISpacetimeDBService spacetimeService,
        ITokenService tokenService,
        ITwoFactorService twoFactorService,
        ISettingsService settingsService,
        ITotpService totpService,
        IWebAuthnService webAuthnService,
        IMagicLinkService magicLinkService,
        IQRAuthenticationService qrAuthService,
        IProfileService profileService,
        IOpenIdConnectService openIdConnectService,
        ILogger<AuthOrchestrationService> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _twoFactorService = twoFactorService ?? throw new ArgumentNullException(nameof(twoFactorService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _totpService = totpService ?? throw new ArgumentNullException(nameof(totpService));
        _webAuthnService = webAuthnService ?? throw new ArgumentNullException(nameof(webAuthnService));
        _magicLinkService = magicLinkService ?? throw new ArgumentNullException(nameof(magicLinkService));
        _qrAuthService = qrAuthService ?? throw new ArgumentNullException(nameof(qrAuthService));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _openIdConnectService = openIdConnectService ?? throw new ArgumentNullException(nameof(openIdConnectService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    [Obsolete("Use LoginAsync instead. This method is kept for backward compatibility only.")]
    public async Task<AuthenticationResult?> AuthenticateAsync(string username, string password)
    {
        _logger.LogInformation("Authentication attempt for user: {Username} (DEPRECATED - use LoginAsync)", username);

        // Delegate to LoginAsync for the actual authentication logic
        var loginResult = await LoginAsync(username, password);

        if (!loginResult.Success)
        {
            _logger.LogWarning("Authentication failed for user: {Username}", username);
            return null;
        }

        // If 2FA is required, return partial result without token
        if (loginResult.RequiresTwoFactor)
        {
            _logger.LogInformation("2FA required for user: {Username}", username);
            
            // Return authentication result without token (2FA required)
            return new AuthenticationResult
            {
                UserId = "pending-2fa", // Placeholder since we don't have full user data yet
                Username = username,
                Email = null,
                PhoneNumber = null,
                EmailConfirmed = null,
                PhoneNumberConfirmed = null,
                TotpEnabled = loginResult.TotpEnabled,
                WebAuthnEnabled = loginResult.WebAuthnEnabled,
                PrimaryRole = 0,
                Roles = new List<string>()
            };
        }

        // Convert LoginResult to AuthenticationResult for backward compatibility
        return new AuthenticationResult
        {
            UserId = loginResult.User?.Id.ToString() ?? "",
            Username = loginResult.User?.Username ?? username,
            Email = loginResult.User?.Email,
            PhoneNumber = loginResult.User?.PhoneNumber,
            EmailConfirmed = null, // Not available in LoginResult
            PhoneNumberConfirmed = null, // Not available in LoginResult
            TotpEnabled = loginResult.TotpEnabled,
            WebAuthnEnabled = loginResult.WebAuthnEnabled,
            PrimaryRole = loginResult.User?.Role ?? 0,
            Roles = new List<string>() // Not available in LoginResult
        };
    }

    /// <inheritdoc />
    public async Task<RegisterResult> RegisterAsync(
        string username, string password, int role,
        string? email, string? phoneNumber, string? adminIdentity)
    {
        _logger.LogInformation("Registration attempt for user: {Username}", username);

        try
        {
            var success = await _authService.RegisterAsync(
                username, password, role, email, phoneNumber,
                adminIdentity != null ? SpacetimeDB.Identity.From(Convert.FromBase64String(adminIdentity)) : null,
                null);

            if (!success)
                return new RegisterResult(false, "Failed to register user");

            var newUser = await _userService.GetUserByLoginAsync(username);
            if (newUser is null)
                return new RegisterResult(false, "User was created but could not be retrieved");

            return new RegisterResult(true, User: new UserDto
            {
                Id = newUser.LegacyUserId,
                Username = newUser.Login,
                Email = newUser.Email,
                PhoneNumber = newUser.PhoneNumber,
                Role = _authService.GetUserRole(newUser.UserId)
            });
        }
        catch (Exception ex) when (ex.Message?.Contains("Unauthorized") == true)
        {
            _logger.LogWarning("Role assignment failed: {Error}", ex.Message);
            var fallback = await _userService.GetUserByLoginAsync(username);
            if (fallback is not null)
            {
                return new RegisterResult(true,
                    "User created with default role (requested role assignment failed).",
                    new UserDto
                    {
                        Id = fallback.LegacyUserId,
                        Username = fallback.Login,
                        Email = fallback.Email,
                        PhoneNumber = fallback.PhoneNumber,
                        Role = _authService.GetUserRole(fallback.UserId)
                    });
            }
            return new RegisterResult(false, "Registration failed during role assignment");
        }
    }

    /// <inheritdoc />
    public async Task<ClaimResult> ClaimAccountAsync(
        string username, string password, bool generateNewIdentity)
    {
        _logger.LogInformation("Account claim attempt for: {Username}", username);

        // Use UserService instead of direct database access
        var user = await _userService.GetUserByLoginAsync(username);

        if (user is null)
            return new ClaimResult(false, "Account not found. Please check your username.");

        string? newIdentity = null;
        if (generateNewIdentity)
        {
            // Identity generation would be delegated to the SpacetimeDB service
            newIdentity = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        }

        var conn = _spacetimeService.GetConnection();
        conn.Reducers.ClaimUserAccount(username, password, newIdentity);
        _logger.LogInformation("Account claim processed for: {Username}", username);

        return new ClaimResult(true);
    }

    /// <inheritdoc />
    public bool IsAdmin(ClaimsPrincipal? user, string? bearerToken)
    {
        // Check ASP.NET Core identity first
        if (user?.Identity?.IsAuthenticated == true)
        {
            if (user.IsInRole("Administrator")) return true;

            var primaryRole = user.FindFirst("primary_role");
            if (primaryRole?.Value == "1") return true;

            var roleClaims = user.Claims.Where(c =>
                c.Type == ClaimTypes.Role || c.Type == "role");
            if (roleClaims.Any(c => c.Value is "Administrator" or "1"))
                return true;
        }

        // Fallback: parse bearer token directly
        if (string.IsNullOrEmpty(bearerToken)) return false;

        var payload = _tokenService.ReadTokenPayload(bearerToken);
        if (payload is null) return false;

        return payload.PrimaryRole == 1
               || payload.Roles.Any(r => r is "Administrator" or "1");
    }

    /// <inheritdoc />
    public bool HasPermission(ClaimsPrincipal? user, string? bearerToken, string permissionName)
    {
        if (user?.Identity?.IsAuthenticated == true)
        {
            if (user.Claims.Any(c => c.Type == "permission" && c.Value == permissionName))
                return true;
        }

        if (string.IsNullOrEmpty(bearerToken)) return false;

        var payload = _tokenService.ReadTokenPayload(bearerToken);
        return payload?.Permissions.Contains(permissionName) == true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Priority 1 Orchestration Methods (Critical - Direct DB Access Elimination)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        _logger.LogInformation("Login orchestration started for user: {Username}", username);

        try
        {
            // Step 1: Authenticate user credentials
            var user = await _authService.AuthenticateAsync(username, password);
            if (user is null)
            {
                _logger.LogWarning("Authentication failed for user: {Username}", username);
                return LoginResult.Failed("Invalid username or password");
            }

            // Step 2: Get or create user settings
            var settings = await _settingsService.GetOrCreateUserSettingsAsync(user.UserId);

            // Step 3: Check if 2FA is required
            bool requiresTwoFactor = settings.TotpEnabled || settings.WebAuthnEnabled;

            if (requiresTwoFactor)
            {
                _logger.LogInformation("2FA required for user: {Username}", username);

                // Step 4: Create temporary token for 2FA validation
                var tempToken = await _twoFactorService.CreateTempTokenAsync(user.UserId, "login");

                // Step 5: Handle WebAuthn-specific requirements
                if (settings.WebAuthnEnabled)
                {
                    // Check if user has any active WebAuthn credentials
                    var conn = _spacetimeService.GetConnection();
                    var credentials = conn.Db.WebAuthnCredential.Iter()
                        .Where(c => c.UserId.Equals(user.UserId) && c.IsActive)
                        .ToList();

                    if (!credentials.Any())
                    {
                        _logger.LogWarning("No WebAuthn credentials found for user: {Username}", username);
                        return LoginResult.Failed("No WebAuthn credentials found");
                    }

                    // Generate assertion options for WebAuthn challenge
                    var (success, options, errorMessage) = await _webAuthnService.GetAssertionOptionsAsync(user.Login);
                    if (!success || options == null)
                    {
                        _logger.LogWarning("Failed to create assertion options for user: {Username}", username);
                        return LoginResult.Failed(errorMessage ?? "Failed to create assertion options");
                    }

                    return LoginResult.RequiresTwoFactorAuth(tempToken, settings.TotpEnabled, settings.WebAuthnEnabled, options);
                }

                return LoginResult.RequiresTwoFactorAuth(tempToken, settings.TotpEnabled, settings.WebAuthnEnabled);
            }

            // Step 6: Generate JWT token and user DTO
            var (jwtToken, userDto, claims) = await GenerateAuthTokenAsync(user);

            _logger.LogInformation("Login successful for user: {Username}", username);
            return LoginResult.Successful(jwtToken, userDto, claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login orchestration for user: {Username}", username);
            return LoginResult.Failed("An error occurred during login");
        }
    }

    /// <inheritdoc />
    public async Task<TotpValidationResult> ValidateTotpAsync(string tempToken, string code)
    {
        _logger.LogInformation("TOTP validation orchestration started");

        try
        {
            // Step 1: Validate temporary token
            var (isValid, userId) = await _twoFactorService.ValidateTempTokenAsync(tempToken);
            if (!isValid || userId is null)
            {
                _logger.LogWarning("Invalid or expired temporary token");
                return TotpValidationResult.Failed("Invalid or expired temporary token");
            }

            // Step 2: Validate TOTP code
            var (success, errorMessage) = await _totpService.ValidateTotpAsync(userId.Value, code);
            if (!success)
            {
                _logger.LogWarning("TOTP validation failed for user: {UserId}", userId);
                return TotpValidationResult.Failed(errorMessage ?? "Invalid TOTP code");
            }

            // Step 3: Mark temporary token as used
            await _twoFactorService.MarkTokenAsUsedAsync(tempToken);

            // Step 4: Get user profile by Identity
            var conn = _spacetimeService.GetConnection();
            var user = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.UserId.Equals(userId.Value));
            if (user is null)
            {
                _logger.LogError("User not found after successful TOTP validation: {UserId}", userId);
                return TotpValidationResult.Failed("User not found");
            }

            // Step 5: Generate JWT token and user DTO
            var (jwtToken, userDto, claims) = await GenerateAuthTokenAsync(user);

            _logger.LogInformation("TOTP validation successful for user: {UserId}", userId);
            return TotpValidationResult.Successful(jwtToken, userDto, claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during TOTP validation orchestration");
            return TotpValidationResult.Failed("An error occurred during TOTP validation");
        }
    }

    /// <inheritdoc />
    public async Task<WebAuthnValidationResult> ValidateWebAuthnAsync(string tempToken, AuthenticatorAssertionRawResponse assertionResponse)
    {
        _logger.LogInformation("WebAuthn validation orchestration started");

        try
        {
            // Step 1: Validate temporary token
            var (isValid, userId) = await _twoFactorService.ValidateTempTokenAsync(tempToken);
            if (!isValid || userId is null)
            {
                _logger.LogWarning("Invalid or expired temporary token");
                return WebAuthnValidationResult.Failed("Invalid or expired temporary token");
            }

            // Step 2: Get user profile by Identity to get username for WebAuthn validation
            var conn = _spacetimeService.GetConnection();
            var user = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.UserId.Equals(userId.Value));
            if (user is null)
            {
                _logger.LogError("User not found for WebAuthn validation: {UserId}", userId);
                return WebAuthnValidationResult.Failed("User not found");
            }

            // Step 3: Validate WebAuthn assertion
            var (success, validatedUser, errorMessage) = await _webAuthnService.CompleteAssertionAsync(user.Login, assertionResponse);
            
            if (!success || validatedUser is null)
            {
                _logger.LogWarning("WebAuthn validation failed for user: {Username}, Error: {ErrorMessage}", user.Login, errorMessage);
                return WebAuthnValidationResult.Failed(errorMessage ?? "WebAuthn authentication failed");
            }

            // Step 4: Mark temporary token as used
            await _twoFactorService.MarkTokenAsUsedAsync(tempToken);

            // Step 5: Generate JWT token and user DTO
            var (jwtToken, userDto, claims) = await GenerateAuthTokenAsync(validatedUser);

            _logger.LogInformation("WebAuthn validation successful for user: {UserId}", userId);
            return WebAuthnValidationResult.Successful(jwtToken, userDto, claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn validation orchestration");
            return WebAuthnValidationResult.Failed("An error occurred during WebAuthn validation");
        }
    }

    /// <inheritdoc />
    public async Task<MagicLinkValidationResult> ValidateMagicLinkAsync(string token)
    {
        _logger.LogInformation("Magic link validation orchestration started");

        try
        {
            // Step 1: Validate magic link token
            var (success, user, errorMessage) = await _magicLinkService.ValidateMagicLinkAsync(token);
            if (!success || user is null)
            {
                _logger.LogWarning("Magic link validation failed: {ErrorMessage}", errorMessage);
                return MagicLinkValidationResult.Failed(errorMessage ?? "Invalid or expired magic link");
            }

            // Step 2: Mark magic link as used
            await _magicLinkService.MarkMagicLinkAsUsedAsync(token);

            // Step 3: Generate JWT token and user DTO
            var (jwtToken, userDto, claims) = await GenerateAuthTokenAsync(user);

            _logger.LogInformation("Magic link validation successful for user: {UserId}", user.UserId);
            return MagicLinkValidationResult.Successful(jwtToken, userDto, claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during magic link validation orchestration");
            return MagicLinkValidationResult.Failed("An error occurred during magic link validation");
        }
    }

    /// <inheritdoc />
    public async Task<ProfileViewModel?> GetProfileAsync(string userId, string? token)
    {
        _logger.LogInformation("Profile retrieval orchestration started for user: {UserId}", userId);

        try
        {
            // Delegate to existing ProfileService
            var profile = await _profileService.GetProfileAsync(userId, token);

            if (profile is null)
            {
                _logger.LogWarning("Profile not found for user: {UserId}", userId);
            }
            else
            {
                _logger.LogInformation("Profile retrieved successfully for user: {UserId}", userId);
            }

            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during profile retrieval orchestration for user: {UserId}", userId);
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Priority 2 Orchestration Methods (High - Complete TOTP/WebAuthn Flows)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<TotpSetupResult> SetupTotpAsync(Identity userId, string username)
    {
        _logger.LogInformation("TOTP setup orchestration started for user: {Username}", username);

        try
        {
            // Step 1: Call TotpService to generate secret key and QR code URI
            var (success, secretKey, qrCodeUri, errorMessage) = await _totpService.SetupTotpAsync(userId, username);

            if (!success || secretKey is null || qrCodeUri is null)
            {
                _logger.LogWarning("TOTP setup failed for user: {Username} - {ErrorMessage}", username, errorMessage);
                return TotpSetupResult.Failed(errorMessage ?? "Failed to setup TOTP");
            }

            _logger.LogInformation("TOTP setup successful for user: {Username}", username);
            return TotpSetupResult.Successful(secretKey, qrCodeUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during TOTP setup orchestration for user: {Username}", username);
            return TotpSetupResult.Failed("An error occurred during TOTP setup");
        }
    }

    /// <inheritdoc />
    public async Task<TotpEnableResult> EnableTotpAsync(Identity userId, string username, string code, string secretKey)
    {
        _logger.LogInformation("TOTP enable orchestration started for user: {Username}", username);

        try
        {
            // Step 1: Enable TOTP via TotpService (this verifies the code and stores the secret)
            var (success, errorMessage) = await _totpService.EnableTotpAsync(userId, code, secretKey);

            if (!success)
            {
                _logger.LogWarning("TOTP enable failed for user: {Username} - {ErrorMessage}", username, errorMessage);
                return TotpEnableResult.Failed(errorMessage ?? "Failed to enable TOTP");
            }

            // Step 2: Update user settings to reflect TOTP enabled
            await _settingsService.EnableTotpAsync(userId);

            _logger.LogInformation("TOTP enabled successfully for user: {Username}", username);
            return TotpEnableResult.Successful();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during TOTP enable orchestration for user: {Username}", username);
            return TotpEnableResult.Failed("An error occurred while enabling TOTP");
        }
    }

    /// <inheritdoc />
    public async Task<TotpDisableResult> DisableTotpAsync(Identity userId)
    {
        _logger.LogInformation("TOTP disable orchestration started for user ID: {UserId}", userId);

        try
        {
            // Step 1: Disable TOTP via TotpService
            var (success, errorMessage) = await _totpService.DisableTotpAsync(userId);

            if (!success)
            {
                _logger.LogWarning("TOTP disable failed for user ID: {UserId} - {ErrorMessage}", userId, errorMessage);
                return TotpDisableResult.Failed(errorMessage ?? "Failed to disable TOTP");
            }

            // Step 2: Update user settings to reflect TOTP disabled
            await _settingsService.DisableTotpAsync(userId);

            _logger.LogInformation("TOTP disabled successfully for user ID: {UserId}", userId);
            return TotpDisableResult.Successful();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during TOTP disable orchestration for user ID: {UserId}", userId);
            return TotpDisableResult.Failed("An error occurred while disabling TOTP");
        }
    }

    /// <inheritdoc />
    public async Task<WebAuthnRegisterResult> RegisterWebAuthnAsync(Identity userId, string username, AuthenticatorAttestationRawResponse attestationResponse)
    {
        _logger.LogInformation("WebAuthn registration orchestration started for user: {Username}", username);

        try
        {
            // Step 1: Complete WebAuthn registration via WebAuthnService
            var (success, errorMessage) = await _webAuthnService.CompleteRegistrationAsync(userId, username, attestationResponse);

            if (!success)
            {
                _logger.LogWarning("WebAuthn registration failed for user: {Username} - {ErrorMessage}", username, errorMessage);
                return WebAuthnRegisterResult.Failed(errorMessage ?? "Failed to register WebAuthn credential");
            }

            // Step 2: Update user settings to reflect WebAuthn enabled (already done in WebAuthnService)
            // Note: WebAuthnService.CompleteRegistrationAsync already calls EnableWebAuthn reducer
            // So we don't need to call SettingsService.EnableWebAuthnAsync here

            _logger.LogInformation("WebAuthn registration successful for user: {Username}", username);
            return WebAuthnRegisterResult.Successful();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn registration orchestration for user: {Username}", username);
            return WebAuthnRegisterResult.Failed("An error occurred during WebAuthn registration");
        }
    }

    /// <inheritdoc />
    public async Task<WebAuthnCredentialsResult> GetWebAuthnCredentialsAsync(Identity userId)
    {
        _logger.LogInformation("WebAuthn credentials retrieval orchestration started for user ID: {UserId}", userId);

        try
        {
            // Step 1: Get credentials via WebAuthnService
            var credentials = await _webAuthnService.GetUserCredentialsAsync(userId);

            // Step 2: Convert to DTOs
            var credentialDtos = credentials.Select(c => new WebAuthnCredentialDto
            {
                Id = Convert.ToBase64String(c.CredentialId.ToArray()),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)c.CreatedAt).DateTime
            }).ToList();

            _logger.LogInformation("Retrieved {Count} WebAuthn credentials for user ID: {UserId}", credentialDtos.Count, userId);
            return WebAuthnCredentialsResult.Successful(credentialDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn credentials retrieval orchestration for user ID: {UserId}", userId);
            return WebAuthnCredentialsResult.Failed("An error occurred while retrieving WebAuthn credentials");
        }
    }

    /// <inheritdoc />
    public async Task<WebAuthnRemoveResult> RemoveWebAuthnCredentialAsync(Identity userId, string credentialId)
    {
        _logger.LogInformation("WebAuthn credential removal orchestration started for user ID: {UserId}", userId);

        try
        {
            // Step 1: Remove credential via WebAuthnService
            var (success, errorMessage) = await _webAuthnService.RemoveCredentialAsync(userId, credentialId);

            if (!success)
            {
                _logger.LogWarning("WebAuthn credential removal failed for user ID: {UserId} - {ErrorMessage}", userId, errorMessage);
                return WebAuthnRemoveResult.Failed(errorMessage ?? "Failed to remove WebAuthn credential");
            }

            // Step 2: Check if this was the last credential and update settings if needed
            // Note: WebAuthnService.RemoveCredentialAsync already handles disabling WebAuthn
            // if this was the last credential, so we don't need to call SettingsService here

            _logger.LogInformation("WebAuthn credential removed successfully for user ID: {UserId}", userId);
            return WebAuthnRemoveResult.Successful();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn credential removal orchestration for user ID: {UserId}", userId);
            return WebAuthnRemoveResult.Failed("An error occurred while removing WebAuthn credential");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Priority 3 Orchestration Methods (Medium - OAuth/OIDC Flows)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<OAuthAuthorizeResult> AuthorizeOAuthAsync(string clientId, string redirectUri, string scope, Identity userId)
    {
        _logger.LogInformation("OAuth authorization orchestration started for ClientId: {ClientId}, UserId: {UserId}", clientId, userId);

        try
        {
            // Step 1: Validate client application
            var (clientSuccess, application, clientError) = await _openIdConnectService.GetApplicationByClientIdAsync(clientId);
            if (!clientSuccess || application == null)
            {
                _logger.LogWarning("OAuth authorization failed - invalid client: {ClientId}, Error: {Error}", clientId, clientError);
                return OAuthAuthorizeResult.Failed(clientError ?? "Invalid client application");
            }

            // Step 2: Get user profile from SpacetimeDB
            var conn = _spacetimeService.GetConnection();
            var user = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);
            if (user == null)
            {
                _logger.LogWarning("OAuth authorization failed - user not found: {UserId}", userId);
                return OAuthAuthorizeResult.Failed("User not found or inactive");
            }

            // Step 3: Parse requested scopes
            var requestedScopes = scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            _logger.LogInformation("OAuth authorization - requested scopes: {Scopes}", string.Join(", ", requestedScopes));

            // Step 4: Create identity from user
            var (identitySuccess, identity, identityError) = await _openIdConnectService.CreateIdentityFromUserAsync(user, requestedScopes);
            if (!identitySuccess || identity == null)
            {
                _logger.LogWarning("OAuth authorization failed - could not create identity: {Error}", identityError);
                return OAuthAuthorizeResult.Failed(identityError ?? "Failed to create user identity");
            }

            // Step 5: Get resources for scopes
            var (resourcesSuccess, resources, resourcesError) = await _openIdConnectService.GetResourcesAsync(requestedScopes);
            if (resourcesSuccess && resources != null)
            {
                identity.SetResources(resources);
                _logger.LogInformation("OAuth authorization - set resources: {Resources}", string.Join(", ", resources));
            }

            // NOTE: Authorization storage is PERMANENTLY DISABLED (.DisableAuthorizationStorage())
            // OpenIddict handles PKCE validation internally via encrypted authorization code payload
            // No need to create authorization entities - OpenIddict creates ad-hoc authorizations automatically

            _logger.LogInformation("OAuth authorization successful for user: {Username}, ClientId: {ClientId}", user.Login, clientId);
            
            // Return success - actual authorization code generation is handled by OpenIddict middleware
            // This orchestration method validates the request and prepares the identity
            return OAuthAuthorizeResult.Successful("authorization_prepared", redirectUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth authorization orchestration for ClientId: {ClientId}", clientId);
            return OAuthAuthorizeResult.Failed("An error occurred during OAuth authorization");
        }
    }

    /// <inheritdoc />
    public async Task<OAuthTokenResult> ExchangeTokenAsync(string code, string clientId, string clientSecret)
    {
        _logger.LogInformation("OAuth token exchange orchestration started for ClientId: {ClientId}", clientId);

        try
        {
            // Step 1: Validate client application
            var (clientSuccess, application, clientError) = await _openIdConnectService.GetApplicationByClientIdAsync(clientId);
            if (!clientSuccess || application == null)
            {
                _logger.LogWarning("OAuth token exchange failed - invalid client: {ClientId}, Error: {Error}", clientId, clientError);
                return OAuthTokenResult.Failed(clientError ?? "Invalid client application");
            }

            // NOTE: This orchestration method validates the client but does NOT perform the actual token exchange.
            // The token exchange MUST be handled by OpenIddict middleware in the controller because it requires:
            // 1. HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
            // 2. Access to the OpenIddict request context
            // 3. PKCE validation (code_verifier against code_challenge from encrypted authorization code)
            // 4. Authorization code validation and expiration checking
            //
            // The controller will:
            // 1. Call HttpContext.AuthenticateAsync to validate the authorization code and get the principal
            // 2. Extract the userId from the principal
            // 3. Call BuildOAuthTokenIdentityAsync (below) to build fresh claims
            // 4. Call SignIn() with the identity to generate tokens
            //
            // This method exists for consistency and future extensibility (e.g., custom validation logic).

            _logger.LogInformation("OAuth token exchange client validation successful for ClientId: {ClientId}", clientId);
            return OAuthTokenResult.Successful("token_exchange_validated", null, null, 3600, "Bearer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth token exchange orchestration for ClientId: {ClientId}", clientId);
            return OAuthTokenResult.Failed("An error occurred during OAuth token exchange");
        }
    }

    /// <summary>
    /// Builds a fresh ClaimsIdentity for OAuth token exchange with all user claims, roles, and permissions.
    /// This method contains the business logic from AuthController.Exchange() for building the token identity.
    /// </summary>
    /// <param name="userId">The user's SpacetimeDB Identity</param>
    /// <param name="scopes">The OAuth scopes from the original authorization</param>
    /// <param name="resources">The OAuth resources from the original authorization</param>
    /// <returns>ClaimsIdentity ready for OpenIddict SignIn, or null if user not found</returns>
    public async Task<ClaimsIdentity?> BuildOAuthTokenIdentityAsync(Identity userId, IEnumerable<string> scopes, IEnumerable<string> resources)
    {
        _logger.LogInformation("Building OAuth token identity for user: {UserId}", userId);

        try
        {
            // Step 1: Verify the user still exists and is active
            var conn = _spacetimeService.GetConnection();
            var user = conn.Db.UserProfile.Iter()
                .FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);

            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found or inactive during token exchange", userId);
                return null;
            }

            // Step 2: Create a new identity for the access token with fresh claims
            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            // Step 3: Add standard OpenID Connect claims
            identity.AddClaim(new Claim(Claims.Subject, user.UserId.ToString()));
            identity.AddClaim(new Claim(Claims.Name, user.Login));
            
            if (!string.IsNullOrEmpty(user.Email))
            {
                identity.AddClaim(new Claim(Claims.Email, user.Email));
                identity.AddClaim(new Claim(Claims.EmailVerified, user.EmailConfirmed?.ToString().ToLower() ?? "false"));
            }
            
            if (!string.IsNullOrEmpty(user.PhoneNumber))
            {
                identity.AddClaim(new Claim(Claims.PhoneNumber, user.PhoneNumber));
                identity.AddClaim(new Claim(Claims.PhoneNumberVerified, user.PhoneNumberConfirmed?.ToString().ToLower() ?? "false"));
            }

            // Step 4: Add role claims
            var roles = conn.Db.UserRole.Iter()
                .Where(ur => ur.UserId.Equals(user.UserId))
                .Join(conn.Db.Role.Iter(), 
                      ur => ur.RoleId, 
                      r => r.RoleId, 
                      (ur, r) => r.Name)
                .ToList();
            
            foreach (var role in roles)
            {
                identity.AddClaim(new Claim(Claims.Role, role));
            }

            _logger.LogInformation("Added {RoleCount} roles to token for user {Username}", roles.Count, user.Login);

            // Step 5: Add permission claims for authorization
            var userRoleIds = conn.Db.UserRole.Iter()
                .Where(ur => ur.UserId.Equals(user.UserId))
                .Select(ur => ur.RoleId)
                .ToList();
            
            _logger.LogInformation("User {UserId} has {RoleCount} roles: {RoleIds}", 
                user.UserId, userRoleIds.Count, string.Join(", ", userRoleIds));
            
            var permissions = conn.Db.RolePermission.Iter()
                .Where(rp => userRoleIds.Contains(rp.RoleId))
                .Join(conn.Db.Permission.Iter(),
                      rp => rp.PermissionId,
                      p => p.PermissionId,
                      (rp, p) => p.Name)
                .Distinct()
                .ToList();
            
            _logger.LogInformation("Found {PermissionCount} permissions for user {Username}: {Permissions}", 
                permissions.Count, user.Login, string.Join(", ", permissions));
            
            foreach (var permission in permissions)
            {
                identity.AddClaim(new Claim("permission", permission));
            }
            
            _logger.LogInformation("Added {PermissionCount} permissions to token for user {Username}", permissions.Count, user.Login);
            
            // Step 6: Add primary role for admin checks
            var primaryRole = conn.Db.UserRole.Iter()
                .Where(ur => ur.UserId.Equals(user.UserId))
                .OrderBy(ur => ur.RoleId)
                .FirstOrDefault();
            
            if (primaryRole != null)
            {
                identity.AddClaim(new Claim("primary_role", primaryRole.RoleId.ToString()));
                _logger.LogInformation("Added primary_role claim: {RoleId}", primaryRole.RoleId);
            }
            
            // Step 7: Add SpacetimeDB identity for database operations
            identity.AddClaim(new Claim("identity", user.UserId.ToString()));
            
            // Step 8: Add XUID if available
            if (user.Xuid.HasValue)
            {
                identity.AddClaim(new Claim("xuid", user.Xuid.Value.ToString()));
            }
            else
            {
                identity.AddClaim(new Claim("xuid", user.LegacyUserId.ToString()));
            }

            // Step 9: Set scopes and resources from the original authorization
            // NOTE: Authorization storage is disabled, so no authorization ID to copy
            identity.SetScopes(scopes);
            identity.SetResources(resources);

            // Step 10: Set claim destinations
            foreach (var claim in identity.Claims)
            {
                claim.SetDestinations(_openIdConnectService.GetDestinations(claim));
            }

            _logger.LogInformation("OAuth token identity built successfully for user {Username}", user.Login);
            return identity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building OAuth token identity for user: {UserId}", userId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<OAuthUserInfoResult> GetUserInfoAsync(string username)
    {
        _logger.LogInformation("OAuth userinfo orchestration started for user: {Username}", username);

        try
        {
            // Step 1: Get user profile from SpacetimeDB
            var conn = _spacetimeService.GetConnection();
            var user = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == username && u.IsActive);
            if (user == null)
            {
                _logger.LogWarning("OAuth userinfo failed - user not found: {Username}", username);
                return OAuthUserInfoResult.Failed("User not found or inactive");
            }

            // Step 2: Build claims dictionary
            var claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = user.UserId.ToString(),
                ["name"] = user.Login,
                ["preferred_username"] = user.Login
            };

            // Step 3: Add email claims if available
            if (!string.IsNullOrEmpty(user.Email))
            {
                claims["email"] = user.Email;
                claims["email_verified"] = user.EmailConfirmed ?? false;
            }

            // Step 4: Add phone claims if available
            if (!string.IsNullOrEmpty(user.PhoneNumber))
            {
                claims["phone_number"] = user.PhoneNumber;
                claims["phone_number_verified"] = user.PhoneNumberConfirmed ?? false;
            }

            // Step 5: Add roles (if scope includes 'roles')
            var roles = conn.Db.UserRole.Iter()
                .Where(ur => ur.UserId.Equals(user.UserId))
                .Join(conn.Db.Role.Iter(), 
                      ur => ur.RoleId, 
                      r => r.RoleId, 
                      (ur, r) => r.Name)
                .ToList();
            
            if (roles.Any())
            {
                claims["role"] = roles;
            }

            _logger.LogInformation("OAuth userinfo successful for user: {Username}, returned {ClaimCount} claims", username, claims.Count);
            return OAuthUserInfoResult.Successful(claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth userinfo orchestration for user: {Username}", username);
            return OAuthUserInfoResult.Failed("An error occurred while retrieving user information");
        }
    }

    /// <inheritdoc />
    public async Task<OAuthClientResult> RegisterOAuthClientAsync(string clientId, string clientSecret, string displayName, 
        string[] redirectUris, string[] postLogoutRedirectUris, string[] allowedScopes, bool requireConsent)
    {
        _logger.LogInformation("OAuth client registration orchestration started for ClientId: {ClientId}", clientId);

        try
        {
            // Step 1: Validate input parameters
            if (string.IsNullOrWhiteSpace(clientId))
            {
                _logger.LogWarning("OAuth client registration failed - clientId is required");
                return OAuthClientResult.Failed("Client ID is required");
            }

            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                _logger.LogWarning("OAuth client registration failed - clientSecret is required");
                return OAuthClientResult.Failed("Client secret is required");
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                _logger.LogWarning("OAuth client registration failed - displayName is required");
                return OAuthClientResult.Failed("Display name is required");
            }

            if (redirectUris == null || redirectUris.Length == 0)
            {
                _logger.LogWarning("OAuth client registration failed - at least one redirect URI is required");
                return OAuthClientResult.Failed("At least one redirect URI is required");
            }

            if (allowedScopes == null || allowedScopes.Length == 0)
            {
                _logger.LogWarning("OAuth client registration failed - at least one scope is required");
                return OAuthClientResult.Failed("At least one scope is required");
            }

            // Step 2: Register client via OpenIdConnectService
            var (success, errorMessage) = await _openIdConnectService.RegisterClientApplicationAsync(
                clientId,
                clientSecret,
                displayName,
                redirectUris,
                postLogoutRedirectUris ?? Array.Empty<string>(),
                allowedScopes,
                requireConsent
            );

            if (!success)
            {
                _logger.LogWarning("OAuth client registration failed for ClientId: {ClientId}, Error: {Error}", clientId, errorMessage);
                return OAuthClientResult.Failed(errorMessage ?? "Failed to register OAuth client");
            }

            _logger.LogInformation("OAuth client registration successful for ClientId: {ClientId}", clientId);
            return OAuthClientResult.Successful(clientId, displayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth client registration orchestration for ClientId: {ClientId}", clientId);
            return OAuthClientResult.Failed("An error occurred while registering OAuth client");
        }
    }

    /// <inheritdoc />
    public async Task<OAuthClientResult> UpdateOAuthClientAsync(string clientId, string? clientSecret, string? displayName,
        string[]? redirectUris, string[]? postLogoutRedirectUris, string[]? allowedScopes, bool? requireConsent)
    {
        _logger.LogInformation("OAuth client update orchestration started for ClientId: {ClientId}", clientId);

        try
        {
            // Step 1: Validate clientId
            if (string.IsNullOrWhiteSpace(clientId))
            {
                _logger.LogWarning("OAuth client update failed - clientId is required");
                return OAuthClientResult.Failed("Client ID is required");
            }

            // Step 2: Verify client exists
            var (clientSuccess, application, clientError) = await _openIdConnectService.GetClientApplicationAsync(clientId);
            if (!clientSuccess || application == null)
            {
                _logger.LogWarning("OAuth client update failed - client not found: {ClientId}, Error: {Error}", clientId, clientError);
                return OAuthClientResult.Failed(clientError ?? "OAuth client not found");
            }

            // Step 3: Update client via OpenIdConnectService
            var (success, errorMessage) = await _openIdConnectService.UpdateClientApplicationAsync(
                clientId,
                clientSecret,
                displayName,
                redirectUris,
                postLogoutRedirectUris,
                allowedScopes,
                requireConsent
            );

            if (!success)
            {
                _logger.LogWarning("OAuth client update failed for ClientId: {ClientId}, Error: {Error}", clientId, errorMessage);
                return OAuthClientResult.Failed(errorMessage ?? "Failed to update OAuth client");
            }

            _logger.LogInformation("OAuth client update successful for ClientId: {ClientId}", clientId);
            return OAuthClientResult.Successful(clientId, displayName ?? "Updated Client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth client update orchestration for ClientId: {ClientId}", clientId);
            return OAuthClientResult.Failed("An error occurred while updating OAuth client");
        }
    }

    /// <inheritdoc />
    public async Task<OAuthClientResult> DeleteOAuthClientAsync(string clientId)
    {
        _logger.LogInformation("OAuth client deletion orchestration started for ClientId: {ClientId}", clientId);

        try
        {
            // Step 1: Validate clientId
            if (string.IsNullOrWhiteSpace(clientId))
            {
                _logger.LogWarning("OAuth client deletion failed - clientId is required");
                return OAuthClientResult.Failed("Client ID is required");
            }

            // Step 2: Verify client exists
            var (clientSuccess, application, clientError) = await _openIdConnectService.GetClientApplicationAsync(clientId);
            if (!clientSuccess || application == null)
            {
                _logger.LogWarning("OAuth client deletion failed - client not found: {ClientId}, Error: {Error}", clientId, clientError);
                return OAuthClientResult.Failed(clientError ?? "OAuth client not found");
            }

            // Step 3: Delete client via OpenIdConnectService
            var (success, errorMessage) = await _openIdConnectService.DeleteClientApplicationAsync(clientId);

            if (!success)
            {
                _logger.LogWarning("OAuth client deletion failed for ClientId: {ClientId}, Error: {Error}", clientId, errorMessage);
                return OAuthClientResult.Failed(errorMessage ?? "Failed to delete OAuth client");
            }

            _logger.LogInformation("OAuth client deletion successful for ClientId: {ClientId}", clientId);
            return OAuthClientResult.Successful(clientId, "Deleted Client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth client deletion orchestration for ClientId: {ClientId}", clientId);
            return OAuthClientResult.Failed("An error occurred while deleting OAuth client");
        }
    }

    /// <inheritdoc />
    public async Task<OAuthClientsResult> GetOAuthClientsAsync()
    {
        _logger.LogInformation("OAuth clients list orchestration started");

        try
        {
            // Step 1: Get all client applications via OpenIdConnectService
            var (success, applications, errorMessage) = await _openIdConnectService.GetAllClientApplicationsAsync();

            if (!success || applications == null)
            {
                _logger.LogWarning("OAuth clients list retrieval failed: {Error}", errorMessage);
                return OAuthClientsResult.Failed(errorMessage ?? "Failed to retrieve OAuth clients");
            }

            // Step 2: Convert applications to DTOs
            var clientDtos = new List<OAuthClientDto>();
            
            // Get application manager to extract client details
            var appManager = _openIdConnectService.GetApplicationManager();
            
            foreach (var app in applications)
            {
                var clientId = await appManager.GetClientIdAsync(app);
                var displayName = await appManager.GetDisplayNameAsync(app);
                var redirectUris = await appManager.GetRedirectUrisAsync(app);
                var permissions = await appManager.GetPermissionsAsync(app);
                
                // Extract scopes from permissions
                var scopes = permissions
                    .Where(p => p.StartsWith("scp:"))
                    .Select(p => p.Substring(4))
                    .ToList();
                
                var consentType = await appManager.GetConsentTypeAsync(app);
                var requireConsent = consentType == "explicit";
                
                clientDtos.Add(new OAuthClientDto
                {
                    ClientId = clientId ?? "unknown",
                    DisplayName = displayName ?? "Unknown Client",
                    RedirectUris = redirectUris.Select(uri => uri.ToString()).ToList(),
                    AllowedScopes = scopes,
                    RequireConsent = requireConsent
                });
            }

            _logger.LogInformation("OAuth clients list retrieval successful, found {Count} clients", clientDtos.Count);
            return OAuthClientsResult.Successful(clientDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth clients list orchestration");
            return OAuthClientsResult.Failed("An error occurred while retrieving OAuth clients");
        }
    }

    /// <inheritdoc />
    public async Task<OAuthScopesResult> GetOAuthScopesAsync()
    {
        _logger.LogInformation("OAuth scopes list orchestration started");

        try
        {
            // Step 1: Get scope manager
            var scopeManager = _openIdConnectService.GetScopeManager();

            // Step 2: List all available scopes
            var scopes = new List<string>();
            await foreach (var scope in scopeManager.ListAsync())
            {
                var scopeName = await scopeManager.GetNameAsync(scope);
                if (!string.IsNullOrEmpty(scopeName))
                {
                    scopes.Add(scopeName);
                }
            }

            _logger.LogInformation("OAuth scopes list retrieval successful, found {Count} scopes", scopes.Count);
            return OAuthScopesResult.Successful(scopes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth scopes list orchestration");
            return OAuthScopesResult.Failed("An error occurred while retrieving OAuth scopes");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════════════════
    // Priority 4 Orchestration Methods (Low - Already Clean)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<QRLoginResult> GenerateQRLoginAsync(Identity userId)
    {
        _logger.LogInformation("QR login generation orchestration started for user ID: {UserId}", userId);

        try
        {
            // Step 1: Get user profile from SpacetimeDB
            var conn = _spacetimeService.GetConnection();
            var user = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);
            
            if (user == null)
            {
                _logger.LogWarning("User not found or inactive for QR login generation: {UserId}", userId);
                return QRLoginResult.Failed("User not found or inactive");
            }

            // Step 2: Generate QR code with data via QRAuthenticationService
            var (qrCode, rawData) = await _qrAuthService.GenerateQRCodeWithDataAsync(user);

            _logger.LogInformation("QR login generated successfully for user ID: {UserId}", userId);
            return QRLoginResult.Successful(qrCode, rawData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during QR login generation orchestration for user ID: {UserId}", userId);
            return QRLoginResult.Failed("An error occurred while generating QR login code");
        }
    }

    /// <inheritdoc />
    public async Task<QRValidationResult> ValidateQRLoginAsync(string token)
    {
        _logger.LogInformation("QR login validation orchestration started");

        try
        {
            // Step 1: Validate QR login token via QRAuthenticationService
            var (success, user) = await _qrAuthService.ValidateQRLoginTokenAsync(token);
            
            if (!success || user == null)
            {
                _logger.LogWarning("QR login validation failed - invalid or expired token");
                return QRValidationResult.Failed("Invalid or expired QR login token");
            }

            // Step 2: Generate JWT token and user DTO
            var (jwtToken, userDto, claims) = await GenerateAuthTokenAsync(user);

            _logger.LogInformation("QR login validation successful for user: {Username}", user.Login);
            return QRValidationResult.Successful(jwtToken, userDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during QR login validation orchestration");
            return QRValidationResult.Failed("An error occurred during QR login validation");
        }
    }

    /// <inheritdoc />
    public async Task<MagicLinkSendResult> SendMagicLinkAsync(string email, string? userAgent, string? ipAddress)
    {
        _logger.LogInformation("Magic link send orchestration started for email: {Email}", email);

        try
        {
            // Step 1: Send magic link via MagicLinkService
            var (success, errorMessage) = await _magicLinkService.SendMagicLinkAsync(email, userAgent, ipAddress);

            if (!success)
            {
                _logger.LogWarning("Magic link send failed for email: {Email} - {ErrorMessage}", email, errorMessage);
                return MagicLinkSendResult.Failed(errorMessage ?? "Failed to send magic link");
            }

            _logger.LogInformation("Magic link sent successfully to email: {Email}", email);
            return MagicLinkSendResult.Successful(email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during magic link send orchestration for email: {Email}", email);
            return MagicLinkSendResult.Failed("An error occurred while sending magic link");
        }
    }

    /// <inheritdoc />
    public async Task<UserResult> GetUserAsync(uint userId)
    {
        _logger.LogInformation("User retrieval orchestration started for user ID: {UserId}", userId);

        try
        {
            // Step 1: Get user via UserService
            var user = await _userService.GetUserByIdAsync(userId);
            
            if (user == null)
            {
                _logger.LogWarning("User not found: {UserId}", userId);
                return UserResult.Failed("User not found");
            }

            // Step 2: Get user roles and permissions from SpacetimeDB
            var conn = _spacetimeService.GetConnection();
            
            var userRoles = conn.Db.UserRole.Iter()
                .Where(ur => ur.UserId.Equals(user.UserId))
                .ToList();
            
            var roles = userRoles.Select(ur =>
            {
                var role = conn.Db.Role.RoleId.Find(ur.RoleId);
                return role != null ? new RoleDto
                {
                    RoleId = role.RoleId,
                    Name = role.Name,
                    Description = role.Description,
                    IsSystem = role.IsSystem
                } : null;
            }).Where(r => r != null).Cast<RoleDto>().ToList();

            var permissionIds = conn.Db.RolePermission.Iter()
                .Where(rp => roles.Select(r => r.RoleId).Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToList();

            var permissions = conn.Db.Permission.Iter()
                .Where(p => permissionIds.Contains(p.PermissionId))
                .Select(p => p.Name)
                .ToList();

            // Step 3: Get primary role
            var primaryRole = _authService.GetUserRole(user.UserId);

            // Step 4: Create UserDetailDto
            var userDetailDto = new UserDetailDto
            {
                Id = user.LegacyUserId,
                UserId = user.UserId.ToString(),
                Username = user.Login,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                EmailConfirmed = user.EmailConfirmed,
                Role = primaryRole,
                Roles = roles,
                Permissions = permissions
            };

            _logger.LogInformation("User retrieved successfully: {UserId}", userId);
            return UserResult.Successful(userDetailDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user retrieval orchestration for user ID: {UserId}", userId);
            return UserResult.Failed("An error occurred while retrieving user");
        }
    }

    /// <inheritdoc />
    public async Task<UsersResult> GetAllUsersAsync()
    {
        _logger.LogInformation("All users retrieval orchestration started");

        try
        {
            // Step 1: Get all users via UserService
            var users = await _userService.GetAllUsersAsync();

            // Step 2: Convert to UserDto list
            var userDtos = users.Select(u => new UserDto
            {
                Id = u.LegacyUserId,
                Username = u.Login,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                Role = _authService.GetUserRole(u.UserId)
            }).ToList();

            _logger.LogInformation("Retrieved {Count} users successfully", userDtos.Count);
            return UsersResult.Successful(userDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during all users retrieval orchestration");
            return UsersResult.Failed("An error occurred while retrieving users");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Private Helper Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates JWT token and UserDto for authenticated user.
    /// Eliminates code duplication across LoginAsync, ValidateTotpAsync, and ValidateMagicLinkAsync.
    /// Matches AuthController behavior exactly: generates token, extracts claims, builds UserDto.
    /// </summary>
    /// <param name="user">The authenticated user profile</param>
    /// <returns>Tuple containing JWT token, UserDto, and token claims</returns>
    private async Task<(string jwtToken, UserDto userDto, Dictionary<string, object> claims)> GenerateAuthTokenAsync(UserProfile user)
    {
        // Step 1: Generate JWT token using TokenService (which queries DB for roles/permissions)
        var jwtToken = _tokenService.GenerateToken(user.UserId);

        // Step 2: Extract token claims for client-side logging (matches AuthController behavior)
        var tokenClaims = _tokenService.ExtractTokenClaims(jwtToken);

        // Step 3: Get primary role for UserDto
        var primaryRole = _authService.GetUserRole(user.UserId);

        // Step 4: Create UserDto
        var userDto = new UserDto
        {
            Id = user.LegacyUserId,
            Username = user.Login,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Role = primaryRole
        };

        return (jwtToken, userDto, tokenClaims);
    }
}
