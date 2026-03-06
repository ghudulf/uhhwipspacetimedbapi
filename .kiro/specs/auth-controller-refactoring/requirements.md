# Requirements Document

## Introduction

This document specifies the requirements for refactoring the AuthController in the BRU-AVTOPARK Authentication System. The AuthController currently contains 8,293 lines of code with 56 endpoints and exhibits three different architecture patterns due to rapid development during SpacetimeDB's evolution from version 0.9 to 2.0. The goal is to migrate all endpoints to a consistent clean service-layer architecture while maintaining 100% backward compatibility.

## Glossary

- **AuthController**: The main authentication controller containing 56 endpoints and 8,293 lines of code (currently UNTOUCHED - will remain operational during refactoring)
- **Service_Layer**: The business logic layer containing services like AuthenticationService, TotpService, WebAuthnService, etc.
- **Orchestration_Service**: A coordination layer that manages interactions between multiple business logic services
- **Direct_DB_Access**: Controller code that queries the database directly without using services
- **Clean_Pattern**: Architecture where Controller → Orchestration → Services → Database
- **Mixed_Pattern**: Architecture where Controller uses services but also has direct database access
- **SpacetimeDB**: The database system used by the application, currently at version 2.0
- **OpenIddict**: The OAuth/OpenID Connect server framework integrated with the system
- **EARS**: Easy Approach to Requirements Syntax - a structured format for writing requirements
- **Endpoint**: An HTTP API endpoint in the AuthController
- **Business_Logic_Service**: A service responsible for a single domain concern (e.g., TOTP, WebAuthn)
- **Feature_Flag**: A mechanism to enable/disable features at runtime for gradual rollout
- **Experimental_Folder**: The location where new modular architecture code is being built (BRU-AVTOPARK-AspireAPI.ApiService/Experimental/)
- **Side_By_Side_Deployment**: Running both legacy AuthController and new modular code simultaneously during migration
- **Legacy_Code**: The current AuthController implementation that will be preserved until new architecture is fully validated

## Background & Context

### Project History

This authentication system started as a **university coursework project** (November 2024) and was completely rewritten to SpacetimeDB in March 2025. Key timeline:

- **November 2024**: Original Entity Framework version (university coursework)
- **March 2025**: Complete rewrite to SpacetimeDB 0.9 (pre-1.0!)
- **March-May 2025**: Survived biweekly breaking updates (0.9 → 1.0 → 1.1 → 1.2)
- **May 2025**: OAuth implementation attempted but broken, project paused
- **June-December 2025**: 8-month gap working on other projects
- **January 2026**: SpacetimeDB 2.0 released, returned to project
- **January 2026**: Upgraded from 1.2 to 2.0 (10-version jump!), fixed broken OAuth
- **March 2026**: Now ready to refactor properly

**Total actual development time**: ~3 months (March-May 2025, January 2026)

**Achievements in 3 months**:
- ✅ 8 different authentication methods (Password, QR, TOTP, WebAuthn, Magic Link, OAuth, Direct QR, Token-based)
- ✅ 56 working endpoints across all authentication methods
- ✅ 7 business logic services (52 methods) in TicketSalesApp.Services
- ✅ 71 models (21 request, 35 response, 15 other)
- ✅ Production-ready system with Avalonia desktop client
- ✅ OpenIddict + SpacetimeDB integration (undocumented territory!)
- ✅ Custom authentication layer with JWT tokens and role-based access control
- ✅ SpacetimeDB 2.0 migration completed successfully

### Current Architecture State

**Controller Size**: 8,293 lines of code in a single file (AuthController.cs)

**Three distinct architecture patterns coexist**:

1. **✅ Clean Service Layer** (28 endpoints, 50%)
   - **Newer features**: TOTP (3/4 endpoints), WebAuthn (6/7 endpoints), Magic Link (all), QR Auth (all)
   - **Characteristics**: Properly delegates to business logic services, no direct database access
   - **Example**: `TotpSetup` → calls `_totpService.SetupTotpAsync()`
   - **Why clean**: Built after service layer was established (April-May 2025)

2. **🔥 Mixed Pattern** (18 endpoints, 32%)
   - **Older features**: Login, Register (March 2025)
   - **OAuth core**: authorize, token, userinfo (January 2026)
   - **OAuth admin pages**: All HTML management pages (January 2026)
   - **Characteristics**: Uses services BUT also has direct DB access in same method
   - **Example**: `Login` → calls `_authService.AuthenticateAsync()` BUT also queries `UserSettings`, `WebAuthnCredential`, `TwoFactorToken` directly
   - **Why mixed**: 
     - Login/Register predate service layer (March 2025)
     - OAuth implemented under extreme pressure after 8-month break (January 2026)
     - SpacetimeDB integration for OAuth/OIDC was extremely challenging
     - Had to get it working first, refactor later

