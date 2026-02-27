# OAuth 2.0 / OpenID Connect Implementation

## Overview

This Avalonia desktop application implements OAuth 2.0 Authorization Code Flow with PKCE (Proof Key for Code Exchange) to authenticate users against the BRU AVTOPARK API server.

## Architecture

### Components

1. **OAuthService** (`Services/OAuthService.cs`)
   - Handles OAuth/OIDC protocol operations
   - Generates authorization URLs with PKCE
   - Exchanges authorization codes for tokens
   - Manages token refresh
   - Validates token expiration

2. **OAuthLoginWindow** (`Views/OAuthLoginWindow.axaml.cs`)
   - Displays the authorization page in an embedded WebView
   - Intercepts the redirect callback
   - Extracts authorization code and state
   - Handles errors and user cancellation

3. **TokenStorageService** (`Services/TokenStorageService.cs`)
   - Securely stores tokens on disk (encrypted)
   - Retrieves stored tokens
   - Clears tokens on logout

4. **AuthenticationManager** (`Services/AuthenticationManager.cs`)
   - Orchestrates the entire authentication flow
   - Manages authentication state
   - Provides singleton access to auth services

## OAuth 2.0 Flow with PKCE

### Step 1: Authorization Request

The client generates:
- **State**: Random 32-character string for CSRF protection
- **Code Verifier**: Random 64-character string for PKCE
- **Code Challenge**: SHA256 hash of code verifier, base64url encoded

Authorization URL format:
```
https://localhost:5001/connect/authorize?
  client_id=bru-avtopark-desktop-client&
  response_type=code&
  redirect_uri=http://localhost:5000/callback&
  scope=openid profile email offline_access api&
  state={random_state}&
  code_challenge={sha256_hash}&
  code_challenge_method=S256
```

### Step 2: User Authentication

1. OAuthLoginWindow opens with the authorization URL
2. User sees the login form rendered by the server
3. User enters credentials (username/password)
4. Server validates credentials against SpacetimeDB
5. Server creates authorization and redirects to callback

### Step 3: Authorization Code Callback

The server redirects to:
```
http://localhost:5000/callback?
  code={authorization_code}&
  state={original_state}
```

The OAuthLoginWindow:
1. Intercepts the redirect URL
2. Validates the state parameter matches
3. Extracts the authorization code
4. Returns the code to the caller

### Step 4: Token Exchange

The client sends a POST request to the token endpoint:
```
POST https://localhost:5001/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code&
code={authorization_code}&
redirect_uri=http://localhost:5000/callback&
client_id=bru-avtopark-desktop-client&
client_secret={client_secret}&
code_verifier={original_code_verifier}
```

The server:
1. Validates the authorization code
2. Verifies the code_verifier matches the stored code_challenge (PKCE validation)
3. Issues tokens:
   - **Access Token**: For API access (JWT)
   - **ID Token**: User identity information (JWT)
   - **Refresh Token**: For obtaining new access tokens

### Step 5: Token Storage

Tokens are encrypted and stored locally:
- Location: `%APPDATA%/BRU.Avtopark.TicketSalesAPP/tokens.dat`
- Encryption: AES-256 with machine-specific key
- Contents: Access token, refresh token, ID token, expiration time

### Step 6: API Requests

For authenticated API requests:
```csharp
var accessToken = await _oauthService.GetValidAccessTokenAsync();
_httpClient.DefaultRequestHeaders.Authorization = 
    new AuthenticationHeaderValue("Bearer", accessToken);
```

The service automatically:
- Checks token expiration
- Refreshes tokens if needed (5-minute buffer)
- Returns valid access token

### Step 7: Token Refresh

When the access token expires:
```
POST https://localhost:5001/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token&
refresh_token={refresh_token}&
client_id=bru-avtopark-desktop-client&
client_secret={client_secret}
```

Server issues new tokens without requiring user re-authentication.

## Security Features

### PKCE (Proof Key for Code Exchange)
- Protects against authorization code interception attacks
- Code verifier never leaves the client
- Server validates code_challenge matches code_verifier

### State Parameter
- Prevents CSRF attacks
- Client generates random state
- Server echoes state back in callback
- Client validates state matches

### Token Encryption
- Tokens encrypted at rest using AES-256
- Machine-specific encryption key
- Protects against token theft from disk

### HTTPS
- All communication over TLS 1.3
- Certificate validation enabled
- Prevents man-in-the-middle attacks

### Token Expiration
- Access tokens expire after 1 hour
- Refresh tokens expire after 30 days
- Automatic token refresh before expiration

