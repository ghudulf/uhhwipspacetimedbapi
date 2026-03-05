# Request/Response Models Comparison

## Status: ✅ ALL MODELS COMPLETE

This document compares the Request/Response models in AuthController.cs with those in the Experimental folder.

**ALL REQUEST AND RESPONSE MODELS HAVE BEEN MIGRATED TO THE EXPERIMENTAL FOLDER.**

---

## ✅ COMPLETE - Request Models

All request models from AuthController exist in `Experimental/Models/Requests/AuthRequests.cs`:

| AuthController Model | Experimental Model | Status |
|---------------------|-------------------|--------|
| LoginRequest | LoginRequest | ✅ Match |
| RegisterRequest | RegisterRequest | ✅ Match |
| ClaimAccountRequest | ClaimAccountRequest | ✅ Match |
| VerifyTotpRequest | VerifyTotpRequest | ✅ Match |
| ValidateTotpRequest | ValidateTotpRequest | ✅ Match |
| MagicLinkRequest | MagicLinkRequest | ✅ Match |
| AuthorizeCallbackRequest | AuthorizeCallbackRequest | ✅ Match |
| RegisterClientRequest | RegisterClientRequest | ✅ Match |
| UpdateClientRequest | UpdateClientRequest | ✅ Match |
| RegisterClientFormRequest | RegisterClientFormRequest | ✅ Match |
| UpdateClientFormRequest | UpdateClientFormRequest | ✅ Match |

---

## ❌ MISSING - Request Models

The following request models from AuthController are **MISSING** in Experimental folder:

### 1. WebAuthnRegisterCompleteRequest
```csharp
public class WebAuthnRegisterCompleteRequest
{
    public required AuthenticatorAttestationRawResponse AttestationResponse { get; set; }
}
```

### 2. WebAuthnLoginOptionsRequest
```csharp
public class WebAuthnLoginOptionsRequest
{
    public required string Username { get; set; }
}
```

### 3. WebAuthnLoginCompleteRequest
```csharp
public class WebAuthnLoginCompleteRequest
{
    public required string Username { get; set; }
    public required AuthenticatorAssertionRawResponse AssertionResponse { get; set; }
}
```

### 4. WebAuthnValidateRequest
```csharp
public class WebAuthnValidateRequest
{
    public required string TempToken { get; set; }
    public required AuthenticatorAssertionRawResponse AssertionResponse { get; set; }
}
```

### 5. ValidateMagicLinkRequest
```csharp
public class ValidateMagicLinkRequest
{
    public required string Token { get; set; }
}
```

### 6. QrLoginRequest
```csharp
public class QrLoginRequest
{
    public required string Username { get; set; }
    public required string Token { get; set; }
}
```

### 7. DirectQrLoginRequest
```csharp
public class DirectQrLoginRequest
{
    public required string Token { get; set; }
    public required string DeviceType { get; set; }
    public bool IsDesktopLogin { get; set; }
}
```

### 8. TokenRequest (OAuth)
```csharp
public class TokenRequest
{
    public required string GrantType { get; set; }
    public string? Code { get; set; }
    public string? RefreshToken { get; set; }
    public required string ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }
}
```

### 9. LegacyLoginRequest (Avalonia UI)
```csharp
public class LegacyLoginRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
```

### 10. LegacyRegisterRequest (Avalonia UI)
```csharp
public class LegacyRegisterRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int Role { get; set; } = 0;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
}
```

---

## ✅ PARTIAL - Response Models

Some response models exist in `Experimental/Models/Responses/AuthResponses.cs`:

| AuthController Model | Experimental Model | Status |
|---------------------|-------------------|--------|
| ApiResponse<T> | ApiResponse<T> | ✅ Match |
| UserDto | UserDto | ✅ Match |
| LoginResponse | LoginResponse | ✅ Match |
| RegisterResponse | RegisterResponse | ✅ Match |
| TwoFactorResponse | TwoFactorResponse | ✅ Match |
| TotpSetupResponse | TotpSetupResponse | ✅ Match |
| MagicLinkResponse | MagicLinkResponse | ✅ Match |
| QrCodeResponse | QrCodeResponse | ✅ Match |
| ClientDto | ClientDto | ✅ Match |
| GetClientResponse | GetClientResponse | ✅ Match |
| ScopeDto | ScopeDto | ✅ Match |
| UserInfoResponse | UserInfoResponse | ✅ Match |

---

## ❌ MISSING - Response Models

The following response models from AuthController are **MISSING** in Experimental folder:

### 1. WebAuthnTwoFactorResponse
```csharp
public class WebAuthnTwoFactorResponse : TwoFactorResponse
{
    public AssertionOptions? Options { get; set; }
}
```

### 2. VerifyTotpResponse
```csharp
public class VerifyTotpResponse
{
    public bool Enabled { get; set; }
}
```

### 3. DisableTotpResponse
```csharp
public class DisableTotpResponse
{
    public bool Disabled { get; set; }
}
```

### 4. ValidateTotpResponse
```csharp
public class ValidateTotpResponse
{
    public string Token { get; set; } = string.Empty;
    public UserDto User { get; set; } = new UserDto();
}
```

### 5. WebAuthnRegisterOptionsResponse
```csharp
public class WebAuthnRegisterOptionsResponse
{
    public CredentialCreateOptions Options { get; set; } = new CredentialCreateOptions();
}
```

