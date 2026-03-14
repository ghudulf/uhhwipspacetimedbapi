# Design Document: AuthController Refactoring

## Overview

This design document describes the refactoring of the AuthController from a monolithic 8,293-line controller with mixed architecture patterns into a clean, layered architecture following the Controller → Orchestration → Services → Database pattern. The refactoring will be performed using a non-destructive approach where new code is built in parallel to existing code, validated incrementally, and rolled out gradually using feature flags.

### Current State

**AuthController Statistics**:
- 8,293 lines of code in a single file
- 56 HTTP endpoints across 8 authentication methods
- Three distinct architecture patterns coexisting
- 32% of endpoints have direct database access
- 50% of endpoints already follow clean architecture
- 18% are HTML-only endpoints (no business logic)

**Architecture Pattern Distribution**:
1. **Clean Service Layer** (28 endpoints, 50%): Properly delegates to business logic services
2. **Mixed Pattern** (18 endpoints, 32%): Uses services BUT also has direct database access
3. **HTML Only** (10 endpoints, 18%): Just render forms, no business logic

**Why This Happened**: The system evolved rapidly during SpacetimeDB's evolution from 0.9 to 2.0 (March 2025 - January 2026), with only ~3 months of actual development time. Early endpoints (Login, Register) were built before the service layer existed. Later endpoints (TOTP, WebAuthn, Magic Link, QR) were built cleanly after the service layer was established. OAuth was implemented under pressure after an 8-month project pause, prioritizing functionality over architecture.

### Target State

**Refactored AuthController**:
- ~2,000 lines of code (75% reduction)
- 56 HTTP endpoints (unchanged)
- Single consistent architecture pattern
- Zero direct database access
- 100% orchestration layer coverage
- Comprehensive test coverage
- Feature flag support for gradual rollout


**Architecture Goals**:
- Consistent layered architecture across all endpoints
- Testable business logic without database dependencies
- Centralized authentication and authorization logic
- Support for caching and rate limiting
- Backward compatibility with existing clients
- Zero downtime during migration

## Architecture

### Layered Architecture Pattern

The refactored system follows a strict three-layer architecture:

```
┌─────────────────────────────────────────────────────────────┐
│                     AuthController                           │
│                    (56 HTTP Endpoints)                       │
│  Responsibilities:                                           │
│  - HTTP request/response handling                            │
│  - Input validation and sanitization                         │
│  - Feature flag checks                                       │
│  - Delegation to orchestration layer                         │
│  - NO business logic                                         │
│  - NO direct database access                                 │
└────────────────────┬────────────────────────────────────────┘
                     │
                     │ Delegates all business logic
                     ↓
┌─────────────────────────────────────────────────────────────┐
│         Orchestration Layer (Experimental/Services)          │
│              Target: 100% coverage (50 methods)              │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ AuthOrchestrationService.cs                          │  │
│  │  Current (5 methods):                                │  │
│  │  ✅ AuthenticateAsync                                 │  │
│  │  ✅ RegisterAsync                                     │  │
│  │  ✅ ClaimAccountAsync                                 │  │
│  │  ✅ IsAdmin                                           │  │
│  │  ✅ HasPermission                                     │  │
│  │                                                       │  │
│  │  New (45 methods):                                   │  │
│  │  - LoginAsync, ValidateTotpAsync                     │  │
│  │  - ValidateWebAuthnAsync, ValidateMagicLinkAsync     │  │
│  │  - SetupTotpAsync, EnableTotpAsync, DisableTotpAsync │  │
│  │  - RegisterWebAuthnAsync, GetWebAuthnCredentialsAsync│  │
│  │  - AuthorizeOAuthAsync, ExchangeTokenAsync           │  │
│  │  - GetUserInfoAsync, RegisterOAuthClientAsync        │  │
│  │  - ... and 30+ more orchestration methods            │  │
│  └──────────────────────────────────────────────────────┘  │
│  Helper Services (✅ Complete):                             │
│  - TokenService, ProfileService, IdentityService            │
│  - OidcHelperService, RequestDetector, HtmlRenderingService │
│                                                              │
│  Responsibilities:                                           │
│  - Coordinate multiple business logic services              │
│  - Aggregate data from multiple sources                     │
│  - Handle cross-cutting concerns (logging, monitoring)      │
│  - Transaction management across service calls              │
│  - Return standardized response objects                     │
│  - NO direct database access                                │
└────────────────────┬────────────────────────────────────────┘
                     │
                     │ Calls business logic
                     ↓
┌─────────────────────────────────────────────────────────────┐
│      Business Logic Layer (TicketSalesApp.Services)         │
│              ✅ COMPLETE - 7 services, 52 methods            │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ AuthenticationService.cs (5 methods)                 │  │
│  │  - AuthenticateAsync, RegisterAsync                   │  │
│  │  - AuthenticateDirectQRAsync, GetUserRole            │  │
│  │  - GetUserIdentityByLoginAsync                        │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ UserService.cs (8 methods)                           │  │
│  │  - GetAllUsersAsync, GetUserByIdAsync                │  │
│  │  - GetUserByLoginAsync, UpdateUserAsync              │  │
│  │  - DeleteUserAsync, GetUserRolesAsync                │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ TotpService.cs (9 methods)                           │  │
│  │  - SetupTotpAsync, EnableTotpAsync, DisableTotpAsync │  │
│  │  - ValidateTotpAsync, ValidateTotpWithTokenAsync     │  │
│  │  - IsTotpEnabledAsync, GenerateTotpSecretKeyAsync    │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ WebAuthnService.cs (7 methods)                       │  │
│  │  - GetCredentialCreateOptionsAsync                    │  │
│  │  - CompleteRegistrationAsync                          │  │
│  │  - GetAssertionOptionsAsync, CompleteAssertionAsync  │  │
│  │  - RemoveCredentialAsync, GetUserCredentialsAsync    │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ MagicLinkService.cs (3 methods)                      │  │
│  │  - SendMagicLinkAsync, ValidateMagicLinkAsync        │  │
│  │  - MarkMagicLinkAsUsedAsync                           │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ QRAuthenticationService.cs (7 methods)               │  │
│  │  - GenerateQRLoginTokenAsync                          │  │
│  │  - ValidateQRLoginTokenAsync, GenerateQRCodeAsync    │  │
│  │  - GenerateDirectLoginQRCodeAsync                     │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ OpenIdConnectService.cs (13 methods)                 │  │
│  │  - GetApplicationByClientIdAsync                      │  │
│  │  - CreateIdentityFromUserAsync                        │  │
│  │  - CreateAuthorizationAsync                           │  │
│  │  - RegisterClientApplicationAsync                     │  │
│  │  - UpdateClientApplicationAsync                       │  │
│  │  - DeleteClientApplicationAsync                       │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ TwoFactorService.cs (4 methods) - NEW               │  │
│  │  - CreateTempTokenAsync                               │  │
│  │  - ValidateTempTokenAsync                             │  │
│  │  - MarkTokenAsUsedAsync                               │  │
│  │  - CleanupExpiredTokensAsync                          │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ SettingsService.cs (6 methods) - NEW                │  │
│  │  - GetOrCreateUserSettingsAsync                       │  │
│  │  - EnableTotpAsync, DisableTotpAsync                 │  │
│  │  - EnableWebAuthnAsync, DisableWebAuthnAsync         │  │
│  │  - UpdateSettingsAsync                                │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                              │
│  Responsibilities:                                           │
│  - Database queries and mutations                           │
│  - Domain logic and validation                              │
│  - Single responsibility per service                        │
│  - Direct SpacetimeDB access                                │
└────────────────────┬────────────────────────────────────────┘
                     │
                     │ Database access
                     ↓
┌─────────────────────────────────────────────────────────────┐
│                      SpacetimeDB 2.0                         │
│  Tables: UserProfile, UserRole, Role, Permission,            │
│          TotpSecret, WebAuthnCredential, TwoFactorToken,     │
│          UserSettings, MagicLink, QRLoginToken, etc.         │
└─────────────────────────────────────────────────────────────┘
```


### Design Decisions

**Decision 1: Two-Layer Service Architecture**
- **Rationale**: Separating orchestration from business logic allows for better testability, reusability, and maintainability
- **Trade-off**: Adds one more layer of indirection, but the benefits outweigh the complexity
- **Alternative Considered**: Single service layer - rejected because it would mix coordination logic with domain logic

**Decision 2: Non-Destructive Refactoring**
- **Rationale**: Building new code in parallel eliminates risk to production system during development
- **Trade-off**: Requires more disk space and temporary code duplication, but ensures zero downtime
- **Alternative Considered**: Direct refactoring - rejected due to high risk of breaking production

**Decision 3: Feature Flags for Gradual Rollout**
- **Rationale**: Allows incremental validation with instant rollback capability
- **Trade-off**: Adds complexity to controller code temporarily, but provides safety net
- **Alternative Considered**: Big bang deployment - rejected due to high risk

**Decision 4: Keep Existing Services Unchanged**
- **Rationale**: Business logic services (7 services, 52 methods) are already well-designed and tested
- **Trade-off**: None - this is purely additive
- **Alternative Considered**: Rewrite services - rejected as unnecessary and risky

**Decision 5: Build in Experimental Folder**
- **Rationale**: Clear separation between legacy and new code during migration
- **Trade-off**: Temporary folder structure complexity, but provides clarity
- **Alternative Considered**: Build in production folders - rejected to avoid confusion

**Decision 6: Dual-Controller Architecture with Dynamic Routing**
- **Rationale**: Keeps legacy controller pristine (only adds attributes), provides clean separation, enables independent testing
- **Trade-off**: More complex than inline feature flag checks, but far superior for maintainability and safety
- **Alternative Considered**: Inline feature flag checks in legacy controller - rejected because it pollutes the 8,293-line file and makes cleanup harder

## Dual-Controller Architecture with Dynamic Routing

### Overview

Instead of modifying the legacy `AuthController` with inline feature flag checks (which would pollute the 8,293-line file), the system implements a **dual-controller architecture** with dynamic routing based on feature flags.

### Architecture Components

**1. AuthController.cs (Legacy Controller)**
- Location: `Controllers/AuthController.cs`
- Size: 8,293 lines (unchanged logic)
- Modification: Only add `[LegacyAction]` attributes to each endpoint
- Selected: When feature flag is DISABLED
- Status: Logic remains completely UNTOUCHED

**2. AuthControllerRefactored.cs (New Controller)**
- Location: `Controllers/AuthControllerRefactored.cs`
- Size: ~2,000 lines (clean implementation)
- Implementation: Uses orchestration service pattern
- Marked: Each endpoint has `[RefactoredAction]` attribute
- Selected: When feature flag is ENABLED
- Status: Clean code with no legacy baggage

**3. FeatureFlagActionConstraint.cs (Routing Logic)**
- Location: `Routing/FeatureFlagActionConstraint.cs`
- Purpose: Custom `IActionConstraint` for dynamic action selection
- Attributes:
  - `RefactoredActionAttribute`: Selects action when flag is ENABLED
  - `LegacyActionAttribute`: Selects action when flag is DISABLED
- Mechanism: Uses reflection to check feature flag values at runtime

### Routing Flow

```
┌─────────────────────────────────────────────────────────────┐
│                    HTTP Request                              │
│                  POST /api/auth/login                        │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────┐
│            ASP.NET Core Routing Engine                       │
│         (with FeatureFlagActionConstraint)                   │
│                                                              │
│  1. Finds all actions matching route                         │
│  2. Evaluates action constraints                             │
│  3. Selects action based on feature flag state               │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ├─── Feature Flag ENABLED ───→ AuthControllerRefactored.Login()
                     │                                (Clean, orchestration-based)
                     │                                [RefactoredAction("EnableLoginRefactoring")]
                     │
                     └─── Feature Flag DISABLED ──→ AuthController.Login()
                                                     (Legacy, untouched)
                                                     [LegacyAction("EnableLoginRefactoring")]
```

### Implementation Example

**Legacy Controller** (AuthController.cs):
```csharp
[HttpPost("login")]
[AllowAnonymous]
[LegacyAction(nameof(FeatureFlagOptions.EnableLoginRefactoring))]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
{
    // ... existing 200+ lines of login logic (UNCHANGED) ...
    // Direct database access, manual JWT parsing, etc.
    // This code is NEVER modified - only the attribute is added
}
```

**Refactored Controller** (AuthControllerRefactored.cs):
```csharp
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

    // Clean orchestration-based implementation
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
```

**Action Constraint Implementation** (FeatureFlagActionConstraint.cs):
```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class FeatureFlagActionConstraintAttribute : Attribute, IActionConstraint
{
    private readonly string _featureFlagProperty;
    private readonly bool _requireEnabled;

    public FeatureFlagActionConstraintAttribute(string featureFlagProperty, bool requireEnabled = true)
    {
        _featureFlagProperty = featureFlagProperty ?? throw new ArgumentNullException(nameof(featureFlagProperty));
        _requireEnabled = requireEnabled;
    }

    public int Order => 0;

    public bool Accept(ActionConstraintContext context)
    {
        var featureFlags = context.RouteContext.HttpContext.RequestServices
            .GetService(typeof(IOptions<FeatureFlagOptions>)) as IOptions<FeatureFlagOptions>;

        if (featureFlags == null)
        {
            // If feature flags service is not available, default to legacy (disabled)
            return !_requireEnabled;
        }

        // Use reflection to get the feature flag value
        var flagProperty = typeof(FeatureFlagOptions).GetProperty(_featureFlagProperty);
        if (flagProperty == null)
        {
            throw new InvalidOperationException($"Feature flag property '{_featureFlagProperty}' not found on FeatureFlagOptions");
        }

        var flagValue = (bool?)flagProperty.GetValue(featureFlags.Value) ?? false;

        // Return true if the flag state matches what we require
        return flagValue == _requireEnabled;
    }
}

// Convenience attributes
public class RefactoredActionAttribute : FeatureFlagActionConstraintAttribute
{
    public RefactoredActionAttribute(string featureFlagProperty) 
        : base(featureFlagProperty, requireEnabled: true) { }
}

public class LegacyActionAttribute : FeatureFlagActionConstraintAttribute
{
    public LegacyActionAttribute(string featureFlagProperty) 
        : base(featureFlagProperty, requireEnabled: false) { }
}
```

### Benefits Over Inline Feature Flag Checks

**Alternative Approach** (Inline feature flag checks - REJECTED):
```csharp
[HttpPost("login")]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
{
    if (_featureFlags.Value.EnableLoginRefactoring)
    {
        // NEW CODE: Call orchestration service
        var result = await _authOrchestrationService.LoginAsync(request.Username, request.Password);
        // ... handle result ...
    }
    else
    {
        // LEGACY CODE: 200+ lines of existing logic
        // ... all the existing code ...
    }
}
```

**Problems with Inline Approach**:
- Pollutes legacy controller with feature flag checks
- Makes 8,293-line file even longer
- Harder to test (both paths in same method)
- Harder to clean up later (must remove if/else blocks)
- Risk of accidentally modifying legacy code
- Difficult to review changes (mixed with legacy code)

**Benefits of Dual-Controller Approach**:
1. **Zero Risk**: Legacy controller logic never modified (only attributes added)
2. **Clean Separation**: Refactored code in separate file, easy to review
3. **Easy Rollback**: Just disable feature flags - no code deployment needed
4. **Easy Cleanup**: After validation, just delete `AuthController.cs` and remove attributes from `AuthControllerRefactored.cs`
5. **Testable**: Can test both controllers independently with different test suites
6. **Gradual Rollout**: Enable flags per-endpoint for fine-grained control
7. **Clear Intent**: Attributes make it obvious which action is legacy vs refactored
8. **No Code Duplication**: Each controller has its own clean implementation

### Endpoint Mapping

| Endpoint | Feature Flag | Legacy Status | Refactored Status |
|----------|-------------|---------------|-------------------|
| POST /api/auth/login | EnableLoginRefactoring | ✅ Has [LegacyAction] | ✅ Implemented |
| POST /api/auth/register | EnableRegisterRefactoring | ✅ Has [LegacyAction] | ✅ Implemented |
| GET /api/auth/totp/setup | EnableTotpSetupRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| POST /api/auth/totp/verify | EnableTotpVerifyRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| POST /api/auth/totp/disable | EnableTotpDisableRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| POST /api/auth/totp/validate | EnableTotpValidateRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| POST /api/auth/webauthn/register/complete | EnableWebAuthnRegisterCompleteRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| POST /api/auth/webauthn/validate | EnableWebAuthnValidateRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| GET /api/auth/webauthn/credentials | EnableWebAuthnCredentialsRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| DELETE /api/auth/webauthn/credentials/{id} | EnableWebAuthnCredentialDeleteRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| POST /api/auth/magic-link/send | EnableMagicLinkSendRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| POST /api/auth/validate-magic-link | EnableMagicLinkValidateRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| GET /api/auth/profile | EnableProfileRefactoring | ⚠️ Needs [LegacyAction] | ✅ Implemented |
| ... | ... | ⚠️ Remaining 43 endpoints | ⚠️ Remaining 43 endpoints |

### Testing Strategy

**With All Flags Disabled** (Default):
- All requests route to legacy `AuthController`
- System behaves exactly as before
- Zero risk to production

**With Individual Flags Enabled**:
- Specific endpoints route to `AuthControllerRefactored`
- Other endpoints still use legacy controller
- Gradual validation per endpoint

**With All Flags Enabled**:
- All requests route to `AuthControllerRefactored`
- Full refactored architecture active
- Ready for legacy code removal

**Testing Both Paths**:
```csharp
[Fact]
public async Task Login_WithFeatureFlagEnabled_UsesRefactoredController()
{
    var factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<FeatureFlagOptions>(options =>
                {
                    options.EnableLoginRefactoring = true;
                });
            });
        });
    
    var client = factory.CreateClient();
    var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { ... });
    
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    // Verify refactored behavior
}

[Fact]
public async Task Login_WithFeatureFlagDisabled_UsesLegacyController()
{
    var factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<FeatureFlagOptions>(options =>
                {
                    options.EnableLoginRefactoring = false;
                });
            });
        });
    
    var client = factory.CreateClient();
    var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { ... });
    
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    // Verify legacy behavior is preserved
}
```

### Remaining Work

**Phase 4 Completion Tasks**:
1. **Add [LegacyAction] attributes to all 56 endpoints in AuthController.cs**
   - This is mechanical work - just add one attribute per endpoint
   - Example: `[LegacyAction(nameof(FeatureFlagOptions.EnableLoginRefactoring))]`
   - Critical for routing to work correctly

2. **Complete remaining endpoints in AuthControllerRefactored.cs**
   - Implement remaining 40+ endpoints with orchestration service
   - Add `[RefactoredAction]` attributes to all
   - Endpoints needed: QR Auth (7), OAuth (20+), utility endpoints (10+)

3. **Test routing behavior**
   - Verify feature flags control routing correctly
   - Test with flags enabled/disabled
   - Verify backward compatibility

4. **Deploy and monitor**
   - Deploy with all flags disabled
   - Enable flags incrementally (1% → 10% → 50% → 100%)
   - Monitor error rates and performance

### Why This Is Production-Grade

This dual-controller approach with dynamic routing is the **correct** way to implement feature-flagged refactoring. It's more complex than inline feature flag checks, but the benefits far outweigh the complexity:

- **Safety**: Legacy code never modified (only attributes added)
- **Clarity**: Clean separation between old and new implementations
- **Testability**: Independent testing of both code paths
- **Maintainability**: Easy cleanup after validation (just delete legacy file)
- **Professionalism**: This is how production systems handle major refactorings

This is not a shortcut or hack - this is production-grade software engineering.

## Components and Interfaces

### New Business Logic Services

#### TwoFactorService

**Purpose**: Manage temporary two-factor authentication tokens used during login flows.

**Location**: `BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/Implementations/TwoFactorService.cs`

