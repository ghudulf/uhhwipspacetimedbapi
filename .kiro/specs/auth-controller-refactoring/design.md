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
```csharp
Task<OAuthAuthorizeResult> AuthorizeOAuthAsync(string clientId, string redirectUri, string scope, Identity userId);
Task<OAuthTokenResult> ExchangeTokenAsync(string code, string clientId, string clientSecret);
Task<OAuthUserInfoResult> GetUserInfoAsync(string accessToken);
Task<OAuthClientResult> RegisterOAuthClientAsync(OAuthClientRequest request);
Task<OAuthClientResult> UpdateOAuthClientAsync(string clientId, OAuthClientRequest request);
Task<OAuthClientResult> DeleteOAuthClientAsync(string clientId);
Task<OAuthClientsResult> GetOAuthClientsAsync();
Task<OAuthScopesResult> GetOAuthScopesAsync();
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

**Controller Integration**:
```csharp
[HttpPost("login")]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
{
    // Check feature flag
    if (_featureFlags.Value.EnableLoginRefactoring)
    {
        // NEW CODE PATH: Delegate to orchestration service
        var result = await _authOrchestrationService.LoginAsync(request.Username, request.Password);
        
        if (!result.Success)
            return Unauthorized(new { error = result.Message });
        
        return Ok(result);
    }
    else
    {
        // LEGACY CODE PATH: Existing implementation (unchanged)
        // ... existing 100+ lines of login logic
    }
}
```

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
- Modify AuthController to check feature flags
- Add delegation to orchestration service when flag enabled
- Preserve existing code path when flag disabled
- Deploy with all flags disabled
- **AuthController**: MODIFIED (first time)
- **Risk Level**: LOW - legacy code path still active by default

**Phase 5: Gradual Rollout (Post-deployment)** - Controlled Risk
- Enable flags incrementally (1% → 10% → 50% → 100%)
- Monitor error rates, performance, user feedback
- Rollback instantly if issues detected
- Iterate and improve based on production data
- **AuthController**: Running both code paths
- **Risk Level**: CONTROLLED - instant rollback available

**Phase 6: Legacy Code Removal (After validation)** - Zero Risk
- Remove feature flag checks
- Remove legacy code paths
- Remove duplicated helper methods
- Clean up and optimize
- **AuthController**: Simplified to ~2,000 lines
- **Risk Level**: ZERO - new code already validated in production

### Timeline

**Total Duration**: 10 weeks + gradual rollout

**Week 1-2**: Service creation (TwoFactorService, SettingsService)
**Week 3-7**: Orchestration expansion (45 methods)
**Week 8**: Feature flag integration
**Week 9-10**: Controller modification and testing
**Week 11+**: Gradual production rollout (1-2 months)
**Week 20+**: Legacy code removal (after full validation)

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
