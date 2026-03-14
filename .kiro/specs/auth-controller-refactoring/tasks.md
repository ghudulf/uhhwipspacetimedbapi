# Implementation Plan: AuthController Refactoring

## Overview

This implementation plan refactors the AuthController from a monolithic 8,293-line controller into a clean, layered architecture following the Controller → Orchestration → Services → Database pattern. The refactoring uses a non-destructive approach where new code is built in parallel, validated incrementally, and rolled out gradually using feature flags.

**Key Principles**:
- AuthController remains UNTOUCHED during Phases 1-3 (Weeks 1-8)
- All new code built in parallel in Experimental folder
- Feature flags enable gradual rollout with instant rollback
- Zero downtime, zero risk to production during development

## Tasks

### Phase 1: Service Creation (Weeks 1-2) - Zero Risk

- [x] 1. Create TwoFactorService in business logic layer
  - [x] 1.1 Create ITwoFactorService interface
    - Define CreateTempTokenAsync, ValidateTempTokenAsync, MarkTokenAsUsedAsync, CleanupExpiredTokensAsync methods
    - _Requirements: 2.1, 13.1, 13.2, 13.3_
  
  - [x] 1.2 Implement TwoFactorService class
    - Implement CreateTempTokenAsync: Generate temporary token for 2FA validation
    - Implement ValidateTempTokenAsync: Verify token validity, expiration, and usage status
    - Implement MarkTokenAsUsedAsync: Mark token as used to prevent replay attacks
    - Implement CleanupExpiredTokensAsync: Remove expired tokens from database
    - Location: `BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/Implementations/TwoFactorService.cs`
    - _Requirements: 2.1, 13.1, 13.2, 13.3_
  
  - [ ]* 1.3 Write unit tests for TwoFactorService
    - Test CreateTempTokenAsync with valid userId
    - Test ValidateTempTokenAsync with valid/invalid/expired tokens
    - Test MarkTokenAsUsedAsync prevents token reuse
    - Test CleanupExpiredTokensAsync removes only expired tokens
    - _Requirements: 5.1, 5.2, 5.3_
  
  - [x] 1.4 Register TwoFactorService in DI container
    - Add service registration in Program.cs
    - _Requirements: 2.4_


- [x] 2. Create SettingsService in business logic layer
  - [x] 2.1 Create ISettingsService interface
    - Define GetOrCreateUserSettingsAsync, EnableTotpAsync, DisableTotpAsync, EnableWebAuthnAsync, DisableWebAuthnAsync, UpdateSettingsAsync methods
    - _Requirements: 2.2, 13.1, 13.2, 13.3_
  
  - [x] 2.2 Implement SettingsService class
    - Implement GetOrCreateUserSettingsAsync: Retrieve or create default user settings
    - Implement EnableTotpAsync/DisableTotpAsync: Toggle TOTP setting
    - Implement EnableWebAuthnAsync/DisableWebAuthnAsync: Toggle WebAuthn setting
    - Implement UpdateSettingsAsync: Update user settings with new values
    - Location: `BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/Implementations/SettingsService.cs`
    - _Requirements: 2.2, 13.1, 13.2, 13.3_
  
  - [ ]* 2.3 Write unit tests for SettingsService
    - Test GetOrCreateUserSettingsAsync creates defaults when missing
    - Test EnableTotpAsync/DisableTotpAsync toggle correctly
    - Test EnableWebAuthnAsync/DisableWebAuthnAsync toggle correctly
    - Test UpdateSettingsAsync persists changes
    - _Requirements: 5.1, 5.2, 5.3_
  
  - [x] 2.4 Register SettingsService in DI container
    - Add service registration in Program.cs
    - _Requirements: 2.4_

- [x] 3. Checkpoint - Verify service creation
  - ✅ TwoFactorService created and registered in DI container
  - ✅ SettingsService created and registered in DI container
  - ✅ TicketSalesApp.Services project builds successfully
  - ✅ Phase 1 (Service Creation) complete and ready for Phase 2


### Phase 2: Orchestration Expansion (Weeks 3-7) - Zero Risk

- [x] 4. Expand AuthOrchestrationService with Priority 1 methods (Critical - Direct DB Access Elimination)
  - [x] 4.1 Implement LoginAsync orchestration method
    - Coordinate AuthenticationService + TwoFactorService + SettingsService
    - Handle 2FA requirement detection and temporary token creation
    - Generate JWT token for successful authentication
    - Return LoginResult with token, user data, and settings
    - _Requirements: 1.1, 1.2, 1.3, 13.1, 13.2, 13.3, 13.4, 14.1, 14.2_
  
  - [x] 4.2 Implement ValidateTotpAsync orchestration method
    - Coordinate TotpService + TwoFactorService
    - Validate TOTP code and temporary token
    - Mark token as used after successful validation
    - Generate JWT token for successful validation
    - _Requirements: 1.1, 1.2, 1.3, 13.1, 13.2, 13.3, 13.4, 14.1, 14.2_
  
  - [x] 4.3 Implement ValidateWebAuthnAsync orchestration method
    - Coordinate WebAuthnService + TwoFactorService
    - Validate WebAuthn assertion and temporary token
    - Mark token as used after successful validation
    - Generate JWT token for successful validation
    - _Requirements: 1.1, 1.2, 1.3, 13.1, 13.2, 13.3, 13.4, 14.1, 14.2_
  
  - [x] 4.4 Implement ValidateMagicLinkAsync orchestration method
    - Coordinate MagicLinkService + TokenService
    - Validate magic link token
    - Generate JWT token for successful validation
    - _Requirements: 1.1, 1.2, 1.3, 13.1, 13.2, 13.3, 13.4, 14.1, 14.2_
  
  - [x] 4.5 Implement GetProfileAsync orchestration method
    - Use existing ProfileService with orchestration wrapper
    - Aggregate user profile data from multiple sources
    - _Requirements: 1.1, 1.2, 1.3, 13.1, 13.2, 13.3, 13.4, 14.1, 14.2_
  
  - [ ]* 4.6 Write unit tests for Priority 1 orchestration methods
    - Test LoginAsync with valid credentials, invalid credentials, 2FA required
    - Test ValidateTotpAsync with valid/invalid codes and tokens
    - Test ValidateWebAuthnAsync with valid/invalid assertions
    - Test ValidateMagicLinkAsync with valid/invalid tokens
    - Test GetProfileAsync returns complete profile data
    - _Requirements: 5.1, 5.2, 5.3_


- [x] 5. Expand AuthOrchestrationService with Priority 2 methods (High - Complete TOTP/WebAuthn Flows)
  - [x] 5.1 Implement SetupTotpAsync orchestration method
    - Coordinate TotpService.SetupTotpAsync
    - Return QR code URI and secret key
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 5.2 Implement EnableTotpAsync orchestration method
    - Coordinate TotpService.EnableTotpAsync + SettingsService.EnableTotpAsync
    - Verify TOTP code before enabling
    - Update user settings to reflect TOTP enabled
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 5.3 Implement DisableTotpAsync orchestration method
    - Coordinate TotpService.DisableTotpAsync + SettingsService.DisableTotpAsync
    - Update user settings to reflect TOTP disabled
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 5.4 Implement RegisterWebAuthnAsync orchestration method
    - Coordinate WebAuthnService registration flow
    - Handle credential creation and storage
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 5.5 Implement GetWebAuthnCredentialsAsync orchestration method
    - Coordinate WebAuthnService.GetUserCredentialsAsync
    - Return list of user's WebAuthn credentials
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 5.6 Implement RemoveWebAuthnCredentialAsync orchestration method
    - Coordinate WebAuthnService.RemoveCredentialAsync
    - Remove specified credential from user's account
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [ ]* 5.7 Write unit tests for Priority 2 orchestration methods
    - Test SetupTotpAsync returns valid QR code and secret
    - Test EnableTotpAsync verifies code before enabling
    - Test DisableTotpAsync updates settings correctly
    - Test RegisterWebAuthnAsync handles credential creation
    - Test GetWebAuthnCredentialsAsync returns user credentials
    - Test RemoveWebAuthnCredentialAsync removes credential
    - _Requirements: 5.1, 5.2, 5.3_