**Interface**:
```csharp
public interface ITwoFactorService
{
    Task<string> CreateTempTokenAsync(Identity userId, string tokenType);
    Task<(bool isValid, Identity? userId)> ValidateTempTokenAsync(string token);
    Task MarkTokenAsUsedAsync(string token);
    Task CleanupExpiredTokensAsync();
}
```

**Methods**:
- `CreateTempTokenAsync`: Generate a temporary token for 2FA validation (TOTP, WebAuthn, Magic Link) - returns the token string
- `ValidateTempTokenAsync`: Verify a temporary token is valid, not expired, and not used - returns validity and associated userId
- `MarkTokenAsUsedAsync`: Mark a token as used to prevent replay attacks
- `CleanupExpiredTokensAsync`: Remove expired tokens from database (background job)

**Database Tables**:
- `TwoFactorToken`: Stores temporary tokens with expiration timestamps

**Current State**:
- **TotpService** already has `ValidateTotpWithTokenAsync` that queries and deletes TwoFactorToken (partial implementation)
- **AuthController** directly calls `conn.Reducers.CreateTwoFactorToken()` in Login endpoint (lines 2074, 2103)
- **AuthController** directly queries `conn.Db.TwoFactorToken.Iter()` in TOTP validate (line 2881) and WebAuthn validate (line 3200)
- **AuthController** directly calls `conn.Reducers.UpdateTwoFactorToken()` to mark as used (lines 2940, 3243)

**Why We Need This Service**:
- Centralize TwoFactorToken management (currently split between TotpService and AuthController)
- Remove direct database access from AuthController
- Provide consistent token validation across TOTP, WebAuthn, and Magic Link flows
- Enable proper unit testing without database dependencies


#### SettingsService

**Purpose**: Manage user authentication settings (TOTP enabled, WebAuthn enabled, etc.).

**Location**: `BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/Implementations/SettingsService.cs`

**Interface**:
```csharp
public interface ISettingsService
{
    Task<UserSettings> GetOrCreateUserSettingsAsync(Identity userId);
    Task EnableTotpAsync(Identity userId);
    Task DisableTotpAsync(Identity userId);
    Task EnableWebAuthnAsync(Identity userId);
    Task DisableWebAuthnAsync(Identity userId);
    Task UpdateSettingsAsync(Identity userId, UserSettings settings);
}
```

**Methods**:
- `GetOrCreateUserSettingsAsync`: Retrieve user settings or create default settings if none exist
- `EnableTotpAsync`: Mark TOTP as enabled for user
- `DisableTotpAsync`: Mark TOTP as disabled for user
- `EnableWebAuthnAsync`: Mark WebAuthn as enabled for user
- `DisableWebAuthnAsync`: Mark WebAuthn as disabled for user
- `UpdateSettingsAsync`: Update user settings with new values

**Database Tables**:
- `UserSettings`: Stores user authentication preferences

**Current Usage**: Login endpoint currently queries and creates UserSettings directly in AuthController.

### Orchestration Service Expansion

#### AuthOrchestrationService

**Purpose**: Coordinate multiple business logic services to fulfill authentication workflows.

**Location**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Experimental/Services/Implementations/AuthOrchestrationService.cs`

**Current Methods** (5):
- `AuthenticateAsync`: Authenticate user credentials
- `RegisterAsync`: Register new user account
- `ClaimAccountAsync`: Claim SpacetimeDB identity
- `IsAdmin`: Check if user has admin privileges
- `HasPermission`: Check if user has specific permission

**New Methods** (45):

**Priority 1 - Critical (Direct DB Access Elimination)**:
```csharp
Task<LoginResult> LoginAsync(string username, string password);
Task<TotpValidationResult> ValidateTotpAsync(Identity userId, string code, string tempToken);
Task<WebAuthnValidationResult> ValidateWebAuthnAsync(Identity userId, string assertion, string tempToken);
Task<MagicLinkValidationResult> ValidateMagicLinkAsync(string token);
Task<UserProfileResult> GetProfileAsync(Identity userId);
```

**Priority 2 - High (Complete TOTP/WebAuthn Flows)**:
```csharp
Task<TotpSetupResult> SetupTotpAsync(Identity userId);
Task<TotpEnableResult> EnableTotpAsync(Identity userId, string code);
Task<TotpDisableResult> DisableTotpAsync(Identity userId);
Task<WebAuthnRegisterResult> RegisterWebAuthnAsync(Identity userId, string attestation);
Task<WebAuthnCredentialsResult> GetWebAuthnCredentialsAsync(Identity userId);
Task<WebAuthnRemoveResult> RemoveWebAuthnCredentialAsync(Identity userId, Guid credentialId);
```


**Priority 3 - Medium (OAuth/OIDC Flows)**:

**⚠️ CRITICAL: OAuth/OIDC methods CANNOT fully delegate to services**

OpenIddict requires specific ASP.NET Core controller operations that MUST stay in the controller. The orchestration service provides HELPER methods for validation and claims building, but the controller MUST handle OpenIddict operations.

**Helper Methods for OAuth (CAN delegate to service)**:
```csharp
// Validation helpers
Task<OAuthValidationResult> ValidateOAuthRequestAsync(string clientId, string redirectUri, string scope);
Task<UserValidationResult> ValidateUserForTokenExchangeAsync(string userId);

// Claims building helpers
Task<ClaimsIdentityResult> BuildOAuthClaimsIdentityAsync(string username, string[] scopes);
Task<ClaimsIdentityResult> BuildTokenClaimsIdentityAsync(UserProfile user, string[] scopes, string[] resources);

// Client management (can fully delegate)
Task<OAuthClientResult> RegisterOAuthClientAsync(OAuthClientRequest request);
Task<OAuthClientResult> UpdateOAuthClientAsync(string clientId, OAuthClientRequest request);
Task<OAuthClientResult> DeleteOAuthClientAsync(string clientId);
Task<OAuthClientsResult> GetOAuthClientsAsync();
Task<OAuthScopesResult> GetOAuthScopesAsync();
```

**Operations That MUST Stay in Controller**:
- `HttpContext.GetOpenIddictServerRequest()` - Get OAuth request from HTTP context
- `HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)` - Validate authorization codes
- `SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)` - Generate OAuth tokens
- `Forbid(authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)` - Return OAuth errors
- `HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ...)` - Cookie authentication

**Why This Matters**:
- OpenIddict operations require HttpContext and ASP.NET Core authentication middleware
- Attempting to delegate SignIn/Forbid to services will fail at runtime
- Service layer provides helper methods for validation and claims building
- Controller orchestrates OpenIddict operations and calls service helpers

**Correct Pattern**:
```csharp
// Controller (AuthControllerRefactored.cs)
[HttpGet("~/connect/authorize")]
public async Task<IActionResult> Authorize()
{
    var request = HttpContext.GetOpenIddictServerRequest(); // MUST be in controller
    
    // Delegate validation to service
    var validationResult = await _authOrchestrationService
        .ValidateOAuthRequestAsync(request.ClientId, request.RedirectUri, request.Scope);
    
    if (!validationResult.Success)
    {
        return Forbid(...); // MUST be in controller
    }
    
    // Check authentication
    var authenticateResult = await HttpContext.AuthenticateAsync(...); // MUST be in controller
    
    // Delegate claims building to service
    var claimsResult = await _authOrchestrationService
        .BuildOAuthClaimsIdentityAsync(username, scopes);
    
    // Sign in with OpenIddict
    return SignIn(new ClaimsPrincipal(claimsResult.Identity), ...); // MUST be in controller
}

// Service (AuthOrchestrationService.cs)
public async Task<OAuthValidationResult> ValidateOAuthRequestAsync(
    string clientId, string redirectUri, string scope)
{
    var (success, application, error) = await _openIdConnectService
        .GetApplicationByClientIdAsync(clientId);
    
    if (!success)
        return OAuthValidationResult.Failed(error);
    
    // Validate redirect URI, scopes, etc.
    return OAuthValidationResult.Successful(application);
}

public async Task<ClaimsIdentityResult> BuildOAuthClaimsIdentityAsync(
    string username, string[] scopes)
{
    var user = await _userService.GetUserByLoginAsync(username);
    if (user == null)
        return ClaimsIdentityResult.Failed("User not found");
    
    var identity = new ClaimsIdentity(...);
    
    // Add claims
    identity.AddClaim(new Claim(Claims.Subject, user.UserId.ToString()));
    identity.AddClaim(new Claim(Claims.Name, user.Login));
    
    // Query and add roles
    var roles = await GetUserRolesAsync(user.UserId);
    foreach (var role in roles)
    {
        identity.AddClaim(new Claim(Claims.Role, role));
    }
    
    // Set scopes and resources
    identity.SetScopes(scopes);
    var resources = await _openIdConnectService.GetResourcesAsync(scopes);
    identity.SetResources(resources.resources);
    
    // Set claim destinations
    foreach (var claim in identity.Claims)
    {
        claim.SetDestinations(_openIdConnectService.GetDestinations(claim));
    }
    
    return ClaimsIdentityResult.Successful(identity);
}
```

**Priority 4 - Low (Already Clean, Minimal Work)**:
```csharp
Task<QRLoginResult> GenerateQRLoginAsync(Identity userId);
Task<QRValidationResult> ValidateQRLoginAsync(string token);
Task<MagicLinkResult> SendMagicLinkAsync(string email);
Task<UserResult> GetUserAsync(Identity userId);
Task<UsersResult> GetAllUsersAsync();
```

**Orchestration Pattern**:
Each orchestration method follows this pattern:
1. Validate input parameters
2. Call one or more business logic services
3. Aggregate results from multiple services
4. Handle errors and logging
5. Return standardized response object

**Example - LoginAsync**:
```csharp
public async Task<LoginResult> LoginAsync(string username, string password)
{
    // Step 1: Authenticate user credentials
    var user = await _authService.AuthenticateAsync(username, password);
    if (user == null)
        return LoginResult.Failed("Invalid credentials");

    // Step 2: Get or create user settings
    var settings = await _settingsService.GetOrCreateUserSettingsAsync(user.UserId);

    // Step 3: Check if 2FA is required
    bool requiresTwoFactor = settings.TotpEnabled || settings.WebAuthnEnabled;
    
    if (requiresTwoFactor)
    {
        // Step 4: Create temporary token for 2FA validation
        var tempToken = await _twoFactorService.CreateTempTokenAsync(user.UserId, "login");
        return LoginResult.RequiresTwoFactor(tempToken.Token, settings);
    }

    // Step 5: Generate custom JWT token for internal authentication
    // NOTE: This is a custom JWT token, NOT an OAuth token
    // OAuth tokens are generated by OpenIddict in /connect/token endpoint
    var jwtToken = _tokenService.GenerateToken(user, settings);
    
    // Step 6: Return success result
    return LoginResult.Success(jwtToken, user, settings);
}
```

### Helper Services (Already Complete)

These services are already implemented and will be used by orchestration methods:

**TokenService**: JWT token generation and validation
**ProfileService**: User profile data aggregation
**IdentityService**: SpacetimeDB identity management
**OidcHelperService**: OAuth/OIDC helper utilities
**RequestDetector**: Browser vs API request detection
**HtmlRenderingService**: Razor view rendering

## Token Types and Authentication Flows

### Overview

The system uses TWO distinct token types for different purposes. Understanding this distinction is critical for proper implementation:

**1. Custom JWT Tokens** (Internal Authentication)
- **Purpose**: Internal authentication between clients and C# backend
- **Used for**: Direct API calls, Avalonia desktop client, internal web pages
- **Generated by**: `TokenService.GenerateToken()` (custom implementation)
- **Validation**: Custom JWT validation in `JwtBearerDefaults.AuthenticationScheme`
- **Claims**: Custom claims (identity, xuid, role, permissions, etc.)
- **Signing**: Symmetric key (HS256)
- **Audience**: Internal API consumers

**2. OpenIddict OAuth Tokens** (OAuth/OIDC Flows)
- **Purpose**: OAuth 2.0 / OpenID Connect compliant tokens for third-party clients
- **Used for**: OAuth authorization code flow, third-party integrations
- **Generated by**: OpenIddict server (spec-compliant implementation)
- **Validation**: OpenIddict validation middleware
- **Token types**: Access tokens, refresh tokens, ID tokens (JWT), authorization codes
- **Claims**: Standard OAuth/OIDC claims (sub, aud, iss, exp, scope, etc.)
- **Signing**: Asymmetric keys (RS256) for ID tokens, symmetric for access tokens
- **Audience**: OAuth clients registered in the system

### When to Use Each Token Type

**Use Custom JWT Tokens**:
- ✅ Avalonia desktop client authentication
- ✅ Direct API calls from trusted clients
- ✅ Internal web pages (CSHTML views)
- ✅ Session-based authentication
- ✅ Simple authentication flows (username/password, TOTP, WebAuthn)

**Use OpenIddict OAuth Tokens**:
- ✅ OAuth 2.0 authorization code flow
- ✅ Third-party client integrations
- ✅ OpenID Connect flows (SSO, federated identity)
- ✅ Refresh token flows
- ✅ When spec compliance is required

### Critical Implementation Notes

**⚠️ DO NOT mix token types**:
- Custom JWT tokens are NOT OAuth-compliant and MUST NOT be used in OAuth flows
- OAuth tokens are more complex and MUST NOT be used for simple internal auth
- Each token type has its own validation pipeline and cannot validate the other

**⚠️ AuthorizationStore Status**:
- **PERMANENTLY DISABLED** in `Program.cs` due to unfixable SpacetimeDB validation bugs
- OAuth authorization data (scopes, client_id, user consent, PKCE data) is embedded in token payload
- This is the PERMANENT solution, not a temporary workaround
- OpenIddict's `DisableAuthorizationStorage()` is used - PKCE data stored in authorization code payload
- Authorization codes are stored in memory cache (10-minute TTL) with all OAuth request parameters
- This approach works correctly and will NOT be changed
- **Why not fixable**: SpacetimeDB's validation system conflicts with OpenIddict's authorization store requirements in ways that cannot be resolved without major changes to either SpacetimeDB or OpenIddict

**Example - LoginAsync with Custom JWT**:
```csharp
public async Task<LoginResult> LoginAsync(string username, string password)
{
    // ... authentication logic ...
    
    // Generate CUSTOM JWT token for internal auth
    var jwtToken = _tokenService.GenerateToken(user, settings);
    
    return LoginResult.Success(jwtToken, user, settings);
}
```

**Example - OAuth Token Exchange with OpenIddict**:
```csharp
[HttpPost("connect/token")]
public async Task<IActionResult> Token()
{
    var request = HttpContext.GetOpenIddictServerRequest();
    
    // ... OAuth flow logic ...
    
    // OpenIddict generates spec-compliant OAuth tokens
    return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
```

### Current OAuth Implementation Details

**Actual OAuth Endpoints** (Already Implemented):
- `GET/POST ~/connect/authorize`: Authorization endpoint (handles login redirect and consent)
- `POST ~/connect/authorize/callback`: Callback after user login (custom endpoint for form submission)
- `POST ~/connect/token`: Token exchange endpoint (OpenIddict standard)
- `GET ~/connect/userinfo`: User info endpoint (OpenIddict standard)
- `GET ~/connect/tokeninfo`: Custom token validation endpoint for internal use

**Authorization Flow** (Current Implementation):
1. Client redirects to `/connect/authorize` with OAuth parameters (client_id, redirect_uri, scope, code_challenge, etc.)
2. If user not authenticated: Store ALL OAuth request parameters in memory cache (10-minute TTL), show login form
3. User submits credentials to `/connect/authorize/callback`
4. System authenticates user, retrieves cached OAuth parameters, reconstructs authorization request
5. System creates ClaimsIdentity with user claims (sub, name, email, roles)
6. System calls `SignIn()` with OpenIddict authentication scheme
7. OpenIddict generates authorization code with embedded PKCE data and redirects to client
8. Client exchanges code for tokens at `/connect/token`
9. OpenIddict validates PKCE, generates access token, refresh token, and ID token
10. Client uses access token to call `/connect/userinfo` for user claims

**PKCE Handling**:
- PKCE parameters (code_challenge, code_challenge_method) stored in memory cache with OAuth request
- OpenIddict automatically embeds PKCE data in authorization code payload
- When client exchanges code for tokens, OpenIddict validates code_verifier against embedded code_challenge
- No separate authorization table needed - all data in token payload

**Memory Cache Usage**:
- Key: `oidc_request_params_{requestId}` where requestId is a GUID
- Value: Dictionary<string, string> containing ALL OAuth request parameters
- TTL: 10 minutes (sufficient for user to complete login)
- Cleared after successful authorization or expiration

**Why This Works**:
- OpenIddict's `DisableAuthorizationStorage()` is designed for this exact scenario
- Authorization codes are short-lived (5 minutes) and single-use
- PKCE provides security without needing persistent authorization storage
- Memory cache is sufficient for temporary request parameter storage
- This is a standard pattern for stateless OAuth servers

### Future Vision: Unified Token Strategy

In the future headless OAuth architecture:

**Internal Authentication** (Next.js ↔ C# Backend):
- Use session cookies OR custom JWT tokens
- Simple, fast, no OAuth overhead
- Session management in both Next.js and C# backend

**OAuth Flows** (Third-party clients ↔ C# Backend):
- Use OpenIddict-generated OAuth tokens exclusively
- C# backend handles ALL OAuth logic via OpenIddict
- Next.js acts as OAuth proxy for HTML rendering only
- Tokens issued by C# backend, never by Next.js

**Key Principle**: Keep internal auth simple (custom JWT/sessions), keep OAuth flows spec-compliant (OpenIddict).


## Data Models

### Request Models

All request models are already defined in `Experimental/Models/Requests/AuthRequests.cs`:

- `LoginRequest`: Username, password
- `RegisterRequest`: Username, password, email
- `TotpSetupRequest`: User identity
- `TotpValidateRequest`: Code, temp token
- `WebAuthnRegisterRequest`: Attestation data
- `WebAuthnValidateRequest`: Assertion data, temp token
- `MagicLinkRequest`: Email address
- `OAuthAuthorizeRequest`: Client ID, redirect URI, scope
- `OAuthTokenRequest`: Code, client ID, client secret

### Response Models

All response models are already defined in `Experimental/Models/Responses/AuthResponses.cs`:

- `LoginResult`: Success flag, JWT token, user data, settings, temp token (if 2FA required)
- `RegisterResult`: Success flag, user data
- `TotpSetupResult`: QR code URI, secret key
- `TotpValidationResult`: Success flag, JWT token
- `WebAuthnRegisterResult`: Success flag, credential ID
- `WebAuthnValidationResult`: Success flag, JWT token
- `MagicLinkResult`: Success flag, message
- `OAuthAuthorizeResult`: Authorization code, redirect URI
- `OAuthTokenResult`: Access token, refresh token, expires in
- `OAuthUserInfoResult`: User claims

### Database Models

All database models are already defined in SpacetimeDB schema:

**Authentication Tables**:
- `UserProfile`: User identity, username, email, password hash
- `UserRole`: User-to-role mappings
- `Role`: Role definitions
- `RolePermission`: Role-to-permission mappings
- `Permission`: Permission definitions

**Two-Factor Authentication Tables**:
- `TotpSecret`: TOTP secret keys
- `WebAuthnCredential`: FIDO2 credentials
- `TwoFactorToken`: Temporary 2FA tokens
- `MagicLink`: Magic link tokens

**Settings Tables**:
- `UserSettings`: User authentication preferences

**OAuth Tables**:
- `OpenIddictSpacetimeApplication`: OAuth client applications
- `OpenIddictSpacetimeAuthorization`: OAuth authorizations (**PERMANENTLY DISABLED** - table exists but not used due to unfixable SpacetimeDB validation bugs)
- `OpenIddictSpacetimeToken`: OAuth tokens (access tokens, refresh tokens, ID tokens)
- `OpenIddictSpacetimeScope`: OAuth scopes

**⚠️ Current OAuth Implementation Note**:
- AuthorizationStore is **permanently disabled** and will NOT be re-enabled
- OpenIddict's `DisableAuthorizationStorage()` is used - this is the correct approach
- All OAuth authorization information (scopes, client_id, user consent, PKCE data) is handled via:
  - **Memory cache**: OAuth request parameters stored temporarily (10-minute TTL)
  - **Token payload**: Authorization codes embed PKCE data automatically via OpenIddict
  - **Claims**: User consent and scopes stored in token claims
- This architecture is production-ready and follows OpenIddict's stateless server pattern
- **Why permanent**: SpacetimeDB's validation system has fundamental conflicts with OpenIddict's authorization store requirements that cannot be resolved without major changes to either framework

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system—essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Consistent Architecture Pattern

*For any* endpoint in AuthController, the endpoint SHALL delegate to AuthOrchestrationService and SHALL NOT contain direct database access.

**Validates: Requirements 1.1, 1.2, 1.3, 1.5, 13.1, 13.2, 13.3, 13.4, 13.5**


### Property 2: Service Layer Completeness

*For any* authentication operation, there SHALL exist a corresponding method in either a Business Logic Service or Orchestration Service.

**Validates: Requirements 2.1, 2.2, 2.3, 14.1, 14.2**

### Property 3: No Code Duplication

*For any* authentication helper method (JWT validation, admin check, permission check), there SHALL exist exactly one implementation in the Orchestration Service.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

### Property 4: Backward Compatibility

*For any* existing API endpoint, the HTTP request/response contract SHALL remain identical after refactoring.

**Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5**

### Property 5: Testability

*For any* orchestration method, the method SHALL be testable with mocked dependencies without requiring a real database.

**Validates: Requirements 5.1, 5.2, 5.3**

### Property 6: Feature Flag Control

*For any* refactored endpoint, when the feature flag is disabled, the endpoint SHALL execute the legacy code path unchanged.

**Validates: Requirements 6.1, 6.2, 6.3**

### Property 7: Performance Maintenance

*For any* refactored endpoint, the response time SHALL NOT increase by more than 5% compared to the legacy implementation.

**Validates: Requirements 7.1, 7.2, 7.3**

### Property 8: Security Centralization

*For any* authentication or authorization logic, the logic SHALL exist only in the Orchestration Service, not in the Controller.

**Validates: Requirements 8.1, 8.2, 8.3, 8.4**

### Property 9: Non-Destructive Refactoring

*For any* phase of refactoring (Phases 1-3), the existing AuthController SHALL remain operational and unchanged.

**Validates: Requirements 15.1, 15.2, 15.3, 15.4, 15.5**

### Property 10: Zero Downtime Deployment

*For any* deployment during refactoring, the system SHALL maintain 99.9% uptime and support instant rollback.

**Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5**

## Error Handling

### Error Handling Strategy

**Layered Error Handling**:
1. **Controller Layer**: Catch exceptions, return appropriate HTTP status codes
2. **Orchestration Layer**: Catch service exceptions, log errors, return standardized error results
3. **Service Layer**: Throw domain-specific exceptions with detailed error messages


**Exception Types**:
- `AuthenticationException`: Invalid credentials, user not found
- `AuthorizationException`: Insufficient permissions, invalid token
- `ValidationException`: Invalid input parameters
- `ServiceException`: Service-level errors (database, external services)

**Error Response Format**:
```csharp
public class ErrorResponse
{
    public string Error { get; set; }
    public string Message { get; set; }
    public int StatusCode { get; set; }
    public Dictionary<string, string[]> ValidationErrors { get; set; }
}
```

**Example Error Handling**:
```csharp
// Controller Layer
[HttpPost("login")]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
{
    try
    {
        var result = await _authOrchestrationService.LoginAsync(request.Username, request.Password);
        
        if (!result.Success)
            return Unauthorized(new ErrorResponse { Error = "authentication_failed", Message = result.Message });
        
        return Ok(result);
    }
    catch (ValidationException ex)
    {
        return BadRequest(new ErrorResponse { Error = "validation_error", Message = ex.Message, ValidationErrors = ex.Errors });
    }
    catch (AuthenticationException ex)
    {
        return Unauthorized(new ErrorResponse { Error = "authentication_failed", Message = ex.Message });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error during login");
        return StatusCode(500, new ErrorResponse { Error = "internal_error", Message = "An unexpected error occurred" });
    }
}

// Orchestration Layer
public async Task<LoginResult> LoginAsync(string username, string password)
{
    try
    {
        var user = await _authService.AuthenticateAsync(username, password);
        // ... orchestration logic
        return LoginResult.Success(token, user, settings);
    }
    catch (AuthenticationException ex)
    {
        _logger.LogWarning(ex, "Authentication failed for user {Username}", username);
        return LoginResult.Failed(ex.Message);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error during login orchestration");
        throw new ServiceException("Login orchestration failed", ex);
    }
}
```

### Logging Strategy

**Structured Logging**:
- Use Serilog for structured logging
- Log all authentication attempts (success and failure)
- Log all authorization checks
- Log all service errors with context
- Log performance metrics for monitoring

**Log Levels**:
- `Debug`: Detailed flow information for development
- `Information`: Authentication success, authorization grants
- `Warning`: Authentication failures, authorization denials
- `Error`: Service exceptions, unexpected errors
- `Critical`: System-level failures


## Testing Strategy

### Testing Philosophy

The refactoring follows a **build-first, test-later** approach due to the non-destructive migration strategy:

**Phase 1-2: Build Without Integration Testing (Weeks 1-7)**
- Write unit tests for TwoFactorService and SettingsService (can test in isolation)
- Write unit tests for orchestration methods (can test with mocked dependencies)
- **Cannot perform integration testing**: New orchestration code is not hooked up to AuthController yet
- **Cannot perform end-to-end testing**: No HTTP endpoints calling the new code
- **Limitation**: Can only verify logic correctness through unit tests, not actual behavior
- **Acceptance**: This is intentional - integration testing would require modifying AuthController, which violates the non-destructive approach

**Phase 4: First Integration Testing Possible (Weeks 9-10)**
- AuthController modified to check feature flags
- Can now enable flags in test environment
- **First time integration testing is possible**: New code paths can be exercised via HTTP
- Write integration tests with feature flags enabled
- Write end-to-end tests for critical flows
- Compare behavior between legacy and new code paths

**Phase 5: Production Testing (Post-deployment)**
- Gradual rollout provides real-world testing
- Monitor error rates, performance, user feedback
- Fix issues discovered in production
- Iterate based on actual usage patterns

### Unit Testing

**Test Coverage Goals**:
- 100% coverage for new services (TwoFactorService, SettingsService)
- 100% coverage for new orchestration methods
- 80%+ coverage for existing services (already tested)

**Testing Framework**: xUnit with Moq for mocking

**Example Unit Test**:
```csharp
[Fact]
public async Task LoginAsync_WithValidCredentials_ReturnsSuccessResult()
{
    // Arrange
    var mockAuthService = new Mock<IAuthenticationService>();
    var mockSettingsService = new Mock<ISettingsService>();
    var mockTokenService = new Mock<ITokenService>();
    
    var user = new UserProfile { UserId = Identity.From("user123"), Username = "testuser" };
    var settings = new UserSettings { TotpEnabled = false, WebAuthnEnabled = false };
    var token = "jwt-token-123";
    
    mockAuthService.Setup(s => s.AuthenticateAsync("testuser", "password123"))
        .ReturnsAsync(user);
    mockSettingsService.Setup(s => s.GetOrCreateUserSettingsAsync(user.UserId))
        .ReturnsAsync(settings);
    mockTokenService.Setup(s => s.GenerateToken(user, settings))
        .Returns(token);
    
    var orchestrationService = new AuthOrchestrationService(
        mockAuthService.Object,
        mockSettingsService.Object,
        mockTokenService.Object
    );
    
    // Act
    var result = await orchestrationService.LoginAsync("testuser", "password123");
    
    // Assert
    Assert.True(result.Success);
    Assert.Equal(token, result.Token);
    Assert.Equal(user, result.User);
}

[Fact]
public async Task LoginAsync_WithInvalidCredentials_ReturnsFailedResult()
{
    // Arrange
    var mockAuthService = new Mock<IAuthenticationService>();
    mockAuthService.Setup(s => s.AuthenticateAsync("testuser", "wrongpassword"))
        .ReturnsAsync((UserProfile)null);
    
    var orchestrationService = new AuthOrchestrationService(mockAuthService.Object, null, null);
    
    // Act
    var result = await orchestrationService.LoginAsync("testuser", "wrongpassword");
    
    // Assert
    Assert.False(result.Success);
    Assert.Equal("Invalid credentials", result.Message);
}
```


### Integration Testing

**Integration Test Scope**:
- Test HTTP endpoints with feature flags enabled
- Test database interactions with real SpacetimeDB instance
- Test authentication flows end-to-end
- Compare behavior between legacy and new code paths

**Testing Framework**: xUnit with WebApplicationFactory for in-memory testing

**Example Integration Test**:
```csharp
[Fact]
public async Task Login_WithFeatureFlagEnabled_UsesNewCodePath()
{
    // Arrange
    var factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Enable feature flag for login endpoint
                services.Configure<FeatureFlagOptions>(options =>
                {
                    options.EnableLoginRefactoring = true;
                });
            });
        });
    
    var client = factory.CreateClient();
    var request = new LoginRequest { Username = "testuser", Password = "password123" };
    
    // Act
    var response = await client.PostAsJsonAsync("/api/auth/login", request);
    
    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<LoginResult>();
    Assert.True(result.Success);
    Assert.NotNull(result.Token);
}

