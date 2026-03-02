# Custom Authentication System - Technical Documentation

## Overview

The BRU AVTOPARK system implements a **dual authentication architecture** that supports both traditional custom JWT tokens and modern OAuth 2.0/OpenID Connect authentication. This hybrid approach allows the system to maintain backward compatibility with legacy authentication while providing enterprise-grade OAuth security for new implementations.

## Architecture Components

### 1. Authentication Methods Supported

The system supports multiple authentication methods:

- **Custom JWT Authentication** - Traditional username/password with custom JWT tokens
- **OAuth 2.0 / OpenID Connect** - Modern OAuth flow with encrypted JWE tokens
- **QR Code Authentication** - Mobile-friendly authentication
- **TOTP (Time-based One-Time Password)** - Two-factor authentication
- **WebAuthn** - Biometric/hardware key authentication
- **Magic Link** - Email-based passwordless authentication

### 2. Token Types

#### Custom JWT Tokens (3-part structure)
```
header.payload.signature
```
- **Format**: Standard JWT (JSON Web Token)
- **Structure**: 3 parts separated by dots
- **Encryption**: Signed but not encrypted (claims are readable)
- **Use Case**: Legacy authentication, internal systems
- **Validation**: Direct parsing with `JwtSecurityTokenHandler`

#### OAuth JWE Tokens (5-part structure)
```
header.encrypted_key.initialization_vector.ciphertext.authentication_tag
```
- **Format**: JWE (JSON Web Encryption)
- **Structure**: 5 parts separated by dots
- **Encryption**: Fully encrypted payload (claims are not readable without decryption)
- **Use Case**: OAuth 2.0 authentication, external integrations
- **Validation**: Requires OpenIddict validation with decryption keys

### 3. Key Components

#### Server-Side Components

**BaseController.cs**
- Central authorization logic for all API controllers
- Implements `IsAdmin()`, `HasPermission()`, `IsAuthenticated()` methods
- Handles both JWT and JWE token validation
- Uses `[AllowAnonymous]` to bypass ASP.NET Core auth middleware
- Manually validates tokens to support dual authentication

**AuthController.cs**
- Handles all authentication endpoints
- Implements OAuth 2.0 authorization code flow
- Generates custom JWT tokens for traditional login
- Provides `/connect/token` endpoint for OAuth token exchange
- Provides `/connect/tokeninfo` endpoint for token introspection

**Token Detection Logic**
```csharp
private bool IsJweToken(string token)
{
    var parts = token.Split('.');
    
    // JWE has 5 parts, JWT has 3 parts
    if (parts.Length == 5) return true;
    if (parts.Length != 3) return false;
    
    // Decode header and check for "enc" field (JWE-specific)
    var headerBytes = Convert.FromBase64String(parts[0]);
    var headerJson = Encoding.UTF8.GetString(headerBytes);
    return headerJson.Contains("\"enc\"");
}
```

#### Client-Side Components

**AuthenticationManager.cs**
- Singleton service managing authentication state
- Handles OAuth flow coordination
- Stores and retrieves tokens from secure storage
- Manages token refresh logic

**OAuthService.cs**
- Implements OAuth 2.0 client logic
- Handles authorization code flow with PKCE
- Manages OAuth callback handling
- Extracts and logs token claims

**ApiClientService.cs**
- Creates HTTP clients with proper authentication headers
- Attaches Bearer tokens to all API requests
- Manages token lifecycle

**TokenStorageService.cs**
- Securely stores tokens in platform-specific storage
- Encrypts sensitive token data
- Handles token persistence across app restarts

## Authentication Flow

### Custom JWT Authentication Flow

```
1. User enters username/password
   ↓
2. Client sends POST to /api/Auth/login
   ↓
3. Server validates credentials against SpacetimeDB
   ↓
4. Server generates custom JWT with claims:
   - sub (user ID)
   - identity (SpacetimeDB identity)
   - primary_role (role ID)
   - role (role name)
   - permissions (array of permission strings)
   ↓
5. Server returns JWT token + user info
   ↓
6. Client stores token in TokenStorageService
   ↓
7. Client attaches token to all API requests as Bearer token
   ↓
8. Server validates token in BaseController methods
```

### OAuth 2.0 Authentication Flow

```
1. User clicks "OAuth Login"
   ↓
2. Client generates PKCE code_verifier and code_challenge
   ↓
3. Client opens browser to /connect/authorize with:
   - client_id
   - redirect_uri
   - response_type=code
   - scope=openid profile email
   - code_challenge
   - code_challenge_method=S256
   ↓
4. User authenticates on OAuth login page
   ↓
5. Server validates credentials
   ↓
6. Server redirects to callback URL with authorization code
   ↓
7. Client extracts authorization code from callback
   ↓
8. Client sends POST to /connect/token with:
   - grant_type=authorization_code
   - code
   - redirect_uri
   - client_id
   - code_verifier
   ↓
9. Server validates code and PKCE
   ↓
10. Server generates encrypted JWE access token
    ↓
11. Server returns:
    - access_token (JWE)
    - id_token (JWT with user info)
    - refresh_token
    - expires_in
    ↓
12. Client stores tokens in TokenStorageService
    ↓
13. Client attaches access_token to all API requests
    ↓
14. Server detects JWE token in BaseController
    ↓
15. Server validates via /connect/tokeninfo endpoint
    ↓
16. OpenIddict decrypts token and returns claims
```

## Authorization Logic

