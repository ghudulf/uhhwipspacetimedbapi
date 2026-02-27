# Avalonia Client 403 Forbidden Issues - Complete Fix Guide

## Problem Summary

The Avalonia client is receiving **403 Forbidden** responses for endpoints like:
- `/api/Buses` - "Failed to load buses. Status: Forbidden"
- `/api/Employees` - "Failed to load employees. Status: Forbidden"  
- `/api/Users` - "Failed to load users. Status: Forbidden"

### Root Cause

The OAuth/OpenIddict tokens being issued **DO NOT contain permission claims** that the API controllers require.

**Server logs show:**
```
2026-02-27 22:41:11.912 [WRN] IsAdmin check (JWT) - User not admin
2026-02-27 22:41:11.938 [WRN] HasPermission check - User does not have permission 'users.view'
2026-02-27 22:41:11.939 [WRN] Unauthorized attempt to access users list
```

**Client logs show:**
```
2026-02-27 22:41:03.991 [INF] Processing Buses response. Status: "Forbidden"
2026-02-27 22:41:05.517 [INF] Processing Employees response. Status: "Forbidden"
2026-02-27 22:41:11.946 [INF] Processing Users response. Status: "Forbidden"
```

---

## Solution: Add Permission Claims to OAuth Tokens

The OAuth token exchange process needs to be modified to include permission and role claims when issuing access tokens.

### Step 1: Locate the Token Endpoint Handler

Find where the `/connect/token` endpoint is handled in `AuthController.cs`. This is where authorization codes are exchanged for access tokens.

### Step 2: Add Claims to Access Token

When creating the access token, you need to:

1. **Retrieve the user's identity** from the authorization code
2. **Query the user's roles and permissions** from SpacetimeDB
3. **Add claims to the token** before signing it

### Example Implementation

```csharp
// In AuthController.cs - Token endpoint handler

[HttpPost("~/connect/token")]
[AllowAnonymous]
public async Task<IActionResult> Exchange()
{
    var request = HttpContext.GetOpenIddictServerRequest();
    
    if (request.IsAuthorizationCodeGrantType())
    {
        // Retrieve the claims principal stored in the authorization code
        var claimsPrincipal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        
        // Get user identity from claims
        var userId = claimsPrincipal.GetClaim(Claims.Subject);
        
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        
        // CRITICAL: Query user's roles and permissions from SpacetimeDB
        var conn = _spacetimeService.GetConnection();
        var user = conn.Db.User.Iter().FirstOrDefault(u => u.Login == userId);
        
        if (user == null)
        {
            _logger.LogError("User not found in database: {UserId}", userId);
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        
        // Create new identity with additional claims
        var identity = new ClaimsIdentity(claimsPrincipal.Claims);
        
        // Add role claims
        identity.AddClaim(new Claim("role", user.PrimaryRole.ToString()));
        identity.AddClaim(new Claim("primary_role", user.PrimaryRole.ToString()));
        
        // Add permission claims based on role
        var permissions = GetPermissionsForRole(user.PrimaryRole);
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim("permission", permission));
        }
        
        // Add SpacetimeDB identity
        identity.AddClaim(new Claim("identity", user.Identity));
        identity.AddClaim(new Claim("xuid", user.Xuid.ToString()));
        
        // Create new principal with enriched claims
        var principal = new ClaimsPrincipal(identity);
        
        // Set destinations for claims (which tokens they should appear in)
        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(GetDestinations(claim));
        }
        
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
    
    return BadRequest(new OpenIddictResponse
    {
        Error = Errors.UnsupportedGrantType,
        ErrorDescription = "The specified grant type is not supported."
    });
}

private IEnumerable<string> GetDestinations(Claim claim)
{
    // Include claim in access token
    yield return Destinations.AccessToken;
    
    // Also include in ID token for OpenID Connect
    if (claim.Type == Claims.Name || 
        claim.Type == Claims.Email ||
        claim.Type == Claims.Subject)
    {
        yield return Destinations.IdentityToken;
    }
}

private List<string> GetPermissionsForRole(uint roleId)
{
    // Map role IDs to permissions
    // Role 1 = Administrator - has all permissions
    if (roleId == 1)
    {
        return new List<string>
        {
            "users.view", "users.create", "users.update", "users.delete",
            "employees.view", "employees.create", "employees.update", "employees.delete",
            "buses.view", "buses.create", "buses.update", "buses.delete",
            "routes.view", "routes.create", "routes.update", "routes.delete",
            "tickets.view", "tickets.create", "tickets.update", "tickets.delete",
            "sales.view", "sales.create", "sales.update", "sales.delete",
            "maintenance.view", "maintenance.create", "maintenance.update", "maintenance.delete",
            "jobs.view", "jobs.create", "jobs.update", "jobs.delete",
            "permissions.view", "permissions.manage",
            "roles.view", "roles.manage"
        };
    }
    
    // Role 2 = Manager - limited permissions
    if (roleId == 2)
    {
        return new List<string>
        {
            "employees.view",
            "buses.view",
            "routes.view", "routes.create", "routes.update",
            "tickets.view",
            "sales.view",
            "maintenance.view"
        };
    }
    
    // Role 4 = Cashier - ticket sales only
    if (roleId == 4)
    {
        return new List<string>
        {
            "tickets.view",
            "sales.view", "sales.create"
        };
    }
    
    // Default: minimal permissions
    return new List<string>
    {
        "tickets.view"
    };
}
```

