# AuthController to Experimental Folder - HTML Migration Analysis

## Overview
This document compares EVERY HTML rendering method in AuthController with the experimental folder structure to identify what needs to be migrated.

## Render Methods in AuthController

### ✅ 1. RenderLoginForm (Line 1040)
**Status:** PARTIALLY MIGRATED
**Location:** `Experimental/Views/Auth/Login.cshtml`

**CRITICAL DIFFERENCES:**
- ❌ **MISSING:** Complete JavaScript for `submitLoginForm()` function
- ❌ **MISSING:** Auto-login overlay with token validation logic
- ❌ **MISSING:** Complete SVG paths for WebAuthn and QR login icons
- ❌ **MISSING:** Footer links (Create account, Magic Link, Claim Account)
- ❌ **MISSING:** "BRU ID — ключ от всех сервисов" tagline
- ❌ **MISSING:** Social login buttons (phone, Google)
- ✅ **PRESENT:** Basic form structure
- ✅ **PRESENT:** Error/message display
- ⚠️ **SIMPLIFIED:** Secondary options use `data-href` instead of `onclick`

**ACTION REQUIRED:** Update Login.cshtml to include ALL missing functionality

---

### ❌ 2. RenderTotpSetup (Line 1264)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/Auth/TotpSetup.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- QR code display
- Secret key for manual entry
- Verification form with 6-digit code input
- Complete JavaScript for form submission
- Info box explaining TOTP setup

**ACTION REQUIRED:** Create TotpSetup.cshtml with complete functionality

---

### ❌ 3. RenderWebAuthnRegistration (Line 1297)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/Auth/WebAuthnRegister.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- WebAuthn registration UI
- Complete JavaScript for `registerWebAuthn()` function
- `arrayBufferToBase64()` helper function
- Status messages and loader
- Info box explaining WebAuthn

**ACTION REQUIRED:** Create WebAuthnRegister.cshtml with complete functionality

---

### ❌ 4. RenderMagicLinkForm (Line 1386)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/Auth/MagicLink.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- Email input form
- Info box explaining magic link
- Secondary option to return to login
- Complete form submission handling

**ACTION REQUIRED:** Create MagicLink.cshtml with complete functionality

---

### ❌ 5. RenderQrLogin (Line 1430)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/Auth/QrLogin.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- QR code display
- Status polling JavaScript (`checkLoginStatus()` function)
- Auto-redirect on successful scan
- Secondary option to return to login
- Complete polling logic with error handling

**ACTION REQUIRED:** Create QrLogin.cshtml with complete functionality

---

### ❌ 6. RenderOAuthLoginForm (Line 1506)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/Auth/OAuthLogin.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- OAuth authorization UI with client name and scopes
- Scope icons and descriptions
- Complete form submission to `/connect/authorize/callback`
- Styling for OAuth-specific elements
- Request ID handling

**ACTION REQUIRED:** Create OAuthLogin.cshtml with complete functionality

---

### ❌ 7. RenderSuccessPage (Line 2638)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/Auth/Success.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- Success animation (checkmark SVG with scale-in animation)
- Token storage in localStorage
- Auto-redirect to profile
- Loader animation

**ACTION REQUIRED:** Create Success.cshtml with complete functionality

---

### ❌ 8. RenderErrorPage (Line 2719)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/Auth/Error.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- Error display with icon
- Back to login button
- Error message encoding

**ACTION REQUIRED:** Create Error.cshtml with complete functionality

---

### ✅ 9. RenderProfilePage (Line 5329)
**Status:** MIGRATED
**Location:** `Experimental/Views/Profile/Index.cshtml`

**COMPARISON:**
- ✅ **PRESENT:** Profile card with avatar
- ✅ **PRESENT:** Security section (TOTP, WebAuthn)
- ✅ **PRESENT:** Roles & Permissions section
- ✅ **PRESENT:** Admin section (conditional)
- ✅ **PRESENT:** WebAuthn credential list with delete functionality
- ✅ **PRESENT:** Logout button
- ⚠️ **DIFFERENT:** Uses Razor syntax instead of inline HTML
- ⚠️ **DIFFERENT:** Relies on external CSS classes

**STATUS:** Properly migrated with improved structure

---

### ✅ 10. RenderSidebar (Line 5539)
**Status:** MIGRATED
**Location:** `Experimental/Views/Shared/_Sidebar.cshtml`