3. **📄 HTML Only** (10 endpoints, 18%)
   - Just render forms, no business logic
   - These are fine as-is (Login page, Register page, OAuth login page, etc.)

### Detailed Endpoint Breakdown

**Traditional Authentication (2 endpoints)**:
- `GET /api/auth/login` - HTML form (clean)
- `POST /api/auth/login` - 🔥 MIXED: Calls `_authService.AuthenticateAsync()` but queries `UserSettings`, `WebAuthnCredential`, `TwoFactorToken` directly

**Registration (2 endpoints)**:
- `GET /api/auth/register` - HTML form (clean)
- `POST /api/auth/register` - 🔥 MIXED: Manually parses JWT, extracts identity, queries `UserProfile` directly, then calls `_authService.RegisterAsync()`

**TOTP (4 endpoints)**:
- `GET /api/auth/totp/setup` - ✅ CLEAN: Uses `_totpService.SetupTotpAsync()`
- `POST /api/auth/totp/verify` - ✅ CLEAN: Uses `_totpService.EnableTotpAsync()`
- `POST /api/auth/totp/disable` - ✅ CLEAN: Uses `_totpService.DisableTotpAsync()`
- `POST /api/auth/totp/validate` - 🔥 MIXED: Queries `TwoFactorToken`, `UserProfile`, `TotpSecret` directly, then calls `_totpService.VerifyTotpCode()`

**WebAuthn (7 endpoints)**:
- `POST /api/auth/webauthn/register/options` - ✅ CLEAN
- `POST /api/auth/webauthn/register/complete` - ✅ CLEAN
- `POST /api/auth/webauthn/login/options` - ✅ CLEAN
- `POST /api/auth/webauthn/login/complete` - ✅ CLEAN
- `POST /api/auth/webauthn/validate` - 🔥 MIXED: Queries `TwoFactorToken` directly
- `GET /api/auth/webauthn/credentials` - ✅ CLEAN
- `DELETE /api/auth/webauthn/credentials/{id}` - ✅ CLEAN

**Magic Link (3 endpoints)** - ✅ ALL CLEAN:
- `GET /api/auth/magic-link` - HTML form
- `POST /api/auth/magic-link/send` - Uses `_magicLinkService.SendMagicLinkAsync()`
- `POST /api/auth/validate-magic-link` - Uses `_magicLinkService.ValidateMagicLinkAsync()`

**QR Authentication (7 endpoints)** - ✅ ALL CLEAN:
- All properly use `_qrAuthService` methods

**OAuth/OIDC (20+ endpoints)** - 🔥 MOSTLY MIXED:
- Core flow endpoints (authorize, token, userinfo) - Direct DB access
- Admin HTML pages (13 endpoints) - All manually validate JWT tokens
- Client management API endpoints (7 endpoints) - Clean, use `_openIdConnectService`

**Profile & Utility (8 endpoints)**:
- `GET /api/auth/profile` - 🔥 MIXED: Manually validates JWT, queries DB, then calls `_profileService`
- Others - Mostly HTML pages (clean)

### Why This Happened

**Timeline Context**:
1. **March 2025**: Login/Register built first, no service layer yet → Direct DB access
2. **April 2025**: Service layer established → New features (TOTP, WebAuthn, Magic Link, QR) built clean
3. **May 2025**: OAuth attempted but broken on SpacetimeDB 1.2 → Project paused
4. **June-December 2025**: 8-month gap, SpacetimeDB evolved 10 versions
5. **January 2026**: Returned to project, upgraded to SpacetimeDB 2.0, fixed OAuth under pressure → Direct DB access to get it working
6. **March 2026**: Now ready to refactor properly

**Key Insight**: This is NOT bad architecture - this is **heroic development** under:
- University deadlines
- Bleeding-edge database (SpacetimeDB 0.9 → 2.0)
- Biweekly breaking changes
- 8-month project pause
- Undocumented OAuth/SpacetimeDB integration

The fact that 50% of endpoints are already clean shows the architecture vision was correct - just need to finish the migration.

### Service Architecture Overview

The system implements a **two-layer service architecture** with clear separation of concerns:

```
┌─────────────────────────────────────────────────────────────┐
│                     AuthController                           │
│                    (56 HTTP Endpoints)                       │
└────────────────────┬────────────────────────────────────────┘
                     │
                     │ Currently: Direct calls (needs refactoring)
                     │ Future: Via orchestration layer
                     ↓
┌─────────────────────────────────────────────────────────────┐
│         Orchestration Layer (Experimental/Services)          │
│              ⚠️ MINIMAL - 9% coverage (5/56 endpoints)       │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ AuthOrchestrationService.cs                          │  │
│  │  ✅ AuthenticateAsync                                 │  │
│  │  ✅ RegisterAsync                                     │  │
│  │  ✅ ClaimAccountAsync                                 │  │
│  │  ✅ IsAdmin                                           │  │
│  │  ✅ HasPermission                                     │  │
│  │  ❌ ~45 methods needed for remaining endpoints       │  │
│  └──────────────────────────────────────────────────────┘  │
│  Helper Services (✅ Complete):                             │
│  - TokenService, ProfileService, IdentityService            │
│  - OidcHelperService, RequestDetector, HtmlRenderingService │
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
│  │  - Password hashing (PBKDF2, SHA-256)                │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ UserService.cs (8 methods)                           │  │
│  │  - GetAllUsersAsync, GetUserByIdAsync                │  │
│  │  - GetUserByLoginAsync, UpdateUserAsync              │  │
│  │  - DeleteUserAsync, GetUserRolesAsync                │  │
│  │  - GetUserPermissionsAsync, CreateUserAsync          │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ TotpService.cs (9 methods)                           │  │
│  │  - SetupTotpAsync, EnableTotpAsync, DisableTotpAsync │  │
│  │  - ValidateTotpAsync, ValidateTotpWithTokenAsync     │  │
│  │  - IsTotpEnabledAsync, GenerateTotpSecretKeyAsync    │  │
│  │  - GenerateTotpQrCodeUri, VerifyTotpCode             │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ WebAuthnService.cs (7 methods)                       │  │
│  │  - GetCredentialCreateOptionsAsync                    │  │
│  │  - CompleteRegistrationAsync                          │  │
│  │  - GetAssertionOptionsAsync, CompleteAssertionAsync  │  │
│  │  - RemoveCredentialAsync, GetUserCredentialsAsync    │  │
│  │  - IsWebAuthnEnabledAsync                             │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ MagicLinkService.cs (3 methods)                      │  │
│  │  - SendMagicLinkAsync, ValidateMagicLinkAsync        │  │
│  │  - MarkMagicLinkAsUsedAsync                           │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ QRAuthenticationService.cs (7 methods)               │  │
│  │  - GenerateQRLoginTokenAsync                          │  │
│  │  - ValidateQRLoginTokenAsync, GenerateQRCodeAsync    │  │
│  │  - GenerateQRCodeWithDataAsync                        │  │
│  │  - GenerateDirectLoginQRCodeAsync                     │  │
│  │  - ValidateDirectLoginTokenAsync                      │  │
│  │  - NotifyDeviceLoginSuccessAsync                      │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ OpenIdConnectService.cs (13 methods)                 │  │
│  │  - GetApplicationByClientIdAsync                      │  │
│  │  - GetAuthorizationsAsync, CreateIdentityFromUserAsync│  │
│  │  - CreateAuthorizationAsync, GetAuthorizationIdAsync │  │
│  │  - GetResourcesAsync, RegisterClientApplicationAsync │  │
│  │  - UpdateClientApplicationAsync                       │  │
│  │  - DeleteClientApplicationAsync                       │  │
│  │  - GetAllClientApplicationsAsync                      │  │
│  │  - GetClientApplicationAsync, GetDestinations        │  │
│  │  - GetScopeManager                                    │  │
│  └──────────────────────────────────────────────────────┘  │
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

**Layer Responsibilities**:

**Business Logic Layer** (`TicketSalesApp.Services/Implementations/`)
- ✅ Status: 100% COMPLETE
- Purpose: Single-responsibility domain services
- Responsibilities:
  - Database queries and mutations
  - Domain logic and validation
  - Password hashing, token generation
  - TOTP/WebAuthn/OAuth operations
- Location: `BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/Implementations/`
- Services: 7 services, 52 methods

**Orchestration Layer** (`Experimental/Services/Implementations/`)
- ⚠️ Status: MINIMAL (9% coverage)
- Purpose: Coordinate multiple business logic services
- Responsibilities:
  - Call multiple services in sequence
  - Aggregate data from multiple sources
  - Handle cross-cutting concerns (logging, error handling)
  - Return standardized response objects
- Location: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Experimental/Services/Implementations/`
- Current: 5 methods, Need: ~45 additional methods

**What's Missing**:
1. **TwoFactorService** (Business Logic Layer) - Centralize TwoFactorToken management (TotpService already has `ValidateTotpWithTokenAsync`, but AuthController still directly creates, queries, and updates TwoFactorToken)
2. **SettingsService** (Business Logic Layer) - Manage user settings
3. **~45 Orchestration Methods** (Orchestration Layer) - Coordinate existing services