## Configuration

### Client Configuration

In `AuthenticationManager.cs`:
```csharp
var clientId = "bru-avtopark-desktop-client";
var clientSecret = "your-secure-client-secret-here-change-in-production";
var authorizationEndpoint = "https://localhost:5001/connect/authorize";
var tokenEndpoint = "https://localhost:5001/connect/token";
var redirectUri = "http://localhost:5000/callback";
```

### Server Configuration

The server must have the client registered in SpacetimeDB:
- Client ID: `bru-avtopark-desktop-client`
- Client Type: Public (desktop app)
- Redirect URIs: `http://localhost:5000/callback`
- Allowed Scopes: `openid`, `profile`, `email`, `offline_access`, `api`
- Grant Types: `authorization_code`, `refresh_token`
- PKCE: Required

## Scopes

### Standard OpenID Connect Scopes

- **openid**: Required for OpenID Connect
- **profile**: User profile information (username, name)
- **email**: User email address
- **offline_access**: Enables refresh tokens

### Custom Scopes

- **api**: Access to BRU AVTOPARK API endpoints

## Token Claims

### Access Token Claims
```json
{
  "sub": "identity_string",
  "name": "username",
  "email": "user@example.com",
  "email_verified": "true",
  "phone_number": "+375333000000",
  "phone_number_verified": "false",
  "role": ["User", "Administrator"],
  "scope": "openid profile email api offline_access",
  "aud": "api_resource",
  "iss": "https://localhost:5001",
  "exp": 1234567890,
  "iat": 1234564290
}
```

### ID Token Claims
```json
{
  "sub": "identity_string",
  "name": "username",
  "email": "user@example.com",
  "email_verified": "true",
  "phone_number": "+375333000000",
  "phone_number_verified": "false",
  "aud": "bru-avtopark-desktop-client",
  "iss": "https://localhost:5001",
  "exp": 1234567890,
  "iat": 1234564290
}
```

## Error Handling

### Authorization Errors
- **access_denied**: User denied authorization
- **invalid_request**: Malformed request
- **unauthorized_client**: Client not authorized
- **unsupported_response_type**: Invalid response_type

### Token Errors
- **invalid_grant**: Invalid or expired authorization code
- **invalid_client**: Invalid client credentials
- **invalid_request**: Malformed token request

### Application Handling
```csharp
try
{
    var result = await AuthenticationManager.Instance.LoginWithOAuthAsync();
    if (result)
    {
        // Authentication successful
    }
    else
    {
        // Authentication failed or cancelled
    }
}
catch (Exception ex)
{
    // Handle errors (network, server, etc.)
}
```

## Testing

### Manual Testing

1. Run the API server: `dotnet run --project BRU-AVTOPARK-AspireAPI.ApiService`
2. Run the Avalonia app
3. Click "Login with OAuth"
4. Enter credentials: `admin` / `admin`
5. Verify tokens are stored
6. Make API requests with access token
7. Wait for token expiration and verify refresh

### Debug Logging

Enable detailed logging in `appsettings.json`:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services": "Debug"
      }
    }
  }
}
```

## Troubleshooting

### "Invalid redirect_uri"
- Verify redirect URI matches server configuration exactly
- Check for trailing slashes
- Ensure URI is registered in SpacetimeDB

### "Invalid code_verifier"
- PKCE validation failed
- Ensure code_verifier is passed correctly
- Check code_challenge generation

### "Token expired"
- Access token expired
- Refresh token should be used automatically
- If refresh fails, user must re-authenticate

### "State mismatch"
- CSRF protection triggered
- Possible attack or browser issue
- Clear cache and try again

## Production Considerations

### Security
1. **Change client_secret**: Use a strong, randomly generated secret
2. **Use HTTPS**: Never use HTTP in production
3. **Validate certificates**: Enable certificate validation
4. **Secure token storage**: Consider using OS keychain/credential manager
5. **Implement token rotation**: Rotate refresh tokens periodically

### Performance
1. **Token caching**: Cache valid tokens in memory
2. **Connection pooling**: Reuse HTTP connections
3. **Async operations**: All network calls are async

### Monitoring
1. **Log authentication events**: Track login/logout
2. **Monitor token refresh**: Alert on high refresh rates
3. **Track errors**: Monitor authentication failures

## References

- [OAuth 2.0 RFC 6749](https://tools.ietf.org/html/rfc6749)
- [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html)
- [PKCE RFC 7636](https://tools.ietf.org/html/rfc7636)
- [OpenIddict Documentation](https://documentation.openiddict.com/)