### BaseController Authorization Methods

#### IsAuthenticated()
```csharp
protected bool IsAuthenticated()
{
    // 1. Check ASP.NET Core authentication (OpenIddict)
    if (User?.Identity?.IsAuthenticated == true)
        return true;
    
    // 2. Check Authorization header for Bearer token
    var token = ExtractBearerToken();
    if (token == null) return false;
    
    // 3. For JWE tokens, assume valid (validated by OpenIddict)
    if (IsJweToken(token)) return true;
    
    // 4. For JWT tokens, check if parseable and has claims
    if (CanReadJwt(token)) return true;
    
    return false;
}
```

#### IsAdmin()
```csharp
protected bool IsAdmin()
{
    // 1. Try ASP.NET Core authentication first
    if (User?.Identity?.IsAuthenticated == true)
    {
        // Check for primary_role=1 or role=Administrator
        if (HasAdminClaims(User.Claims))
            return true;
    }
    
    // 2. Extract Bearer token
    var token = ExtractBearerToken();
    if (token == null) return false;
    
    // 3. Handle JWE tokens (encrypted OAuth tokens)
    if (IsJweToken(token))
    {
        // Validate via tokeninfo endpoint
        var claims = ValidateOAuthTokenAsync().Result;
        return HasAdminClaims(claims);
    }
    
    // 4. Handle JWT tokens (custom tokens)
    if (CanReadJwt(token))
    {
        var jwt = ParseJwt(token);
        return HasAdminClaims(jwt.Claims);
    }
    
    return false;
}
```

### Token Validation Strategy

**For Custom JWT Tokens:**
1. Parse token directly with `JwtSecurityTokenHandler`
2. Extract claims from payload
3. Check `primary_role` claim for admin status
4. Validate signature (if configured)

**For OAuth JWE Tokens:**
1. Detect JWE format (5 parts or "enc" in header)
2. Make internal HTTP call to `/connect/tokeninfo`
3. OpenIddict validates and decrypts token
4. Returns claims dictionary
5. Check claims for admin status

## Security Considerations

### Why [AllowAnonymous] on Controllers?

Controllers use `[AllowAnonymous]` attribute because:

1. **Dual Authentication Support**: ASP.NET Core's built-in authentication middleware only supports one authentication scheme at a time
2. **Custom JWT Compatibility**: Custom JWT tokens don't go through ASP.NET Core authentication pipeline
3. **Manual Validation**: BaseController manually validates both token types
4. **Flexibility**: Allows fine-grained control over authentication logic

### Token Storage Security

**Client-Side:**
- Tokens stored in platform-specific secure storage
- Encryption at rest
- Automatic cleanup on logout
- No tokens in localStorage or sessionStorage

**Server-Side:**
- JWE tokens encrypted with server keys
- Tokens stored in SpacetimeDB with encryption
- Token revocation support
- Refresh token rotation

### PKCE (Proof Key for Code Exchange)

OAuth flow uses PKCE to prevent authorization code interception:

1. Client generates random `code_verifier`
2. Client creates `code_challenge` = SHA256(code_verifier)
3. Authorization request includes `code_challenge`
4. Token request includes `code_verifier`
5. Server validates: SHA256(code_verifier) == code_challenge

## Claims Structure

### Custom JWT Claims
```json
{
  "sub": "user_id_123",
  "identity": "spacetime_identity_hex",
  "primary_role": "1",
  "role": "Administrator",
  "permissions": [
    "buses.view",
    "buses.create",
    "buses.edit",
    "buses.delete",
    "users.manage"
  ],
  "xuid": "user_xuid",
  "exp": 1234567890,
  "iat": 1234567890
}
```

### OAuth Token Claims (after decryption)
```json
{
  "sub": "user_id_123",
  "name": "John Doe",
  "email": "john@example.com",
  "primary_role": "1",
  "role": "Administrator",
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "Administrator",
  "permissions": ["buses.view", "buses.create"],
  "aud": "api_resource",
  "iss": "https://localhost:5000",
  "exp": 1234567890,
  "iat": 1234567890
}
```

## Troubleshooting

### Common Issues

**403 Forbidden with OAuth tokens:**
- **Cause**: JWE token detected as JWT, claims not extracted
- **Solution**: Proper JWE detection with `IsJweToken()` method
- **Fix**: Check for 5 parts or "enc" field in header

**Token validation fails:**
- **Cause**: OpenIddict can't decrypt token
- **Solution**: Ensure encryption keys are configured correctly
- **Check**: `/connect/tokeninfo` endpoint logs

**Claims not found:**
- **Cause**: Wrong claim type names
- **Solution**: Check both standard and custom claim names
- **Example**: Check "primary_role", "role", and ClaimTypes.Role

### Debug Endpoints

**`/debug/tokentest`** - Token inspection endpoint
- Shows token type (JWT vs JWE)
- Displays all claims
- Shows encryption status
- Validates OpenIddict authentication

 

 
## References

- [RFC 7519 - JSON Web Token (JWT)](https://tools.ietf.org/html/rfc7519)
- [RFC 7516 - JSON Web Encryption (JWE)](https://tools.ietf.org/html/rfc7516)
- [RFC 6749 - OAuth 2.0 Authorization Framework](https://tools.ietf.org/html/rfc6749)
- [RFC 7636 - PKCE](https://tools.ietf.org/html/rfc7636)
- [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html)
- [OpenIddict Documentation](https://documentation.openiddict.com/)