**Key Insight**: Business logic is NOT missing - it's complete and functional in `TicketSalesApp.Services`. TotpService already handles TwoFactorToken validation. What's missing is:
- Centralizing TwoFactorToken CREATE and UPDATE operations (currently in AuthController lines 2074, 2103, 2881, 2940, 3200, 3243)
- SettingsService for UserSettings management
- Orchestration layer expansion to coordinate existing services
- Elimination of direct database access from AuthController

## Requirements

### Requirement 1: Consistent Architecture Pattern

**User Story:** As a developer, I want all endpoints to follow a consistent architecture pattern, so that I can easily understand and maintain the codebase.

#### Acceptance Criteria

1. THE AuthController SHALL delegate all business logic to the Orchestration_Service
2. THE Orchestration_Service SHALL coordinate interactions between multiple Business_Logic_Service instances
3. THE Business_Logic_Service SHALL handle database queries and domain logic
4. WHEN a developer adds a new endpoint, THE System SHALL enforce the Controller → Orchestration → Services → Database pattern
5. THE AuthController SHALL NOT contain Direct_DB_Access code

### Requirement 2: Service Layer Completeness

**User Story:** As a developer, I want complete service layer coverage, so that all authentication operations are testable and maintainable.

#### Acceptance Criteria

1. THE System SHALL provide a TwoFactorService with methods CreateTempTokenAsync, ValidateTempTokenAsync, MarkTokenAsUsedAsync, and CleanupExpiredTokensAsync to centralize TwoFactorToken management currently split between TotpService and AuthController
2. THE System SHALL provide a SettingsService with methods GetOrCreateUserSettingsAsync, EnableTotpAsync, DisableTotpAsync, EnableWebAuthnAsync, DisableWebAuthnAsync, and UpdateSettingsAsync
3. THE Orchestration_Service SHALL provide methods covering all 56 existing endpoints
4. WHEN a Business_Logic_Service is created, THE System SHALL register it in the dependency injection container
5. THE System SHALL provide unit tests for all Business_Logic_Service methods

**Technical Context - Service Architecture**:

The system uses a **two-layer service architecture**:

**Layer 1: Business Logic Services** (Location: `BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/Implementations/`)
- **Purpose**: Domain logic, database access, single responsibility
- **Status**: ✅ 100% COMPLETE - 7 services with 52 methods
- **Existing Services**:
  - `AuthenticationService.cs` (5 methods): Core authentication, password hashing, user identity lookup
  - `UserService.cs` (8 methods): User CRUD operations, roles, permissions
  - `TotpService.cs` (9 methods): Complete TOTP/2FA implementation
  - `WebAuthnService.cs` (7 methods): Complete FIDO2/WebAuthn implementation
  - `MagicLinkService.cs` (3 methods): Email-based passwordless authentication
  - `QRAuthenticationService.cs` (7 methods): QR code login flows
  - `OpenIdConnectService.cs` (13 methods): OAuth/OIDC implementation

