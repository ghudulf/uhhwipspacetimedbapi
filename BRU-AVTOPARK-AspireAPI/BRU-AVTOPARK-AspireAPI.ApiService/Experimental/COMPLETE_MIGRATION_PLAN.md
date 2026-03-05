# Complete AuthController to Experimental Folder Migration Plan

## Executive Summary

The experimental folder modularization effort has **barely started**. Only 18.75% of HTML views are fully migrated, with 68.75% completely missing. This document provides a complete plan to finish the migration.

## CSS Migration Status

✅ **COMPLETED**: Extracted all CSS from `BaseHtmlTemplate` to `wwwroot/css/bru-design-system.css`

The CSS file now contains:
- All CSS custom properties (CSS variables)
- Dark mode support
- Responsive design breakpoints
- All component styles (cards, buttons, forms, etc.)
- Yandex ID-inspired design system classes
- Animation keyframes

## Required File Structure

```
Experimental/
├── Views/
│   ├── Auth/
│   │   ├── Login.cshtml ✅ (NEEDS UPDATE)
│   │   ├── Register.cshtml ✅ (NEEDS UPDATE)
│   │   ├── TotpSetup.cshtml ❌ MISSING
│   │   ├── WebAuthnRegister.cshtml ❌ MISSING
│   │   ├── MagicLink.cshtml ❌ MISSING
│   │   ├── QrLogin.cshtml ❌ MISSING
│   │   ├── OAuthLogin.cshtml ❌ MISSING
│   │   ├── Success.cshtml ❌ MISSING
│   │   ├── Error.cshtml ❌ MISSING
│   │   └── ClaimAccount.cshtml ❌ MISSING
│   ├── OAuth/
│   │   ├── ClientsList.cshtml ❌ MISSING
│   │   ├── ScopesList.cshtml ❌ MISSING
│   │   ├── ClientDetails.cshtml ❌ MISSING
│   │   └── ClientForm.cshtml ❌ MISSING
│   ├── Profile/
│   │   └── Index.cshtml ✅ COMPLETE
│   └── Shared/
│       ├── _AuthLayout.cshtml ✅ COMPLETE
│       ├── _AdminLayout.cshtml ✅ COMPLETE
│       ├── _Sidebar.cshtml ✅ COMPLETE
│       ├── _BruIdHeader.cshtml ✅ COMPLETE
│       ├── _FormField.cshtml ✅ COMPLETE
│       ├── _StatusMessages.cshtml ✅ COMPLETE
│       ├── _AuthFooterLinks.cshtml ❌ MISSING
│       └── _SectionWrapper.cshtml ✅ COMPLETE
├── Models/
│   ├── Requests/
│   │   └── AuthRequests.cs ✅ COMPLETE
│   ├── Responses/
│   │   └── AuthResponses.cs ✅ COMPLETE
│   └── ViewModels/
│       └── AuthViewModels.cs ✅ COMPLETE
└── Services/
    ├── Interfaces/
    │   ├── IAuthServices.cs ✅ COMPLETE
    │   ├── ITokenService.cs ✅ COMPLETE
    │   ├── IIdentityService.cs ✅ COMPLETE
    │   ├── IHtmlRenderingService.cs ✅ COMPLETE
    │   └── IOidcHelperService.cs ✅ COMPLETE
    └── Implementations/
        ├── TokenService.cs ✅ COMPLETE
        ├── AuthOrchestrationService.cs ✅ COMPLETE
        ├── IdentityService.cs ✅ COMPLETE
        ├── ProfileService.cs ✅ COMPLETE
        ├── RequestDetector.cs ✅ COMPLETE
        ├── OidcHelperService.cs ✅ COMPLETE
        └── HtmlRenderingService.cs ❌ MISSING

wwwroot/
├── css/
│   └── bru-design-system.css ✅ COMPLETE
└── js/
    ├── theme-toggle.js ❌ MISSING
    └── auth/
        ├── login.js ❌ MISSING
        ├── register.js ❌ MISSING
        ├── totp-setup.js ❌ MISSING
        ├── webauthn-register.js ❌ MISSING
        ├── qr-login.js ❌ MISSING
        └── oauth-login.js ❌ MISSING
```

## Critical Missing Components

### 1. JavaScript Files (ALL MISSING)

All inline JavaScript from AuthController needs to be extracted to external files:

#### `wwwroot/js/theme-toggle.js`
- Theme detection from localStorage
- System preference detection
- Theme toggle functionality
- Icon switching (🌙/☀️)

#### `wwwroot/js/auth/login.js`
- `submitLoginForm()` function
- Auto-login overlay logic
- Token validation on page load
- Form submission with fetch API
- Redirect handling
- Error display
- Enter key support

#### `wwwroot/js/auth/register.js`
- Admin token validation (3 attempts)
- Form submission
- Role dropdown handling
- Status message display

#### `wwwroot/js/auth/totp-setup.js`
- QR code display
- Manual code entry
- 6-digit code validation
- Form submission

#### `wwwroot/js/auth/webauthn-register.js`
- `registerWebAuthn()` function
- `arrayBufferToBase64()` helper
- WebAuthn API integration
- Credential creation
- Server communication

#### `wwwroot/js/auth/qr-login.js`
- `checkLoginStatus()` polling function
- QR code display
- Auto-redirect on success
- Error handling

#### `wwwroot/js/auth/oauth-login.js`
- OAuth scope display
- Form submission to callback
- Request ID handling

