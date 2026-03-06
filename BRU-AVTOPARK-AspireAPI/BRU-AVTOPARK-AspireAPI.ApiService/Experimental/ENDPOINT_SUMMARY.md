# AuthController Endpoint Summary

**Quick Reference Guide**  
**Date**: March 6, 2026  
**Total Endpoints**: 56

---

## Architecture Pattern Legend

- ✅ **CLEAN** - Properly uses service layer, no direct DB access
- 🔥 **MIXED** - Uses both services AND direct DB access
- 🔴 **DIRECT DB** - Only direct DB access, no services
- 📄 **HTML** - Just renders HTML, no business logic

---

## Endpoint Breakdown by Category

### Traditional Authentication (2 endpoints)
- ✅ GET `/api/auth/login` - HTML form
- 🔥 POST `/api/auth/login` - Mixed (calls AuthService but also queries UserSettings, WebAuthnCredential directly)

### Registration (2 endpoints)
- ✅ GET `/api/auth/register` - HTML form
- 🔥 POST `/api/auth/register` - Mixed (manually parses JWT, queries DB for admin check)

### TOTP (4 endpoints)
- ✅ GET `/api/auth/totp/setup` - Clean
- ✅ POST `/api/auth/totp/verify` - Clean
- ✅ POST `/api/auth/totp/disable` - Clean
- 🔥 POST `/api/auth/totp/validate` - Mixed (queries TwoFactorToken, UserProfile, TotpSecret directly)

### WebAuthn (7 endpoints)
- ✅ POST `/api/auth/webauthn/register/options` - Clean
- ✅ POST `/api/auth/webauthn/register/complete` - Clean
- ✅ POST `/api/auth/webauthn/login/options` - Clean
- ✅ POST `/api/auth/webauthn/login/complete` - Clean
- 🔥 POST `/api/auth/webauthn/validate` - Mixed (queries TwoFactorToken directly)
- ✅ GET `/api/auth/webauthn/credentials` - Clean
- ✅ DELETE `/api/auth/webauthn/credentials/{id}` - Clean

### Magic Link (3 endpoints)
- ✅ GET `/api/auth/magic-link` - HTML form
- ✅ POST `/api/auth/magic-link/send` - Clean
- ✅ POST `/api/auth/validate-magic-link` - Clean

### QR Authentication (7 endpoints)
- ✅ GET `/api/auth/qr/login` - HTML (calls service for QR generation)
- ✅ GET `/api/auth/qr/generate` - Clean
- ✅ POST `/api/auth/qr/login` - Clean
- ✅ GET `/api/auth/qr/direct/generate` - Clean
- ✅ POST `/api/auth/qr/direct/login` - Clean
- ✅ GET `/api/auth/qr/direct/check` - Clean
- ✅ POST `/api/auth/qr/token/generate` - Clean

### OAuth/OIDC Core (7 endpoints)
- 🔥 GET `~/connect/authorize` - Mixed (queries DB, calls OpenIdConnectService)
- 🔥 POST `~/connect/authorize` - Mixed (queries DB, calls OpenIdConnectService)
- 🔥 POST `~/connect/authorize/callback` - Mixed (manually parses JWT, queries cache)
- 🔥 POST `~/connect/token` - Mixed (queries UserProfile, UserRole, Role, RolePermission, Permission directly)
- 🔴 GET `~/connect/userinfo` - Direct DB (queries UserProfile, UserRole, Role directly)
- ✅ GET `~/connect/tokeninfo` - Clean (just extracts claims)
- ✅ GET `~/debug/tokentest` - Clean (debug endpoint)

### OAuth Client Management API (4 endpoints)
- ✅ POST `/api/auth/connect/registerclient` - Clean
- ✅ PUT `/api/auth/connect/update-client/{id}` - Clean
- ✅ DELETE `/api/auth/connect/delete-client/{id}` - Clean
- ✅ GET `/api/auth/connect/client/{id}` - Clean

### OAuth Admin HTML Pages (9 endpoints)
- 🔥 GET `/api/auth/connect/clients` - Mixed (manually validates JWT, then calls service)
- 🔥 GET `/api/auth/connect/clients/{id}` - Mixed (manually validates JWT, then calls service)
- 🔥 GET `/api/auth/connect/clients/new` - Mixed (manually validates JWT, renders form)
- 🔥 GET `/api/auth/connect/clients/{id}/edit` - Mixed (manually validates JWT, then calls service)
- 🔥 POST `/api/auth/connect/register-client` - Mixed (manually validates JWT, then calls service)
- 🔥 POST `/api/auth/connect/update-client/{id}` - Mixed (manually validates JWT, then calls service)
- 🔥 POST `/api/auth/connect/clients/{id}/delete` - Mixed (manually validates JWT, then calls service)
- 🔥 GET `/api/auth/connect/scopes` - Mixed (manually validates JWT, then calls service)
- 🔥 GET `/api/auth/oauth/login` - Mixed (queries cache for OAuth params)