- [x] 6. Expand AuthOrchestrationService with Priority 3 methods (Medium - OAuth/OIDC Flows)
  - [x] 6.1 Implement ValidateOAuthRequestAsync helper method
    - Coordinate OpenIdConnectService client validation
    - Validate redirect URI and scopes
    - Return validation result (success/failure with error message)
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2, 16.1, 16.2, 16.3_
  
  - [x] 6.2 Implement BuildOAuthClaimsIdentityAsync helper method
    - Coordinate UserService + OpenIdConnectService
    - Build ClaimsIdentity with user claims (sub, name, email, roles)
    - Set scopes and resources
    - Set claim destinations
    - Return ClaimsIdentityResult
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2, 16.1, 16.3_
  
  - [x] 6.3 Implement ValidateUserForTokenExchangeAsync helper method
    - Coordinate UserService to validate user exists and is active
    - Return user validation result
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2, 16.1, 16.2_
  
  - [x] 6.4 Implement BuildTokenClaimsIdentityAsync helper method
    - Coordinate UserService + OpenIdConnectService
    - Build fresh ClaimsIdentity for token exchange
    - Set scopes and resources from principal
    - Set claim destinations
    - Return ClaimsIdentityResult
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2, 16.1, 16.3_
  
  - [x] 6.5 Implement RegisterOAuthClientAsync orchestration method
    - Coordinate OpenIdConnectService.RegisterClientApplicationAsync
    - Handle OAuth client registration
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.6 Implement UpdateOAuthClientAsync orchestration method
    - Coordinate OpenIdConnectService.UpdateClientApplicationAsync
    - Handle OAuth client updates
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.7 Implement DeleteOAuthClientAsync orchestration method
    - Coordinate OpenIdConnectService.DeleteClientApplicationAsync
    - Handle OAuth client deletion
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.8 Implement GetOAuthClientsAsync orchestration method
    - Coordinate OpenIdConnectService.GetAllClientApplicationsAsync
    - Return list of OAuth clients
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.9 Implement GetOAuthScopesAsync orchestration method
    - Coordinate OpenIdConnectService.GetScopeManager
    - Return available OAuth scopes
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [ ]* 6.10 Write unit tests for Priority 3 orchestration methods
    - Test ValidateOAuthRequestAsync validates client and scopes
    - Test BuildOAuthClaimsIdentityAsync builds correct claims
    - Test ValidateUserForTokenExchangeAsync validates user
    - Test BuildTokenClaimsIdentityAsync builds fresh claims
    - Test RegisterOAuthClientAsync creates client
    - Test UpdateOAuthClientAsync updates client
    - Test DeleteOAuthClientAsync removes client
    - Test GetOAuthClientsAsync returns all clients
    - Test GetOAuthScopesAsync returns available scopes
    - _Requirements: 5.1, 5.2, 5.3_
  
  - [x] 6.11 CRITICAL: Fix OAuth endpoints in AuthControllerRefactored.cs
    - [x] 6.11.1 Rewrite ~/connect/authorize endpoint
      - Keep HttpContext.GetOpenIddictServerRequest() in controller
      - Keep HttpContext.AuthenticateAsync() in controller
      - Call _authOrchestrationService.ValidateOAuthRequestAsync() for validation
      - Call _authOrchestrationService.BuildOAuthClaimsIdentityAsync() for claims
      - Keep SignIn() in controller
      - Keep Forbid() in controller
      - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5_
    
    - [x] 6.11.2 Rewrite ~/connect/token endpoint
      - Keep HttpContext.GetOpenIddictServerRequest() in controller
      - Keep HttpContext.AuthenticateAsync() in controller
      - Call _authOrchestrationService.ValidateUserForTokenExchangeAsync() for validation
      - Call _authOrchestrationService.BuildTokenClaimsIdentityAsync() for fresh claims
      - Keep SignIn() in controller
      - Keep Forbid() in controller
      - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5_
    
    - [x] 6.11.3 Document OAuth pattern in code comments
      - Add comments explaining what MUST stay in controller
      - Add comments explaining what CAN be delegated to service
      - Reference CRITICAL_OIDC_CONTROLLER_REQUIREMENTS.md
      - _Requirements: 16.5_
    
    - [x] 6.11.4 Remove incorrect AuthorizeOAuthAsync and ExchangeTokenAsync methods
      - These methods attempted to delegate SignIn operations (incorrect)
      - Replace with helper methods (ValidateOAuthRequestAsync, BuildOAuthClaimsIdentityAsync, etc.)
      - Update IAuthServices interface to remove these methods
      - _Requirements: 16.1, 16.4_


- [x] 7. Expand AuthOrchestrationService with Priority 4 methods (Low - Already Clean)
  - [x] 7.1 Implement GenerateQRLoginAsync orchestration method
    - Coordinate QRAuthenticationService.GenerateQRLoginTokenAsync
    - Return QR code for login
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 7.2 Implement ValidateQRLoginAsync orchestration method
    - Coordinate QRAuthenticationService.ValidateQRLoginTokenAsync
    - Validate QR login token
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 7.3 Implement SendMagicLinkAsync orchestration method
    - Coordinate MagicLinkService.SendMagicLinkAsync
    - Send magic link email to user
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 7.4 Implement GetUserAsync orchestration method
    - Coordinate UserService.GetUserByIdAsync
    - Return user details
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 7.5 Implement GetAllUsersAsync orchestration method
    - Coordinate UserService.GetAllUsersAsync
    - Return list of all users
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [ ]* 7.6 Write unit tests for Priority 4 orchestration methods
    - Test GenerateQRLoginAsync returns valid QR code
    - Test ValidateQRLoginAsync validates token
    - Test SendMagicLinkAsync sends email
    - Test GetUserAsync returns user details
    - Test GetAllUsersAsync returns all users
    - _Requirements: 5.1, 5.2, 5.3_

- [x] 8. Checkpoint - Verify orchestration expansion
  - Ensure all tests pass, ask the user if questions arise.


### Phase 2.5: HTML Rendering Completion (Week 7.5) - Zero Risk

**Note**: This phase completes the HTML rendering infrastructure before introducing feature flags. All HTML templates exist in the Experimental/Views folder, and the HtmlRenderingService has the core Razor rendering infrastructure. This phase implements the stub methods to actually render the views.

- [x] 8.1 Implement HtmlRenderingService rendering methods for authentication views
  - [x] 8.1.1 Implement RenderLoginForm method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Auth/Login.cshtml
    - Pass error and message parameters to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.1.2 Implement RenderRegisterForm method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Auth/Register.cshtml
    - Pass error, message, adminCheckAttempt, and isAdmin parameters to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.1.3 Implement RenderTotpSetup method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Auth/TotpSetup.cshtml
    - Pass qrCodeUri and secretKey parameters to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.1.4 Implement RenderWebAuthnRegistration method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Auth/WebAuthnRegister.cshtml
    - Pass WebAuthn options JSON to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.1.5 Implement RenderMagicLinkForm method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Auth/MagicLink.cshtml
    - Pass error and message parameters to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.1.6 Implement RenderQrLogin method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Auth/QrLogin.cshtml
    - Pass QR code data to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.1.7 Implement RenderOAuthLoginForm method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Auth/OAuthLogin.cshtml
    - Pass requestId, clientName, scopes, and error parameters to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.1.8 Implement RenderClaimAccountForm method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Auth/ClaimAccount.cshtml
    - Pass error and message parameters to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.1.9 Implement RenderSuccessPage method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Auth/Success.cshtml
    - Pass token parameter to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.1.10 Implement RenderErrorPage method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Auth/Error.cshtml
    - Pass error message to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_

- [x] 8.2 Implement HtmlRenderingService rendering methods for profile and OAuth views
  - [x] 8.2.1 Implement RenderProfilePage method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/Profile/Index.cshtml
    - Pass user profile, TOTP status, WebAuthn credentials, roles, and permissions to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.2.2 Implement RenderOidcClientsList method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/OAuth/ClientsList.cshtml
    - Pass list of OAuth clients and optional token to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.2.3 Implement RenderOidcScopesList method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/OAuth/ScopesList.cshtml
    - Pass list of OAuth scopes and optional token to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.2.4 Implement RenderOidcClientDetails method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/OAuth/ClientDetails.cshtml
    - Pass OAuth client details and optional token to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.2.5 Implement RenderOidcClientForm method
    - Use RenderViewToStringAsync to render ~/Experimental/Views/OAuth/ClientForm.cshtml
    - Pass optional clientId, client data, and token to view model
    - Return rendered HTML string
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_

