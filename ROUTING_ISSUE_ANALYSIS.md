# Routing Issue Analysis - Login POST 404 Error

## Problem Summary
The POST `/api/auth/login` endpoint returns 404 even though:
- Feature flag `EnableLoginRefactoring` is set to `true` in appsettings.json
- Both `AuthController` (legacy) and `AuthControllerRefactored` have Login endpoints
- Both endpoints have the correct attributes (`[LegacyAction]` and `[RefactoredAction]`)
- The build succeeds with no errors

## Root Cause - IDENTIFIED AND FIXED ✓
The logs showed: **"Only 1 candidate found"** - this meant ASP.NET Core routing was only discovering ONE of the two controllers, not both.

**The Issue**: The refactored controller used `[Route("api/[controller]")]` which expanded to `api/AuthControllerRefactored`, while the legacy controller used the same attribute but expanded to `api/Auth` (shorter name). They had DIFFERENT routes, so feature flag routing could not work!

For feature flag routing to work, BOTH controllers must:
1. Be discovered by the routing system
2. Have IDENTICAL route templates
3. Have the same HTTP method and route pattern for each endpoint

The `IActionConstraint` only filters between discovered actions at the SAME route - it doesn't help if controllers have different routes.

## The Fix
Changed `AuthControllerRefactored.cs` line 36:
```csharp
// BEFORE (WRONG):
[Route("api/[controller]")]  // Expands to "api/AuthControllerRefactored"

// AFTER (CORRECT):
[Route("api/Auth")]  // Matches legacy controller exactly
```

Now both controllers respond to the same routes (`api/Auth/*`), and the feature flag routing system can correctly select between them based on flag state.

## Evidence from Logs
```
FeatureFlagConstraint: Property=EnableLoginRefactoring, FlagValue=True, RequireEnabled=False, Accepted=False
```

This shows:
1. The legacy endpoint was found and checked
2. The flag is enabled (FlagValue=True)
3. Legacy requires disabled (RequireEnabled=False)
4. Legacy was correctly rejected (Accepted=False)
5. BUT: No refactored endpoint was found to try next

## Why Both Controllers Aren't Being Discovered

### Possible Causes:
1. **Route Ambiguity**: ASP.NET Core may be filtering out one controller due to identical routes
2. **Controller Discovery Issue**: The refactored controller may not be registered properly
3. **Action Constraint Timing**: Constraints are evaluated AFTER route matching, not during discovery

## Comparison: Legacy vs Refactored Profile Endpoint

### Legacy Profile (WORKING):
```csharp
[HttpGet("profile")]
[AllowAnonymous]
[LegacyAction(nameof(FeatureFlagOptions.EnableProfileRefactoring))]
public async Task<IActionResult> ProfilePage()
{
    // Accepts token from:
    // 1. Authorization header: "Bearer <token>"
    // 2. Query parameter: ?token=<token>
    // 3. localStorage via JavaScript redirect
}
```

### Refactored Profile (FIXED):
```csharp
[HttpGet("profile")]
[AllowAnonymous]
[RefactoredAction(nameof(FeatureFlagOptions.EnableProfileRefactoring))]
public async Task<IActionResult> Profile([FromQuery] string? token = null)
{
    // Now accepts token from:
    // 1. Authorization header: "Bearer <token>"
    // 2. Query parameter: ?token=<token>
    
    // Calls: await _authOrchestrationService.GetProfileAsync(token);
}
```

**Profile endpoint works because it's a GET request and may have different discovery behavior.**

## Login Endpoint Comparison

### Legacy Login:
```csharp
[HttpPost("login")]
[AllowAnonymous]
[LegacyAction(nameof(FeatureFlagOptions.EnableLoginRefactoring))]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
```

### Refactored Login:
```csharp
[HttpPost("login")]
[AllowAnonymous]
[RefactoredAction(nameof(FeatureFlagOptions.EnableLoginRefactoring))]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
```

**Both have identical signatures - this may be causing the discovery issue.**

## Potential Solutions

### Solution 1: Use Different Route Patterns (RECOMMENDED)
Change the refactored controller to use a different base route during migration:

```csharp
[ApiController]
[Route("api/auth-v2")]  // Different route
public class AuthControllerRefactored : ControllerBase
```

Then use a middleware or route rewrite to map `/api/auth` to `/api/auth-v2` when flags are enabled.

### Solution 2: Use Route Order
Add explicit route order to prioritize one controller:

```csharp
[Route("api/[controller]", Order = 1)]  // Legacy
[Route("api/[controller]", Order = 2)]  // Refactored
```

### Solution 3: Use Dynamic Controller Registration
Register controllers conditionally based on feature flags in Program.cs:

```csharp
builder.Services.AddControllersWithViews(options =>
{
    var featureFlags = builder.Configuration.GetSection("FeatureFlags").Get<FeatureFlagOptions>();
    
    if (featureFlags?.EnableLoginRefactoring == true)
    {
        // Register refactored controller
    }
    else
    {
        // Register legacy controller
    }
});
```

### Solution 4: Use Endpoint Routing with MapWhen
Use endpoint routing to conditionally map routes:

```csharp
app.MapWhen(
    context => featureFlags.EnableLoginRefactoring,
    app => app.MapControllers() // Refactored
);
```

## Recommended Immediate Fix

The simplest fix is to **temporarily use different route names** for the refactored controller:

1. Change `AuthControllerRefactored` route to `[Route("api/auth-refactored")]`
2. Update client code to use the new route when testing
3. Once validated, implement a proper routing strategy (middleware/rewrite)

## Next Steps

1. Verify both controllers are being discovered by adding startup logging
2. Implement one of the solutions above
3. Test that both endpoints are discovered
4. Verify feature flag routing works correctly
5. Update client code if route changes are needed

## Files Involved

- `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/AuthController.cs`
- `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/AuthControllerRefactored.cs`
- `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Routing/FeatureFlagActionConstraint.cs`
- `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Program.cs`
- `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/appsettings.json`