**Layer 2: Orchestration Services** (Location: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Experimental/Services/Implementations/`)
- **Purpose**: Coordinate multiple business logic services, handle cross-cutting concerns
- **Status**: ⚠️ MINIMAL - Only 5 of 56 endpoints orchestrated (9% coverage)
- **Existing Orchestration**:
  - `AuthOrchestrationService.cs` (5 methods): AuthenticateAsync, RegisterAsync, ClaimAccountAsync, IsAdmin, HasPermission
- **Helper Services** (✅ Complete):
  - `TokenService.cs` (4 methods): JWT token generation and validation
  - `ProfileService.cs` (1 method): Profile data aggregation
  - `IdentityService.cs` (4 methods): SpacetimeDB identity management
  - `OidcHelperService.cs` (10 methods): OAuth helper utilities
  - `RequestDetector.cs` (1 method): Browser vs API detection
  - `HtmlRenderingService.cs` (2 methods): Razor view rendering

**What's Missing**:
- **TwoFactorService**: Currently split between TotpService (has `ValidateTotpWithTokenAsync`) and AuthController (directly creates, queries, and updates `TwoFactorToken`). Need to centralize all TwoFactorToken operations in a dedicated service.
- **SettingsService**: Currently, Login endpoint directly queries and creates `UserSettings` in controller
- **Orchestration methods**: ~45 additional methods needed in AuthOrchestrationService to cover remaining 51 endpoints

**Key Insight**: Business logic is NOT missing - it's complete and functional. What's missing is:
1. Centralization of TwoFactorToken management (currently split between TotpService and AuthController)
2. SettingsService for UserSettings management
3. Orchestration layer that coordinates these services and eliminates direct database access from AuthController

### Requirement 3: Elimination of Code Duplication

**User Story:** As a developer, I want centralized authentication helper methods, so that security logic is consistent and maintainable.

#### Acceptance Criteria

1. THE System SHALL provide a single centralized method for JWT token validation
2. THE System SHALL provide a single centralized method for admin privilege checking
3. THE System SHALL provide a single centralized method for permission checking
4. THE System SHALL provide a single centralized method for user identity extraction
5. WHEN authentication logic is needed, THE AuthController SHALL use the centralized methods from Orchestration_Service

**Technical Context - Duplicated Methods**:
- **IsAdmin()**: Exists in BOTH AuthController (lines 2050-2100) AND AuthOrchestrationService - manually parses JWT tokens, checks claims
- **HasPermission()**: Exists in BOTH AuthController (lines 2110-2150) AND AuthOrchestrationService - manually parses JWT tokens, checks role permissions
- **GenerateJwtToken()**: Exists in AuthController, should ONLY be in TokenService
- **GetUserIdentity()**: Exists in AuthController, should ONLY be in IdentityService
- **IsBrowserRequest()**: Exists in BOTH AuthController AND RequestDetector service
- **GenerateRandomToken()**: Exists in AuthController, should ONLY be in TokenService
- **Manual JWT validation**: Repeated in ~10 OAuth admin HTML endpoints - each manually parses JWT from query parameter, validates claims, checks admin role

**Impact**: Changes to authentication logic must be made in multiple places, creating maintenance burden and risk of inconsistent behavior.

### Requirement 4: Backward Compatibility

**User Story:** As a product manager, I want zero breaking changes to the API, so that existing clients continue working without modification.

#### Acceptance Criteria

1. THE System SHALL maintain identical HTTP request/response contracts for all endpoints
2. THE System SHALL maintain identical error response formats
3. THE System SHALL maintain identical authentication token formats
4. WHEN the refactored code is deployed, THE Avalonia client SHALL continue functioning without changes
5. THE System SHALL maintain identical database schema

### Requirement 5: Testability

**User Story:** As a developer, I want to unit test authentication logic without database dependencies, so that tests run quickly and reliably.

#### Acceptance Criteria

1. THE System SHALL allow mocking of Orchestration_Service in controller tests
2. THE System SHALL allow mocking of Business_Logic_Service instances in orchestration tests
3. THE System SHALL allow mocking of database access in service tests
4. WHEN unit tests are executed, THE test suite SHALL complete in less than 5 seconds
5. THE System SHALL achieve greater than 80% code coverage for new code

**Testing Strategy Context**:

The refactoring follows a **build-first, test-later** approach due to the non-destructive migration strategy:

**Phase 1-2: Build Without Integration Testing (Weeks 1-7)**
- Write unit tests for TwoFactorService and SettingsService (can test in isolation)
- Write unit tests for orchestration methods (can test with mocked dependencies)
- **Cannot perform integration testing**: New orchestration code is not hooked up to AuthController yet
- **Cannot perform end-to-end testing**: No HTTP endpoints calling the new code
- **Limitation**: Can only verify logic correctness through unit tests, not actual behavior
- **Acceptance**: This is intentional - integration testing would require modifying AuthController, which violates the non-destructive approach

**Phase 3: Enable Integration Testing (Week 8)**
- Feature flags added but not yet used in AuthController
- Still cannot integration test - flags not hooked up yet
- Continue with unit tests only

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

**Testing Philosophy**:
- **Weeks 1-7**: Trust unit tests and code review - integration testing not possible yet
- **Weeks 9-10**: Comprehensive integration testing once code is hooked up
- **Post-deployment**: Real-world validation with feature flags and monitoring
- **Trade-off**: Accept delayed integration testing to maintain zero-risk approach

**Why This Approach**:
- Modifying AuthController early would enable integration testing BUT introduce risk to production
- Building code without integration testing is safer than risking production stability
- Unit tests provide sufficient confidence for isolated service logic
- Integration testing becomes possible once feature flags are added (Week 9)
- This is a conscious trade-off: safety over early testability

### Requirement 6: Gradual Rollout Support

**User Story:** As a product manager, I want to gradually roll out refactored endpoints, so that I can minimize risk and quickly rollback if issues occur.

#### Acceptance Criteria

1. THE System SHALL provide Feature_Flag support for controlling endpoint behavior
2. WHEN a Feature_Flag is enabled, THE System SHALL route requests to refactored code
3. WHEN a Feature_Flag is disabled, THE System SHALL route requests to legacy code
4. THE System SHALL support enabling Feature_Flag for a percentage of users
5. THE System SHALL provide monitoring for error rates per Feature_Flag

**Migration Strategy Context**:
- **Phase 1 (Weeks 1-7)**: Build new modular architecture in Experimental folder WITHOUT touching AuthController
- **Phase 2 (Week 8)**: Add feature flag system to orchestration layer
- **Phase 3 (Weeks 9-10)**: Modify AuthController to check feature flags and delegate to new architecture when enabled
- **Phase 4 (Post-deployment)**: Gradually enable feature flags, monitor, improve as needed
- **Phase 5 (Final)**: Once fully validated, remove legacy code and feature flags

**Critical Constraint**: The current AuthController (8,293 lines) MUST remain completely untouched and operational until Phase 3. All new code is built in parallel in the Experimental folder, allowing the system to continue functioning normally during development.

### Requirement 7: Performance Maintenance

**User Story:** As a DevOps engineer, I want refactored endpoints to maintain current performance levels, so that user experience is not degraded.

#### Acceptance Criteria

1. THE System SHALL NOT increase response time by more than 5% for any endpoint
2. THE System SHALL NOT increase database query count for any endpoint
3. THE System SHALL NOT increase memory usage by more than 10%
4. THE System SHALL support adding caching in the Orchestration_Service layer
5. WHEN performance tests are executed, THE System SHALL pass all performance benchmarks

### Requirement 8: Security Centralization

**User Story:** As a security engineer, I want all authentication and authorization logic centralized, so that I can audit security controls effectively.

#### Acceptance Criteria

1. THE Orchestration_Service SHALL contain all authentication logic
2. THE Orchestration_Service SHALL contain all authorization logic
3. THE Orchestration_Service SHALL contain all JWT token validation logic
4. WHEN a security audit is performed, THE auditor SHALL review only the Orchestration_Service for authentication logic
5. THE System SHALL pass security audit before production deployment

### Requirement 9: Caching Support

**User Story:** As a DevOps engineer, I want to add caching to reduce database load, so that the system can handle higher traffic volumes.

#### Acceptance Criteria

1. THE Orchestration_Service SHALL support adding caching without modifying Business_Logic_Service code
2. THE System SHALL allow configuring cache TTL per orchestration method
3. THE System SHALL provide monitoring for cache hit rates
4. WHEN caching is enabled, THE System SHALL reduce database queries by at least 50% for cached operations
5. THE System SHALL support both in-memory and distributed caching

### Requirement 10: Rate Limiting Support

**User Story:** As a security engineer, I want to add rate limiting to authentication endpoints, so that the system is protected from abuse.

#### Acceptance Criteria

1. THE System SHALL support adding rate limiting middleware
2. WHEN rate limiting is configured, THE middleware SHALL apply to all authentication endpoints automatically
3. THE System SHALL allow configuring per-endpoint rate limits
4. THE System SHALL allow configuring per-user and per-IP rate limits
5. THE System SHALL provide monitoring for rate limit violations

### Requirement 11: Documentation

**User Story:** As a developer, I want comprehensive documentation of the architecture pattern, so that I can correctly implement new features.

#### Acceptance Criteria

1. THE System SHALL provide architecture documentation explaining the layered pattern
2. THE System SHALL provide service documentation with usage examples
3. THE System SHALL provide a migration guide explaining how to refactor endpoints
4. THE System SHALL maintain accurate API documentation
5. WHEN a new developer joins, THE documentation SHALL enable them to understand the architecture within one day

### Requirement 12: Zero Downtime Deployment

**User Story:** As a product manager, I want zero downtime during refactoring, so that users are not impacted.

#### Acceptance Criteria

1. THE System SHALL support running legacy and refactored code side-by-side
2. WHEN deployment occurs, THE System SHALL maintain 99.9% uptime
3. THE System SHALL NOT require data migration
4. THE System SHALL allow rollback within 5 minutes if issues are detected
5. WHEN refactoring is complete, THE System SHALL have error rate increase of less than 0.1%

**Implementation Strategy - Non-Destructive Refactoring**:

The refactoring follows a strict **copy-first, modify-later** approach to ensure zero downtime:

**Phase 1: Build New Architecture (Weeks 1-7)** - AuthController UNTOUCHED
- Create TwoFactorService and SettingsService in TicketSalesApp.Services
- Expand AuthOrchestrationService with ~45 new methods in Experimental/Services
- Build all orchestration logic in parallel to existing code
- Write comprehensive tests for new services
- **Critical**: AuthController remains completely unchanged and operational
- **Result**: System continues running on legacy code while new architecture is built

**Phase 2: Add Feature Flags (Week 8)** - AuthController STILL UNTOUCHED
- Integrate feature flag library (LaunchDarkly, Unleash, or custom)
- Add feature flag checks to orchestration service methods
- Configure flags for each endpoint (disabled by default)
- Set up monitoring dashboards
- **Critical**: AuthController still unchanged, flags not yet used
- **Result**: Feature flag infrastructure ready, but not yet activated

**Phase 3: Modify AuthController (Weeks 9-10)** - FIRST TIME TOUCHING AuthController
- Add feature flag checks at the START of each endpoint method
- IF flag enabled → delegate to new orchestration service
- IF flag disabled → execute existing legacy code (unchanged)
- Deploy to production with ALL flags disabled
- **Critical**: This is the ONLY time AuthController is modified
- **Result**: Both code paths exist, legacy path still active by default

**Phase 4: Gradual Rollout (Post-deployment)**
- Enable flags for 1% of users, monitor for 24 hours
- If stable, increase to 10%, monitor for 24 hours
- If stable, increase to 50%, monitor for 48 hours
- If stable, enable for 100% of users
- **Critical**: Can instantly rollback by disabling flag
- **Result**: New architecture validated in production with real traffic

**Phase 5: Cleanup (After full validation)**
- Remove feature flag checks from AuthController
- Remove legacy code paths
- Delete old helper methods
- Reduce AuthController from 8,293 lines to ~2,000 lines
- **Critical**: Only done after weeks/months of stable operation
- **Result**: Clean, maintainable codebase

**Risk Mitigation**:
- Legacy code continues working throughout Phases 1-3
- Feature flags provide instant rollback capability in Phase 4
- No "big bang" rewrite - incremental validation at every step
- Can pause migration at any phase if issues arise
- Production system never at risk during development

### Requirement 13: Direct Database Access Elimination

**User Story:** As a developer, I want to eliminate all direct database access from controllers, so that business logic is properly encapsulated and testable.

#### Acceptance Criteria

1. THE AuthController SHALL NOT directly access SpacetimeDBService.GetConnection()
2. THE AuthController SHALL NOT directly query database tables using conn.Db.TableName.Iter()
3. THE AuthController SHALL NOT directly call reducers using conn.Reducers.MethodName()
4. WHEN database access is needed, THE AuthController SHALL delegate to Business_Logic_Service or Orchestration_Service
5. THE System SHALL have zero direct database queries in AuthController after refactoring

**Technical Context - Current Direct DB Access**:
- **Login endpoint**: Queries `UserSettings`, `WebAuthnCredential` tables; creates `TwoFactorToken` via reducer
- **Register endpoint**: Queries `UserProfile` table to extract admin identity
- **TOTP validate**: Queries `TwoFactorToken`, `UserProfile`, `TotpSecret` tables; updates `TwoFactorToken` via reducer
- **WebAuthn validate**: Queries `TwoFactorToken` table
- **OAuth token endpoint**: Queries `UserProfile`, `UserRole`, `Role`, `RolePermission`, `Permission` tables
- **OAuth userinfo endpoint**: Queries `UserProfile`, `UserRole`, `Role` tables
- **OAuth authorize endpoint**: Queries OAuth client data
- **Profile page**: Queries user data directly

**Impact**: 18 of 56 endpoints (32%) have direct database access, making them impossible to unit test without a real database.

### Requirement 14: Orchestration Layer Expansion

**User Story:** As a developer, I want comprehensive orchestration methods for all endpoints, so that business logic coordination is centralized and reusable.

#### Acceptance Criteria

1. THE AuthOrchestrationService SHALL provide orchestration methods for all 56 endpoints
2. WHEN an orchestration method is added, THE System SHALL coordinate calls to multiple Business_Logic_Service instances
3. THE Orchestration_Service SHALL handle transaction management across service calls
4. THE Orchestration_Service SHALL provide consistent error handling and logging
5. WHEN orchestration is complete, THE System SHALL have 100% endpoint coverage (currently 9%)

**Technical Context - Current Orchestration Coverage**:

**Current Orchestration (5 methods in AuthOrchestrationService)**:
- `AuthenticateAsync` - Calls AuthenticationService, aggregates user settings and roles
- `RegisterAsync` - Calls AuthenticationService.RegisterAsync, handles role assignment
- `ClaimAccountAsync` - Calls SpacetimeDB reducer for account claiming
- `IsAdmin` - Checks admin privileges from claims or JWT token
- `HasPermission` - Checks specific permissions from claims or JWT token

**Missing Orchestration (~45 methods needed)**:

**Priority 1 - Critical (Direct DB Access Elimination)**:
- `LoginAsync` - Orchestrate AuthenticationService + TwoFactorService + SettingsService
- `ValidateTotpAsync` - Orchestrate TotpService + TwoFactorService
- `ValidateWebAuthnAsync` - Orchestrate WebAuthnService + TwoFactorService
- `ValidateMagicLinkAsync` - Orchestrate MagicLinkService + TokenService
- `GetProfileAsync` - Already has ProfileService, needs orchestration wrapper

**Priority 2 - High (Complete TOTP/WebAuthn Flows)**:
- `SetupTotpAsync` - Orchestrate TotpService.SetupTotpAsync
- `EnableTotpAsync` - Orchestrate TotpService.EnableTotpAsync + SettingsService
- `DisableTotpAsync` - Orchestrate TotpService.DisableTotpAsync + SettingsService
- `RegisterWebAuthnAsync` - Orchestrate WebAuthnService registration flow
- `GetWebAuthnCredentialsAsync` - Orchestrate WebAuthnService.GetUserCredentialsAsync
- `RemoveWebAuthnCredentialAsync` - Orchestrate WebAuthnService.RemoveCredentialAsync

**Priority 3 - Medium (OAuth/OIDC Flows)**:
- `AuthorizeOAuthAsync` - Orchestrate OpenIdConnectService authorization flow
- `ExchangeTokenAsync` - Orchestrate OpenIdConnectService token exchange
- `GetUserInfoAsync` - Orchestrate OpenIdConnectService.CreateIdentityFromUserAsync
- `RegisterOAuthClientAsync` - Orchestrate OpenIdConnectService.RegisterClientApplicationAsync
- `UpdateOAuthClientAsync` - Orchestrate OpenIdConnectService.UpdateClientApplicationAsync
- `DeleteOAuthClientAsync` - Orchestrate OpenIdConnectService.DeleteClientApplicationAsync
- `GetOAuthClientsAsync` - Orchestrate OpenIdConnectService.GetAllClientApplicationsAsync
- `GetOAuthScopesAsync` - Orchestrate OpenIdConnectService.GetScopeManager

**Priority 4 - Low (Already Clean, Minimal Work)**:
- QR authentication methods (already use QRAuthenticationService cleanly)
- Magic Link methods (already use MagicLinkService cleanly)
- User management methods (already use UserService cleanly)

**Orchestration Pattern Example**:
```csharp
// Current: AuthController directly accesses database
public async Task<IActionResult> Login(LoginRequest request)
{
    var user = await _authService.AuthenticateAsync(request.Username, request.Password);
    var settings = conn.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId.Equals(user.UserId)); // Direct DB access
    var credentials = conn.Db.WebAuthnCredential.Iter().Where(c => c.UserId.Equals(user.UserId)); // Direct DB access
    // ... more direct DB queries
}