[Fact]
public async Task Login_WithFeatureFlagDisabled_UsesLegacyCodePath()
{
    // Arrange
    var factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Disable feature flag for login endpoint
                services.Configure<FeatureFlagOptions>(options =>
                {
                    options.EnableLoginRefactoring = false;
                });
            });
        });
    
    var client = factory.CreateClient();
    var request = new LoginRequest { Username = "testuser", Password = "password123" };
    
    // Act
    var response = await client.PostAsJsonAsync("/api/auth/login", request);
    
    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    // Verify legacy behavior is preserved
}
```

### Performance Testing

**Performance Benchmarks**:
- Response time: < 5% increase compared to legacy
- Database queries: No increase in query count
- Memory usage: < 10% increase

**Testing Framework**: BenchmarkDotNet

**Example Performance Test**:
```csharp
[MemoryDiagnoser]
public class LoginPerformanceBenchmark
{
    private AuthController _legacyController;
    private AuthController _refactoredController;
    
    [GlobalSetup]
    public void Setup()
    {
        // Setup legacy and refactored controllers
    }
    
    [Benchmark(Baseline = true)]
    public async Task<IActionResult> Legacy_Login()
    {
        return await _legacyController.Login(new LoginRequest { Username = "test", Password = "pass" });
    }
    
    [Benchmark]
    public async Task<IActionResult> Refactored_Login()
    {
        return await _refactoredController.Login(new LoginRequest { Username = "test", Password = "pass" });
    }
}
```


## Feature Flag Implementation

### Feature Flag Strategy

**Feature Flag Library**: Custom implementation using configuration-based flags (can be replaced with LaunchDarkly or Unleash later)

**Configuration Modes**: The system supports TWO configuration modes for maximum flexibility:

1. **File-Based Configuration** (appsettings.json) - For static configuration, no rebuild required
2. **Runtime Configuration** (Web UI + API) - For dynamic configuration with hot reload, no rebuild or restart required

**Flag Structure**:
```csharp
public class FeatureFlagOptions
{
    // Authentication endpoints
    public bool EnableLoginRefactoring { get; set; } = false;
    public bool EnableRegisterRefactoring { get; set; } = false;
    
    // TOTP endpoints
    public bool EnableTotpSetupRefactoring { get; set; } = false;
    public bool EnableTotpValidateRefactoring { get; set; } = false;
    public bool EnableTotpEnableRefactoring { get; set; } = false;
    public bool EnableTotpDisableRefactoring { get; set; } = false;
    
    // WebAuthn endpoints
    public bool EnableWebAuthnRegisterRefactoring { get; set; } = false;
    public bool EnableWebAuthnValidateRefactoring { get; set; } = false;
    public bool EnableWebAuthnCredentialsRefactoring { get; set; } = false;
    
    // OAuth endpoints
    public bool EnableOAuthAuthorizeRefactoring { get; set; } = false;
    public bool EnableOAuthTokenRefactoring { get; set; } = false;
    public bool EnableOAuthUserInfoRefactoring { get; set; } = false;
    