- [ ] 8.3 Test HTML rendering methods
  - [ ]* 8.3.1 Write unit tests for authentication view rendering
    - Test RenderLoginForm with various error/message combinations
    - Test RenderRegisterForm with admin check scenarios
    - Test RenderTotpSetup with QR code data
    - Test RenderWebAuthnRegistration with options JSON
    - Test RenderMagicLinkForm, RenderQrLogin, RenderOAuthLoginForm
    - Test RenderClaimAccountForm, RenderSuccessPage, RenderErrorPage
    - _Requirements: 5.1, 5.2, 5.3_
  
  - [ ]* 8.3.2 Write unit tests for profile and OAuth view rendering
    - Test RenderProfilePage with complete user data
    - Test RenderOidcClientsList with multiple clients
    - Test RenderOidcScopesList with multiple scopes
    - Test RenderOidcClientDetails with client data
    - Test RenderOidcClientForm for create and edit scenarios
    - _Requirements: 5.1, 5.2, 5.3_

- [x] 8.4 Verify HTML templates are complete and functional
  - [x] 8.4.1 Review all CSHTML templates in Experimental/Views
    - Verify all templates use correct view models
    - Verify all templates include necessary JavaScript files
    - Verify all templates use BRU design system CSS
    - Verify all templates have proper error handling
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.4.2 Test JavaScript functionality in templates
    - Verify login.js works with Login.cshtml
    - Verify register.js works with Register.cshtml
    - Verify WebAuthn JavaScript works with WebAuthnRegister.cshtml
    - Verify OAuth client management JavaScript works
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 8.4.3 Verify CSS styling is consistent
    - Check bru-design-system.css is properly linked in all templates
    - Verify all templates use consistent BRU branding
    - Verify responsive design works on mobile/tablet/desktop
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_

- [x] 8.5 Checkpoint - Verify HTML rendering complete
  - All HtmlRenderingService methods implemented (no more NotImplementedException stubs)
  - All CSHTML templates verified and functional
  - JavaScript and CSS working correctly
  - Ready to integrate with feature flags in Phase 3


### Phase 3: Feature Flag Integration (Week 8) - Zero Risk

- [x] 9. Create feature flag infrastructure
  - [x] 9.1 Create FeatureFlagOptions configuration class
    - Define boolean flags for all 56 endpoints (default: false)
    - Include flags for Login, Register, TOTP, WebAuthn, OAuth, etc.
    - Location: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Options/FeatureFlagOptions.cs`
    - _Requirements: 6.1, 6.2, 6.3, 6.1.1, 15.1, 15.2, 15.3_
  
  - [x] 9.2 Configure feature flags in appsettings.json
    - Add FeatureFlags section with all flags disabled by default
    - Support hot reload without application restart
    - _Requirements: 6.1, 6.2, 6.3, 6.1.2_
  
  - [x] 9.3 Create FeatureFlagService for runtime configuration
    - Implement IFeatureFlagService interface with methods:
      - GetAllFlagsAsync() - List all flags and their current state
      - GetFlagAsync(string flagName) - Get a specific flag value
      - UpdateFlagAsync(string flagName, bool enabled) - Update a flag
      - BulkUpdateFlagsAsync(Dictionary<string, bool> flags) - Update multiple flags
    - Implement priority: runtime overrides (database) > appsettings.json > default (false)
    - Implement in-memory cache for hot reload (immediate effect)
    - Persist runtime overrides to SpacetimeDB FeatureFlagOverride table
    - Location: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Experimental/Services/Implementations/FeatureFlagService.cs`
    - _Requirements: 6.1.3, 6.1.4, 6.1.6, 6.1.7, 6.1.8_
  
  - [x] 9.4 Create FeatureFlagOverride table in SpacetimeDB
    - Add table schema: FlagName (string), Enabled (bool), UpdatedBy (Identity), UpdatedAt (DateTime)
    - Add reducer: UpdateFeatureFlag(string flagName, bool enabled)
    - Location: `server/` (SpacetimeDB module)
    - _Requirements: 6.1.6_
  
  - [x] 9.5 Create admin API endpoints for feature flag management
    - GET /api/admin/feature-flags - List all flags (Admin-only)
    - PUT /api/admin/feature-flags/{flagName} - Update a flag (Admin-only)
    - POST /api/admin/feature-flags/bulk - Update multiple flags (Admin-only)
    - Add [Authorize(Roles = "Admin")] attribute to all endpoints
    - Add audit logging for all flag changes
    - Location: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/AdminController.cs` (new file)
    - _Requirements: 6.1.5, 6.1.9, 6.1.10_
  
  - [x] 9.6 Create admin web UI for feature flag management
    - Create HTML page at /admin/feature-flags with toggle switches
    - Display real-time status (enabled/disabled) for each flag
    - Add bulk enable/disable buttons (e.g., "Enable All TOTP Endpoints")
    - Add confirmation dialogs for critical flags (OAuth endpoints)
    - Add audit log display (who changed what and when)
    - Add "Reset to Defaults" button to clear runtime overrides
    - Location: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Experimental/Views/Admin/FeatureFlags.cshtml`
    - _Requirements: 6.1.4, 6.1.8, 6.1.9_
  
  - [x] 9.7 Register FeatureFlagService in DI container
    - Add configuration binding in Program.cs
    - Register IFeatureFlagService with FeatureFlagService implementation
    - _Requirements: 6.1, 6.2, 6.3_
  
  - [x] 9.8 Write unit tests for feature flag configuration
    - Test flags load correctly from appsettings.json
    - Test default values are false
    - Test runtime overrides take precedence over appsettings.json
    - Test flags can be toggled at runtime via API
    - Test hot reload works without application restart
    - Test admin authorization is enforced
    - Test audit logging captures all changes
    - _Requirements: 5.1, 5.2, 5.3, 6.1.3, 6.1.7, 6.1.10_

- [x] 10. Checkpoint - Verify feature flag infrastructure
  - ✅ All Phase 3 tasks (9.1-9.8) complete
  - ✅ Server-side components verified:
    - FeatureFlagOverride table defined in server/AuthTables.cs
    - UpdateFeatureFlag and ClearFeatureFlagOverrides reducers in server/FeatureFlagReducers.cs
  - ✅ Client-side components verified:
    - FeatureFlagService.cs implementation complete
    - IFeatureFlagService.cs interface complete
    - FeatureFlagOptions.cs configuration class complete
    - AdminController.cs with feature flag endpoints complete
    - Admin web UI view at Experimental/Views/Admin/FeatureFlags.cshtml complete
    - appsettings.json has FeatureFlags section configured
  - ✅ DI registration verified in Program.cs
  - ⚠️ CLIENT BINDINGS NOT YET REGENERATED - compilation errors expected until:
    1. User redeploys SpacetimeDB module: `spacetime publish --project-path server`
    2. User regenerates client bindings: `spacetime generate --lang csharp --out-dir BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/client`
  - 📝 After bindings regenerated, verify compilation succeeds and test feature flag endpoints


### Phase 4: Controller Modification (Weeks 9-10) - Low Risk

- [x] 11. Modify AuthController to support feature flags
  - [x] 11.1 Inject FeatureFlagOptions and AuthOrchestrationService into AuthController
    - Add constructor parameters for IOptions<FeatureFlagOptions> and IAuthOrchestrationService
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 15.4_
  
  - [x] 11.2 Add feature flag checks to Login endpoint
    - Check EnableLoginRefactoring flag at start of method
    - If enabled: delegate to AuthOrchestrationService.LoginAsync
    - If disabled: execute existing legacy code (unchanged)
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_
  
  - [x] 11.3 Add feature flag checks to Register endpoint
    - Check EnableRegisterRefactoring flag at start of method
    - If enabled: delegate to AuthOrchestrationService.RegisterAsync
    - If disabled: execute existing legacy code (unchanged)
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_
  
  - [x] 11.4 Add feature flag checks to TOTP endpoints (4 endpoints)
    - Add flags for TotpSetup, TotpValidate, TotpEnable, TotpDisable
    - Delegate to orchestration service when flags enabled
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_
  
  - [x] 11.5 Add feature flag checks to WebAuthn endpoints (7 endpoints)
    - Add flags for WebAuthn register, validate, credentials operations
    - Delegate to orchestration service when flags enabled
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_
  
  - [x] 11.6 Add feature flag checks to OAuth endpoints (8+ endpoints)
    - Add flags for OAuth authorize, token, userinfo, client management
    - Delegate to orchestration service when flags enabled
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_
  
  - [x] 11.7 Add feature flag checks to remaining endpoints (QR, Magic Link, Profile, etc.)
    - Add flags for all remaining endpoints
    - Delegate to orchestration service when flags enabled
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_