// Future: AuthController calls orchestration
public async Task<IActionResult> Login(LoginRequest request)
{
    var result = await _authOrchestrationService.LoginAsync(request.Username, request.Password);
    return Ok(result);
}

// Orchestration service coordinates business logic
public async Task<LoginResult> LoginAsync(string username, string password)
{
    var user = await _authService.AuthenticateAsync(username, password);
    var settings = await _settingsService.GetOrCreateUserSettingsAsync(user.UserId);
    var credentials = await _webAuthnService.GetUserCredentialsAsync(user.UserId);
    var token = _tokenService.GenerateToken(user, settings, credentials);
    return new LoginResult { Token = token, User = user, Settings = settings };
}
```

**Estimated Effort**: ~45 orchestration methods, ~2,000-3,000 lines of code, 2-3 days of development

### Requirement 15: Non-Destructive Refactoring Approach

**User Story:** As a developer, I want to build the new architecture without modifying existing code, so that the system remains stable and operational during development.

#### Acceptance Criteria

1. THE System SHALL build all new services and orchestration code in the Experimental folder
2. THE AuthController SHALL remain completely unmodified during Phases 1-2 (Weeks 1-8)
3. WHEN new services are created, THE System SHALL NOT remove or modify existing service implementations
4. WHEN orchestration methods are added, THE System SHALL NOT modify existing controller endpoints until Phase 3
5. THE System SHALL allow both legacy and new code to coexist until validation is complete

**Refactoring Phases**:

**Phase 1: Service Creation (Weeks 1-2)** - Zero Risk
- Create TwoFactorService in TicketSalesApp.Services
- Create SettingsService in TicketSalesApp.Services
- Write unit tests for new services
- Register services in DI container
- **AuthController**: UNTOUCHED
- **Risk Level**: ZERO - new code doesn't affect existing functionality

**Phase 2: Orchestration Expansion (Weeks 3-7)** - Zero Risk
- Add ~45 orchestration methods to AuthOrchestrationService
- Implement all business logic coordination
- Write comprehensive unit and integration tests
- Validate orchestration logic in isolation
- **AuthController**: UNTOUCHED
- **Risk Level**: ZERO - orchestration not yet called by controller

**Phase 3: Feature Flag Integration (Week 8)** - Zero Risk
- Add feature flag library to project
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

**Key Principle**: At no point during Phases 1-3 is the production system at risk. The existing AuthController continues serving all requests normally while the new architecture is built in parallel.
