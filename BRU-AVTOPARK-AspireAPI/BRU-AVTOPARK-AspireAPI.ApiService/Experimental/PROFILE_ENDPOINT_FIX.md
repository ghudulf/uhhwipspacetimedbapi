# Profile Endpoint Fix - Complete Report

**Date**: March 11, 2026  
**Issue**: Refactored profile endpoint not rendering HTML views  
**Status**: ✅ FIXED

---

## Problem Analysis

### Original Issue
The refactored `AuthControllerRefactored.Profile()` endpoint was returning JSON instead of HTML, breaking browser-based profile viewing.

### Root Causes
1. **Missing browser detection**: Endpoint didn't check if request was from browser vs API client
2. **No HTML rendering path**: Only returned JSON via `GetProfileAsync()`
3. **Missing SpacetimeDB data retrieval**: HtmlRenderingService needs raw SpacetimeDB types, not ViewModels
4. **Legacy behavior not preserved**: Legacy controller supported both HTML (browser) and JSON (API)

---

## Solution Implementation

### 1. Added ProfileRenderData Class

**Location**: `AuthOrchestrationService.cs` (end of file)

```csharp
/// <summary>
/// Result containing raw SpacetimeDB types for HTML rendering.
/// Used by profile endpoint to pass data to HtmlRenderingService.
/// </summary>
public class ProfileRenderData
{
    public required UserProfile User { get; init; }
    public required bool TotpEnabled { get; init; }
    public required List<WebAuthnCredentialDto> WebAuthnCredentials { get; init; }
    public required List<Role> Roles { get; init; }
    public required List<Permission> Permissions { get; init; }
}
```

**Purpose**: Holds raw SpacetimeDB types needed by `HtmlRenderingService.RenderProfilePage()`

---

### 2. Added GetProfileWithSpacetimeDataAsync Method

**Location**: `AuthOrchestrationService.cs`

**Signature**:
```csharp
public async Task<ProfileRenderData?> GetProfileWithSpacetimeDataAsync(string token)
```

**Functionality**:
- Validates JWT token
- Extracts userId from token claims
- Queries SpacetimeDB for:
  - User profile
  - User settings (TOTP enabled)
  - WebAuthn credentials
  - User roles
  - User permissions
- Returns raw SpacetimeDB types wrapped in ProfileRenderData

**Key Differences from GetProfileAsync**:
- `GetProfileAsync`: Returns `ProfileViewModel` (for JSON API responses)
- `GetProfileWithSpacetimeDataAsync`: Returns `ProfileRenderData` (for HTML rendering)

---

### 3. Updated Profile Endpoint

**Location**: `AuthControllerRefactored.cs`

**New Behavior**:

#### For Browser Requests (Accept: text/html)
1. Check for token in Authorization header
2. Check for token in query string (?token=)
3. If no token, return JavaScript to check localStorage
4. Validate token format
5. Call `GetProfileWithSpacetimeDataAsync(token)`
6. Render HTML using `HtmlRenderingService.RenderProfilePage()`
7. Return HTML content

#### For API Requests (Accept: application/json)
1. Check for token in Authorization header
2. Check for token in query string (?token=)
3. If no token, return 401 Unauthorized JSON
4. Validate token format
5. Call `GetProfileAsync(token)`
6. Return JSON with ProfileViewModel

---

### 4. Updated Interface

**Location**: `IAuthServices.cs`

**Added**:
```csharp
/// <summary>
/// Get user profile with raw SpacetimeDB types for HTML rendering.
/// Returns data in the format expected by HtmlRenderingService.RenderProfilePage.
/// </summary>
Task<ProfileRenderData?> GetProfileWithSpacetimeDataAsync(string token);
```

**Added using statement**:
```csharp
using BRU_AVTOPARK.Services.Implementations;
```

---

## Behavioral Comparison

### Legacy Controller (AuthController.ProfilePage)

| Request Type | Token Source | Response |
|--------------|--------------|----------|
| Browser | None | JavaScript redirect to check localStorage |
| Browser | Query/Header | HTML profile page |
| Browser | Invalid token | JavaScript redirect to login |
| API | None | JSON error |
| API | Query/Header | JSON error (browser-only message) ❌ |

**Issue**: Legacy controller rejected API requests with "Please use a browser" message

---

### Refactored Controller (AuthControllerRefactored.Profile)

| Request Type | Token Source | Response |
|--------------|--------------|----------|
| Browser | None | JavaScript redirect to check localStorage |
| Browser | Query/Header | HTML profile page |
| Browser | Invalid token | JavaScript redirect to login |
| API | None | 401 Unauthorized JSON |
| API | Query/Header | JSON with ProfileViewModel ✅ |
| API | Invalid token | 404 Not Found JSON |

**Improvement**: Refactored controller supports BOTH browser and API requests

---

## Code Flow Diagrams

### Browser Request Flow

```
Browser Request (Accept: text/html)
    ↓
AuthControllerRefactored.Profile()
    ↓
_requestDetector.IsBrowserRequest() → true
    ↓
Check token (header → query → localStorage)
    ↓
_authOrchestrationService.GetProfileWithSpacetimeDataAsync(token)
    ↓
    ├─ _tokenService.ValidateToken(token)
    ├─ Query SpacetimeDB for user data
    ├─ Query SpacetimeDB for settings
    ├─ Query SpacetimeDB for WebAuthn credentials
    ├─ Query SpacetimeDB for roles
    └─ Query SpacetimeDB for permissions
    ↓
Return ProfileRenderData
    ↓
_htmlRenderingService.RenderProfilePage(...)
    ↓
Return HTML Content
```