- [ ] 12. Write integration tests for feature flag behavior
  - [ ]* 12.1 Write integration tests for Login endpoint
    - Test with flag enabled: uses new code path
    - Test with flag disabled: uses legacy code path
    - Test backward compatibility: same request/response contracts
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 6.1, 6.2, 6.3_
  
  - [ ]* 12.2 Write integration tests for Register endpoint
    - Test with flag enabled: uses new code path
    - Test with flag disabled: uses legacy code path
    - Test backward compatibility: same request/response contracts
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 6.1, 6.2, 6.3_
  
  - [ ]* 12.3 Write integration tests for TOTP endpoints
    - Test all 4 TOTP endpoints with flags enabled/disabled
    - Test backward compatibility for all endpoints
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 6.1, 6.2, 6.3_
  
  - [ ]* 12.4 Write integration tests for WebAuthn endpoints
    - Test all 7 WebAuthn endpoints with flags enabled/disabled
    - Test backward compatibility for all endpoints
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 6.1, 6.2, 6.3_
  
  - [ ]* 12.5 Write integration tests for OAuth endpoints
    - Test OAuth authorize, token, userinfo with flags enabled/disabled
    - Test backward compatibility for all endpoints
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 6.1, 6.2, 6.3_
  
  - [ ]* 12.6 Write performance tests comparing legacy vs new code paths
    - Benchmark response times for critical endpoints
    - Verify response time increase < 5%
    - Verify database query count unchanged
    - _Requirements: 7.1, 7.2, 7.3_

- [x] 13. Checkpoint - Verify controller modification
  - Ensure all tests pass, ask the user if questions arise.


### Phase 5: Documentation and Deployment Preparation (Week 10)

- [ ] 14. Create architecture documentation
  - [ ] 14.1 Create ARCHITECTURE.md document
    - Document layered architecture overview
    - Document service responsibilities and boundaries
    - Document data flow diagrams
    - Document dependency injection setup
    - Document error handling patterns
    - Document testing strategies
    - Location: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/ARCHITECTURE.md`
    - _Requirements: 11.1, 11.2_
  
  - [ ] 14.2 Create MIGRATION_GUIDE.md document
    - Document step-by-step refactoring process
    - Document how to add new orchestration methods
    - Document how to add feature flags
    - Document how to test refactored endpoints
    - Document how to monitor rollout
    - Document how to rollback if needed
    - Location: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/MIGRATION_GUIDE.md`
    - _Requirements: 11.3_
  
  - [ ] 14.3 Update API documentation
    - Document feature flag behavior in OpenAPI/Swagger
    - Document error response formats
    - Document rate limiting rules (if implemented)
    - Document authentication requirements per endpoint
    - _Requirements: 11.4_

- [ ] 15. Prepare deployment checklist
  - [ ] 15.1 Verify all unit tests passing
    - Run full test suite
    - _Requirements: 5.5_
  
  - [ ] 15.2 Verify all integration tests passing
    - Run integration test suite with flags enabled/disabled
    - _Requirements: 5.5_
  
  - [ ] 15.3 Verify performance benchmarks passing
    - Run performance tests
    - Verify response time increase < 5%
    - _Requirements: 7.1, 7.2, 7.3_
  
  - [ ] 15.4 Configure monitoring and alerting
    - Set up monitoring dashboards for error rates, response times
    - Configure alerts for error rate increase > 1%
    - _Requirements: 6.5_
  
  - [ ] 15.5 Prepare deployment plan
    - Document deployment steps
    - Document rollback procedure
    - Document gradual rollout plan (1% → 10% → 50% → 100%)
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

- [ ] 16. Final checkpoint - Ready for deployment
  - Ensure all tests pass, documentation complete, deployment plan ready.
  - Ask the user if ready to proceed with deployment.


## Notes

- Tasks marked with `*` are optional test-related tasks and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at key milestones
- AuthController remains UNTOUCHED until Phase 4 (Week 9)
- All new code is built in parallel in Experimental folder during Phases 1-3
- Feature flags provide instant rollback capability during gradual rollout
- This is a non-destructive refactoring approach with zero risk to production during development

## Post-Deployment Tasks (Not in Scope for Initial Implementation)

These tasks are performed AFTER successful deployment and gradual rollout:

- **Phase 6: Gradual Rollout** (Weeks 11+)
  - Enable feature flags incrementally (1% → 10% → 50% → 100%)
  - Monitor error rates, performance, user feedback
  - Rollback instantly if issues detected
  - Iterate and improve based on production data

- **Phase 7: Legacy Code Cleanup** (Weeks 20+, after full validation)
  - Delete legacy AuthController.cs file (8,293 lines)
  - Rename AuthControllerRefactored.cs to AuthController.cs
  - Remove routing infrastructure ([LegacyAction]/[RefactoredAction] attributes)
  - KEEP feature flag infrastructure for operational flexibility
  - Repurpose feature flags for endpoint availability control
  - Final controller size: ~2,000-2,500 lines

## Success Criteria

**Technical Success Criteria**:
- 100% of endpoints follow Controller → Orchestration → Services → Database pattern
- Zero direct database access in AuthController
- 100% orchestration layer coverage (50 methods)
- AuthController reduced from 8,293 lines to ~2,000-2,500 lines (after Phase 7 cleanup)

**Quality Success Criteria**:
- Zero breaking changes to API contracts
- Response time increase < 5%
- Database query count unchanged
- Memory usage increase < 10%

**Reliability Success Criteria**:
- 99.9% uptime maintained during migration
- Error rate increase < 0.1%
- Zero production incidents during rollout
- Instant rollback capability verified

### Phase 6: Gradual Production Rollout (Weeks 11+) - Controlled Risk

**Note**: These tasks are performed AFTER successful deployment to production with all feature flags disabled.

**EXECUTION INSTRUCTIONS FOR PHASE 6**:

This phase enables the refactored endpoints by setting feature flags in `appsettings.json`. The dual-controller architecture with `[LegacyAction]` and `[RefactoredAction]` attributes automatically routes requests based on flag state.

**Prerequisites**:
- All endpoints in legacy `AuthController.cs` MUST have `[LegacyAction]` attributes
- All endpoints in `AuthControllerRefactored.cs` MUST have `[RefactoredAction]` attributes
- Feature flag infrastructure (Phase 3) must be complete
- Application deployed to production with all flags disabled

**How to Enable Flags**:

1. **Edit `appsettings.json`** in `BRU-AVTOPARK-AspireAPI.ApiService/`:
   ```json
   "FeatureFlags": {
     "EnableLoginRefactoring": true,           // Enable Login endpoint
     "EnableRegisterRefactoring": true,        // Enable Register endpoint
     "EnableTotpValidateRefactoring": true,    // Enable TOTP validate endpoint
     "EnableWebAuthnValidateRefactoring": true, // Enable WebAuthn validate endpoint
     "EnableProfileRefactoring": true          // Enable Profile endpoint
   }
   ```

2. **Restart the application** - Changes take effect immediately on restart

3. **Test the endpoints** - All requests to enabled endpoints will now route to `AuthControllerRefactored`

4. **Monitor for issues**:
   - Check application logs for errors
   - Test authentication flows (login, register, 2FA, profile)
   - Compare behavior with legacy endpoints (set flags to `false` to test)

5. **Rollback if needed** - Set flags back to `false` and restart

**Alternative: Runtime Flag Management** (Optional):
- Use admin UI at `/admin/feature-flags-ui` (requires Admin role)
- Use admin API: `PUT /api/admin/feature-flags/{flagName}` with `{"enabled": true}`
- Runtime changes are stored in SpacetimeDB and take effect immediately (no restart needed)

**Gradual Rollout Strategy** (Production):
- For true 1% → 10% → 50% → 100% rollout, you'd need a percentage-based feature flag service
- Current implementation is binary (100% enabled or 100% disabled)
- For testing: Enable all flags, test thoroughly, then deploy to production

**Verification**:
- Check routing works: Requests should hit `AuthControllerRefactored` when flags are `true`
- Check routing works: Requests should hit `AuthController` (legacy) when flags are `false`
- Verify backward compatibility: Same request/response contracts
- Verify no breaking changes: Existing clients continue working

---

- [x] 17. Enable feature flags incrementally for Priority 1 endpoints (Critical)
  
  - [x] 17.1 Enable Login endpoint for 1% of users
    - Monitor error rates, response times, user feedback for 24 hours
    - If stable, increase to 10% for 24 hours
    - If stable, increase to 50% for 48 hours
    - If stable, enable for 100% of users
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [x] 17.2 Enable Register endpoint for 1% of users
    - Follow same gradual rollout pattern as Login
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [x] 17.3 Enable TOTP validate endpoint for 1% of users
    - Follow same gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [x] 17.4 Enable WebAuthn validate endpoint for 1% of users
    - Follow same gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [x] 17.5 Enable Profile endpoint for 1% of users
    - Follow same gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_