    // ... flags for all 56 endpoints
}
```

### Configuration Mode 1: File-Based (appsettings.json)

**Configuration** (appsettings.json):
```json
{
  "FeatureFlags": {
    "EnableLoginRefactoring": false,
    "EnableRegisterRefactoring": false,
    "EnableTotpSetupRefactoring": false
  }
}
```

**Characteristics**:
- Static configuration loaded at application startup
- Changes require application restart (or file watcher with hot reload)
- Suitable for environment-specific configuration (dev, staging, production)
- No database dependency
- Simple and reliable

### Configuration Mode 2: Runtime Configuration (Web UI + API)

**API Endpoints** (Admin-only):
```csharp
// GET /api/admin/feature-flags - List all feature flags and their current state
[HttpGet("api/admin/feature-flags")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> GetFeatureFlags()
{
    var flags = await _featureFlagService.GetAllFlagsAsync();
    return Ok(flags);
}

// PUT /api/admin/feature-flags/{flagName} - Update a specific feature flag
[HttpPut("api/admin/feature-flags/{flagName}")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> UpdateFeatureFlag(string flagName, [FromBody] bool enabled)
{
    await _featureFlagService.UpdateFlagAsync(flagName, enabled);
    return Ok(new { message = "Feature flag updated successfully", flagName, enabled });
}

// POST /api/admin/feature-flags/bulk - Update multiple feature flags at once
[HttpPost("api/admin/feature-flags/bulk")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> BulkUpdateFeatureFlags([FromBody] Dictionary<string, bool> flags)
{
    await _featureFlagService.BulkUpdateFlagsAsync(flags);
    return Ok(new { message = "Feature flags updated successfully", count = flags.Count });
}
```

**Web UI** (Admin Dashboard):
- HTML page at `/admin/feature-flags` with toggle switches for each flag
- Real-time status display (enabled/disabled)
- Bulk enable/disable buttons (e.g., "Enable All TOTP Endpoints")
- Audit log showing who changed what and when
- Confirmation dialogs for critical flags (e.g., OAuth endpoints)

**Implementation**:
```csharp
public interface IFeatureFlagService
{
    Task<Dictionary<string, bool>> GetAllFlagsAsync();
    Task<bool> GetFlagAsync(string flagName);
    Task UpdateFlagAsync(string flagName, bool enabled);
    Task BulkUpdateFlagsAsync(Dictionary<string, bool> flags);
}

public class FeatureFlagService : IFeatureFlagService
{
    private readonly IOptionsMonitor<FeatureFlagOptions> _options;
    private readonly ISpacetimeDBService _spacetimeService;
    private readonly ILogger<FeatureFlagService> _logger;
    
    // Store runtime overrides in SpacetimeDB
    public async Task UpdateFlagAsync(string flagName, bool enabled)
    {
        var conn = await _spacetimeService.GetConnection();
        
        // Store in database for persistence
        conn.Reducers.UpdateFeatureFlag(flagName, enabled);
        
        // Update in-memory cache for immediate effect (hot reload)
        FeatureFlagCache.Set(flagName, enabled);
        
        _logger.LogInformation("Feature flag {FlagName} set to {Enabled}", flagName, enabled);
    }
    
    public async Task<bool> GetFlagAsync(string flagName)
    {
        // Check runtime override first (database)
        if (FeatureFlagCache.TryGet(flagName, out bool cachedValue))
            return cachedValue;
        
        // Fall back to appsettings.json configuration
        var property = typeof(FeatureFlagOptions).GetProperty(flagName);
        if (property != null)
            return (bool)property.GetValue(_options.CurrentValue);
        
        return false; // Default to disabled
    }
}
```

**Hot Reload Mechanism**:
- Feature flag changes take effect IMMEDIATELY without application restart
- Uses in-memory cache + database persistence
- Cache invalidation on update ensures all instances see the change
- Distributed cache (Redis) for multi-instance deployments

**Persistence**:
- Runtime configuration stored in SpacetimeDB `FeatureFlagOverride` table
- Survives application restarts
- Can be reset to appsettings.json defaults via admin UI

**Security**:
- Feature flag management endpoints require Admin role
- Audit logging for all flag changes (who, what, when)
- Rate limiting to prevent abuse
- CSRF protection for web UI

**Characteristics**:
- Dynamic configuration without application restart (hot reload)
- Immediate effect across all application instances
- Suitable for gradual rollout and A/B testing
- Requires database for persistence
- Admin-only access via web UI or API

**Controller Integration**:

The system uses a **dual-controller architecture** with dynamic routing instead of inline feature flag checks:

```csharp
// AuthController.cs (LEGACY - UNTOUCHED LOGIC)
[HttpPost("login")]
[AllowAnonymous]
[LegacyAction(nameof(FeatureFlagOptions.EnableLoginRefactoring))]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
{
    // ... existing 200+ lines of login logic (UNCHANGED) ...
    // This code is NEVER modified - only the attribute is added
}

// AuthControllerRefactored.cs (NEW - CLEAN)
[HttpPost("login")]
[AllowAnonymous]
[RefactoredAction(nameof(FeatureFlagOptions.EnableLoginRefactoring))]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
{
    // Clean orchestration-based implementation
    var result = await _authOrchestrationService.LoginAsync(request.Username, request.Password);
    
    if (!result.Success)
        return Unauthorized(new ApiResponse<object>
        {
            Success = false,
            Message = result.ErrorMessage ?? "Authentication failed"
        });
    
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
```

**Routing Mechanism**:
- ASP.NET Core routing engine evaluates `FeatureFlagActionConstraint` for each action
- When flag is ENABLED: Routes to `AuthControllerRefactored.Login()` (marked with `[RefactoredAction]`)
- When flag is DISABLED: Routes to `AuthController.Login()` (marked with `[LegacyAction]`)
- No code deployment needed to switch between implementations - just toggle the flag

**Benefits**:
- Legacy controller logic never modified (zero risk)
- Clean separation between old and new code
- Easy to test both implementations independently
- Easy cleanup after validation (just delete legacy file)

### Gradual Rollout Plan

**Phase 1: Internal Testing (Week 9)**
- Enable flags in development environment
- Test all endpoints with new code paths
- Fix any issues discovered
- Validate backward compatibility

**Phase 2: Staging Deployment (Week 10)**
- Deploy to staging with all flags disabled
- Enable flags one by one in staging
- Run automated test suite
- Perform manual testing

**Phase 3: Production Rollout (Post-deployment)**
- **Day 1**: Deploy to production with all flags disabled (zero risk)
- **Day 2**: Enable 1 endpoint for 1% of users, monitor for 24 hours
- **Day 3**: If stable, increase to 10% of users
- **Day 5**: If stable, increase to 50% of users
- **Day 7**: If stable, enable for 100% of users
- **Day 14**: Enable next endpoint, repeat process

**Rollback Strategy**:
- Instant rollback: Disable feature flag (takes effect immediately)
- No code deployment needed for rollback
- Monitor error rates, response times, user feedback
- Automatic rollback if error rate increases by > 1%


## Migration Strategy

### Non-Destructive Refactoring Phases

**Phase 1: Service Creation (Weeks 1-2)** - Zero Risk
- Create TwoFactorService in `TicketSalesApp.Services/Implementations/`
- Create SettingsService in `TicketSalesApp.Services/Implementations/`
- Write unit tests for new services
- Register services in DI container
- **AuthController**: UNTOUCHED
- **Risk Level**: ZERO - new code doesn't affect existing functionality

**Phase 2: Orchestration Expansion (Weeks 3-7)** - Zero Risk
- Add ~45 orchestration methods to AuthOrchestrationService
- Implement all business logic coordination
- Write comprehensive unit tests
- Validate orchestration logic in isolation
- **AuthController**: UNTOUCHED
- **Risk Level**: ZERO - orchestration not yet called by controller

**Phase 3: Feature Flag Integration (Week 8)** - Zero Risk
- Add FeatureFlagOptions configuration class
- Configure flags for all 56 endpoints (disabled by default)
- Set up monitoring and dashboards
- Test flag toggling in development environment
- **AuthController**: UNTOUCHED
- **Risk Level**: ZERO - flags exist but not yet used

**Phase 4: Controller Modification (Weeks 9-10)** - Low Risk
- Create `AuthControllerRefactored.cs` with clean orchestration-based implementations
- Create `FeatureFlagActionConstraint.cs` for dynamic routing
- Add `[RefactoredAction]` attributes to all endpoints in `AuthControllerRefactored.cs`
- Add `[LegacyAction]` attributes to all 56 endpoints in legacy `AuthController.cs`
- Inject `IAuthOrchestrationService` and `IOptions<FeatureFlagOptions>` into legacy `AuthController.cs`
- Deploy with all flags disabled
- **AuthController**: MODIFIED (only attributes and DI added, logic UNTOUCHED)
- **AuthControllerRefactored**: CREATED (clean implementation)
- **Risk Level**: LOW - legacy code path still active by default, logic never modified

**Phase 5: Gradual Rollout (Post-deployment)** - Controlled Risk
- Enable flags incrementally (1% → 10% → 50% → 100%)
- Monitor error rates, performance, user feedback
- Rollback instantly if issues detected (just disable flag)
- Iterate and improve based on production data
- **AuthController**: Running legacy code path (flag disabled)
- **AuthControllerRefactored**: Running refactored code path (flag enabled)
- **Routing**: Dynamic selection based on feature flags
- **Risk Level**: CONTROLLED - instant rollback available, both controllers coexist

**Phase 6: Legacy Code Cleanup (After validation)** - Zero Risk
- Delete legacy `AuthController.cs` file (8,293 lines removed)
- Rename `AuthControllerRefactored.cs` to `AuthController.cs`
- Remove `[RefactoredAction]` and `[LegacyAction]` attributes (no longer needed)
- Remove `FeatureFlagActionConstraint.cs` and related routing infrastructure
- KEEP feature flag infrastructure for operational flexibility
- Repurpose feature flags for endpoint availability control (enable/disable endpoints)
- Add feature flag checks to control endpoint availability (503 if disabled)
- **AuthController**: Simplified to ~2,000-2,500 lines (from 8,293)
- **Feature Flags**: Repurposed for operational control (not implementation selection)
- **Risk Level**: ZERO - new code already validated in production for weeks/months

**Why Keep Feature Flags**:
- Operational flexibility: Instantly disable problematic endpoints without deployment
- A/B testing: Test endpoint changes with subset of users
- Emergency response: Quick mitigation for security issues or performance problems
- Gradual rollout: Enable new features incrementally
- Monitoring: Track endpoint usage and performance per flag

### Timeline

**Total Duration**: 10 weeks + gradual rollout

**Week 1-2**: Service creation (TwoFactorService, SettingsService)
**Week 3-7**: Orchestration expansion (45 methods)
**Week 8**: Feature flag integration
**Week 9-10**: Controller modification and testing
**Week 11+**: Gradual production rollout (1-2 months)
**Week 20+**: Legacy code cleanup (after full validation) - delete old controller, rename new one, repurpose feature flags

### Risk Mitigation

**Key Principle**: At no point during Phases 1-3 is the production system at risk. The existing AuthController continues serving all requests normally while the new architecture is built in parallel.

**Risk Mitigation Strategies**:
1. **Non-destructive approach**: Build new code without touching existing code
2. **Feature flags**: Instant rollback capability
3. **Gradual rollout**: Validate with small user percentage first
4. **Comprehensive testing**: Unit, integration, and performance tests
5. **Monitoring**: Real-time error rates, response times, user feedback
6. **Automated rollback**: Disable flags automatically if error rate increases


## Monitoring and Observability

### Metrics to Track

**Performance Metrics**:
- Response time per endpoint (p50, p95, p99)
- Database query count per endpoint
- Memory usage per request
- CPU usage per request

**Error Metrics**:
- Error rate per endpoint
- Error rate by error type (authentication, authorization, validation, service)
- Error rate by feature flag status (legacy vs new)

**Business Metrics**:
- Authentication success rate
- Authentication failure rate by reason
- 2FA usage rate
- OAuth client registrations

**Feature Flag Metrics**:
- Percentage of requests using new code path
- Error rate comparison (legacy vs new)
- Performance comparison (legacy vs new)

### Monitoring Tools

**Application Performance Monitoring**: Application Insights or Prometheus
**Logging**: Serilog with structured logging
**Dashboards**: Grafana or Application Insights dashboards
**Alerting**: Alert on error rate increase > 1%, response time increase > 10%

### Example Monitoring Dashboard

**Dashboard Sections**:
1. **Overview**: Total requests, error rate, average response time
2. **Feature Flags**: Percentage of requests per flag, error rate per flag
3. **Endpoints**: Response time per endpoint, error rate per endpoint
4. **Authentication**: Success rate, failure rate by reason
5. **Performance**: Database queries, memory usage, CPU usage

## Caching Strategy

### Caching Opportunities

**User Settings Cache**:
- Cache user settings for 5 minutes
- Invalidate on settings update
- Reduces database queries by ~50% for authenticated requests

**User Roles and Permissions Cache**:
- Cache user roles and permissions for 10 minutes
- Invalidate on role/permission changes
- Reduces database queries for authorization checks

**OAuth Client Cache**:
- Cache OAuth client data for 30 minutes
- Invalidate on client update/delete
- Reduces database queries for OAuth flows

### Caching Implementation

**Caching Library**: Microsoft.Extensions.Caching.Memory (in-memory) or Redis (distributed)

**Example Caching in Orchestration Service**:
```csharp
public async Task<UserSettings> GetUserSettingsAsync(Identity userId)
{
    var cacheKey = $"user-settings:{userId}";
    
    if (_cache.TryGetValue(cacheKey, out UserSettings cachedSettings))
    {
        _logger.LogDebug("Cache hit for user settings: {UserId}", userId);
        return cachedSettings;
    }
    
    var settings = await _settingsService.GetOrCreateUserSettingsAsync(userId);
    
    _cache.Set(cacheKey, settings, TimeSpan.FromMinutes(5));
    _logger.LogDebug("Cache miss for user settings: {UserId}", userId);
    
    return settings;
}
```

**Cache Invalidation**:
```csharp
public async Task UpdateUserSettingsAsync(Identity userId, UserSettings settings)
{
    await _settingsService.UpdateSettingsAsync(userId, settings);
    
    // Invalidate cache
    var cacheKey = $"user-settings:{userId}";
    _cache.Remove(cacheKey);
    
    _logger.LogInformation("User settings updated and cache invalidated: {UserId}", userId);
}
```


## Rate Limiting Strategy

### Rate Limiting Requirements

**Endpoint-Specific Limits**:
- Login: 5 attempts per minute per IP
- Register: 3 attempts per hour per IP
- TOTP validate: 10 attempts per minute per user
- WebAuthn validate: 10 attempts per minute per user
- OAuth token: 20 requests per minute per client
- OAuth authorize: 10 requests per minute per user

### Rate Limiting Implementation

**Rate Limiting Library**: AspNetCoreRateLimit

**Configuration** (appsettings.json):
```json
{
  "IpRateLimiting": {
    "EnableEndpointRateLimiting": true,
    "StackBlockedRequests": false,
    "RealIpHeader": "X-Real-IP",
    "ClientIdHeader": "X-ClientId",
    "HttpStatusCode": 429,
    "GeneralRules": [
      {
        "Endpoint": "POST:/api/auth/login",
        "Period": "1m",
        "Limit": 5
      },
      {
        "Endpoint": "POST:/api/auth/register",
        "Period": "1h",
        "Limit": 3
      },
      {
        "Endpoint": "POST:/api/auth/totp/validate",
        "Period": "1m",
        "Limit": 10
      }
    ]
  }
}
```

**Middleware Registration** (Program.cs):
```csharp
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

var app = builder.Build();
app.UseIpRateLimiting();
```

## Security Considerations

### Authentication Security

**Password Security**:
- PBKDF2 with SHA-256 for password hashing (already implemented in AuthenticationService)
- Minimum password length: 8 characters
- Password complexity requirements enforced

**Token Security**:
- JWT tokens with RS256 signing algorithm
- Short-lived access tokens (15 minutes)
- Refresh tokens with rotation
- Token revocation support

**Two-Factor Authentication**:
- TOTP with 30-second time window
- WebAuthn with FIDO2 compliance
- Backup codes for account recovery

### Authorization Security

**Role-Based Access Control**:
- Centralized permission checking in orchestration service
- Fine-grained permissions per endpoint
- Admin role with elevated privileges

**OAuth Security**:
- PKCE (Proof Key for Code Exchange) for public clients
- Client secret validation for confidential clients
- Scope-based access control
- Token introspection support

### Audit Logging

**Audit Events**:
- All authentication attempts (success and failure)
- All authorization checks (grant and deny)
- All sensitive operations (password change, 2FA enable/disable)
- All OAuth client operations (register, update, delete)

**Audit Log Format**:
```csharp
public class AuditLog
{
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; }
    public Identity UserId { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
    public bool Success { get; set; }
    public string Details { get; set; }
}
```


## Documentation

### Architecture Documentation

**Document**: `ARCHITECTURE.md` (to be created)

**Contents**:
- Layered architecture overview
- Service responsibilities and boundaries
- Data flow diagrams
- Dependency injection setup
- Error handling patterns
- Testing strategies

### API Documentation

**Document**: OpenAPI/Swagger specification (already exists)

**Updates Needed**:
- Document feature flag behavior
- Document error response formats
- Document rate limiting rules
- Document authentication requirements per endpoint

### Migration Guide

**Document**: `MIGRATION_GUIDE.md` (to be created)

**Contents**:
- Step-by-step refactoring process
- How to add new orchestration methods
- How to add feature flags
- How to test refactored endpoints
- How to monitor rollout
- How to rollback if needed

### Developer Onboarding

**Document**: `DEVELOPER_ONBOARDING.md` (to be created)

**Contents**:
- Project structure overview
- How to run the project locally
- How to run tests
- How to add new authentication methods
- How to debug authentication issues
- Common pitfalls and solutions

## Deployment Strategy

### Deployment Phases

**Phase 1: Development Deployment**
- Deploy to development environment
- Enable all feature flags
- Run automated test suite
- Perform manual testing

**Phase 2: Staging Deployment**
- Deploy to staging environment
- Enable feature flags one by one
- Run automated test suite
- Perform manual testing
- Load testing with production-like data

**Phase 3: Production Deployment**
- Deploy to production with all flags disabled
- Monitor for 24 hours (zero risk - legacy code still running)
- Enable flags gradually (1% → 10% → 50% → 100%)
- Monitor error rates, performance, user feedback
- Rollback instantly if issues detected

### Deployment Checklist

**Pre-Deployment**:
- [ ] All unit tests passing
- [ ] All integration tests passing
- [ ] Performance tests passing
- [ ] Security audit completed
- [ ] Documentation updated
- [ ] Feature flags configured (disabled by default)
- [ ] Monitoring dashboards configured
- [ ] Alerting rules configured

**Deployment**:
- [ ] Deploy to production
- [ ] Verify deployment successful
- [ ] Verify all endpoints responding
- [ ] Verify legacy code path working
- [ ] Monitor for 24 hours

**Post-Deployment**:
- [ ] Enable feature flags gradually
- [ ] Monitor error rates
- [ ] Monitor performance metrics
- [ ] Monitor user feedback
- [ ] Document any issues
- [ ] Iterate and improve


## Success Criteria

### Technical Success Criteria

**Architecture**:
- [ ] 100% of endpoints follow Controller → Orchestration → Services → Database pattern
- [ ] Zero direct database access in AuthController
- [ ] 100% orchestration layer coverage (50 methods)
- [ ] AuthController reduced from 8,293 lines to ~2,000 lines

**Testing**:
- [ ] 100% unit test coverage for new services
- [ ] 100% unit test coverage for orchestration methods
- [ ] 80%+ integration test coverage
- [ ] All performance benchmarks passing

**Quality**:
- [ ] Zero breaking changes to API contracts
- [ ] Response time increase < 5%
- [ ] Database query count unchanged
- [ ] Memory usage increase < 10%

**Security**:
- [ ] Security audit passed
- [ ] All authentication logic centralized
- [ ] All authorization logic centralized
- [ ] Audit logging implemented

### Business Success Criteria

**Reliability**:
- [ ] 99.9% uptime maintained during migration
- [ ] Error rate increase < 0.1%
- [ ] Zero production incidents during rollout
- [ ] Instant rollback capability verified

**Performance**:
- [ ] User-perceived performance unchanged
- [ ] Authentication success rate unchanged
- [ ] 2FA usage rate unchanged

**Maintainability**:
- [ ] Developer onboarding time reduced by 50%
- [ ] Time to add new authentication method reduced by 50%
- [ ] Code review time reduced by 50%

## Future Vision: Frontend-Agnostic Authentication Service

This section outlines the long-term evolution of the authentication system beyond the current refactoring. The goal is to transform the auth service into a truly modular, frontend-agnostic system that can serve any client type (web, mobile, desktop) with a clean API-first architecture.

### Current State vs. Future Vision

**Current State** (Post-Refactoring):
- Clean layered architecture (Controller → Orchestration → Services → Database)
- Mixed rendering: CSHTML views for browsers + JSON APIs for programmatic clients
- Tightly coupled to ASP.NET Core Razor view engine
- OAuth/OIDC with OpenIddict + SpacetimeDB custom stores
- AuthorizationStore permanently disabled (memory cache + token payload approach)

**Future Vision** (6-12 months post-refactoring):
- Pure JSON API backend (no CSHTML, no Razor engine)
- Complete frontend flexibility (Next.js, React, Vue, Blazor WASM, mobile apps)
- Modern frontend tooling and developer experience
- AuthorizationStore remains disabled (current stateless approach is production-ready)
- CDN-hostable frontend, independently scalable backend

### Architecture Evolution

**Current Architecture** (Post-Refactoring):
```
┌─────────────────────────────────────────┐
│  C# Backend (Mixed Rendering)           │
│  - JSON APIs for programmatic clients   │
│  - CSHTML views for browsers            │
│  - Razor view engine                    │
│  - OAuth/OIDC provider                  │
└─────────────────────────────────────────┘
                    ↑
                    │ HTTP/JSON + HTML
                    │
        ┌───────────┴───────────┐
        │                       │
┌───────▼────────┐    ┌────────▼─────────┐
│  Web Browsers  │    │  Avalonia Client │
│  (CSHTML)      │    │  (JSON API)      │
└────────────────┘    └──────────────────┘
```

**Target Architecture** (Future Vision):
```
┌─────────────────────────────────────────┐
│  C# Backend (Pure JSON API)             │
│  - /api/auth/* → JSON only              │
│  - /connect/authorize → JSON only       │
│  - /connect/token → JSON only           │
│  - NO CSHTML, NO Razor engine           │
│  - OAuth/OIDC provider (API-first)      │
└─────────────────────────────────────────┘
                    ↑
                    │ JSON API only
                    │
        ┌───────────┴───────────┬───────────┐
        │                       │           │
┌───────▼────────┐    ┌────────▼─────┐  ┌─▼──────────┐
│  Next.js Web   │    │  Avalonia    │  │  Mobile    │
│  (React/SSR)   │    │  Desktop     │  │  Apps      │
└────────────────┘    └──────────────┘  └────────────┘
```

### Benefits of Frontend-Agnostic Architecture

1. **Separation of Concerns**: Backend focuses purely on auth logic, frontend focuses purely on UX
2. **Framework Flexibility**: Swap CSHTML for Next.js, React, Vue, Svelte, Blazor WASM, or any modern framework
3. **Better Developer Experience**: Modern frontend tooling (Vite, hot reload, TypeScript, Tailwind CSS)
4. **API-First Design**: Forces clean API contracts, easier to add mobile apps and third-party integrations
5. **Modern Frontend**: React Server Components, streaming SSR, better performance, better accessibility
6. **Easier Maintenance**: Frontend devs work in their preferred stack, backend devs work in C#
7. **Independent Scaling**: Frontend can be CDN-hosted globally, backend scales independently
8. **Multi-Platform**: Same backend serves web, mobile, desktop, IoT devices

### Migration Path (Phased Approach)

**Phase 1: Current Refactoring** (Weeks 1-10)
- Separate business logic into services
- Build orchestration layer
- Eliminate direct database access from controllers
- **Result**: Clean architecture, but still mixed rendering

**Phase 2: API-First Endpoints** (Months 1-2 post-refactoring)
- Ensure ALL auth flows have JSON API endpoints
- Add content negotiation (detect browser vs API client)
- Keep CSHTML views for backward compatibility
- **Result**: Dual-mode system (HTML + JSON)

**Phase 3: Extract Rendering** (Months 3-4 post-refactoring)
- Move all HTML rendering to `IViewRenderingService` interface
- Create abstraction layer for view rendering
- Prepare for view engine swap
- **Result**: Rendering is pluggable

**Phase 4: Frontend Decoupling** (Months 5-8 post-refactoring)
- Build Next.js frontend that calls C# JSON APIs
- Run Next.js side-by-side with CSHTML views
- Gradual migration of users to Next.js
- **Result**: Two frontends, one backend

**Phase 5: CSHTML Deprecation** (Months 9-12 post-refactoring)
- Remove Razor view engine
- Remove CSHTML views
- Pure JSON API backend
- **Result**: Complete frontend-agnostic architecture

### Headless OAuth Implementation Strategies

The most complex aspect of frontend-agnostic architecture is OAuth/OIDC, which traditionally expects HTML rendering for login/consent pages. This section presents two implementation strategies based on the current OAuth implementation.

#### Current OAuth Implementation

**Existing Endpoints**:
- `GET/POST ~/connect/authorize`: Authorization endpoint with embedded HTML login form
- `POST ~/connect/authorize/callback`: Custom callback endpoint for form submission
- `POST ~/connect/token`: Standard OpenIddict token exchange
- `GET ~/connect/userinfo`: Standard OpenIddict user info endpoint

**Current Flow**:
1. Client redirects to `/connect/authorize` with OAuth parameters
2. If user not authenticated: C# backend stores OAuth params in memory cache (10-minute TTL), returns HTML login form
3. User submits credentials to `/connect/authorize/callback`
4. C# authenticates user, retrieves cached params, reconstructs OAuth request
5. C# creates ClaimsIdentity and calls `SignIn()` with OpenIddict scheme
6. OpenIddict generates authorization code with embedded PKCE data
7. Client exchanges code for tokens at `/connect/token`

**Key Implementation Details**:
- Memory cache stores OAuth request parameters (10-minute TTL)
- PKCE data embedded in authorization code payload by OpenIddict
- AuthorizationStore permanently disabled (stateless approach)
- HTML form rendered inline in C# controller using string interpolation

#### Strategy A: Hybrid OAuth (Recommended for Short-term)

Add JSON API support alongside existing HTML rendering for backward compatibility.

**Implementation Approach**:
**Content Negotiation in `/connect/authorize`**:
```csharp
[HttpGet("~/connect/authorize")]
[HttpPost("~/connect/authorize")]
public async Task<IActionResult> Authorize()
{
    var request = HttpContext.GetOpenIddictServerRequest();
    
    if (!User.Identity.IsAuthenticated)
    {
        var requestId = Guid.NewGuid().ToString();
        _cache.Set($"oidc_request_params_{requestId}", /* store params */, TimeSpan.FromMinutes(10));
        
        // NEW: Content negotiation
        if (Request.Headers["Accept"].Contains("application/json"))
        {
            // Return JSON for API clients (Next.js, mobile apps)
            return Ok(new 
            { 
                requiresLogin = true,
                loginUrl = $"/api/auth/oauth/login?request_id={requestId}",
                requestId = requestId,
                clientName = await GetClientNameAsync(request.ClientId),
                scopes = request.GetScopes()
            });
        }
        
        // Existing: Return HTML for browsers
        return Content(RenderOAuthLoginForm(requestId, clientName, scopes), "text/html");
    }
    
    // Continue existing OAuth flow...
}
```

**New JSON API Endpoints**:
- `POST /api/auth/oauth/login`: Authenticate user and return success/failure
- `GET /api/auth/oauth/consent`: Check if consent needed, return client info and scopes
- `POST /api/auth/oauth/consent`: Grant consent and return success

**Benefits**:
- Minimal changes to existing working OAuth flow
- Backward compatible (browsers still get HTML)
- Enables Next.js and mobile clients
- No changes to OpenIddict configuration
- **Implementation Effort**: 2-3 weeks

#### Strategy B: Fully Headless OAuth (Long-term Goal)

C# backend becomes pure JSON API, Next.js handles ALL HTML rendering.

**Architecture**:
```
User Browser → Next.js Server → C# Backend (JSON only)
     ↓              ↓                  ↓
  HTML pages   OAuth proxy      Auth logic + DB
```

**Key Changes**:

1. **Remove HTML rendering from C# backend**:
   - Convert `/connect/authorize` to JSON-only endpoint
   - Remove all CSHTML views and Razor engine
   - All endpoints return JSON responses

2. **Add JSON API endpoints**:
   - `POST /api/auth/oauth/login`: Authenticate user for OAuth flow
   - `GET /api/auth/oauth/consent`: Check consent status
   - `POST /api/auth/oauth/consent`: Grant consent

3. **Next.js OAuth proxy**:
   - `/oauth/authorize`: Proxy route that handles OAuth flow logic
   - `/login`: React page for authentication
   - `/consent`: React page for consent
   - Store OAuth request parameters in Next.js session

**Benefits**:
- C# backend is pure JSON API (no HTML rendering)
- Next.js handles ALL user-facing pages
- Modern React components, Tailwind CSS, better UX
- Complete separation of concerns

**Trade-offs**:
- Requires significant refactoring of OAuth flow
- Two servers to deploy and maintain
- More complex CORS configuration
- Session management in both Next.js and C#
- **Implementation Effort**: 2-3 months

#### Recommended Migration Path

**Short-term** (Next 6 months): Implement Strategy A (Hybrid OAuth)
- Add JSON API endpoints alongside existing HTML rendering
- Enable Next.js and mobile clients
- Maintain backward compatibility with browsers
- Low risk, incremental improvement

**Long-term** (12+ months): Migrate to Strategy B (Fully Headless)
- Once Next.js frontend is stable and proven
- Remove HTML rendering from C# backend
- Pure JSON API architecture
- Complete frontend flexibility

### Implementation Roadmap

**Phase 1: Add JSON API Support** (2-3 weeks)
- [ ] Add content negotiation to `/connect/authorize`
- [ ] Create `POST /api/auth/oauth/login` endpoint
- [ ] Create `GET /api/auth/oauth/consent` endpoint
- [ ] Create `POST /api/auth/oauth/consent` endpoint
- [ ] Update memory cache to support both HTML and JSON flows
- [ ] Add CORS configuration for Next.js origin

**Phase 2: Build Next.js OAuth Proxy** (4-6 weeks)
- [ ] Create Next.js OAuth proxy routes
- [ ] Build React login page that calls C# JSON API
- [ ] Build React consent page that calls C# JSON API
- [ ] Implement session management in Next.js
- [ ] Handle OAuth redirects and state management
- [ ] Test with Avalonia client (should work unchanged)

**Phase 3: Deprecate HTML Rendering** (2-3 weeks)
- [ ] Remove inline HTML generation from `/connect/authorize`
- [ ] Remove CSHTML views
- [ ] Remove Razor view engine dependency
- [ ] Update documentation

**Total Effort**: 8-12 weeks for complete headless OAuth implementation

### Technical Requirements

**1. AuthorizationStore Status**:
- **Permanently disabled** - SpacetimeDB validation bugs are unfixable without major framework changes
- Current implementation uses `DisableAuthorizationStorage()` - this is the correct and final approach
- PKCE data stored in authorization code payload (OpenIddict handles automatically)
- OAuth request parameters stored in memory cache (10-minute TTL)
- Consent management handled through token claims
- This architecture is production-ready and will remain as-is

**2. Session Management**:
- C# backend: ASP.NET Core session middleware with SpacetimeDB backing
- Next.js: Server-side session management (iron-session or similar)
- Store OAuth request parameters temporarily (5-10 minute TTL)

**3. Content Negotiation**:
- Detect client type via `Accept` header or client registration
- Return JSON for SPAs, HTML for traditional clients (during migration)
- Consistent API contracts regardless of client type

**4. CORS Configuration**:
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowNextJs", policy =>
    {
        policy.WithOrigins("https://frontend.example.com")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Important for cookies
    });
});
```

**5. Cookie Configuration**:
```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.SameSite = SameSiteMode.None; // Allow cross-origin
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // HTTPS only
        options.Cookie.Domain = ".example.com"; // Share across subdomains
    });
```

### Post-Refactoring Enhancements

These improvements can be added incrementally after the main refactoring:

**Caching Layer**:
- Implement distributed caching with Redis
- Cache user settings, roles, permissions
- Reduce database load by 50%
- Cache invalidation on updates

**Rate Limiting**:
- Per-user rate limiting
- Per-client rate limiting for OAuth
- Adaptive rate limiting based on load
- Brute force attack detection

**Monitoring & Observability**:
- Distributed tracing with OpenTelemetry
- Real-time alerting with PagerDuty
- Anomaly detection for authentication patterns
- Performance metrics and dashboards

**Security Enhancements**:
- Device fingerprinting
- Geolocation-based anomaly detection
- Account lockout after failed attempts
- Suspicious activity alerts

**Performance Optimizations**:
- Connection pooling for SpacetimeDB
- Query optimization
- Lazy loading for user data
- Response compression

**New Authentication Methods**:
- Social login (Google, Facebook, GitHub)
- Biometric authentication (Face ID, Touch ID)
- Hardware security keys (YubiKey)
- SMS-based 2FA
- Email-based 2FA

### Why This Approach Works

This frontend-agnostic architecture is a **proven pattern** used by major auth providers:

- **Auth0**: Pure API backend, supports any frontend framework
- **Keycloak**: API-first design, customizable frontend
- **Supabase**: Headless auth, works with any framework
- **Firebase Auth**: SDK-based, framework-agnostic

The current refactoring (separating business logic into services and creating orchestration layer) is already **70% of the way there**. The remaining 30% is:
- Adding JSON API endpoints for all auth flows
- Implementing content negotiation
- Building a modern frontend (Next.js or similar)
- Deprecating CSHTML views

### Timeline & Feasibility

**Current Refactoring**: 10 weeks (Phases 1-3 of main design)
**API-First Endpoints**: 2 months (add JSON APIs, keep CSHTML)
**Frontend Decoupling**: 6 months (build Next.js, run side-by-side)
**CSHTML Deprecation**: 3 months (remove Razor, pure API)

**Total Timeline**: 12-15 months from start of refactoring to fully frontend-agnostic

**Feasibility**: HIGH - This is a well-trodden path. The main challenges are:
1. OAuth flow complexity (solved by hybrid approach with content negotiation)
2. AuthorizationStore limitations (already solved with stateless approach)
3. Maintaining backward compatibility during migration (solved by gradual rollout)

### Strategic Recommendation

**Short-term** (Current refactoring): Focus on clean architecture, keep mixed rendering
**Medium-term** (6 months post-refactoring): Add JSON APIs, implement content negotiation
**Long-term** (12 months post-refactoring): Build Next.js frontend, deprecate CSHTML

This phased approach minimizes risk while moving toward the ultimate goal of a truly frontend-agnostic authentication service.

### Alternative Architecture: Elysia JS as Authentication Gateway

This section explores using **Elysia JS** (a modern TypeScript web framework) as an authentication gateway or proxy layer in front of the C# backend. This represents an alternative architectural evolution that could complement or replace parts of the current system.

#### What is Elysia JS?

Elysia is a fast, ergonomic TypeScript web framework built on Bun runtime with first-class support for:
- **Type-safe routing**: Compile-time type checking for routes and parameters
- **Plugin ecosystem**: JWT, OAuth2 Server, Session, CORS, Helmet, Rate Limiting
- **OpenAPI generation**: Auto-generates Swagger/Scalar documentation
- **End-to-end type safety**: Eden client provides type-safe API consumption
- **Performance**: ~10k req/s on single core (comparable to Go/Rust frameworks)
- **Built-in auth**: JWT plugin, OAuth2 server plugin, session management
- **WebSocket support**: Native WebSocket handling for real-time authentication

#### Why Consider Elysia JS?

**Current Pain Points** (Based on Actual Codebase):
1. **OpenIddict + SpacetimeDB complexity**:
   - Custom stores (TokenStore, AuthorizationStore, ApplicationStore) with 3-tier ID mapping
   - ReferenceId → InternalId → DatabaseId mapping requires ConcurrentDictionary "safety nets"
   - AuthorizationStore permanently disabled due to SpacetimeDB validation bugs
   - PKCE data stored in token payload instead of authorization table
   - Polling-based confirmation (50 attempts × 100ms) after reducer calls

2. **SpacetimeDB reactive state challenges**:
   - "Lawn sync problem": Database updates not immediately visible to queries
   - Race conditions between reducer execution and query results
   - Manual cache management to work around reactive state issues
   - Example from TokenStore.cs: `_referenceIdToInternalId`, `_internalIdToReferenceId`, `_internalIdToDatabaseId` static dictionaries

3. **Data Protection key management**:
   - Persistent key storage required for PKCE decryption across requests
   - Keys stored in `DataProtectionKeys/` folder
   - Without stable keys, OpenIddict cannot decrypt authorization code payloads

4. **Mixed authentication schemes**:
   - JWT Bearer (custom tokens with SymmetricSecurityKey)
   - OpenIddict validation (for OAuth tokens)
   - Cookie authentication (for web pages)
   - Complex policy configuration to support all three

5. **WebSocket authentication limitations**:
   - Current implementation: Query parameter token passing (`?access_token=...`)
   - No OIDC-over-WebSocket support
   - SignalR hubs require separate authentication configuration

**Elysia JS Benefits**:
1. **Lightweight**: Purpose-built for auth/proxy use cases
2. **Modern DX**: TypeScript, hot reload, modern tooling
3. **Production-grade OIDC**: `oidc-provider` (v9.x) integrated via `elysiajs/node` adapter for full OIDC compliance
4. **Easy integration**: Can proxy to existing C# backend for business logic
5. **Independent scaling**: Auth layer scales separately from business logic
6. **Native WebSocket support**: Built-in WebSocket handling for real-time auth

**Note on OAuth packages**:
- `elysia-oauth2` (v2+) is for **consuming** third-party OAuth2 providers (e.g., login with Google/GitHub), not for implementing your own OAuth server
- For implementing a full OIDC provider, use `oidc-provider` (v9.x) mounted on http.Server before Elysia's routing for `/oidc/*` endpoints

#### Architecture Option 1: Elysia as Authentication Gateway

Replace ASP.NET Core authentication layer with Elysia JS, keep C# for business logic.

```
┌─────────────────────────────────────────────────────────────┐
│  Elysia JS Authentication Gateway (Port 3000)               │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Authentication Endpoints                             │  │
│  │  - POST /auth/login → JWT generation                 │  │
│  │  - POST /auth/register → User creation               │  │
│  │  - POST /auth/totp/verify → TOTP validation          │  │
│  │  - POST /auth/webauthn/validate → WebAuthn           │  │
│  └──────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ OAuth/OIDC Provider (OpenID Connect)                 │  │
│  │  - GET /connect/authorize → Authorization flow       │  │
│  │  - POST /connect/token → Token exchange              │  │
│  │  - GET /connect/userinfo → User claims               │  │
│  │  - GET /.well-known/openid-configuration             │  │
│  └──────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Proxy Layer                                          │  │
│  │  - Forward authenticated requests to C# backend      │  │
│  │  - Add user context to headers                       │  │
│  │  - Handle rate limiting and caching                  │  │
│  └──────────────────────────────────────────────────────┘  │
└────────────────────┬────────────────────────────────────────┘
                     │
                     │ HTTP/JSON (authenticated)
                     ↓
┌─────────────────────────────────────────────────────────────┐
│  C# Backend (Business Logic Only) (Port 5000)               │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Business Logic Services                              │  │
│  │  - UserService, BusService, RouteService             │  │
│  │  - TicketService, MaintenanceService                 │  │
│  │  - NO authentication logic (delegated to Elysia)    │  │
│  └──────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ SpacetimeDB Integration                              │  │
│  │  - Database queries and mutations                    │  │
│  │  - Real-time subscriptions                           │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

**CRITICAL: Elysia as Standalone Auth Server**:
- Elysia JS connects DIRECTLY to SpacetimeDB (no C# backend calls for auth data)
- Elysia handles ALL authentication: JWT, OAuth/OIDC, TOTP, WebAuthn, QR codes
- C# backend MUST validate all tokens: the C# service must validate token signature, issuer, audience, and expiry before accepting requests from Elysia (or require documented mTLS plus signed assertions)
- SpacetimeDB is accessed by BOTH Elysia (for auth data) and C# (for business data)

**Request Flow**:
1. Client → Elysia JS `/auth/login` with credentials
2. Elysia validates credentials against SpacetimeDB **DIRECTLY** (using SpacetimeDB TypeScript SDK)
3. Elysia generates JWT token and returns to client
4. Client → Elysia JS `/api/buses` with JWT token
5. Elysia validates JWT, extracts user context
6. Elysia proxies request to C# backend with JWT token or signed assertion in headers
7. C# backend independently validates JWT/signed assertion (signature, issuer, audience, expiry) before processing business logic and returns response
8. Elysia returns response to client

**Benefits**:
- **Separation of concerns**: Auth logic in Elysia, business logic in C#
- **Independent scaling**: Scale auth layer separately from business logic
- **Modern auth infrastructure**: Use production-grade OIDC provider (oidc-provider) for token issuance
- **Simplified C# backend**: Remove ALL authentication code, focus purely on business logic
- **Better performance**: Elysia handles auth faster than ASP.NET Core
- **Direct database access**: Elysia reads/writes auth data directly to SpacetimeDB without C# proxy

**Important Note on OAuth2 Plugins**:
- **For implementing an OAuth/OIDC server** (token issuance): Use `oidc-provider` (production-grade OIDC implementation)
- **For consuming third-party OAuth providers** (e.g., "Login with Google"): Use `elysia-oauth2` (OAuth2 client plugin)
- Do NOT use `elysia-oauth2` to replace OpenIddict - it's a client library, not a server implementation

**Trade-offs**:
- **Two runtimes**: Bun (Elysia) + .NET (C# backend)
- **Deployment complexity**: Two services to deploy and monitor
- **Learning curve**: Team needs TypeScript/Elysia expertise
- **Migration effort**: Significant refactoring required
- **Dual SpacetimeDB access**: Both Elysia and C# connect to SpacetimeDB independently

#### Architecture Option 2: Elysia as OAuth/OIDC Proxy

**Use Case**: Keep existing C# authentication system (OpenIddict + all auth flows), use Elysia ONLY to proxy OAuth/OIDC endpoints to JavaScript frontends that need standards-compliant OAuth2/OIDC discovery.

**Why This Exists**: If the current C# authentication system must remain operational (e.g., for legacy clients, gradual migration, or organizational requirements), this option allows adding OAuth/OIDC capabilities without replacing the entire auth system.

**CRITICAL: Proxy Architecture**:
- Elysia PROXIES OpenIddict/OIDC endpoints from C# backend to JavaScript frontends
- C# backend retains ALL authentication logic (OpenIddict, TOTP, WebAuthn, QR, etc.)
- Elysia acts as a translation layer for JavaScript clients that need OAuth2/OIDC discovery
- C# backend continues to access SpacetimeDB directly for all auth operations

```
┌─────────────────────────────────────────────────────────────┐
│  Elysia JS OAuth Proxy (Port 3000)                          │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ OAuth/OIDC Proxy Endpoints                           │  │
│  │  - GET /connect/authorize → Proxies to C# OpenIddict│  │
│  │  - POST /connect/token → Proxies to C# OpenIddict    │  │
│  │  - GET /connect/userinfo → Proxies to C# OpenIddict  │  │
│  │  - GET /.well-known/openid-configuration → Proxies  │  │
│  │  - Translates requests for JavaScript clients       │  │
│  └──────────────────────────────────────────────────────┘  │
└────────────────────┬────────────────────────────────────────┘
                     │
                     │ Proxy all auth requests to C# backend
                     ↓
┌─────────────────────────────────────────────────────────────┐
│  C# Backend (Full Authentication + Business Logic)          │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ OpenIddict OAuth/OIDC Server                         │  │
│  │  - Authorization endpoints                           │  │
│  │  - Token issuance and validation                     │  │
│  │  - Discovery document generation                     │  │
│  └──────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Authentication Endpoints                             │  │
│  │  - POST /api/auth/login → Custom JWT                 │  │
│  │  - POST /api/auth/register → User creation           │  │
│  │  - All existing auth flows (TOTP, WebAuthn, QR)     │  │
│  └──────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Business Logic Services                              │  │
│  │  - UserService, BusService, RouteService             │  │
│  └──────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ SpacetimeDB Integration                              │  │
│  │  - Direct access for auth AND business data         │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

**Use Case**: Provide OAuth/OIDC endpoints to JavaScript frontends while keeping the entire C# authentication system operational.

**Benefits**:
- **OAuth/OIDC for JS clients**: JavaScript frontends get standards-compliant OAuth2/OIDC
- **Zero C# changes**: C# backend keeps ALL existing auth logic unchanged
- **Gradual adoption**: JavaScript clients can migrate to OAuth while other clients use existing endpoints
- **Minimal risk**: Elysia is just a proxy, all auth logic remains in proven C# code

**Important Note**:
- This is a PROXY pattern, not a replacement
- Elysia does NOT implement auth - it forwards requests to C# OpenIddict
- C# backend remains the authoritative authentication server

**Complete Proxy Implementation**:
```typescript
import { Elysia } from 'elysia'
import { cors } from '@elysiajs/cors'

// Elysia OAuth/OIDC Proxy - Forwards to existing C# OpenIddict endpoints
const app = new Elysia()
  .use(cors({
    origin: ['https://app.bru-avtopark.com', 'http://localhost:5000'],
    credentials: true
  }))

  // Health check
  .get('/health', () => ({ 
    status: 'ok', 
    service: 'elysia-oauth-proxy',
    backend: 'csharp-openiddict'
  }))

  // Proxy OpenIddict discovery document
  .get('/.well-known/openid-configuration', async ({ request }) => {
    const response = await fetch('https://localhost:5001/.well-known/openid-configuration', {
      method: 'GET',
      headers: {
        'Accept': 'application/json'
      }
    })
    
    if (!response.ok) {
      return new Response(JSON.stringify({ error: 'Discovery endpoint unavailable' }), {
        status: 503,
        headers: { 'Content-Type': 'application/json' }
      })
    }

    // Return discovery document as-is from C# backend
    return response
  })

  // Proxy OAuth authorization endpoint (GET and POST)
  // C# endpoint: ~/connect/authorize in AuthController.cs
  .get('/connect/authorize', async ({ request, query }) => {
    const queryString = new URLSearchParams(query as Record<string, string>).toString()
    const response = await fetch(`https://localhost:5001/connect/authorize?${queryString}`, {
      method: 'GET',
      headers: {
        'Accept': 'text/html,application/xhtml+xml,application/xml',
        'Cookie': request.headers.get('cookie') || ''
      },
      redirect: 'manual' // Handle redirects manually
    })

    return response
  })

  .post('/connect/authorize', async ({ request, body }) => {
    const rawBody = await request.text()
    const response = await fetch('https://localhost:5001/connect/authorize', {
      method: 'POST',
      headers: {
        'Content-Type': request.headers.get('content-type') || 'application/x-www-form-urlencoded',
        'Cookie': request.headers.get('cookie') || '',
        'Accept': 'text/html,application/xhtml+xml,application/xml'
      },
      body: rawBody,
      redirect: 'manual'
    })

    return response
  })

  // Proxy OAuth authorization callback
  // C# endpoint: ~/connect/authorize/callback in AuthController.cs
  .post('/connect/authorize/callback', async ({ request, body }) => {
    const rawBody = await request.text()
    const response = await fetch('https://localhost:5001/connect/authorize/callback', {
      method: 'POST',
      headers: {
        'Content-Type': request.headers.get('content-type') || 'application/x-www-form-urlencoded',
        'Cookie': request.headers.get('cookie') || ''
      },
      body: rawBody,
      redirect: 'manual'
    })

    return response
  })

  // Proxy OAuth token endpoint
  // C# endpoint: ~/connect/token in AuthController.cs
  .post('/connect/token', async ({ request, body }) => {
    const response = await fetch('https://localhost:5001/connect/token', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded',
        'Accept': 'application/json'
      },
      body: new URLSearchParams(body as Record<string, string>).toString()
    })

    if (!response.ok) {
      return new Response(await response.text(), {
        status: response.status,
        headers: { 'Content-Type': 'application/json' }
      })
    }

    return response
  })

  // Proxy OAuth userinfo endpoint
  // C# endpoint: ~/connect/userinfo in AuthController.cs (if implemented)
  .get('/connect/userinfo', async ({ request }) => {
    const authHeader = request.headers.get('authorization')
    if (!authHeader) {
      return new Response(JSON.stringify({ error: 'unauthorized' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' }
      })
    }

    const response = await fetch('https://localhost:5001/connect/userinfo', {
      method: 'GET',
      headers: {
        'Authorization': authHeader,
        'Accept': 'application/json'
      }
    })

    return response
  })

  // Proxy token validation endpoint (custom endpoint for BaseController)
  // C# endpoint: ~/connect/tokeninfo in AuthController.cs
  .get('/connect/tokeninfo', async ({ request }) => {
    const authHeader = request.headers.get('authorization')
    if (!authHeader) {
      return new Response(JSON.stringify({ error: 'unauthorized' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' }
      })
    }

    const response = await fetch('https://localhost:5001/connect/tokeninfo', {
      method: 'GET',
      headers: {
        'Authorization': authHeader,
        'Accept': 'application/json'
      }
    })

    return response
  })

  // Proxy all other API requests to C# backend
  .all('/api/*', async ({ request }) => {
    const url = new URL(request.url)
    const response = await fetch(`https://localhost:5001${url.pathname}${url.search}`, {
      method: request.method,
      headers: request.headers as HeadersInit,
      body: request.method !== 'GET' && request.method !== 'HEAD' ? await request.text() : undefined
    })

    return response
  })

  .listen(3000, () => {
    console.log('🔄 Elysia OAuth Proxy running on http://localhost:3000')
    console.log('📋 Proxying to C# OpenIddict: https://localhost:5001')
    console.log('🔐 OAuth Endpoints:')
    console.log('   - GET  /.well-known/openid-configuration')
    console.log('   - GET  /connect/authorize')
    console.log('   - POST /connect/authorize')
    console.log('   - POST /connect/authorize/callback')
    console.log('   - POST /connect/token')
    console.log('   - GET  /connect/userinfo')
    console.log('   - GET  /connect/tokeninfo')
  })
```

**Key Points**:
- All `/connect/*` endpoints proxy directly to C# AuthController
- No authentication logic in Elysia - pure passthrough
- Cookies and headers forwarded to maintain session state
- Redirects handled manually to preserve OAuth flow
- C# backend remains authoritative for all auth decisions

**Trade-offs**:
- **Two auth systems**: Elysia for OAuth, C# for everything else
- **Complexity**: Managing two authentication layers
- **Limited benefit**: Only helps with OAuth, not other auth flows

---

### Architecture Comparison Summary

| Aspect | Option 1: Full Auth Server | Option 2: OAuth Proxy | Option 3: Hybrid |
|--------|----------------------------|----------------------|------------------|
| **Elysia Role** | Standalone auth server | Proxy to C# OpenIddict | New features only |
| **SpacetimeDB Access** | **Direct** (TypeScript SDK) | Via C# backend | Mixed |
| **C# Backend Role** | Business logic only | Full auth + business | Full auth + business |
| **Auth Implementation** | Elysia (oidc-provider) | C# (OpenIddict) | Both |
| **Migration Effort** | High (full rewrite) | Low (proxy layer) | Medium (gradual) |
| **Deployment** | 2 services | 2 services | 2 services |
| **Best For** | Greenfield or full modernization | Legacy system compatibility | Incremental adoption |

**Key Architectural Decisions**:

1. **Option 1 (Full Auth Server)** - Future Vision:
   - Elysia becomes THE authentication server
   - Elysia connects DIRECTLY to SpacetimeDB using TypeScript SDK
   - NO C# backend calls for authentication data
   - C# backend becomes a pure business logic API
   - C# backend MUST validate JWT tokens (verify signature, issuer, audience, expiry) OR enforce mTLS with strict network isolation and signed internal assertions

2. **Option 2 (OAuth Proxy)** - Compatibility Layer:
   - Elysia PROXIES requests to existing C# OpenIddict endpoints
   - C# backend retains ALL authentication logic
   - Used when C# auth system must remain operational
   - Provides OAuth/OIDC to JavaScript clients without changing C# code
   - C# backend validates tokens as it currently does (OpenIddict validation)

3. **Option 3 (Hybrid)** - Gradual Migration:
   - Mix of both approaches
   - New features in Elysia, existing features in C#
   - Allows incremental migration over time
   - C# backend validates tokens for its own endpoints; Elysia validates for its endpoints

#### Architecture Option 3: Hybrid Approach

Use Elysia for new features, keep C# for existing features.

```
┌─────────────────────────────────────────────────────────────┐
│  Elysia JS (New Features)                                   │
│  - OAuth/OIDC provider (replaces OpenIddict)                │
│  - Rate limiting and caching                                │
│  - WebSocket proxy for SpacetimeDB real-time                │
└────────────────────┬────────────────────────────────────────┘
                     │
                     │ Coexist
                     ↓
┌─────────────────────────────────────────────────────────────┐
│  C# Backend (Existing Features)                             │
│  - All existing auth flows (Login, TOTP, WebAuthn, etc.)   │
│  - Business logic services                                  │
│  - SpacetimeDB integration                                  │
└─────────────────────────────────────────────────────────────┘
```

**Use Case**: Add Elysia for new capabilities without disrupting existing system.

**Benefits**:
- **Zero risk**: Existing system unchanged
- **Incremental adoption**: Add Elysia features gradually
- **Best of both worlds**: C# for business logic, Elysia for auth/proxy

**Trade-offs**:
- **Operational complexity**: Two services to maintain
- **Unclear boundaries**: Which features go where?

#### Implementation Example: Elysia OIDC Server

For production OIDC implementation, use `oidc-provider` (v9.x) - the industry-standard library used by Auth0, Okta, and other major identity providers.

**Installation**:
```bash
bun add oidc-provider
bun add @elysiajs/node
```

**Why oidc-provider?**:
- **Battle-tested**: Used in production by major identity providers
- **Full OIDC spec**: ID tokens, discovery endpoints, userinfo, token introspection
- **PKCE support**: Built-in support for public clients
- **Flexible adapters**: Works with any database (including SpacetimeDB via custom adapter)

**Note**: `elysia-oauth2` (v2+) is designed for **consuming** third-party OAuth2 providers, not for implementing your own OAuth server.

**Complete Implementation**:
```typescript
import { Elysia, t } from 'elysia'
import Provider from 'oidc-provider'
import { node } from '@elysiajs/node'

// Custom SpacetimeDB adapter for oidc-provider
// This adapter connects Elysia DIRECTLY to SpacetimeDB for auth data storage
import { DbConnection } from 'spacetimedb'

class SpacetimeDBAdapter {
  private spacetimeDB: DbConnection

  constructor(private name: string) {
    // PRODUCTION: Direct SpacetimeDB connection using new builder pattern
    this.spacetimeDB = DbConnection.builder()
      .withUri(process.env.SPACETIMEDB_HOST || 'localhost:3000')
      .withDatabaseName(process.env.SPACETIMEDB_MODULE || 'bru-avtopark')
      .withToken(process.env.SPACETIMEDB_TOKEN)
      .withLifecycleCallbacks({
        onConnect: () => console.log('Connected to SpacetimeDB'),
        onDisconnect: () => console.log('Disconnected from SpacetimeDB')
      })
      .build()
  }

  async upsert(id: string, payload: any, expiresIn: number) {
    // PRODUCTION: Direct SpacetimeDB reducer call
    await this.spacetimeDB.reducers.store_oidc_token(
      this.name,
      id,
      JSON.stringify(payload),
      BigInt(Date.now() + (expiresIn * 1000))
    )
  }

  async find(id: string) {
    // PRODUCTION: Direct SpacetimeDB table query
    const tokens = Array.from(this.spacetimeDB.db.OpenIddictSpacetimeToken.iter())
      .filter(token => token.TokenType === this.name && token.TokenId === id)
    if (tokens.length === 0) return undefined
    return JSON.parse(tokens[0].Payload)
  }

  async findByUserCode(userCode: string) {
    // PRODUCTION: Direct SpacetimeDB table query
    const tokens = Array.from(this.spacetimeDB.db.OpenIddictSpacetimeToken.iter())
      .filter(token => {
        const payload = JSON.parse(token.Payload)
        return payload.userCode === userCode
      })
    if (tokens.length === 0) return undefined
    return JSON.parse(tokens[0].Payload)
  }

  async findByUid(uid: string) {
    // PRODUCTION: Direct SpacetimeDB table query
    const tokens = Array.from(this.spacetimeDB.db.OpenIddictSpacetimeToken.iter())
      .filter(token => token.TokenType === this.name && token.Uid === uid)
    if (tokens.length === 0) return undefined
    return JSON.parse(tokens[0].Payload)
  }

  async destroy(id: string) {
    // PRODUCTION: Direct SpacetimeDB reducer call
    await this.spacetimeDB.reducers.delete_oidc_token(
      this.name,
      id
    )
  }

  async revokeByGrantId(grantId: string) {
    // PRODUCTION: Direct SpacetimeDB reducer call
    await this.spacetimeDB.reducers.revoke_oidc_tokens_by_grant(grantId)
  }

  async consume(id: string) {
    // PRODUCTION: Direct SpacetimeDB reducer call
    await this.spacetimeDB.reducers.consume_oidc_token(
      this.name,
      id
    )
  }
}

// Configure oidc-provider with SpacetimeDB adapter
const oidc = new Provider('https://auth.bru-avtopark.com', {
  adapter: SpacetimeDBAdapter,
  clients: [
    {
      client_id: 'avalonia-desktop-client',
      client_secret: process.env.AVALONIA_CLIENT_SECRET,
      redirect_uris: ['http://localhost:5000/callback', 'bruavtopark://callback'],
      response_types: ['code'],
      grant_types: ['authorization_code', 'refresh_token'],
      token_endpoint_auth_method: 'client_secret_post',
    },
    {
      client_id: 'web-spa-client',
      // Public client - no secret
      redirect_uris: ['https://app.bru-avtopark.com/callback'],
      response_types: ['code'],
      grant_types: ['authorization_code', 'refresh_token'],
      token_endpoint_auth_method: 'none', // PKCE required for public clients
    },
  ],
  pkce: {
    required: (ctx, client) => {
      // Enforce PKCE for public clients (no client_secret)
      return !client.clientSecret
    },
  },
  features: {
    devInteractions: { enabled: false }, // Use custom login pages
    rpInitiatedLogout: { enabled: true },
    revocation: { enabled: true },
    introspection: { enabled: true },
    resourceIndicators: { enabled: true }, // For API scopes
  },
  findAccount: async (ctx, id) => {
    // PRODUCTION: Fetch user directly from SpacetimeDB
    const spacetimeDB = DbConnection.builder()
      .withUri(process.env.SPACETIMEDB_HOST || 'localhost:3000')
      .withDatabaseName('bru-avtopark')
      .withToken(process.env.SPACETIMEDB_TOKEN)
      .build()

    const users = Array.from(spacetimeDB.db.UserProfile.iter())
      .filter(user => user.UserId === parseInt(id))
    if (users.length === 0) return undefined

    const user = users[0]
    return {
      accountId: id,
      async claims(use, scope) {
        return {
          sub: id,
          email: user.Email,
          name: user.FullName || user.Login,
          preferred_username: user.Login,
          email_verified: user.EmailVerified,
          // Add custom claims based on scope
          ...(scope.includes('profile') && {
            phone_number: user.PhoneNumber,
            phone_number_verified: user.PhoneVerified,
          }),
          ...(scope.includes('roles') && {
            roles: user.Roles || [],
            permissions: user.Permissions || [],
          }),
        }
      },
    }
  },
  // Custom login/consent pages
  interactions: {
    url(ctx, interaction) {
      return `/auth/interaction/${interaction.uid}`
    },
  },
})

// Elysia application with oidc-provider integration
// FULL STANDALONE AUTH SERVER - Handles ALL authentication
import { Elysia } from 'elysia'
import { cors } from '@elysiajs/cors'
import { jwt } from '@elysiajs/jwt'
import { DbConnection } from 'spacetimedb'
import Provider from 'oidc-provider'

// Initialize SpacetimeDB connection using official SDK
const spacetimeDB = DbConnection.builder()
  .withUri(process.env.SPACETIMEDB_HOST || 'localhost:3000')
  .withDatabaseName('bru-avtopark')
  .withToken(process.env.SPACETIMEDB_TOKEN)
  .build()

const app = new Elysia()
  .use(cors({
    origin: ['https://app.bru-avtopark.com', 'http://localhost:5000'],
    credentials: true
  }))
  .use(jwt({
    name: 'jwt',
    secret: process.env.JWT_SECRET!
  }))

  // Health check
  .get('/health', () => ({ 
    status: 'ok', 
    service: 'elysia-standalone-auth-server',
    spacetimedb: spacetimeDB.isConnected ? 'connected' : 'disconnected'
  }))

  //===========================================
  // AUTHENTICATION ENDPOINTS (Direct SpacetimeDB)
  //===========================================

  // Login with username/password
  .post('/auth/login', async ({ body, jwt, set }) => {
    const { username, password } = body as { username: string; password: string }
    
    // Query SpacetimeDB directly for user
    const users = spacetimeDB.db.UserProfile.filter(u => u.Login === username)
    if (users.length === 0) {
      set.status = 401
      return { error: 'Invalid credentials' }
    }

    const user = users[0]
    
    // Verify password using Bun's built-in bcrypt
    const isValid = await Bun.password.verify(password, user.PasswordHash)
    if (!isValid) {
      set.status = 401
      return { error: 'Invalid credentials' }
    }

    // Check if 2FA is enabled
    if (user.TotpEnabled || user.WebAuthnEnabled) {
      // Return challenge token for 2FA
      const challengeToken = await jwt.sign({
        sub: user.UserId.toString(),
        type: '2fa_challenge',
        exp: Math.floor(Date.now() / 1000) + (5 * 60) // 5 minutes
      })

      return {
        requires_2fa: true,
        challenge_token: challengeToken,
        methods: {
          totp: user.TotpEnabled,
          webauthn: user.WebAuthnEnabled
        }
      }
    }

    // Generate access token
    const accessToken = await jwt.sign({
      sub: user.UserId.toString(),
      username: user.Login,
      email: user.Email,
      roles: user.Roles || [],
      permissions: user.Permissions || [],
      exp: Math.floor(Date.now() / 1000) + (60 * 60 * 24) // 24 hours
    })

    // Log authentication event to SpacetimeDB
    await spacetimeDB.call('log_auth_event', {
      user_id: user.UserId,
      event_type: 'login',
      success: true,
      ip_address: '', // Extract from request
      user_agent: ''  // Extract from request
    })

    return {
      access_token: accessToken,
      token_type: 'Bearer',
      expires_in: 86400,
      user: {
        id: user.UserId,
        username: user.Login,
        email: user.Email,
        roles: user.Roles || []
      }
    }
  })

  // Verify TOTP code
  .post('/auth/totp/verify', async ({ body, jwt, set }) => {
    const { challenge_token, code } = body as { challenge_token: string; code: string }
    
    // Verify challenge token
    const challenge = await jwt.verify(challenge_token)
    if (!challenge || challenge.type !== '2fa_challenge') {
      set.status = 401
      return { error: 'Invalid challenge token' }
    }

    const userId = parseInt(challenge.sub)
    
    // Get user's TOTP secret from SpacetimeDB
    const users = spacetimeDB.db.UserProfile.filter(u => u.UserId === userId)
    if (users.length === 0) {
      set.status = 401
      return { error: 'User not found' }
    }

    const user = users[0]
    
    // Verify TOTP code (implement TOTP verification logic)
    const isValid = await verifyTOTP(user.TotpSecret, code)
    if (!isValid) {
      set.status = 401
      return { error: 'Invalid TOTP code' }
    }

    // Generate access token
    const accessToken = await jwt.sign({
      sub: user.UserId.toString(),
      username: user.Login,
      email: user.Email,
      roles: user.Roles || [],
      permissions: user.Permissions || [],
      exp: Math.floor(Date.now() / 1000) + (60 * 60 * 24)
    })

    return {
      access_token: accessToken,
      token_type: 'Bearer',
      expires_in: 86400
    }
  })

  // WebAuthn authentication challenge
  .post('/auth/webauthn/challenge', async ({ body, jwt, set }) => {
    const { challenge_token } = body as { challenge_token: string }
    
    const challenge = await jwt.verify(challenge_token)
    if (!challenge || challenge.type !== '2fa_challenge') {
      set.status = 401
      return { error: 'Invalid challenge token' }
    }

    const userId = parseInt(challenge.sub)
    
    // Get user's WebAuthn credentials from SpacetimeDB
    const credentials = spacetimeDB.db.WebAuthnCredential.filter(c => c.UserId === userId)
    
    // Generate WebAuthn challenge
    const webauthnChallenge = crypto.randomUUID()
    
    // Store challenge in SpacetimeDB
    await spacetimeDB.call('store_webauthn_challenge', {
      user_id: userId,
      challenge: webauthnChallenge,
      expires_at: BigInt(Date.now() + (5 * 60 * 1000))
    })

    return {
      challenge: webauthnChallenge,
      credentials: credentials.map(c => ({
        id: c.CredentialId,
        type: 'public-key'
      }))
    }
  })

  // WebAuthn authentication verification
  .post('/auth/webauthn/verify', async ({ body, jwt, set }) => {
    const { challenge_token, credential } = body as { 
      challenge_token: string; 
      credential: any 
    }
    
    // Verify challenge token and WebAuthn assertion
    // (Implementation details omitted for brevity)
    
    // Generate access token on success
    const accessToken = await jwt.sign({
      sub: credential.userId.toString(),
      // ... other claims
      exp: Math.floor(Date.now() / 1000) + (60 * 60 * 24)
    })

    return {
      access_token: accessToken,
      token_type: 'Bearer',
      expires_in: 86400
    }
  })

  // QR Code authentication - Generate QR
  .post('/auth/qr/generate', async ({ jwt }) => {
    // Generate unique QR session ID
    const sessionId = crypto.randomUUID()
    
    // Store QR session in SpacetimeDB
    await spacetimeDB.call('create_qr_session', {
      session_id: sessionId,
      status: 'pending',
      expires_at: BigInt(Date.now() + (5 * 60 * 1000)) // 5 minutes
    })

    return {
      session_id: sessionId,
      qr_data: `bruavtopark://auth/${sessionId}`,
      expires_in: 300
    }
  })

  // QR Code authentication - Poll status
  .get('/auth/qr/status/:sessionId', async ({ params, jwt, set }) => {
    const { sessionId } = params
    
    // Query QR session from SpacetimeDB
    const sessions = spacetimeDB.db.QRAuthSession.filter(s => s.SessionId === sessionId)
    if (sessions.length === 0) {
      set.status = 404
      return { error: 'Session not found' }
    }

    const session = sessions[0]
    
    if (session.Status === 'approved' && session.UserId) {
      // Get user details
      const users = spacetimeDB.db.UserProfile.filter(u => u.UserId === session.UserId)
      if (users.length === 0) {
        set.status = 404
        return { error: 'User not found' }
      }

      const user = users[0]
      
      // Generate access token
      const accessToken = await jwt.sign({
        sub: user.UserId.toString(),
        username: user.Login,
        email: user.Email,
        roles: user.Roles || [],
        exp: Math.floor(Date.now() / 1000) + (60 * 60 * 24)
      })

      return {
        status: 'approved',
        access_token: accessToken,
        token_type: 'Bearer',
        expires_in: 86400
      }
    }

    return {
      status: session.Status // 'pending', 'denied', 'expired'
    }
  })

  // User registration
  .post('/auth/register', async ({ body, set }) => {
    const { username, email, password, fullName } = body as {
      username: string;
      email: string;
      password: string;
      fullName?: string;
    }

    // Check if user already exists
    const existingUsers = spacetimeDB.db.UserProfile.filter(
      u => u.Login === username || u.Email === email
    )
    if (existingUsers.length > 0) {
      set.status = 409
      return { error: 'User already exists' }
    }

    // Hash password
    const passwordHash = await Bun.password.hash(password)

    // Create user in SpacetimeDB
    await spacetimeDB.call('register_user', {
      login: username,
      email: email,
      password_hash: passwordHash,
      full_name: fullName || username,
      phone_number: null,
      roles: ['user'], // Default role
      permissions: []
    })

    return {
      success: true,
      message: 'User registered successfully'
    }
  })

  //===========================================
  // BUSINESS LOGIC PROXY (to C# backend)
  //===========================================

  // Proxy to C# backend for business logic
  // C# backend accepts JWTs issued by Elysia after performing full validation
  // (signature verification, claim checks, and expiration) or via mTLS + signed internal assertions
  .all('/api/*', async ({ request, headers, jwt, set }) => {
    const authHeader = headers.authorization
    if (!authHeader?.startsWith('Bearer ')) {
      set.status = 401
      return { error: 'Unauthorized' }
    }

    // Verify JWT token
    const token = authHeader.substring(7)
    const payload = await jwt.verify(token)
    if (!payload) {
      set.status = 401
      return { error: 'Invalid token' }
    }

    // Build safe headers - explicitly whitelist and set server-generated identity headers
    // NOTE: The original Authorization header is intentionally NOT forwarded to the C# backend.
    // The C# backend trusts the X-User-* headers set here (derived from the verified JWT),
    // not the raw Bearer token from the client. This prevents token replay attacks.
    const safeHeaders: Record<string, string> = {
      'Content-Type': headers['content-type'] || 'application/json',
      'Accept': headers['accept'] || '*/*',
      // Server-generated identity headers from verified JWT
      'X-User-Id': payload.sub,
      'X-User-Username': payload.username,
      'X-User-Email': payload.email,
      'X-User-Roles': JSON.stringify(payload.roles),
      'X-User-Permissions': JSON.stringify(payload.permissions),
      // Allowlist: only the above headers are forwarded; all other client headers are dropped
    }

    // Forward to C# backend with safe headers only
    const url = new URL(request.url)
    const response = await fetch(`http://csharp-backend:5000${url.pathname}${url.search}`, {
      method: request.method,
      headers: safeHeaders,
      body: request.method !== 'GET' && request.method !== 'HEAD' ? await request.text() : undefined
    })

    return response
  })

