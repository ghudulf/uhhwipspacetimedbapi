# OAuth WebView Implementation

## Overview

The OAuth authentication flow has been properly implemented using the official **Avalonia.Controls.WebView** package, which provides native web browser functionality through the `WebAuthenticationBroker` API.

## Implementation Details

### Package Used

- **Avalonia.Controls.WebView** (v11.2.3)
  - Uses native platform web rendering
  - Windows: Microsoft Edge WebView2
  - macOS/iOS: WKWebView
  - Linux: WebKitGTK 4.1

### Key Components

#### 1. AuthenticationManager (`Services/AuthenticationManager.cs`)

The main authentication orchestrator that:
- Uses `WebAuthenticationBroker.AuthenticateAsync()` for OAuth flow
- Implements PKCE (Proof Key for Code Exchange) for security
- Handles state validation to prevent CSRF attacks
- Exchanges authorization codes for access tokens
- Manages token storage and refresh

**Key Features:**
- Comprehensive logging at every step
- Proper error handling and validation
- State parameter verification
- PKCE code verifier/challenge implementation

#### 2. OAuthService (`Services/OAuthService.cs`)

Handles OAuth protocol operations:
- Generates authorization URLs with PKCE
- Exchanges authorization codes for tokens
- Refreshes expired tokens
- Validates and stores tokens securely

#### 3. LoginMethodSelectorWindow

Allows users to choose between:
- Traditional username/password login
- OAuth/OpenID Connect login (recommended)

### OAuth Flow

```
1. User selects OAuth login method
   ↓
2. AuthenticationManager generates authorization URL with:
   - Client ID
   - Redirect URI
   - Scopes (openid, profile, email, offline_access, api)
   - State (CSRF protection)
   - PKCE code challenge
   ↓
3. WebAuthenticationBroker opens native browser with auth URL
   ↓
4. User authenticates on authorization server
   ↓
5. Server redirects to callback URL with authorization code
   ↓
6. WebAuthenticationBroker captures redirect and returns callback URI
   ↓
7. AuthenticationManager validates state parameter
   ↓
8. Exchange authorization code for tokens using PKCE verifier
   ↓
9. Store tokens securely
   ↓
10. User is authenticated
```

### Configuration

OAuth settings in `AuthenticationManager`:

```csharp
var clientId = "bru-avtopark-desktop-client";
var clientSecret = "your-secure-client-secret-here-change-in-production";
var authorizationEndpoint = "https://localhost:5001/connect/authorize";
var tokenEndpoint = "https://localhost:5001/connect/token";
var redirectUri = "http://localhost:5000/callback";
```

### Platform Requirements

#### Windows
- **Windows 10/11**: WebView2 Runtime required
  - Pre-installed on Windows 11
  - May need installation on Windows 10
  - Download: https://developer.microsoft.com/microsoft-edge/webview2/

#### macOS/iOS
- **macOS 10.15+** or **iOS 12.0+**
- Uses built-in WKWebView (no additional setup)

#### Linux
- **GTK 3.0** and **WebKitGTK 4.1**
- Install on Debian/Ubuntu:
  ```bash
  apt install libgtk-3-0 libwebkit2gtk-4.1-0
  ```
- Install on Fedora:
  ```bash
  dnf install gtk3 webkit2gtk4.1
  ```

### Security Features

1. **PKCE (RFC 7636)**
   - Protects against authorization code interception
   - Uses SHA256 code challenge method
   - Code verifier: 64-character random string

2. **State Parameter**
   - Prevents CSRF attacks
   - 32-character random string
   - Validated on callback

3. **Secure Token Storage**
   - Tokens stored using `TokenStorageService`
   - Encrypted storage recommended for production

4. **Token Refresh**
   - Automatic token refresh when expired
   - Uses refresh tokens for seamless re-authentication

### Logging

Comprehensive logging throughout the flow:
- Authorization URL generation
- WebAuthenticationBroker invocation
- Callback URI processing
- State validation
- Token exchange requests/responses
- Error conditions

All logs use Serilog with appropriate log levels (Debug, Information, Warning, Error).

### Error Handling

The implementation handles:
- User cancellation
- Network errors
- Invalid state parameters
- Missing authorization codes
- Token exchange failures
- Server errors

### Testing

To test the OAuth flow:

1. Ensure the API server is running at `https://localhost:5001`
2. Register the OAuth client with the authorization server
3. Run the application
4. Select "OAuth / OpenID Connect" login method
5. Complete authentication in the browser
6. Check logs for detailed flow information

### Future Enhancements

1. **Dynamic Configuration**
   - Load OAuth settings from configuration file
   - Support multiple OAuth providers

2. **Enhanced Security**
   - Implement token encryption at rest
   - Add certificate pinning for API calls

3. **User Experience**
   - Remember last login method
   - Show authentication progress
   - Handle token expiration gracefully in UI

4. **Multi-Platform**
   - Test on macOS and Linux
   - Optimize for each platform's native browser

## Troubleshooting

### WebView2 Not Found (Windows)
- Install WebView2 Runtime from Microsoft
- Restart application after installation

### Browser Not Opening
- Check firewall settings
- Verify authorization endpoint is accessible
- Check application logs for errors

### State Mismatch Error
- Ensure callback URL matches registered redirect URI
- Check for URL encoding issues
- Verify state parameter is preserved

### Token Exchange Fails
- Verify client credentials
- Check token endpoint URL
- Ensure PKCE code verifier matches challenge
- Review server logs for detailed error

## References

- [Avalonia WebView Documentation](https://docs.avaloniaui.net/docs/controls/webview)
- [OAuth 2.0 RFC 6749](https://tools.ietf.org/html/rfc6749)
- [PKCE RFC 7636](https://tools.ietf.org/html/rfc7636)
- [OpenID Connect Core](https://openid.net/specs/openid-connect-core-1_0.html)