---

## Step 3: Verify Token Contains Claims

After implementing the fix, verify the token contains the required claims:

### Decode the JWT Token

Use a tool like [jwt.io](https://jwt.io) or add logging to decode the token:

```csharp
// In client or server code
var tokenHandler = new JwtSecurityTokenHandler();
var jwtToken = tokenHandler.ReadJwtToken(accessToken);

foreach (var claim in jwtToken.Claims)
{
    Log.Information("Token claim: {Type} = {Value}", claim.Type, claim.Value);
}
```

### Expected Claims

The token should contain:
```json
{
  "sub": "admin",
  "role": "1",
  "primary_role": "1",
  "permission": "users.view",
  "permission": "employees.view",
  "permission": "buses.view",
  "permission": "routes.view",
  "permission": "tickets.view",
  "permission": "sales.view",
  "permission": "maintenance.view",
  "permission": "jobs.view",
  "permission": "permissions.view",
  "permission": "roles.view",
  "identity": "C200A612C52D6987B10DEC7091FD4034DC17076C28CD9A72E78E10EF52DAD167",
  "xuid": "12345",
  "iat": 1709067600,
  "exp": 1709074800,
  "iss": "http://localhost:5000/",
  "aud": "bru-avtopark-desktop-client"
}
```

---

## Step 4: Alternative - Check Authorization Endpoint

If the token endpoint is correct, check the **authorization endpoint** (`/connect/authorize`) to ensure it's storing the user's identity correctly:

```csharp
[HttpGet("~/connect/authorize")]
[HttpPost("~/connect/authorize")]
[AllowAnonymous]
public async Task<IActionResult> Authorize()
{
    var request = HttpContext.GetOpenIddictServerRequest();
    
    // ... existing authorization logic ...
    
    // CRITICAL: Store user identity in authorization code
    var identity = new ClaimsIdentity(
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
        Claims.Name,
        Claims.Role);
    
    // Add subject claim (user ID)
    identity.AddClaim(new Claim(Claims.Subject, userId));
    identity.AddClaim(new Claim(Claims.Name, userName));
    
    // Store in authorization code for later retrieval in token endpoint
    var principal = new ClaimsPrincipal(identity);
    
    return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
```

---

## Step 5: Test the Fix

1. **Clear existing tokens** on the client:
   ```
   Delete: %LOCALAPPDATA%\BRU.Avtopark.TicketSalesApp\tokens.dat
   ```

2. **Restart the API server** to apply changes

3. **Login again** through the Avalonia client

4. **Check the logs** for permission claims:
   ```
   Server: "HasPermission check - User has permission 'users.view'"
   Client: "Processing Users response. Status: OK"
   ```

5. **Verify endpoints work**:
   - Buses endpoint should return data
   - Employees endpoint should return data
   - Users endpoint should return data

---

## Step 6: Handle Token Refresh

When refreshing tokens, ensure permissions are also refreshed:

```csharp
if (request.IsRefreshTokenGrantType())
{
    // Retrieve the claims principal from the refresh token
    var claimsPrincipal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
    
    var userId = claimsPrincipal.GetClaim(Claims.Subject);
    
    // Re-query user's current roles and permissions
    var conn = _spacetimeService.GetConnection();
    var user = conn.Db.User.Iter().FirstOrDefault(u => u.Login == userId);
    
    if (user == null)
    {
        return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
    
    // Create new identity with updated claims
    var identity = new ClaimsIdentity(claimsPrincipal.Claims);
    
    // Update role and permission claims
    // ... (same as authorization code flow)
    
    var principal = new ClaimsPrincipal(identity);
    
    foreach (var claim in principal.Claims)
    {
        claim.SetDestinations(GetDestinations(claim));
    }
    
    return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
```

---

## Common Issues

### Issue 1: Claims Not Appearing in Token

**Cause**: Claims don't have destinations set

**Fix**: Ensure `claim.SetDestinations()` is called for each claim

### Issue 2: Token Too Large

**Cause**: Too many permission claims

**Fix**: Use permission groups or roles instead of individual permissions

### Issue 3: Permissions Change But Token Doesn't Update

**Cause**: Token is cached and not refreshed

**Fix**: Implement token refresh or reduce token expiration time

---

## Verification Checklist

- [ ] Token endpoint adds role claims
- [ ] Token endpoint adds permission claims
- [ ] Token endpoint adds identity/xuid claims
- [ ] Claims have proper destinations set
- [ ] Authorization endpoint stores user identity
- [ ] Refresh token flow updates permissions
- [ ] Client receives 200 OK for protected endpoints
- [ ] Server logs show "User has permission 'X'"
- [ ] No more 403 Forbidden errors in client logs

---

## Next Steps

After fixing the permission claims issue:

1. **Optimize permission checks** - Cache permissions to reduce database queries
2. **Implement permission groups** - Group related permissions together
3. **Add permission UI** - Allow admins to manage user permissions
4. **Audit permission changes** - Log when permissions are granted/revoked
5. **Test with different roles** - Verify each role has correct permissions