// Mount oidc-provider at server level
// Pattern: Server-level interception (Pattern B - fallback only, bypasses Elysia middleware)
// RECOMMENDED: Use Pattern A (Elysia app.fetch + Fetch↔Node shim) instead.
// See "Approach 1: Elysia app.fetch + Fetch↔Node Shim" section below for the canonical pattern.
app.listen(3000, (server) => {
  const httpServer = server.server as any

  if (!httpServer) {
    console.error('Could not access underlying Node.js server')
    return
  }

  // Intercept requests BEFORE they reach Elysia routing
  const originalListener = httpServer.listeners('request')[0]
  httpServer.removeAllListeners('request')

  httpServer.on('request', (req: any, res: any) => {
    if (req.url?.startsWith('/oidc') || req.url?.startsWith('/.well-known/openid-configuration')) {
      // Route to oidc-provider
      oidc.callback()(req, res, (err: any) => {
        if (err) {
          res.statusCode = 500
          res.end(JSON.stringify({ error: err.message }))
        } else {
          res.statusCode = 404
          res.end('Not Found')
        }
      })
    } else {
      // Route to Elysia
      originalListener(req, res)
    }
  })

  console.log('🚀 Elysia Standalone Auth Server running on http://localhost:3000')
  console.log('📊 SpacetimeDB:', spacetimeDB.isConnected ? 'Connected' : 'Disconnected')
  console.log('')
  console.log('🔐 Authentication Endpoints:')
  console.log('   - POST /auth/login')
  console.log('   - POST /auth/register')
  console.log('   - POST /auth/totp/verify')
  console.log('   - POST /auth/webauthn/challenge')
  console.log('   - POST /auth/webauthn/verify')
  console.log('   - POST /auth/qr/generate')
  console.log('   - GET  /auth/qr/status/:sessionId')
  console.log('')
  console.log('📋 OIDC Endpoints:')
  console.log('   - GET  /.well-known/openid-configuration')
  console.log('   - GET  /oidc/authorize')
  console.log('   - POST /oidc/authorize')
  console.log('   - POST /oidc/token')
  console.log('   - GET  /oidc/userinfo')
  console.log('')
  console.log('🔄 Business API Proxy: /api/* → C# Backend')
})