### API Request Flow

```
API Request (Accept: application/json)
    ↓
AuthControllerRefactored.Profile()
    ↓
_requestDetector.IsBrowserRequest() → false
    ↓
Check token (header → query)
    ↓
_authOrchestrationService.GetProfileAsync(token)
    ↓
    ├─ _tokenService.ValidateToken(token)
    └─ _profileService.GetProfileAsync(userId, token)
        ↓
        ├─ Query SpacetimeDB for user data
        ├─ Query SpacetimeDB for settings
        ├─ Query SpacetimeDB for WebAuthn credentials
        ├─ Query SpacetimeDB for roles
        └─ Query SpacetimeDB for permissions
        ↓
        Return ProfileViewModel
    ↓
Return JSON ApiResponse<ProfileViewModel>
```

---

## Service Architecture

### Before Fix

```
AuthControllerRefactored
    ↓
_authOrchestrationService.GetProfileAsync(token)
    ↓
Returns ProfileViewModel (JSON only)
    ↓
❌ No HTML rendering path
```

### After Fix

```
AuthControllerRefactored
    ├─ Browser Request
    │   ↓
    │   _authOrchestrationService.GetProfileWithSpacetimeDataAsync(token)
    │   ↓
    │   Returns ProfileRenderData
    │   ↓
    │   _htmlRenderingService.RenderProfilePage(...)
    │   ↓
    │   ✅ HTML Content
    │
    └─ API Request
        ↓
        _authOrchestrationService.GetProfileAsync(token)
        ↓
        Returns ProfileViewModel
        ↓
        ✅ JSON Response
```

---

## Testing Checklist

### Browser Tests
- [ ] Navigate to `/api/auth/profile` without token → redirects to login
- [ ] Navigate to `/api/auth/profile?token=<valid>` → shows HTML profile
- [ ] Navigate to `/api/auth/profile` with localStorage token → shows HTML profile
- [ ] Navigate to `/api/auth/profile?token=<invalid>` → redirects to login
- [ ] Navigate to `/api/auth/profile?token=<expired>` → redirects to login
- [ ] Verify profile shows:
  - [ ] User login/email/phone
  - [ ] TOTP status
  - [ ] WebAuthn credentials
  - [ ] Roles
  - [ ] Permissions
  - [ ] Admin links (if admin)

### API Tests
- [ ] `GET /api/auth/profile` without token → 401 Unauthorized
- [ ] `GET /api/auth/profile` with Bearer token → 200 OK with ProfileViewModel
- [ ] `GET /api/auth/profile?token=<valid>` → 200 OK with ProfileViewModel
- [ ] `GET /api/auth/profile?token=<invalid>` → 404 Not Found
- [ ] `GET /api/auth/profile?token=<expired>` → 404 Not Found
- [ ] Verify JSON response contains:
  - [ ] User data
  - [ ] TotpEnabled flag
  - [ ] WebAuthnEnabled flag
  - [ ] WebAuthnCredentials array
  - [ ] Roles array
  - [ ] Permissions array

---

## Files Modified

1. **AuthControllerRefactored.cs**
   - Updated `Profile()` method to support both browser and API requests
   - Added browser detection logic
   - Added dual response paths (HTML vs JSON)

2. **AuthOrchestrationService.cs**
   - Added `ProfileRenderData` class
   - Added `GetProfileWithSpacetimeDataAsync(string token)` method

3. **IAuthServices.cs**
   - Added `GetProfileWithSpacetimeDataAsync(string token)` interface method
   - Added `using BRU_AVTOPARK.Services.Implementations;`

---

## Compilation Status

✅ **All files compile without errors**

```
AuthControllerRefactored.cs: No diagnostics found
AuthOrchestrationService.cs: No diagnostics found
IAuthServices.cs: No diagnostics found
```

---

## Key Improvements Over Legacy

1. **Dual Response Support**: Supports both HTML (browser) and JSON (API) responses
2. **Clean Separation**: HTML rendering logic in HtmlRenderingService, not controller
3. **Testable**: Business logic in orchestration service, not controller
4. **Type Safety**: Separate types for HTML rendering (ProfileRenderData) vs API (ProfileViewModel)
5. **Better Error Handling**: Different error responses for browser vs API
6. **Consistent Architecture**: Follows orchestration pattern like other endpoints

---

## Migration Notes

### Feature Flag
- Endpoint enabled by: `EnableProfileRefactoring` feature flag
- Legacy endpoint disabled when flag is true
- Both endpoints use same route: `GET /api/auth/profile`

### Backward Compatibility
- ✅ Browser behavior: Identical to legacy (HTML rendering)
- ✅ Token sources: Header, query string, localStorage
- ✅ Error handling: Redirects for browser, JSON for API
- ✅ Data displayed: Same profile information
- ✅ **IMPROVED**: API clients can now get JSON responses (legacy rejected them)

---

## Conclusion

The profile endpoint has been successfully refactored to:
1. ✅ Render HTML views for browser requests
2. ✅ Return JSON for API requests
3. ✅ Maintain backward compatibility with legacy behavior
4. ✅ Follow orchestration service pattern
5. ✅ Improve upon legacy by supporting API clients

**Status**: Ready for testing and deployment