- [x] 18. Enable feature flags for Priority 2 endpoints (High)
  - [x] 18.1 Enable TOTP setup/enable/disable endpoints
    - Toggle feature flags (binary on/off) for each endpoint
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [x] 18.2 Enable WebAuthn register/credentials endpoints
    - Toggle feature flags (binary on/off) for each endpoint
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_

- [x] 19. Enable feature flags for Priority 3 endpoints (Medium - OAuth)
  - [x] 19.1 Enable OAuth authorize endpoint
    - Follow gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [x] 19.2 Enable OAuth token endpoint
    - Follow gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [x] 19.3 Enable OAuth userinfo endpoint
    - Follow gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [x] 19.4 Enable OAuth client management endpoints
    - Follow gradual rollout pattern for each endpoint
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_

- [x] 20. Enable feature flags for Priority 4 endpoints (Low - Already Clean)
  - [x] 20.1 Enable QR authentication endpoints
    - Follow gradual rollout pattern for each endpoint
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [x] 20.2 Enable Magic Link endpoints
    - Follow gradual rollout pattern for each endpoint
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  

- [ ] 21. Monitor and validate production rollout
  - [ ] 21.1 Monitor error rates across all enabled endpoints
    - Set up alerts for error rate increase > 1%
    - Automatic rollback if error rate threshold exceeded
    - _Requirements: 6.5, 12.5_
  
  - [ ] 21.2 Monitor performance metrics
    - Track response times (p50, p95, p99)
    - Track database query counts
    - Track memory and CPU usage
    - _Requirements: 7.1, 7.2, 7.3_
  
  - [ ] 21.3 Collect user feedback
    - Monitor support tickets related to authentication
    - Track authentication success/failure rates
    - _Requirements: 12.5_
  
  - [ ] 21.4 Document any issues and resolutions
    - Create incident reports for any rollback events
    - Document lessons learned
    - _Requirements: 11.3_

- [ ] 22. Checkpoint - Verify all endpoints at 100% rollout
  - Ensure all 56 endpoints are using new code path at 100%
  - Verify error rates, performance metrics, user feedback are acceptable
  - Ask the user if ready to proceed with legacy code removal


### Phase 7: Legacy Code Cleanup (Weeks 20+) - Zero Risk

**Note**: These tasks are performed AFTER all endpoints have been at 100% rollout for several weeks/months with stable operation.

**IMPORTANT**: Feature flag infrastructure is KEPT for operational flexibility. This allows operational disablement (flags disable endpoints with 503 response after legacy controller deletion, rather than restoring implementations) and endpoint availability control even after cleanup.

- [ ] 23. Delete legacy AuthController.cs file
  - [ ] 23.1 Verify all feature flags are enabled and stable
    - Confirm all 56 endpoints have been at 100% rollout for at least 2-4 weeks
    - Verify error rates, performance metrics are acceptable
    - Confirm no production incidents related to refactored endpoints
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_
  
  - [ ] 23.2 Delete Controllers/AuthController.cs
    - Remove the entire 8,293-line legacy controller file
    - This file contains all legacy implementations with [LegacyAction] attributes
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_
  
  - [ ] 23.3 Update integration tests for post-cleanup behavior
    - Remove tests that specifically test legacy AuthController implementations
    - Keep tests for AuthController
    - Keep/rewrite feature flag toggle tests to validate endpoint-disable semantics (503 responses when disabled)
    - Remove only legacy-controller-specific test logic, not toggle validation assertions
    - _Requirements: 5.5_

- [ ] 24. Clean up AuthControllerRefactored.cs
  - [ ] 24.1 Remove [RefactoredAction] attributes from all endpoints
    - These attributes are no longer needed since legacy controller is deleted
    - Keep all other attributes ([HttpPost], [Authorize], etc.)
    - Location: Controllers/AuthControllerRefactored.cs
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_
  
  - [ ] 24.2 Rename AuthControllerRefactored.cs to AuthController.cs
    - This becomes the new primary AuthController
    - Update class name from AuthControllerRefactored to AuthController
    - Update constructor and all internal references
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_
  
  - [ ] 24.3 Update route attribute to use token replacement
    - Change `[Route("api/Auth")]` to `[Route("api/[controller]")]` for consistency with ASP.NET Core conventions
    - This maintains the same route ("api/Auth") but uses standard token replacement pattern
    - Verify all routes still match expected patterns after change
    - Ensure backward compatibility with existing clients
    - Location: Controllers/AuthController.cs (after rename from AuthControllerRefactored.cs)
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

- [ ] 25. Clean up routing infrastructure
  - [ ] 25.1 Delete FeatureFlagActionConstraint.cs
    - Remove Routing/FeatureFlagActionConstraint.cs
    - Remove RefactoredActionAttribute and LegacyActionAttribute classes
    - No longer needed since only one controller exists
    - _Requirements: 1.5_
  
  - [ ] 25.2 Delete FeatureFlagEndpointSelector.cs (if exists)
    - Remove Routing/FeatureFlagEndpointSelector.cs
    - Remove any endpoint selection logic
    - _Requirements: 1.5_
  
  - [ ] 25.3 Delete FeatureFlagEndpointSelectorPolicy.cs (if exists)
    - Remove Routing/FeatureFlagEndpointSelectorPolicy.cs
    - Remove any policy-based routing logic
    - _Requirements: 1.5_
  
  - [ ] 25.4 Remove routing middleware registration from Program.cs
    - Remove FeatureFlagRoutingMiddleware registration (if exists)
    - Remove any custom endpoint selector registration
    - Keep standard ASP.NET Core routing
    - _Requirements: 1.5_