// Helper function for TOTP verification
async function verifyTOTP(secret: string, code: string): Promise<boolean> {
  // Implement TOTP verification using a library like @levminer/speakeasy
  // This is a placeholder
  return true
}
       

// Alternative approach 2: Fetch-based shim (simpler but adds overhead)
// Convert Elysia Request ↔ Node.js req/res for oidc-provider compatibility
/*
app.all('/oidc/*', async ({ request }) => {
  // Create Node.js-like req/res objects from Fetch Request
  const { toNodeRequest, toFetchResponse } = await import('./node-shim')

  return new Promise((resolve) => {
    const nodeReq = toNodeRequest(request)
    const nodeRes = {
      statusCode: 200,
      headers: {},
      setHeader(name: string, value: string) {
        this.headers[name] = value
      },
      end(body: string) {
        resolve(toFetchResponse(this.statusCode, this.headers, body))
      }
    }

    oidc.callback()(nodeReq, nodeRes)
  })
})
*/
```

**Key Features of oidc-provider**:

1. **Production-grade**: Used by Auth0, Okta, and other major identity providers
2. **Full OIDC compliance**: ID tokens, discovery endpoints, userinfo, token introspection
3. **PKCE support**: Built-in support for public clients (mobile apps, SPAs)
4. **Flexible adapters**: Works with any storage backend (SpacetimeDB, PostgreSQL, Redis)
5. **Extensive features**: Dynamic client registration, session management, consent flows
6. **Active maintenance**: Regular security patches and updates

**Integration with SpacetimeDB**:

The `SpacetimeDBAdapter` class shown above demonstrates how oidc-provider integrates with SpacetimeDB. In the **production/standalone architecture** (Architecture Option 1), Elysia connects DIRECTLY to SpacetimeDB using the TypeScript SDK with no C# backend involvement for auth data. The adapter methods (`upsert`, `find`, `findByUserCode`, `findByUid`, `destroy`, `revokeByGrantId`, `consume`) call SpacetimeDB reducers and query tables directly.

**IMPORTANT**: The C# backend proxy pattern shown in the adapter code (with `useCSharpProxyForMigration` flag) is ONLY for gradual migration scenarios. In production standalone mode, all adapter methods use direct SpacetimeDB access:
- `upsert` → calls `spacetimeDB.call('store_oidc_token', ...)`
- `find` → queries `spacetimeDB.db.OpenIddictSpacetimeToken.filter(...)`
- `destroy` → calls `spacetimeDB.call('delete_oidc_token', ...)`
- etc.

The C# proxy approach is documented separately in the "Migration Bridge" appendix below for teams that need to migrate gradually from the current C# OpenIddict implementation.

**Comparison with OpenIddict**:

| Feature | OpenIddict (C#) | oidc-provider (Node.js) |
|---------|-----------------|-------------------------|
| **Language** | C# | TypeScript/JavaScript |
| **Runtime** | .NET 8.0 | Node.js 18+/Bun 1.0+ |
| **OAuth2 Spec** | ✅ Full compliance | ✅ Full compliance |
| **OIDC Support** | ✅ Full OIDC 1.0 | ✅ Full OIDC 1.0 |
| **PKCE** | ✅ Built-in (RFC 7636) | ✅ Built-in (RFC 7636) |
| **Storage** | Custom stores (IOpenIddictTokenStore, IOpenIddictAuthorizationStore, IOpenIddictApplicationStore) | Adapter-based (single interface) |
| **SpacetimeDB Integration** | ⚠️ Complex: 3-tier ID mapping (ReferenceId→InternalId→DatabaseId), ConcurrentDictionary safety nets, 50-attempt polling loops, AuthorizationStore permanently disabled due to validation bugs | ✅ Simpler: Direct adapter with async/await, no ID mapping, no polling, works with SpacetimeDB reactive state |
| **Authorization Storage** | ⚠️ Permanently disabled (`DisableAuthorizationStorage()`) - unfixable SpacetimeDB validation conflicts | ✅ Works with adapter (or can be disabled if needed) |
| **Data Protection Keys** | ⚠️ Required for PKCE payload encryption/decryption, must persist to disk (`DataProtectionKeys/` folder) | ✅ Not required - oidc-provider handles encryption internally |
| **Memory Cache Usage** | ✅ Used for OAuth request parameters (10-minute TTL) | ✅ Can use same pattern or store in adapter |
| **Performance** | ~3-5k req/s (ASP.NET Core overhead) | ~8-12k req/s (Bun/Node.js) |
| **Developer Experience** | ⚠️ Complex: Custom stores, ID mapping, polling confirmations, Data Protection setup, SpacetimeDB workarounds | ✅ Simpler: Single adapter interface, well-documented, fewer edge cases |
| **Discovery Endpoint** | ✅ Auto-generated (`/.well-known/openid-configuration`) | ✅ Auto-generated (`/.well-known/openid-configuration`) |
| **Token Introspection** | ✅ Built-in (`/connect/introspect`) | ✅ Built-in (configurable endpoint) |
| **Refresh Tokens** | ✅ Full support | ✅ Full support |
| **Device Flow** | ✅ Supported | ✅ Supported |
| **Dynamic Client Registration** | ⚠️ Manual implementation required | ✅ Built-in support |
| **Session Management** | ⚠️ Manual implementation | ✅ Built-in support |
| **Production Readiness** | ✅ Mature, widely used in .NET ecosystem | ✅ Battle-tested (used by Auth0, Okta, Keycloak) |
| **Current Issues** | ⚠️ AuthorizationStore disabled, complex workarounds for SpacetimeDB, polling-based confirmations, 3-tier ID mapping | ✅ No known blockers for SpacetimeDB integration |
| **Migration Complexity** | N/A (current implementation) | ⚠️ High: Requires TypeScript expertise, dual runtime (Bun + .NET), adapter implementation |

**Note**: `oidc-provider` is a production-grade OIDC implementation. For **consuming** third-party OAuth2 providers (e.g., "Login with Google"), use `elysia-oauth2` (v2+) instead.

#### SpacetimeDB Integration with Elysia

Elysia can integrate with SpacetimeDB in two ways:

**Option 1: Direct SpacetimeDB Client**
```typescript
import { DbConnection } from 'spacetimedb'