### 6. WebAuthnRegisterCompleteResponse
```csharp
public class WebAuthnRegisterCompleteResponse
{
    public bool Registered { get; set; }
}
```

### 7. WebAuthnLoginOptionsResponse
```csharp
public class WebAuthnLoginOptionsResponse
{
    public AssertionOptions Options { get; set; } = new AssertionOptions();
}
```

### 8. WebAuthnLoginCompleteResponse
```csharp
public class WebAuthnLoginCompleteResponse
{
    public string Token { get; set; } = string.Empty;
    public UserDto User { get; set; } = new UserDto();
}
```

### 9. WebAuthnValidateResponse
```csharp
public class WebAuthnValidateResponse
{
    public string Token { get; set; } = string.Empty;
    public UserDto User { get; set; } = new UserDto();
}
```

### 10. WebAuthnCredentialDto
```csharp
public class WebAuthnCredentialDto
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
```

### 11. WebAuthnCredentialsResponse
```csharp
public class WebAuthnCredentialsResponse
{
    public List<WebAuthnCredentialDto> Credentials { get; set; } = new List<WebAuthnCredentialDto>();
}
```

### 12. WebAuthnRemoveCredentialResponse
```csharp
public class WebAuthnRemoveCredentialResponse
{
    public bool Removed { get; set; }
}
```

### 13. ValidateMagicLinkResponse
```csharp
public class ValidateMagicLinkResponse
{
    public string Token { get; set; } = string.Empty;
    public UserDto User { get; set; } = new UserDto();
}
```

### 14. QrLoginResponse
```csharp
public class QrLoginResponse
{
    public string Token { get; set; } = string.Empty;
    public UserDto User { get; set; } = new UserDto();
}
```

### 15. DirectQrCodeResponse
```csharp
public class DirectQrCodeResponse
{
    public string QrCode { get; set; } = string.Empty;
    public string? RawData { get; set; }
}
```

### 16. DirectQrLoginResponse
```csharp
public class DirectQrLoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public UserDto User { get; set; } = new UserDto();
}
```

### 17. CheckQrLoginResponse
```csharp
public class CheckQrLoginResponse
{
    public bool Success { get; set; }
    public string? Token { get; set; }
}
```

### 18. TokenResponse (OAuth)
```csharp
public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public string? RefreshToken { get; set; }
    public string? IdToken { get; set; }
    public string Scope { get; set; } = string.Empty;
    public Dictionary<string, object>? Claims { get; set; }
}
```

### 19. RegisterClientResponse
```csharp
public class RegisterClientResponse
{
    public string ClientId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
```

### 20. UpdateClientResponse
```csharp
public class UpdateClientResponse
{
    public string ClientId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
```

### 21. DeleteClientResponse
```csharp
public class DeleteClientResponse
{
    public string ClientId { get; set; } = string.Empty;
    public bool Deleted { get; set; }
}
```

### 22. GetClientsResponse
```csharp
public class GetClientsResponse
{
    public List<ClientDto> Clients { get; set; } = new List<ClientDto>();
}
```

### 23. GetScopesResponse
```csharp
public class GetScopesResponse
{
    public List<ScopeDto> Scopes { get; set; } = new List<ScopeDto>();
}
```

---

## ❌ MISSING - Helper Classes

### 1. OpenIdConnectRequest
```csharp
public class OpenIdConnectRequest
{
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string ResponseType { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? Nonce { get; set; }
}
```

### 2. AuthorizationCodeData
```csharp
public class AuthorizationCodeData
{
    public uint UserId { get; set; }
    public string[] Scopes { get; set; } = Array.Empty<string>();
    public string RedirectUri { get; set; } = string.Empty;
}
```

---

## Summary

### Request Models
- ✅ Complete: 21/21 (100%)
- ❌ Missing: 0/21 (0%)

### Response Models
- ✅ Complete: 35/35 (100%)
- ❌ Missing: 0/35 (0%)

### Helper Classes
- ✅ Complete: 2/2 (100%)
- ❌ Missing: 0/2 (0%)

### Total
- ✅ Complete: 58/58 (100%)
- ❌ Missing: 0/58 (0%)

---

## ✅ Action Completed

All missing models have been created in the Experimental folder:

1. ✅ **Experimental/Models/Requests/WebAuthnRequests.cs** - All WebAuthn request models
2. ✅ **Experimental/Models/Requests/QrLoginRequests.cs** - All QR login request models
3. ✅ **Experimental/Models/Requests/OAuthRequests.cs** - OAuth token request models
4. ✅ **Experimental/Models/Requests/LegacyRequests.cs** - Legacy Avalonia UI request models
5. ✅ **Experimental/Models/Responses/WebAuthnResponses.cs** - All WebAuthn response models
6. ✅ **Experimental/Models/Responses/QrLoginResponses.cs** - All QR login response models
7. ✅ **Experimental/Models/Responses/TotpResponses.cs** - TOTP verification responses
8. ✅ **Experimental/Models/Responses/OAuthResponses.cs** - OAuth token response and client management responses
9. ✅ **Experimental/Models/Helpers/OAuthHelpers.cs** - OpenIdConnectRequest and AuthorizationCodeData

