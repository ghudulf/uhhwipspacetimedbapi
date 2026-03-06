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
  - [x] 6.1 Implement AuthorizeOAuthAsync orchestration method
    - Coordinate OpenIdConnectService authorization flow
    - Handle client validation and scope checking
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.2 Implement ExchangeTokenAsync orchestration method
    - Coordinate OpenIdConnectService token exchange
    - Validate authorization code and client credentials
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.3 Implement GetUserInfoAsync orchestration method
    - Coordinate OpenIdConnectService.CreateIdentityFromUserAsync
    - Return user claims for OAuth userinfo endpoint
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.4 Implement RegisterOAuthClientAsync orchestration method
    - Coordinate OpenIdConnectService.RegisterClientApplicationAsync
    - Handle OAuth client registration
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.5 Implement UpdateOAuthClientAsync orchestration method
    - Coordinate OpenIdConnectService.UpdateClientApplicationAsync
    - Handle OAuth client updates
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.6 Implement DeleteOAuthClientAsync orchestration method
    - Coordinate OpenIdConnectService.DeleteClientApplicationAsync
    - Handle OAuth client deletion
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.7 Implement GetOAuthClientsAsync orchestration method
    - Coordinate OpenIdConnectService.GetAllClientApplicationsAsync
    - Return list of OAuth clients
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [x] 6.8 Implement GetOAuthScopesAsync orchestration method
    - Coordinate OpenIdConnectService.GetScopeManager
    - Return available OAuth scopes
    - _Requirements: 1.1, 1.2, 1.3, 14.1, 14.2_
  
  - [ ]* 6.9 Write unit tests for Priority 3 orchestration methods
    - Test AuthorizeOAuthAsync validates client and scopes
    - Test ExchangeTokenAsync validates code and credentials
    - Test GetUserInfoAsync returns correct user claims
    - Test RegisterOAuthClientAsync creates client
    - Test UpdateOAuthClientAsync updates client
    - Test DeleteOAuthClientAsync removes client
    - Test GetOAuthClientsAsync returns all clients
    - Test GetOAuthScopesAsync returns available scopes
    - _Requirements: 5.1, 5.2, 5.3_


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


### Phase 3: Feature Flag Integration (Week 8) - Zero Risk