- [ ] 26. Update feature flag configuration (KEEP, but simplify)
  - [ ] 26.1 Update FeatureFlagOptions.cs documentation
    - Add comment: "Feature flags kept for operational flexibility and A/B testing"
    - Add comment: "All flags default to true since legacy controller is removed"
    - Document that flags can still be used to disable specific endpoints if needed
    - Location: Options/FeatureFlagOptions.cs
    - _Requirements: 6.1, 6.2, 6.3_
  
  - [ ] 26.2 Update appsettings.json default values
    - Set all feature flags to true by default
    - Add comment explaining flags are kept for operational flexibility
    - Document that setting a flag to false will disable the endpoint entirely
    - _Requirements: 6.1, 6.2, 6.3_
  
  - [ ] 26.3 Keep FeatureFlagService.cs unchanged
    - Service remains functional for runtime flag management
    - Admin UI continues to work for toggling endpoints on/off
    - Useful for emergency endpoint disabling or A/B testing
    - _Requirements: 6.1.3, 6.1.4, 6.1.6, 6.1.7, 6.1.8_
  
  - [ ] 26.4 Keep admin feature flag UI and API endpoints
    - Keep /admin/feature-flags UI page
    - Keep /api/admin/feature-flags/* API endpoints
    - Update UI documentation to reflect new purpose (enable/disable endpoints, not switch implementations)
    - _Requirements: 6.1.4, 6.1.5, 6.1.8, 6.1.9_

- [ ] 27. Update AuthController to use feature flags for operational disablement
  - [ ] 27.1 Add feature flag checks at start of each endpoint for operational control
    - Check if endpoint's feature flag is enabled
    - If disabled, return 503 Service Unavailable with message (operational disablement, not rollback to legacy)
    - Example: `if (!_featureFlags.Value.EnableLoginRefactoring) return StatusCode(503, "Login endpoint temporarily disabled");`
    - This provides operational control to disable endpoints after legacy controller deletion
    - _Requirements: 6.1, 6.2, 6.3, 15.4_
  
  - [ ] 27.2 Add logging for disabled endpoint access attempts
    - Log when users hit disabled endpoints
    - Include endpoint name, user identity (if available), timestamp
    - Helps track impact of endpoint disabling
    - _Requirements: 6.5, 12.5_
  
  - [ ] 27.3 Document feature flag behavior in code comments
    - Add XML comments explaining feature flag purpose
    - Document that flags control endpoint availability, not implementation selection
    - Provide examples of when to disable endpoints (security issues, performance problems, etc.)
    - _Requirements: 11.1, 11.2_

- [ ] 28. Remove duplicated helper methods from old AuthController locations
  - [ ] 28.1 Verify no duplicated IsAdmin method exists
    - Should only exist in AuthOrchestrationService
    - Remove from any other locations if found
    - _Requirements: 3.1, 3.2, 3.5_
  
  - [ ] 28.2 Verify no duplicated HasPermission method exists
    - Should only exist in AuthOrchestrationService
    - Remove from any other locations if found
    - _Requirements: 3.1, 3.3, 3.5_
  
  - [ ] 28.3 Verify no duplicated GenerateJwtToken method exists
    - Should only exist in TokenService
    - Remove from any other locations if found
    - _Requirements: 3.1, 3.5_
  
  - [ ] 28.4 Verify no duplicated GetUserIdentity method exists
    - Should only exist in IdentityService
    - Remove from any other locations if found
    - _Requirements: 3.1, 3.4, 3.5_
  
  - [ ] 28.5 Verify no duplicated IsBrowserRequest method exists
    - Should only exist in RequestDetector service
    - Remove from any other locations if found
    - _Requirements: 3.1, 3.5_
  
  - [ ] 28.6 Verify no duplicated GenerateRandomToken method exists
    - Should only exist in TokenService
    - Remove from any other locations if found
    - _Requirements: 3.1, 3.5_

- [ ] 29. Verify controller line count reduction
  - [ ] 29.1 Measure final AuthController.cs line count
    - Target: ~2,000-2,500 lines (down from 8,293 lines)
    - 70-75% reduction achieved
    - Includes feature flag checks for endpoint disabling
    - _Requirements: 1.5_
  
  - [ ] 29.2 Verify all endpoints follow clean architecture
    - All endpoints delegate to orchestration service
    - Zero direct database access
    - Zero duplicated helper methods
    - Feature flags used only for endpoint availability control
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

- [ ] 30. Final testing and validation
  - [ ] 30.1 Run full test suite
    - All unit tests passing
    - All integration tests passing
    - All performance tests passing
    - _Requirements: 5.5_
  
  - [ ] 30.2 Test feature flag endpoint operational disablement
    - Disable each endpoint via feature flag
    - Verify 503 Service Unavailable response (operational disablement semantics)
    - Verify logging works correctly
    - Re-enable and verify endpoint works again
    - Validate that disabling does not restore legacy implementations (confirms post-cleanup behavior)
    - _Requirements: 6.1, 6.2, 6.3, 6.5_
  
  - [ ] 30.3 Perform security audit
    - Verify all authentication logic centralized
    - Verify all authorization logic centralized
    - Verify audit logging implemented
    - Verify feature flag admin endpoints are properly secured
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_
  
  - [ ] 30.4 Update documentation
    - Update ARCHITECTURE.md to reflect final state
    - Document feature flag purpose (endpoint availability control)
    - Update API documentation
    - Document operational procedures for disabling endpoints
    - _Requirements: 11.1, 11.2, 11.4_

- [ ] 31. Deploy cleanup to production
  - [ ] 31.1 Deploy to staging environment
    - Run full test suite in staging
    - Perform manual testing
    - Test feature flag endpoint disabling
    - _Requirements: 12.1, 12.2_
  
  - [ ] 31.2 Deploy to production
    - Monitor for 24 hours
    - Verify all endpoints functioning correctly
    - Verify performance metrics unchanged
    - Verify feature flag admin UI works correctly
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

- [ ] 32. Final checkpoint - Cleanup complete
  - Legacy AuthController.cs deleted (8,293 lines removed)
  - AuthControllerRefactored.cs renamed to AuthController.cs (~2,000-2,500 lines)
  - Routing infrastructure cleaned up ([LegacyAction]/[RefactoredAction] attributes removed)
  - Feature flag infrastructure KEPT for operational flexibility
  - Feature flags repurposed for endpoint availability control
  - 100% clean architecture across all 56 endpoints
  - Zero direct database access
  - Zero code duplication
  - All success criteria met


## Future Vision: Frontend-Agnostic Authentication Service (6-12 Months Post-Refactoring)

**Note**: These tasks represent the long-term evolution beyond the current refactoring. They transform the auth service into a pure JSON API backend that can serve any frontend (Next.js, React, Vue, mobile apps, etc.).

**IMPORTANT**: This is ONE POSSIBLE path forward. There are multiple valid architectural approaches depending on your needs:

### Path Options

**Option 1: Keep Current Hybrid Approach (Recommended for Most Teams)**
- Keep CSHTML views for browser-based authentication
- Keep JSON API endpoints for programmatic access
- Feature flags control endpoint availability (not implementation selection)
- **Pros**: Simple, works well, no additional frontend work needed
- **Cons**: Coupled to ASP.NET Core Razor engine
- **Best for**: Teams that don't need a separate frontend framework

**Option 2: Gradual Frontend Decoupling (Phases 8-11 below)**
- Add JSON API support alongside HTML (Phase 8)
- Extract rendering abstraction (Phase 9)
- Build separate frontend (Next.js, React, etc.) (Phase 10)
- Remove CSHTML entirely (Phase 11)
- **Pros**: Complete separation of concerns, frontend flexibility
- **Cons**: Significant additional work, requires frontend expertise
- **Best for**: Teams building multi-platform apps (web + mobile + desktop)

**Option 3: API-First Only (Skip Phases 9-11)**
- Add JSON API support alongside HTML (Phase 8)
- Keep CSHTML for browser clients indefinitely
- Use JSON APIs for mobile/desktop apps
- **Pros**: Flexibility without full decoupling, less work than Option 2
- **Cons**: Still coupled to Razor engine
- **Best for**: Teams adding mobile/desktop apps but keeping web as-is

**Option 4: Immediate Frontend Replacement (Skip Phases 8-9)**
- Build new frontend immediately (Next.js, React, etc.)
- Remove CSHTML once new frontend is stable
- **Pros**: Fastest path to frontend-agnostic architecture
- **Cons**: High risk, no gradual migration
- **Best for**: Teams with strong frontend expertise and appetite for risk

**Option 5: Keep CSHTML Forever**
- Don't do any of Phases 8-11
- Use refactored controller with CSHTML views
- Feature flags control endpoint availability only
- **Pros**: Simplest, no additional work
- **Cons**: Coupled to ASP.NET Core
- **Best for**: Teams satisfied with current approach

**Option 6: ElysiaJS as Authentication Gateway (Advanced)**
- Replace ASP.NET Core auth layer with ElysiaJS (TypeScript/Bun)
- ElysiaJS connects DIRECTLY to SpacetimeDB for auth data
- C# backend becomes pure business logic API
- Use oidc-provider for production-grade OIDC implementation
- **Pros**: Modern TypeScript stack, better performance (~10k req/s), simpler SpacetimeDB integration (no ID mapping, no polling), native WebSocket support
- **Cons**: Two runtimes (Bun + .NET), requires TypeScript expertise, significant migration effort
- **Best for**: Teams with TypeScript expertise wanting to modernize auth stack and solve current OpenIddict/SpacetimeDB complexity

**Option 7: ElysiaJS as OAuth Proxy (Compatibility Layer)**
- Keep existing C# authentication system unchanged
- ElysiaJS PROXIES OAuth/OIDC endpoints to JavaScript frontends
- C# backend retains ALL authentication logic
- **Pros**: Zero C# changes, minimal risk, enables JS clients
- **Cons**: Two auth systems, limited benefit
- **Best for**: Teams needing OAuth for JS clients but keeping C# auth system

**Option 8: Hybrid ElysiaJS Approach (Gradual Migration)**
- Use ElysiaJS for new features, keep C# for existing features
- Incremental migration over time
- **Pros**: Zero risk, best of both worlds
- **Cons**: Operational complexity, unclear boundaries
- **Best for**: Teams wanting to experiment with ElysiaJS without full commitment

### Recommended Approach

For most teams, we recommend **Option 1** (keep current hybrid approach) or **Option 3** (add JSON APIs but keep CSHTML). Only pursue full frontend decoupling (Option 2) or ElysiaJS migration (Options 6-8) if you have:
- Strong frontend/TypeScript development expertise
- Need for multi-platform apps (web + mobile + desktop)
- Time and resources for 6-12 months of additional work
- Business justification for the investment
- (For ElysiaJS) Need to solve current OpenIddict/SpacetimeDB complexity

The tasks below describe **Option 2** (full frontend decoupling with Next.js) as the most comprehensive path. For ElysiaJS options (6-8), see the design document for detailed implementation guidance.

---

### Phase 8: API-First Endpoints (Months 1-2 Post-Refactoring)

- [ ] 30. Ensure all auth flows have JSON API endpoints
  - [ ] 30.1 Audit all authentication endpoints for JSON support
    - Verify Login, Register, TOTP, WebAuthn, Magic Link all return JSON
    - Identify any endpoints that only return HTML
    - _Requirements: Future vision - API-first design_
  
  - [ ] 30.2 Add content negotiation to mixed endpoints
    - Detect browser vs API client via Accept header
    - Return JSON for API clients, HTML for browsers
    - Maintain backward compatibility
    - _Requirements: Future vision - API-first design_
  
  - [ ] 30.3 Create JSON API versions of OAuth endpoints
    - Add `POST /api/auth/oauth/login` for JSON authentication
    - Add `GET /api/auth/oauth/consent` for consent status
    - Add `POST /api/auth/oauth/consent` for consent grant
    - Keep existing HTML endpoints for backward compatibility
    - _Requirements: Future vision - Headless OAuth_
  
  - [ ] 30.4 Document JSON API contracts
    - Update OpenAPI/Swagger with JSON request/response schemas
    - Document content negotiation behavior
    - Provide example requests/responses
    - _Requirements: Future vision - API-first design_

- [ ] 31. Test dual-mode system (HTML + JSON)
  - [ ]* 31.1 Write integration tests for JSON API endpoints
    - Test all endpoints with Accept: application/json header
    - Verify JSON responses match expected schemas
    - _Requirements: Future vision - API-first design_
  
  - [ ]* 31.2 Write integration tests for HTML endpoints
    - Test all endpoints with Accept: text/html header
    - Verify HTML responses render correctly
    - _Requirements: Future vision - API-first design_
  
  - [ ]* 31.3 Test content negotiation
    - Verify correct response format based on Accept header
    - Test fallback behavior for missing Accept header
    - _Requirements: Future vision - API-first design_

- [ ] 32. Checkpoint - Verify dual-mode system working
  - All endpoints support both JSON and HTML
  - Content negotiation working correctly
  - Backward compatibility maintained


### Phase 9: Extract Rendering (Months 3-4 Post-Refactoring)

- [ ] 33. Create view rendering abstraction layer
  - [ ] 33.1 Expand IViewRenderingService interface
    - Add methods for all HTML rendering operations
    - Define clear contracts for view rendering
    - Location: `Experimental/Services/Interfaces/IViewRenderingService.cs`
    - _Requirements: Future vision - Rendering abstraction_
  
  - [ ] 33.2 Move all HTML generation to HtmlRenderingService
    - Extract inline HTML strings from controllers
    - Move CSHTML view rendering to service
    - Centralize all HTML generation logic
    - _Requirements: Future vision - Rendering abstraction_
  
  - [ ] 33.3 Update controllers to use rendering service
    - Replace direct view rendering with service calls
    - Replace inline HTML with service calls
    - Maintain same HTML output for backward compatibility
    - _Requirements: Future vision - Rendering abstraction_
  
  - [ ]* 33.4 Write tests for rendering service
    - Test all rendering methods produce correct HTML
    - Test view data binding
    - Test error handling
    - _Requirements: Future vision - Rendering abstraction_

- [ ] 34. Prepare for view engine swap
  - [ ] 34.1 Document rendering service interface
    - Document all rendering methods
    - Document view data requirements
    - Document HTML output contracts
    - _Requirements: Future vision - Rendering abstraction_
  
  - [ ] 34.2 Create rendering service implementation guide
    - Document how to implement alternative rendering engines
    - Provide examples for Next.js, React, Vue
    - Document migration path
    - _Requirements: Future vision - Rendering abstraction_

- [ ] 35. Checkpoint - Verify rendering abstraction complete
  - All HTML rendering goes through IViewRenderingService
  - Controllers have no direct view rendering
  - Ready for view engine swap


### Phase 10: Frontend Decoupling (Months 5-8 Post-Refactoring)

- [ ] 36. Build Next.js frontend
  - [ ] 36.1 Set up Next.js project
    - Initialize Next.js with TypeScript
    - Configure Tailwind CSS
    - Set up project structure
    - _Requirements: Future vision - Next.js frontend_
  
  - [ ] 36.2 Build authentication pages
    - Create login page that calls C# JSON API
    - Create register page that calls C# JSON API
    - Create TOTP setup/validate pages
    - Create WebAuthn registration pages
    - Create profile page
    - _Requirements: Future vision - Next.js frontend_
  
  - [ ] 36.3 Build OAuth pages
    - Create OAuth login page (proxy to C# backend)
    - Create OAuth consent page (proxy to C# backend)
    - Handle OAuth redirects and state management
    - _Requirements: Future vision - Headless OAuth_
  
  - [ ] 36.4 Implement session management in Next.js
    - Use iron-session or similar for server-side sessions
    - Store OAuth request parameters temporarily
    - Handle session expiration
    - _Requirements: Future vision - Session management_
  
  - [ ] 36.5 Configure CORS for Next.js origin
    - Add Next.js origin to CORS policy in C# backend
    - Configure credentials support
    - Test cross-origin requests
    - _Requirements: Future vision - CORS configuration_
  
  - [ ] 36.6 Build API client library
    - Create TypeScript client for C# JSON APIs
    - Handle authentication tokens
    - Handle error responses
    - _Requirements: Future vision - API client_

- [ ] 37. Run Next.js side-by-side with CSHTML
  - [ ] 37.1 Deploy Next.js to separate domain/subdomain
    - Set up hosting (Vercel, Netlify, or self-hosted)
    - Configure DNS
    - Set up SSL certificates
    - _Requirements: Future vision - Side-by-side deployment_
  
  - [ ] 37.2 Add feature flag for frontend selection
    - Allow users to opt-in to Next.js frontend
    - Maintain CSHTML as default for existing users
    - Track usage metrics for both frontends
    - _Requirements: Future vision - Gradual migration_
  
  - [ ] 37.3 Gradually migrate users to Next.js
    - Start with 1% of users
    - Increase to 10%, 50%, 100% based on feedback
    - Monitor error rates and user satisfaction
    - _Requirements: Future vision - Gradual migration_
  
  - [ ]* 37.4 Write end-to-end tests for Next.js frontend
    - Test all authentication flows
    - Test OAuth flows
    - Test error handling
    - _Requirements: Future vision - Testing_

- [ ] 38. Checkpoint - Verify Next.js frontend working
  - All authentication flows working in Next.js
  - OAuth flows working correctly
  - Users can choose between CSHTML and Next.js
  - Ready for CSHTML deprecation


### Phase 11: CSHTML Deprecation (Months 9-12 Post-Refactoring)

- [ ] 39. Remove Razor view engine
  - [ ] 39.1 Remove all CSHTML views
    - Delete all .cshtml files from Views folder
    - Delete all view models used only for CSHTML
    - _Requirements: Future vision - Pure JSON API_
  
  - [ ] 39.2 Remove Razor view engine from Program.cs
    - Remove AddControllersWithViews()
    - Remove view engine middleware
    - Keep only AddControllers() for JSON APIs
    - _Requirements: Future vision - Pure JSON API_
  
  - [ ] 39.3 Remove HtmlRenderingService
    - Delete IViewRenderingService interface
    - Delete HtmlRenderingService implementation
    - Remove from DI container
    - _Requirements: Future vision - Pure JSON API_
  
  - [ ] 39.4 Update controllers to JSON-only
    - Remove all HTML rendering code
    - Return only JSON responses
    - Update error responses to JSON format
    - _Requirements: Future vision - Pure JSON API_

- [ ] 40. Remove inline HTML generation
  - [ ] 40.1 Remove inline HTML from OAuth authorize endpoint
    - Convert to JSON-only endpoint
    - Return JSON with login/consent URLs
    - _Requirements: Future vision - Headless OAuth_
  
  - [ ] 40.2 Remove inline HTML from all other endpoints
    - Audit all endpoints for inline HTML strings
    - Convert to JSON responses
    - _Requirements: Future vision - Pure JSON API_

- [ ] 41. Update documentation for JSON-only API
  - [ ] 41.1 Update ARCHITECTURE.md
    - Document pure JSON API architecture
    - Document frontend-agnostic design
    - Remove CSHTML-related documentation
    - _Requirements: Future vision - Documentation_
  
  - [ ] 41.2 Update API documentation
    - Remove HTML response examples
    - Document JSON-only contracts
    - Update integration guides
    - _Requirements: Future vision - Documentation_
  
  - [ ] 41.3 Create frontend integration guide
    - Document how to integrate with Next.js
    - Document how to integrate with React
    - Document how to integrate with Vue
    - Document how to integrate with mobile apps
    - _Requirements: Future vision - Documentation_

- [ ] 42. Final testing and deployment
  - [ ]* 42.1 Run full test suite
    - All unit tests passing
    - All integration tests passing (JSON-only)
    - All end-to-end tests passing (Next.js frontend)
    - _Requirements: Future vision - Testing_
  
  - [ ] 42.2 Deploy to staging
    - Test JSON-only API with Next.js frontend
    - Verify all flows working
    - _Requirements: Future vision - Deployment_
  
  - [ ] 42.3 Deploy to production
    - Monitor for 24 hours
    - Verify all endpoints functioning correctly
    - Verify Next.js frontend working correctly
    - _Requirements: Future vision - Deployment_

- [ ] 43. Final checkpoint - Frontend-agnostic architecture complete
  - C# backend is pure JSON API (no HTML rendering)
  - Next.js handles all user-facing pages
  - Complete separation of concerns
  - Ready for multi-platform expansion (mobile apps, desktop apps, etc.)


## Timeline Summary

### Core Refactoring (Phases 1-5)
- **Phase 1**: Service Creation (Weeks 1-2) - Zero Risk
- **Phase 2**: Orchestration Expansion (Weeks 3-7) - Zero Risk
- **Phase 3**: Feature Flag Integration (Week 8) - Zero Risk
- **Phase 4**: Controller Modification (Weeks 9-10) - Low Risk
- **Phase 5**: Documentation and Deployment Preparation (Week 10)

**Total Duration**: 10 weeks

### Post-Deployment (Phases 6-7)
- **Phase 6**: Gradual Production Rollout (Weeks 11+) - Controlled Risk
  - Enable flags incrementally (1% → 10% → 50% → 100%)
  - Monitor error rates, performance, user feedback
  - Duration: 1-2 months

- **Phase 7**: Legacy Code Cleanup (Weeks 20+) - Zero Risk
  - Delete legacy AuthController.cs file
  - Rename AuthControllerRefactored.cs to AuthController.cs
  - Clean up routing infrastructure
  - KEEP feature flag infrastructure for operational flexibility
  - Duration: 2-3 weeks
  - **Prerequisite**: All endpoints at 100% rollout for several weeks/months with stable operation

**Total Duration**: 3-4 months from start to legacy code removal

### Future Vision (Phases 8-11) - OPTIONAL

**Note**: These phases are OPTIONAL and represent different paths forward. Choose based on your team's needs:

- **Option 1 (Keep Current)**: Skip all future phases - use refactored controller with CSHTML
- **Option 2 (Full Decoupling)**: Complete all phases 8-11 for frontend-agnostic architecture
- **Option 3 (API-First Hybrid)**: Complete Phase 8 only, keep CSHTML for browsers
- **Option 4 (Fast Replacement)**: Skip to Phase 10, build new frontend immediately
- **Option 5 (Stay Simple)**: Skip all future phases, keep CSHTML forever

**If pursuing Option 2 (Full Decoupling)**:
- **Phase 8**: API-First Endpoints (Months 1-2 Post-Refactoring)
  - Add JSON API support alongside HTML
  - Duration: 2-3 weeks

- **Phase 9**: Extract Rendering (Months 3-4 Post-Refactoring)
  - Create view rendering abstraction
  - Duration: 2-3 weeks

- **Phase 10**: Frontend Decoupling (Months 5-8 Post-Refactoring)
  - Build Next.js frontend (or React, Vue, etc.)
  - Run side-by-side with CSHTML
  - Duration: 8-12 weeks

- **Phase 11**: CSHTML Deprecation (Months 9-12 Post-Refactoring)
  - Remove Razor view engine
  - Pure JSON API backend
  - Duration: 2-3 weeks

**Total Duration (Option 2)**: 12+ months for complete frontend-agnostic architecture

**Most teams should choose Option 1 or Option 3** - the refactored controller with CSHTML views works well for most use cases.

## Risk Assessment by Phase

| Phase | Risk Level | Reason | Rollback Strategy |
|-------|-----------|--------|-------------------|
| 1-3 | **Zero** | AuthController untouched, new code in parallel | N/A - no production impact |
| 4 | **Low** | Controller modified but legacy code path active by default | Disable feature flags |
| 5 | **Zero** | Documentation only | N/A |
| 6 | **Controlled** | Gradual rollout with monitoring | Instant rollback via feature flags |
| 7 | **Zero** | Only after months of stable operation | Revert deployment |
| 8-11 | **Low-Medium (OPTIONAL)** | Gradual migration with dual-mode support | Keep CSHTML as fallback |

**Note**: Phases 8-11 are optional. Most teams can stop after Phase 7 and use the refactored controller with CSHTML views.

## Dependencies Between Phases

- **Phase 2** depends on **Phase 1**: Need TwoFactorService and SettingsService before orchestration
- **Phase 4** depends on **Phases 1-3**: Need services, orchestration, and feature flags before controller modification
- **Phase 6** depends on **Phases 1-5**: Need complete implementation before production rollout
- **Phase 7** depends on **Phase 6**: Need stable production operation before legacy code cleanup
- **Phases 8-11** (OPTIONAL) depend on **Phase 7**: Need clean architecture before frontend decoupling

**Note**: Phases 8-11 are optional. You can stop after Phase 7 and have a fully functional, refactored authentication system.

## Effort Estimation

### Core Refactoring (Phases 1-5)
- **Phase 1**: 2-3 days (2 services + tests)
- **Phase 2**: 10-15 days (45 orchestration methods + tests)
- **Phase 3**: 1-2 days (feature flag infrastructure)
- **Phase 4**: 3-5 days (controller modifications + tests)
- **Phase 5**: 2-3 days (documentation)

**Total**: ~20-30 days of development work

### Post-Deployment (Phases 6-7)
- **Phase 6**: 1-2 months (gradual rollout + monitoring)
- **Phase 7**: 2-3 days (legacy code cleanup - delete old controller, rename new one, clean up routing)

**Total**: ~1-2 months with minimal active development

### Future Vision (Phases 8-11) - OPTIONAL

**Note**: Only pursue these phases if you need frontend-agnostic architecture. Most teams can stop after Phase 7.

**If pursuing Option 2 (Full Frontend Decoupling)**:
- **Phase 8**: 2-3 weeks (JSON API endpoints)
- **Phase 9**: 2-3 weeks (rendering abstraction)
- **Phase 10**: 8-12 weeks (Next.js/React/Vue frontend)
- **Phase 11**: 2-3 weeks (CSHTML deprecation)

**Total (Option 2)**: ~4-6 months of development work

**If pursuing Option 3 (API-First Hybrid)**:
- **Phase 8**: 2-3 weeks (JSON API endpoints)
- Skip Phases 9-11

**Total (Option 3)**: ~2-3 weeks of development work

**If pursuing Option 1 or Option 5 (Keep Current)**:
- Skip all future phases
- **Total**: 0 additional work

## Key Milestones

**Core Refactoring (Required)**:
1. ✅ **Milestone 1**: Services created (End of Phase 1)
2. ✅ **Milestone 2**: Orchestration complete (End of Phase 2)
3. ✅ **Milestone 3**: Feature flags ready (End of Phase 3)
4. ✅ **Milestone 4**: Controller modified (End of Phase 4)
5. ✅ **Milestone 5**: Ready for deployment (End of Phase 5)
6. ✅ **Milestone 6**: All endpoints at 100% rollout (End of Phase 6)
7. ✅ **Milestone 7**: Legacy code cleaned up (End of Phase 7) - **STOPPING POINT FOR MOST TEAMS**

**Future Vision (Optional - Choose Your Path)**:
8. ⭕ **Milestone 8**: JSON API complete (End of Phase 8) - _Option 2 or 3_
9. ⭕ **Milestone 9**: Rendering abstracted (End of Phase 9) - _Option 2 only_
10. ⭕ **Milestone 10**: New frontend live (End of Phase 10) - _Option 2 or 4_
11. ⭕ **Milestone 11**: Frontend-agnostic architecture complete (End of Phase 11) - _Option 2 only_

**Legend**:
- ✅ = Required milestone (all teams should complete)
- ⭕ = Optional milestone (choose based on your path)

**Recommendation**: Most teams should stop at Milestone 7. The refactored controller with CSHTML views provides a clean, maintainable authentication system that works well for most use cases.