### Profile & Utility (8 endpoints)
- 🔥 GET `/api/auth/profile` - Mixed (manually validates JWT, queries DB, calls ProfileService)
- ✅ GET `/api/auth/logout` - HTML
- ✅ GET `/api/auth/success` - HTML
- ✅ GET `/api/auth/error` - HTML
- ✅ GET `/api/auth/claim-account` - HTML form
- ✅ POST `/api/auth/claim-account` - Clean
- ✅ POST `/api/auth/webauthn/credentials/{id}` (form DELETE) - Clean

---

## Statistics

**By Pattern**:
- ✅ Clean: 28 endpoints (50%)
- 🔥 Mixed: 18 endpoints (32%)
- 🔴 Direct DB: 1 endpoint (2%)
- 📄 HTML Only: 9 endpoints (16%)

**By Category**:
- Traditional Auth: 50% clean (1/2)
- Registration: 50% clean (1/2)
- TOTP: 75% clean (3/4)
- WebAuthn: 86% clean (6/7)
- Magic Link: 100% clean (2/2 API endpoints)
- QR Auth: 100% clean (6/6 API endpoints)
- OAuth Core: 29% clean (2/7) - **Most recent addition, implemented under SpacetimeDB integration challenges**
- OAuth Client API: 100% clean (4/4)
- OAuth Admin HTML: 0% clean (0/9) - **Manual JWT validation everywhere**
- Profile/Utility: 75% clean (6/8)

---

## Key Insights

### What's Clean
- **Newer features** (TOTP, WebAuthn, Magic Link, QR) are clean because they were built with service layer from the start
- **API endpoints** for OAuth client management are clean
- **HTML-only pages** have no business logic (good)

### What's Messy
- **Older features** (Login, Register) predate service layer architecture
- **OAuth core flow** (authorize, token, userinfo) has direct DB access
- **OAuth admin HTML pages** all manually validate JWT tokens
- **2FA validation** (TOTP, WebAuthn) queries TwoFactorToken directly

### Root Causes
1. **Technical debt** - Old code (Login, Register) not refactored when service layer was added
2. **Implementation under pressure** - OAuth was most recent addition, had to work despite SpacetimeDB integration challenges
3. **Inconsistent patterns** - No enforcement of architecture rules
4. **Missing services** - TwoFactorService, SettingsService don't exist yet
5. **Duplicated logic** - IsAdmin, HasPermission, JWT parsing repeated everywhere

---

## Priority Fixes

### Critical (Week 1)
1. Create `TwoFactorService` - Used by Login, TOTP validate, WebAuthn validate
2. Create `SettingsService` - Used by Login, Profile

### High (Week 2-3)
3. Refactor Login endpoint - Most used, most complex
4. Refactor Register endpoint - Security critical
5. Refactor OAuth token endpoint - Core OAuth functionality

### Medium (Week 4-6)
6. Refactor OAuth admin HTML pages - Remove manual JWT validation
7. Refactor TOTP/WebAuthn validate - Use TwoFactorService
8. Refactor Profile page - Remove manual JWT validation

### Low (Week 7-8)
9. Remove duplicated helper methods from controller
10. Add feature flagging to orchestration service

---

## Next Steps

1. ✅ **DONE**: Complete endpoint analysis (this document)
2. **TODO**: Review with team
3. **TODO**: Prioritize Phase 1 services (TwoFactorService, SettingsService)
4. **TODO**: Create service interfaces and implementations
5. **TODO**: Expand AuthOrchestrationService with new methods
6. **TODO**: Refactor endpoints one by one
7. **TODO**: Add feature flagging
8. **TODO**: Create new AuthControllerV2 in Experimental folder

---

**See Also**:
- `DETAILED_ENDPOINT_ANALYSIS.md` - Complete analysis with code examples
- `ARCHITECTURE_MESS_ANALYSIS.md` - Architecture pattern analysis
- `SERVICES_VERIFICATION.md` - Business logic services inventory
- `MODELS_COMPARISON.md` - Request/response models inventory