- [ ] 9. Create feature flag infrastructure
  - [ ] 9.1 Create FeatureFlagOptions configuration class
    - Define boolean flags for all 56 endpoints (default: false)
    - Include flags for Login, Register, TOTP, WebAuthn, OAuth, etc.
    - Location: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Options/FeatureFlagOptions.cs`
    - _Requirements: 6.1, 6.2, 6.3, 15.1, 15.2, 15.3_
  
  - [ ] 9.2 Configure feature flags in appsettings.json
    - Add FeatureFlags section with all flags disabled by default
    - _Requirements: 6.1, 6.2, 6.3_
  
  - [ ] 9.3 Register FeatureFlagOptions in DI container
    - Add configuration binding in Program.cs
    - _Requirements: 6.1, 6.2, 6.3_
  
  - [ ]* 9.4 Write unit tests for feature flag configuration
    - Test flags load correctly from configuration
    - Test default values are false
    - Test flags can be toggled at runtime
    - _Requirements: 5.1, 5.2, 5.3_

- [ ] 10. Checkpoint - Verify feature flag infrastructure
  - Ensure all tests pass, ask the user if questions arise.


### Phase 4: Controller Modification (Weeks 9-10) - Low Risk

- [ ] 11. Modify AuthController to support feature flags
  - [ ] 11.1 Inject FeatureFlagOptions and AuthOrchestrationService into AuthController
    - Add constructor parameters for IOptions<FeatureFlagOptions> and IAuthOrchestrationService
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 15.4_
  
  - [ ] 11.2 Add feature flag checks to Login endpoint
    - Check EnableLoginRefactoring flag at start of method
    - If enabled: delegate to AuthOrchestrationService.LoginAsync
    - If disabled: execute existing legacy code (unchanged)
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_
  
  - [ ] 11.3 Add feature flag checks to Register endpoint
    - Check EnableRegisterRefactoring flag at start of method
    - If enabled: delegate to AuthOrchestrationService.RegisterAsync
    - If disabled: execute existing legacy code (unchanged)
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_
  
  - [ ] 11.4 Add feature flag checks to TOTP endpoints (4 endpoints)
    - Add flags for TotpSetup, TotpValidate, TotpEnable, TotpDisable
    - Delegate to orchestration service when flags enabled
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_
  
  - [ ] 11.5 Add feature flag checks to WebAuthn endpoints (7 endpoints)
    - Add flags for WebAuthn register, validate, credentials operations
    - Delegate to orchestration service when flags enabled
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_
  
  - [ ] 11.6 Add feature flag checks to OAuth endpoints (8+ endpoints)
    - Add flags for OAuth authorize, token, userinfo, client management
    - Delegate to orchestration service when flags enabled
    - _Requirements: 1.1, 1.2, 6.1, 6.2, 6.3, 13.1, 13.2, 13.3, 13.4, 15.4_
  
  - [ ] 11.7 Add feature flag checks to remaining endpoints (QR, Magic Link, Profile, etc.)
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

- [ ] 13. Checkpoint - Verify controller modification
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

- **Phase 7: Legacy Code Removal** (Weeks 20+, after full validation)
  - Remove feature flag checks from AuthController
  - Remove legacy code paths
  - Remove duplicated helper methods
  - Clean up and optimize
  - Reduce AuthController from 8,293 lines to ~2,000 lines

## Success Criteria

**Technical Success Criteria**:
- 100% of endpoints follow Controller → Orchestration → Services → Database pattern
- Zero direct database access in AuthController
- 100% orchestration layer coverage (50 methods)
- AuthController reduced from 8,293 lines to ~2,000 lines (after Phase 7)

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

- [ ] 17. Enable feature flags incrementally for Priority 1 endpoints (Critical)
  - [ ] 17.1 Enable Login endpoint for 1% of users
    - Monitor error rates, response times, user feedback for 24 hours
    - If stable, increase to 10% for 24 hours
    - If stable, increase to 50% for 48 hours
    - If stable, enable for 100% of users
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [ ] 17.2 Enable Register endpoint for 1% of users
    - Follow same gradual rollout pattern as Login
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [ ] 17.3 Enable TOTP validate endpoint for 1% of users
    - Follow same gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [ ] 17.4 Enable WebAuthn validate endpoint for 1% of users
    - Follow same gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [ ] 17.5 Enable Profile endpoint for 1% of users
    - Follow same gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_

- [ ] 18. Enable feature flags for Priority 2 endpoints (High)
  - [ ] 18.1 Enable TOTP setup/enable/disable endpoints
    - Follow gradual rollout pattern for each endpoint
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [ ] 18.2 Enable WebAuthn register/credentials endpoints
    - Follow gradual rollout pattern for each endpoint
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_

- [ ] 19. Enable feature flags for Priority 3 endpoints (Medium - OAuth)
  - [ ] 19.1 Enable OAuth authorize endpoint
    - Follow gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [ ] 19.2 Enable OAuth token endpoint
    - Follow gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [ ] 19.3 Enable OAuth userinfo endpoint
    - Follow gradual rollout pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [ ] 19.4 Enable OAuth client management endpoints
    - Follow gradual rollout pattern for each endpoint
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_

- [ ] 20. Enable feature flags for Priority 4 endpoints (Low - Already Clean)
  - [ ] 20.1 Enable QR authentication endpoints
    - Follow gradual rollout pattern for each endpoint
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [ ] 20.2 Enable Magic Link endpoints
    - Follow gradual rollout pattern for each endpoint
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 12.1, 12.2, 12.4_
  
  - [ ] 20.3 Enable remaining user management endpoints
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


### Phase 7: Legacy Code Removal (Weeks 20+) - Zero Risk

**Note**: These tasks are performed AFTER all endpoints have been at 100% rollout for several weeks/months with stable operation.

- [ ] 23. Remove feature flag infrastructure
  - [ ] 23.1 Remove feature flag checks from AuthController
    - Remove all `if (_featureFlags.Value.Enable*Refactoring)` checks
    - Keep only the new code paths (orchestration service calls)
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_
  
  - [ ] 23.2 Remove FeatureFlagOptions class
    - Delete Options/FeatureFlagOptions.cs
    - Remove configuration from appsettings.json
    - Remove DI registration from Program.cs
    - _Requirements: 1.5_
  
  - [ ] 23.3 Update tests to remove feature flag logic
    - Remove feature flag setup from integration tests
    - Simplify test code to only test new code paths
    - _Requirements: 5.5_

- [ ] 24. Remove legacy code paths from AuthController
  - [ ] 24.1 Remove legacy Login implementation
    - Delete all legacy code from Login endpoint
    - Keep only orchestration service call
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_
  
  - [ ] 24.2 Remove legacy Register implementation
    - Delete all legacy code from Register endpoint
    - Keep only orchestration service call
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_
  
  - [ ] 24.3 Remove legacy TOTP implementations
    - Delete legacy code from all 4 TOTP endpoints
    - Keep only orchestration service calls
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_
  
  - [ ] 24.4 Remove legacy WebAuthn implementations
    - Delete legacy code from all 7 WebAuthn endpoints
    - Keep only orchestration service calls
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_
  
  - [ ] 24.5 Remove legacy OAuth implementations
    - Delete legacy code from all OAuth endpoints
    - Keep only orchestration service calls
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_
  
  - [ ] 24.6 Remove legacy implementations from remaining endpoints
    - Delete legacy code from QR, Magic Link, Profile, User management endpoints
    - Keep only orchestration service calls
    - _Requirements: 1.5, 13.1, 13.2, 13.3, 13.4, 13.5_

- [ ] 25. Remove duplicated helper methods from AuthController
  - [ ] 25.1 Remove IsAdmin method from AuthController
    - Already exists in AuthOrchestrationService
    - Update any remaining references to use orchestration service
    - _Requirements: 3.1, 3.2, 3.5_
  
  - [ ] 25.2 Remove HasPermission method from AuthController
    - Already exists in AuthOrchestrationService
    - Update any remaining references to use orchestration service
    - _Requirements: 3.1, 3.3, 3.5_
  
  - [ ] 25.3 Remove GenerateJwtToken method from AuthController
    - Already exists in TokenService
    - Update any remaining references to use TokenService
    - _Requirements: 3.1, 3.5_
  
  - [ ] 25.4 Remove GetUserIdentity method from AuthController
    - Already exists in IdentityService
    - Update any remaining references to use IdentityService
    - _Requirements: 3.1, 3.4, 3.5_
  
  - [ ] 25.5 Remove IsBrowserRequest method from AuthController
    - Already exists in RequestDetector service
    - Update any remaining references to use RequestDetector
    - _Requirements: 3.1, 3.5_
  
  - [ ] 25.6 Remove GenerateRandomToken method from AuthController
    - Already exists in TokenService
    - Update any remaining references to use TokenService
    - _Requirements: 3.1, 3.5_
  
  - [ ] 25.7 Remove manual JWT validation code from OAuth admin endpoints
    - Use centralized validation from TokenService
    - _Requirements: 3.1, 3.5_

- [ ] 26. Verify AuthController line count reduction
  - [ ] 26.1 Measure final AuthController line count
    - Target: ~2,000 lines (down from 8,293 lines)
    - 75% reduction achieved
    - _Requirements: 1.5_
  
  - [ ] 26.2 Verify all endpoints follow clean architecture
    - All endpoints delegate to orchestration service
    - Zero direct database access
    - Zero duplicated helper methods
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

- [ ] 27. Final testing and validation
  - [ ] 27.1 Run full test suite
    - All unit tests passing
    - All integration tests passing
    - All performance tests passing
    - _Requirements: 5.5_
  
  - [ ] 27.2 Perform security audit
    - Verify all authentication logic centralized
    - Verify all authorization logic centralized
    - Verify audit logging implemented
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_
  
  - [ ] 27.3 Update documentation
    - Update ARCHITECTURE.md to reflect final state
    - Update API documentation
    - Remove migration-related documentation
    - _Requirements: 11.1, 11.2, 11.4_

- [ ] 28. Deploy legacy code removal to production
  - [ ] 28.1 Deploy to staging environment
    - Run full test suite in staging
    - Perform manual testing
    - _Requirements: 12.1, 12.2_
  
  - [ ] 28.2 Deploy to production
    - Monitor for 24 hours
    - Verify all endpoints functioning correctly
    - Verify performance metrics unchanged
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

- [ ] 29. Final checkpoint - Refactoring complete
  - AuthController reduced from 8,293 lines to ~2,000 lines
  - 100% clean architecture across all 56 endpoints
  - Zero direct database access
  - Zero code duplication
  - All success criteria met


## Future Vision: Frontend-Agnostic Authentication Service (6-12 Months Post-Refactoring)

**Note**: These tasks represent the long-term evolution beyond the current refactoring. They transform the auth service into a pure JSON API backend that can serve any frontend (Next.js, React, Vue, mobile apps, etc.).

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

- **Phase 7**: Legacy Code Removal (Weeks 20+) - Zero Risk
  - Remove feature flags and legacy code
  - Reduce AuthController from 8,293 lines to ~2,000 lines
  - Duration: 2-3 weeks
  - **Prerequisite**: All endpoints at 100% rollout for several weeks/months with stable operation

**Total Duration**: 3-4 months from start to legacy code removal

### Future Vision (Phases 8-11)
- **Phase 8**: API-First Endpoints (Months 1-2 Post-Refactoring)
  - Add JSON API support alongside HTML
  - Duration: 2-3 weeks

- **Phase 9**: Extract Rendering (Months 3-4 Post-Refactoring)
  - Create view rendering abstraction
  - Duration: 2-3 weeks

- **Phase 10**: Frontend Decoupling (Months 5-8 Post-Refactoring)
  - Build Next.js frontend
  - Run side-by-side with CSHTML
  - Duration: 8-12 weeks

- **Phase 11**: CSHTML Deprecation (Months 9-12 Post-Refactoring)
  - Remove Razor view engine
  - Pure JSON API backend
  - Duration: 2-3 weeks

**Total Duration**: 12+ months for complete frontend-agnostic architecture

## Risk Assessment by Phase

| Phase | Risk Level | Reason | Rollback Strategy |
|-------|-----------|--------|-------------------|
| 1-3 | **Zero** | AuthController untouched, new code in parallel | N/A - no production impact |
| 4 | **Low** | Controller modified but legacy code path active by default | Disable feature flags |
| 5 | **Zero** | Documentation only | N/A |
| 6 | **Controlled** | Gradual rollout with monitoring | Instant rollback via feature flags |
| 7 | **Zero** | Only after months of stable operation | Revert deployment |
| 8-11 | **Low-Medium** | Gradual migration with dual-mode support | Keep CSHTML as fallback |

## Dependencies Between Phases

- **Phase 2** depends on **Phase 1**: Need TwoFactorService and SettingsService before orchestration
- **Phase 4** depends on **Phases 1-3**: Need services, orchestration, and feature flags before controller modification
- **Phase 6** depends on **Phases 1-5**: Need complete implementation before production rollout
- **Phase 7** depends on **Phase 6**: Need stable production operation before legacy code removal
- **Phases 8-11** depend on **Phase 7**: Need clean architecture before frontend decoupling

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
- **Phase 7**: 2-3 days (legacy code removal)

**Total**: ~1-2 months with minimal active development

### Future Vision (Phases 8-11)
- **Phase 8**: 2-3 weeks (JSON API endpoints)
- **Phase 9**: 2-3 weeks (rendering abstraction)
- **Phase 10**: 8-12 weeks (Next.js frontend)
- **Phase 11**: 2-3 weeks (CSHTML deprecation)

**Total**: ~4-6 months of development work

## Key Milestones

1. ✅ **Milestone 1**: Services created (End of Phase 1)
2. ✅ **Milestone 2**: Orchestration complete (End of Phase 2)
3. ✅ **Milestone 3**: Feature flags ready (End of Phase 3)
4. ✅ **Milestone 4**: Controller modified (End of Phase 4)
5. ✅ **Milestone 5**: Ready for deployment (End of Phase 5)
6. ✅ **Milestone 6**: All endpoints at 100% rollout (End of Phase 6)
7. ✅ **Milestone 7**: Legacy code removed (End of Phase 7)
8. ✅ **Milestone 8**: JSON API complete (End of Phase 8)
9. ✅ **Milestone 9**: Rendering abstracted (End of Phase 9)
10. ✅ **Milestone 10**: Next.js frontend live (End of Phase 10)
11. ✅ **Milestone 11**: Frontend-agnostic architecture complete (End of Phase 11)