### 2. Missing Shared Partials

#### `_AuthFooterLinks.cshtml`
```razor
<div class="auth-footer">
    <div style="margin-top: 2rem; display: flex; justify-content: center;">
        <a href="/api/auth/register" class="link" style="color: white; margin: 0 0.5rem;">Create account</a>
        <span style="color: #555;">|</span>
        <a href="/api/auth/magic-link" class="link" style="color: white; margin: 0 0.5rem;">Magic Link</a>
        <span style="color: #555;">|</span>
        <a href="/api/auth/claim-account" class="link" style="color: white; margin: 0 0.5rem;">Claim Account</a>
    </div>
    <div style="margin-top: 1rem; color: #555;">
        BRU ID — ключ от всех сервисов
    </div>
</div>
```

### 3. Updates Needed for Existing Files

#### `Login.cshtml` - Missing:
1. Complete `submitLoginForm()` JavaScript
2. Auto-login overlay HTML and logic
3. Complete SVG paths for icons
4. Footer links partial
5. Social login buttons
6. Enter key handler

#### `Register.cshtml` - Missing:
1. Complete admin validation JavaScript
2. Auto-retry logic (3 attempts)
3. Token extraction from Authorization header
4. Status polling

### 4. Service Implementation

#### `HtmlRenderingService.cs` - COMPLETELY MISSING

This service needs to:
- Implement `IHtmlRenderingService`
- Use Razor view engine to render .cshtml files
- Pass ViewModels to views
- Return rendered HTML strings
- Replace all inline string formatting from AuthController

Example structure:
```csharp
public class HtmlRenderingService : IHtmlRenderingService
{
    private readonly IRazorViewEngine _razorViewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;

    public string RenderLoginForm(string? error = null, string? message = null)
    {
        var viewModel = new LoginViewModel { Error = error, Message = message };
        return RenderView("Auth/Login", viewModel);
    }

    private string RenderView<TModel>(string viewName, TModel model)
    {
        // Use Razor engine to render view to string
        // Implementation details...
    }
}
```

## Migration Priority (Recommended Order)

### Phase 1: Critical Authentication Views (Week 1)
1. ✅ Extract CSS to external file
2. Create `theme-toggle.js`
3. Create `login.js` with complete functionality
4. Update `Login.cshtml` with all missing features
5. Create `_AuthFooterLinks.cshtml` partial
6. Test login flow end-to-end

### Phase 2: Registration & 2FA (Week 2)
7. Create `register.js` with admin validation
8. Update `Register.cshtml` with complete logic
9. Create `TotpSetup.cshtml` with QR code
10. Create `totp-setup.js`
11. Create `WebAuthnRegister.cshtml`
12. Create `webauthn-register.js`
13. Test registration and 2FA flows

### Phase 3: Alternative Auth Methods (Week 3)
14. Create `MagicLink.cshtml`
15. Create `QrLogin.cshtml`
16. Create `qr-login.js` with polling
17. Create `ClaimAccount.cshtml`
18. Create `Success.cshtml`
19. Create `Error.cshtml`
20. Test all alternative auth methods

### Phase 4: OAuth/OIDC Admin Pages (Week 4)
21. Create `OAuthLogin.cshtml`
22. Create `oauth-login.js`
23. Create `ClientsList.cshtml`
24. Create `ScopesList.cshtml`
25. Create `ClientDetails.cshtml`
26. Create `ClientForm.cshtml`
27. Test OAuth flows

### Phase 5: Service Layer & Integration (Week 5)
28. Implement `HtmlRenderingService.cs`
29. Update all controller methods to use services
30. Remove inline HTML from AuthController
31. Integration testing
32. Performance testing

## Estimated Effort

- **Total Lines of Code to Migrate:** ~8,000+ lines
- **Total Files to Create:** 25 files
- **Total Files to Update:** 5 files
- **Estimated Time:** 5 weeks (1 developer)
- **Current Completion:** 18.75%

## Testing Requirements

Each migrated view must be tested for:
1. ✅ Visual parity with original
2. ✅ Functional parity (all JavaScript works)
3. ✅ Responsive design (mobile, tablet, desktop)
4. ✅ Dark mode support
5. ✅ Accessibility (ARIA labels, keyboard navigation)
6. ✅ Browser compatibility (Chrome, Firefox, Safari, Edge)
7. ✅ Error handling
8. ✅ Loading states
9. ✅ Form validation
10. ✅ Token management

## Risks & Mitigation

### Risk 1: JavaScript Functionality Loss
**Mitigation:** Extract JavaScript incrementally, test each function individually

### Risk 2: CSS Conflicts
**Mitigation:** Use BEM naming convention, scope styles properly

### Risk 3: Razor View Engine Issues
**Mitigation:** Implement HtmlRenderingService early, test rendering pipeline

### Risk 4: Breaking Existing Functionality
**Mitigation:** Keep AuthController intact until all views are migrated and tested

## Success Criteria

Migration is complete when:
1. ✅ All 16 render methods have corresponding .cshtml files
2. ✅ All inline JavaScript is extracted to external files
3. ✅ All inline CSS is in bru-design-system.css
4. ✅ HtmlRenderingService is implemented and tested
5. ✅ All views render identically to original
6. ✅ All functionality works (auth, 2FA, OAuth, etc.)
7. ✅ All tests pass
8. ✅ AuthController can be safely deprecated

 