**COMPARISON:**
- ✅ **PRESENT:** Navigation items (Home, Security, OAuth Clients, OAuth Scopes, Logout)
- ✅ **PRESENT:** SVG icons for each item
- ✅ **PRESENT:** Active state styling
- ✅ **PRESENT:** Token passing for OAuth links

**STATUS:** Properly migrated

---

### ❌ 11. RenderOidcClientsList (Line 5996)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/OAuth/ClientsList.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- Clients grid layout
- Client cards with hover effects
- Empty state message
- "New Client" button
- Complete styling for client cards

**ACTION REQUIRED:** Create ClientsList.cshtml with complete functionality

---

### ❌ 12. RenderOidcScopesList (Line 6171)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/OAuth/ScopesList.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- Scopes list layout
- Scope cards with descriptions
- Empty state message
- Complete styling

**ACTION REQUIRED:** Create ScopesList.cshtml with complete functionality

---

### ❌ 13. RenderOidcClientDetails (Line 6288)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/OAuth/ClientDetails.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- Client details display
- Redirect URIs list
- Post-logout redirect URIs list
- Allowed scopes list
- Edit and Delete buttons
- Complete styling

**ACTION REQUIRED:** Create ClientDetails.cshtml with complete functionality

---

### ❌ 14. RenderOidcClientForm (Line 6559)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/OAuth/ClientForm.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- Client create/edit form
- Textarea inputs for URIs and scopes
- Consent checkbox
- Form validation
- Complete styling

**ACTION REQUIRED:** Create ClientForm.cshtml with complete functionality

---

### ✅ 15. RenderRegisterForm (Line 7383)
**Status:** MIGRATED
**Location:** `Experimental/Views/Auth/Register.cshtml`

**COMPARISON:**
- ✅ **PRESENT:** Admin check status
- ✅ **PRESENT:** Registration form with all fields
- ✅ **PRESENT:** Role dropdown
- ⚠️ **MISSING:** Complete JavaScript for admin token validation
- ⚠️ **MISSING:** Auto-retry logic (3 attempts)

**ACTION REQUIRED:** Update Register.cshtml to include complete admin validation logic

---

### ❌ 16. RenderClaimAccountForm (Line 7747)
**Status:** NOT MIGRATED
**Expected Location:** `Experimental/Views/Auth/ClaimAccount.cshtml` (DOES NOT EXIST)

**MISSING CONTENT:**
- Account claim form
- Username and password inputs
- "Generate New Identity" checkbox
- Info box explaining account claiming
- Complete form submission handling

**ACTION REQUIRED:** Create ClaimAccount.cshtml with complete functionality

---

## Summary Statistics

- **Total Render Methods:** 16
- **Fully Migrated:** 3 (18.75%)
- **Partially Migrated:** 2 (12.5%)
- **Not Migrated:** 11 (68.75%)

## Critical Missing Files

1. `Experimental/Views/Auth/TotpSetup.cshtml`
2. `Experimental/Views/Auth/WebAuthnRegister.cshtml`
3. `Experimental/Views/Auth/MagicLink.cshtml`
4. `Experimental/Views/Auth/QrLogin.cshtml`
5. `Experimental/Views/Auth/OAuthLogin.cshtml`
6. `Experimental/Views/Auth/Success.cshtml`
7. `Experimental/Views/Auth/Error.cshtml`
8. `Experimental/Views/Auth/ClaimAccount.cshtml`
9. `Experimental/Views/OAuth/ClientsList.cshtml`
10. `Experimental/Views/OAuth/ScopesList.cshtml`
11. `Experimental/Views/OAuth/ClientDetails.cshtml`
12. `Experimental/Views/OAuth/ClientForm.cshtml`

## JavaScript Files Needed

Based on the inline JavaScript in AuthController, these external JS files are needed:

1. `wwwroot/js/auth/login.js` - Login form submission and auto-login
2. `wwwroot/js/auth/register.js` - Registration with admin validation
3. `wwwroot/js/auth/totp-setup.js` - TOTP verification
4. `wwwroot/js/auth/webauthn-register.js` - WebAuthn registration
5. `wwwroot/js/auth/qr-login.js` - QR code polling
6. `wwwroot/js/theme-toggle.js` - Theme switching (already referenced)

## Next Steps

1. Create all missing .cshtml files
2. Extract all inline JavaScript to external files
3. Update partially migrated files (Login.cshtml, Register.cshtml)
4. Create corresponding ViewModels for each view
5. Update IHtmlRenderingService interface to match all render methods
6. Implement HtmlRenderingService to use Razor views instead of string formatting
7. Test each view individually to ensure no functionality is lost