// Initialize SpacetimeDB connection using canonical builder pattern
const spacetimeDB = DbConnection.builder()
  .withUri(process.env.SPACETIMEDB_HOST || 'localhost:3000')
  .withDatabaseName(process.env.SPACETIMEDB_MODULE || 'bru-avtopark')
  .withToken(process.env.SPACETIMEDB_TOKEN)
  .withLifecycleCallbacks({
    onConnect: () => console.log('Connected to SpacetimeDB'),
    onDisconnect: () => console.log('Disconnected from SpacetimeDB')
  })
  .build()

app.state('spacetimeDB', spacetimeDB)

// Use in handlers
app.post('/auth/login', async ({ body, spacetimeDB }) => {
  // Query SpacetimeDB tables directly
  const users = Array.from(spacetimeDB.db.UserProfile.iter())
    .filter(user => user.Login === body.username)
  
  if (users.length === 0) {
    return { error: 'User not found' }
  }
  
  const user = users[0]
  // ... authentication logic
})
```

**Option 2: Proxy to C# Backend**
```typescript
// Elysia delegates to C# backend for all SpacetimeDB operations
app.post('/auth/login', async ({ body }) => {
  const response = await fetch('http://csharp-backend:5000/api/auth/validate', {
    method: 'POST',
    body: JSON.stringify(body)
  })
  
  if (response.ok) {
    const user = await response.json()
    // Generate JWT in Elysia
    const token = await jwt.sign({ sub: user.id, roles: user.roles })
    return { token, user }
  }
  
  return { error: 'Invalid credentials' }
})
```

#### Integrating oidc-provider with Elysia

Since oidc-provider is built for Node.js and expects `req`/`res` objects, while Elysia uses modern Fetch API `Request`/`Response` objects, we need an integration layer. There are two approaches:

**Approach 1: Elysia app.fetch + Fetch↔Node Shim** (Recommended - Canonical Pattern)

OIDC traffic flows through Elysia's Fetch-based routing. The Node.js compatibility shim intercepts at the server level to bridge oidc-provider's req/res expectations with Elysia's Fetch API, but the interception happens WITHIN Elysia's route handlers, allowing Elysia middleware and features to apply.

**Architecture Flow**:
```
Client Request → Elysia Middleware (CORS, JWT, etc.) → Elysia Route Handler → 
Node.js Shim (Fetch→req/res conversion) → oidc-provider → 
Node.js Shim (res→Fetch conversion) → Elysia Response
```

**Key Point**: The Node.js shim does server-level interception magic to make oidc-provider compatible with Elysia, but it's invoked FROM WITHIN Elysia route handlers, so Elysia's middleware chain and features still apply to OIDC endpoints.

**Benefits**:
- ✅ Elysia middleware (CORS, JWT, rate limiting) applies to OIDC endpoints
- ✅ Type safety across all routes
- ✅ Single request flow through Elysia makes debugging easier
- ✅ Can use Elysia features (hooks, guards, decorators) on OIDC endpoints
- ✅ Unified logging and monitoring

**Implementation**:

```typescript
import { Elysia } from 'elysia'
import Provider from 'oidc-provider'
import { Readable } from 'stream'

// Configure oidc-provider
const oidc = new Provider('http://localhost:3000', {
  adapter: SpacetimeDBAdapter,
  clients: [...],
  // ... oidc-provider configuration
})

// Helper: Convert Elysia Request to Node.js req/res for oidc-provider
async function handleOidcRequest(request: Request): Promise<Response> {
  return new Promise((resolve) => {
    // Create Node.js-compatible request object
    const url = new URL(request.url)
    const nodeReq = {
      method: request.method,
      url: url.pathname + url.search,
      headers: Object.fromEntries(request.headers.entries()),
      body: request.body ? Readable.from(request.body) : undefined,
    } as any

    // Create Node.js-compatible response object
    const chunks: Buffer[] = []
    const nodeRes = {
      statusCode: 200,
      headers: {} as Record<string, string>,
      setHeader(name: string, value: string | string[]) {
        this.headers[name.toLowerCase()] = Array.isArray(value) ? value.join(', ') : value
      },
      getHeader(name: string) {
        return this.headers[name.toLowerCase()]
      },
      write(chunk: any) {
        chunks.push(Buffer.from(chunk))
      },
      end(chunk?: any) {
        if (chunk) chunks.push(Buffer.from(chunk))
        const body = Buffer.concat(chunks).toString()
        
        // Convert Node.js response back to Fetch Response
        resolve(new Response(body, {
          status: this.statusCode,
          headers: this.headers
        }))
      }
    } as any

    // Call oidc-provider handler
    oidc.callback()(nodeReq, nodeRes, (err: any) => {
      if (err) {
        resolve(new Response(JSON.stringify({ error: err.message }), {
          status: 500,
          headers: { 'Content-Type': 'application/json' }
        }))
      }
    })
  })
}

// Elysia application with OIDC routes
const app = new Elysia()
  .use(cors({ origin: '*', credentials: true }))
  
  // OIDC endpoints handled by Elysia routes, forwarded to oidc-provider via shim
  .all('/.well-known/openid-configuration', async ({ request }) => {
    return await handleOidcRequest(request)
  })
  
  .all('/oidc/auth', async ({ request }) => {
    return await handleOidcRequest(request)
  })
  
  .all('/oidc/token', async ({ request }) => {
    return await handleOidcRequest(request)
  })
  
  .all('/oidc/userinfo', async ({ request }) => {
    return await handleOidcRequest(request)
  })
  
  .all('/oidc/interaction/:uid', async ({ request }) => {
    return await handleOidcRequest(request)
  })
  
  // Regular Elysia routes for authentication
  .post('/auth/login', async ({ body, jwt }) => {
    // Direct SpacetimeDB access for authentication
    // ...
  })
  
  .listen(3000)
```

**Approach 2: Server-Level Interception Before Elysia** (Fallback-Only Pattern)

OIDC traffic is intercepted at the Node.js HTTP server level BEFORE reaching Elysia routing. This approach completely bypasses Elysia for OIDC endpoints.

**Architecture Flow**:
```
Client Request → Node.js HTTP Server → 
  [if /oidc/* → oidc-provider directly] OR 
  [if other → Elysia Middleware → Elysia Handler]
```

**Key Difference**: The interception happens BEFORE Elysia sees the request, so Elysia middleware and features don't apply to OIDC endpoints.

**Trade-offs**:
- ✅ Simpler integration (no Fetch↔Node.js conversion needed)
- ✅ Potentially better performance (one less conversion layer)
- ⚠️ Bypasses Elysia middleware for OIDC endpoints (no CORS, no rate limiting, etc.)
- ⚠️ No type safety for OIDC routes
- ⚠️ Two separate request paths (harder to debug)
- ⚠️ Cannot use Elysia features on OIDC endpoints

**Implementation**:

```typescript
import { Elysia } from 'elysia'
import Provider from 'oidc-provider'

// Configure oidc-provider
const oidc = new Provider('http://localhost:3000', {
  adapter: SpacetimeDBAdapter,
  clients: [...],
})

// Elysia application
const app = new Elysia()
  .use(cors({ origin: '*', credentials: true }))
  
  // Regular Elysia routes
  .post('/auth/login', async ({ body, jwt }) => {
    // Direct SpacetimeDB access
  })
  
  .listen(3000, (server) => {
    const httpServer = server.server as any

    if (!httpServer) {
      console.error('Could not access underlying Node.js server')
      return
    }

    // Intercept requests BEFORE they reach Elysia routing
    const originalListener = httpServer.listeners('request')[0]
    httpServer.removeAllListeners('request')

    httpServer.on('request', (req: any, res: any) => {
      if (req.url?.startsWith('/oidc') || req.url?.startsWith('/.well-known/openid-configuration')) {
        // Route to oidc-provider (bypasses Elysia)
        oidc.callback()(req, res, () => {
          res.statusCode = 404
          res.end('Not Found')
        })
      } else {
        // Route to Elysia
        originalListener(req, res)
      }
    })

    console.log('🚀 Elysia Auth Server running on http://localhost:3000')
  })
```

**Recommendation**: Use **Approach 1** (Elysia app.fetch + Fetch↔Node Shim) as the canonical/primary pattern for production. The unified middleware and type safety benefits outweigh the small performance overhead of the Fetch↔Node.js conversion. Use **Approach 2** (Server-Level Interception) only as a fallback if you need maximum performance and don't require Elysia middleware on OIDC endpoints.

**Full Integration Example**:

```typescript
import { Elysia } from 'elysia'
import Provider from 'oidc-provider'

// Custom SpacetimeDB adapter for oidc-provider
class SpacetimeDBAdapter {
  constructor(private name: string, private spacetimeClient: any) {}
  
  async upsert(id: string, payload: any, expiresIn: number) {
    // PRODUCTION: Direct SpacetimeDB access via client
    await this.spacetimeClient.call('StoreOidcToken', {
      type: this.name,
      id,
      payload: JSON.stringify(payload),
      expiresAt: Date.now() + (expiresIn * 1000)
    })
  }
  
  async find(id: string) {
    // PRODUCTION: Direct SpacetimeDB query
    const result = await this.spacetimeClient.query(`
      SELECT * FROM OidcTokens WHERE type = ? AND id = ?
    `, [this.name, id])
    if (!result || result.length === 0) return undefined
    return JSON.parse(result[0].payload)
  }
  
  async findByUserCode(userCode: string) {
    const result = await this.spacetimeClient.query(`
      SELECT * FROM OidcTokens WHERE userCode = ?
    `, [userCode])
    if (!result || result.length === 0) return undefined
    return JSON.parse(result[0].payload)
  }
  
  async findByUid(uid: string) {
    const result = await this.spacetimeClient.query(`
      SELECT * FROM OidcTokens WHERE type = ? AND uid = ?
    `, [this.name, uid])
    if (!result || result.length === 0) return undefined
    return JSON.parse(result[0].payload)
  }
  
  async destroy(id: string) {
    await this.spacetimeClient.call('DeleteOidcToken', {
      type: this.name,
      id
    })
  }
  
  async revokeByGrantId(grantId: string) {
    await this.spacetimeClient.call('RevokeOidcTokensByGrantId', {
      grantId
    })
  }
  
  async consume(id: string) {
    await this.spacetimeClient.call('ConsumeOidcToken', {
      type: this.name,
      id
    })
  }
}

// ============================================================================
// C# PROXY MIGRATION BRIDGE (Use only during migration from OpenIddict)
// ============================================================================
// This section shows how to call ACTUAL AuthController endpoints during migration.
// Once migration is complete, use the direct SpacetimeDB implementation above.
//
// ACTUAL ENDPOINTS from AuthController.cs:
// - POST /auth/login - Login with username/password
// - POST /auth/register - Register new user
// - GET  /auth/totp/setup - Get TOTP QR code
// - POST /auth/totp/verify - Verify TOTP code
// - POST /auth/totp/validate - Validate TOTP for login
// - POST /auth/webauthn/register/options - Get WebAuthn registration options
// - POST /auth/webauthn/register/complete - Complete WebAuthn registration
// - POST /auth/webauthn/login/options - Get WebAuthn login options
// - POST /auth/webauthn/login/complete - Complete WebAuthn login
// - GET  /auth/qr/generate - Generate QR code for login
// - POST /auth/qr/login - Login via QR code
// - GET  ~/connect/authorize - OAuth authorization endpoint
// - POST ~/connect/token - OAuth token endpoint
// - GET  ~/connect/userinfo - Get user info from token
// - GET  ~/connect/tokeninfo - Get token info
// - POST /connect/registerclient - Register OAuth client
// - PUT  /connect/update-client/{clientId} - Update OAuth client
// - DELETE /connect/delete-client/{clientId} - Delete OAuth client
// - GET  /connect/client/{clientId} - Get OAuth client details
// - GET  /connect/clients - List all OAuth clients
// - GET  /connect/scopes - List available OAuth scopes

class CSharpProxyAuthClient {
  constructor(private baseUrl: string = 'http://localhost:5000') {}
  
  // MIGRATION ONLY: Login via C# backend
  async login(username: string, password: string) {
    const response = await fetch(`${this.baseUrl}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    })
    if (!response.ok) throw new Error('Login failed')
    return await response.json() // Returns { token, userId, ... }
  }
  
  // MIGRATION ONLY: Register via C# backend
  async register(username: string, password: string, email: string) {
    const response = await fetch(`${this.baseUrl}/auth/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password, email })
    })
    if (!response.ok) throw new Error('Registration failed')
    return await response.json()
  }
  
  // MIGRATION ONLY: OAuth authorization flow
  async authorize(clientId: string, redirectUri: string, scope: string, state: string, codeChallenge: string) {
    const params = new URLSearchParams({
      client_id: clientId,
      redirect_uri: redirectUri,
      response_type: 'code',
      scope,
      state,
      code_challenge: codeChallenge,
      code_challenge_method: 'S256'
    })
    const response = await fetch(`${this.baseUrl}/connect/authorize?${params}`, {
      method: 'GET',
      credentials: 'include' // Include cookies for session
    })
    return response // Returns redirect or login page
  }
  
  // MIGRATION ONLY: Exchange authorization code for token
  async getToken(code: string, codeVerifier: string, clientId: string, redirectUri: string) {
    const response = await fetch(`${this.baseUrl}/connect/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'authorization_code',
        code,
        code_verifier: codeVerifier,
        client_id: clientId,
        redirect_uri: redirectUri
      })
    })
    if (!response.ok) throw new Error('Token exchange failed')
    return await response.json() // Returns { access_token, refresh_token, ... }
  }
  
  // MIGRATION ONLY: Get user info from token
  async getUserInfo(accessToken: string) {
    const response = await fetch(`${this.baseUrl}/connect/userinfo`, {
      method: 'GET',
      headers: { 'Authorization': `Bearer ${accessToken}` }
    })
    if (!response.ok) throw new Error('Failed to get user info')
    return await response.json()
  }
  
  // MIGRATION ONLY: Register OAuth client
  async registerClient(clientName: string, redirectUris: string[], scopes: string[]) {
    const response = await fetch(`${this.baseUrl}/connect/registerclient`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        clientName,
        redirectUris,
        scopes
      })
    })
    if (!response.ok) throw new Error('Client registration failed')
    return await response.json() // Returns { clientId, clientSecret, ... }
  }
  
  // MIGRATION ONLY: Setup TOTP
  async setupTotp(token: string) {
    const response = await fetch(`${this.baseUrl}/auth/totp/setup`, {
      method: 'GET',
      headers: { 'Authorization': `Bearer ${token}` }
    })
    if (!response.ok) throw new Error('TOTP setup failed')
    return await response.json() // Returns { qrCodeUri, secretKey }
  }
  
  // MIGRATION ONLY: Verify TOTP
  async verifyTotp(token: string, code: string) {
    const response = await fetch(`${this.baseUrl}/auth/totp/verify`, {
      method: 'POST',
      headers: { 
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ code })
    })
    if (!response.ok) throw new Error('TOTP verification failed')
    return await response.json()
  }
  
  // MIGRATION ONLY: WebAuthn registration
  async webAuthnRegisterOptions(token: string) {
    const response = await fetch(`${this.baseUrl}/auth/webauthn/register/options`, {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${token}` }
    })
    if (!response.ok) throw new Error('WebAuthn options failed')
    return await response.json()
  }
  
  async webAuthnRegisterComplete(token: string, credential: any) {
    const response = await fetch(`${this.baseUrl}/auth/webauthn/register/complete`, {
      method: 'POST',
      headers: { 
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(credential)
    })
    if (!response.ok) throw new Error('WebAuthn registration failed')
    return await response.json()
  }
  
  // MIGRATION ONLY: QR code login
  async generateQrCode() {
    const response = await fetch(`${this.baseUrl}/auth/qr/generate`, {
      method: 'GET'
    })
    if (!response.ok) throw new Error('QR generation failed')
    return await response.json() // Returns { qrCode, sessionId }
  }
  
  async loginWithQr(sessionId: string, username: string, password: string) {
    const response = await fetch(`${this.baseUrl}/auth/qr/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sessionId, username, password })
    })
    if (!response.ok) throw new Error('QR login failed')
    return await response.json()
  }
}

// Example usage during migration:
const authClient = new CSharpProxyAuthClient('http://localhost:5000')

// Login flow
const loginResult = await authClient.login('user@example.com', 'password123')
console.log('Logged in:', loginResult.token)

// OAuth flow
const authUrl = await authClient.authorize(
  'avalonia-client',
  'http://localhost:3000/callback',
  'openid profile email',
  'random-state',
  'code-challenge-here'
)
// User completes login, gets authorization code
const tokenResult = await authClient.getToken(
  'auth-code',
  'code-verifier',
  'avalonia-client',
  'http://localhost:3000/callback'
)
const userInfo = await authClient.getUserInfo(tokenResult.access_token)

// ============================================================================
// END C# PROXY MIGRATION BRIDGE
// ============================================================================

// Configure oidc-provider
const oidc = new Provider('http://localhost:3000', {
  adapter: SpacetimeDBAdapter, // Use direct SpacetimeDB adapter (production)
  // adapter: SpacetimeDBAdapterWithCSharpProxy, // Use C# proxy (migration only)
  clients: [
    {
      client_id: 'avalonia-client',
      client_secret: 'client-secret',
      redirect_uris: ['http://localhost:3000/callback'],
      response_types: ['code'],
      grant_types: ['authorization_code', 'refresh_token'],
      token_endpoint_auth_method: 'client_secret_post',
    },
  ],
  pkce: {
    required: () => true, // Enforce PKCE for all clients
  },
  features: {
    devInteractions: { enabled: false }, // Use custom login pages
    rpInitiatedLogout: { enabled: true },
    revocation: { enabled: true },
    introspection: { enabled: true },
  },
  findAccount: async (ctx, id) => {
    // PRODUCTION: Fetch user directly from SpacetimeDB
    const result = await spacetimeClient.query(`
      SELECT * FROM Users WHERE UserId = ?
    `, [id])
    if (!result || result.length === 0) return undefined
    
    const user = result[0]
    return {
      accountId: id,
      async claims() {
        return {
          sub: id,
          email: user.Email,
          name: user.Login,
          preferred_username: user.Login,
        }
      },
    }
  },
})

// Integrate with Elysia
// NOTE: oidc-provider requires Node.js req/res objects and cannot be directly
// integrated into Elysia route handlers. Use one of these integration patterns:
//
// ALLOWED Integration Patterns:
// Pattern A (RECOMMENDED): Elysia-first routing with Node.js shim inside handlers
// Pattern B (Alternative): Server-level interception before Elysia
//
// Pattern A: Elysia-first routing (RECOMMENDED)
// All OIDC traffic flows through Elysia routes, and Fetch↔Node conversion
// happens inside the OIDC handlers via handleOidcRequest helper.
// This approach preserves Elysia middleware and features.
const app = new Elysia()
  .all('/oidc/*', async ({ request }) => {
    // Convert Fetch Request to Node.js req/res inside Elysia handler
    return await handleOidcRequest(request, oidc)
  })
  .listen(3000)

async function handleOidcRequest(request: Request, oidc: Provider) {
  // Construct Node-style req from Fetch Request
  const nodeReq = {
    method: request.method,
    url: new URL(request.url).pathname + new URL(request.url).search,
    headers: Object.fromEntries(request.headers.entries()),
    body: await request.text(),
    // ... other Node.js req properties
  }
  
  // Construct Node-style res to collect response
  const nodeRes = {
    statusCode: 200,
    headers: {},
    body: '',
    setHeader(name: string, value: string) { this.headers[name] = value },
    writeHead(status: number) { this.statusCode = status },
    write(chunk: string) { this.body += chunk },
    end(chunk?: string) { if (chunk) this.body += chunk },
  }
  
  // Call oidc.callback() with Node-style req/res
  await new Promise((resolve) => {
    oidc.callback()(nodeReq as any, nodeRes as any, resolve)
  })
  
  // Convert Node-style res back to Fetch Response
  return new Response(nodeRes.body, {
    status: nodeRes.statusCode,
    headers: nodeRes.headers
  })
}

// Pattern B: Server-level interception (Alternative - bypasses Elysia middleware)
// This pattern intercepts requests BEFORE Elysia, so Elysia middleware does NOT apply.
// Use only if Pattern A is insufficient.
const app = new Elysia()
  .get('/', () => 'Elysia + oidc-provider')
  .listen(3000, (server) => {
    // Access underlying Node.js server and mount oidc-provider
    if (server.server) {
      const nodeServer = server.server as any
      const originalListener = nodeServer.listeners('request')[0]
      nodeServer.removeAllListeners('request')
      
      nodeServer.on('request', (req: any, res: any) => {
        if (req.url?.startsWith('/oidc')) {
          oidc.callback()(req, res, () => {})
        } else {
          originalListener(req, res)
        }
      })
    }
  })
```

**Benefits over Current OpenIddict Implementation**:
1. **No 3-tier ID mapping**: oidc-provider handles ID management internally
2. **No polling**: Async/await based, no need for 50-attempt polling loops
3. **No AuthorizationStore issues**: Custom adapter works around SpacetimeDB reactive state
4. **Better PKCE handling**: Native PKCE support without Data Protection keys
5. **Simpler debugging**: TypeScript stack traces vs C# + SpacetimeDB + OpenIddict

#### Migration Path to Elysia

**Phase 1: Proof of Concept** (2-4 weeks)
- [ ] Set up Elysia project with oidc-provider
- [ ] Route OIDC traffic through Elysia app.fetch using a Fetch↔Node shim (Pattern A - RECOMMENDED) and implement OIDC handlers (authorize, token, userinfo) that convert between Fetch and Node requests/responses
- [ ] Implement server-level interception for /oidc/* endpoints (Pattern B - fallback/alternative)
- [ ] Implement basic OIDC flow (authorize, token, userinfo)
- [ ] Test with Avalonia client
- [ ] Compare performance with OpenIddict
- [ ] Evaluate developer experience

**Phase 2: OAuth Migration** (4-6 weeks)
- [ ] Implement full OAuth/OIDC spec in Elysia
- [ ] Migrate client registrations from OpenIddict to Elysia
- [ ] Run Elysia side-by-side with C# backend
- [ ] Gradually migrate OAuth clients to Elysia
- [ ] Monitor error rates and performance

**Phase 3: Authentication Gateway** (8-12 weeks)
- [ ] Migrate all auth endpoints to Elysia
- [ ] Implement proxy layer to C# backend
- [ ] Remove authentication code from C# backend
- [ ] C# backend becomes pure business logic API
- [ ] Full production deployment

**Total Effort**: 14-22 weeks for complete migration

#### OIDC over WebSockets: Real-Time Authentication

Current WebSocket authentication in the codebase uses query parameter token passing (`?access_token=...`). This section explores OIDC-over-WebSocket for more secure real-time authentication.

**Current Implementation** (from WebSocketEventStreamWriter.cs and Program.cs):
```csharp
// Token passed as query parameter
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var path = context.HttpContext.Request.Path;
        if (path.StartsWithSegments("/hubs/system-events") && 
            context.Request.Query.TryGetValue("access_token", out var accessToken))
        {
            context.Token = accessToken;
        }
        return Task.CompletedTask;
    }
}
```

**Problems with Query Parameter Auth**:
1. **Security**: Tokens visible in logs, browser history, proxy logs
2. **Token refresh**: No mechanism to refresh expired tokens over WebSocket
3. **Revocation**: Cannot revoke tokens mid-session
4. **Limited metadata**: Cannot send additional auth context

**OIDC-over-WebSocket Benefits**:
1. **Secure**: Tokens in headers, not query parameters
2. **Token refresh**: Automatic token refresh over WebSocket
3. **Revocation**: Server can close connection on token revocation
4. **Better UX**: No reconnection needed for token refresh
5. **Audit trail**: All auth events logged server-side

**Client-Specific WebSocket Authentication Strategies**:

**For Avalonia/Desktop Clients (using ClientWebSocket)**:
- **Recommended**: Header-based Authorization during handshake
- **Implementation**: Use `ClientWebSocket.Options.SetRequestHeader("Authorization", "Bearer {token}")` before connecting
- **Benefits**: Full control over headers, secure token transmission, standard OAuth2 bearer token pattern
- **Example**:
  ```csharp
  var ws = new ClientWebSocket();
  ws.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
  await ws.ConnectAsync(new Uri("wss://api.example.com/ws"), cancellationToken);
  ```

**For Browser Clients (JavaScript/TypeScript)**:
- **Problem**: Browsers do not allow setting arbitrary headers (including Authorization) on WebSocket handshakes
- **Alternative Strategies**:
  1. **Secure HTTP-only Cookies**: Set authentication cookie via standard login, automatically sent with WebSocket handshake
  2. **Short-lived Query Tickets**: Exchange access token for single-use WebSocket ticket via API, use ticket in query parameter
  3. **Sec-WebSocket-Protocol Subprotocol**: Encode token in subprotocol header (e.g., `["access_token", base64Token]`)
- **Security Note**: Query parameter tokens should only be short-lived tickets, never long-lived access tokens

**Migration Strategy by Client Type**:
1. **Phase 1 - Avalonia Clients**: 
   - Enable header-based Authorization in server WebSocket handlers
   - Update Avalonia clients to use `SetRequestHeader` for Authorization
   - Test and validate header-based auth flow
2. **Phase 1 - Browser Clients**:
   - Deploy secure cookie-based auth OR ticket exchange endpoint
   - Implement Sec-WebSocket-Protocol fallback if needed
   - Update browser clients to use chosen strategy
3. **Phase 2**: Implement token refresh protocol for both client types
4. **Phase 3**: Monitor and validate both authentication paths in production
5. **Phase 4**: Deprecate legacy query parameter auth (if previously used)
6. **Phase 5**: Remove query parameter auth support entirely

#### When to Consider Elysia JS

**Good Fit If**:
- Team has TypeScript expertise
- OAuth/OIDC is a major pain point
- Need independent scaling of auth layer
- Want modern developer experience
- Building microservices architecture

**Not a Good Fit If**:
- Team is C#-only
- Current OpenIddict implementation works well
- Want to minimize operational complexity
- Prefer monolithic architecture
- Limited resources for migration

#### Recommendation

**Short-term** (Current refactoring): **Do NOT use Elysia**
- Focus on completing C# refactoring first
- Elysia would add unnecessary complexity
- Current OpenIddict + SpacetimeDB approach is working

**Medium-term** (6-12 months post-refactoring): **Evaluate Elysia for OAuth**
- If OAuth continues to be painful, consider Elysia as OAuth proxy
- Run proof of concept to validate benefits
- Compare with improving current OpenIddict implementation

**Long-term** (12+ months post-refactoring): **Consider Elysia as Auth Gateway**
- If moving to microservices architecture
- If need independent scaling of auth layer
- If team gains TypeScript expertise
- Full migration to Elysia as authentication gateway

**Key Insight**: Elysia JS is a **valid alternative** for authentication/proxy layer, but it's **not a priority** for the current refactoring. Complete the C# refactoring first, then evaluate Elysia based on actual pain points and team capabilities.

## Conclusion

This design document provides a comprehensive plan for refactoring the AuthController from a monolithic 8,293-line controller into a clean, layered architecture. The refactoring will be performed using a non-destructive approach with feature flags for gradual rollout, ensuring zero downtime and minimal risk.

**Key Principles**:
1. **Non-destructive**: Build new code in parallel without touching existing code
2. **Gradual**: Roll out changes incrementally with instant rollback capability
3. **Testable**: Comprehensive unit, integration, and performance testing
4. **Maintainable**: Clean architecture with clear separation of concerns
5. **Backward compatible**: Zero breaking changes to API contracts

**Timeline**: 10 weeks of development time plus 1-2 months of gradual production rollout

**Result**: A maintainable, testable, and scalable authentication system that can easily accommodate future enhancements, including the long-term vision of a fully frontend-agnostic architecture.
