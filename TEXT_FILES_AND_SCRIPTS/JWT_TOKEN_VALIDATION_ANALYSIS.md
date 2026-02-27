# JWT Token Validation and Authorization Issues - Root Cause Analysis

## Executive Summary
The system is experiencing `IDX10618: Key unwrap failed` errors due to **encryption key mismatches** between the API service and token validation layers. Additionally, authorization failures are occurring because permission claims are not being properly propagated through the OAuth/OpenIddict flow.

---

## Issue 1: JWT Secret Key Mismatch

### Root Cause
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/appsettings.json`

```json
"JwtSettings": {
    "Secret": "vYr8zF2kX9pN3mJ5hA7dC1wE4iL6gQ8sBnM0tU3jR4cK",  // 44 characters
    "ExpirationInMinutes": 120
}
```

**Problem**: The secret is 44 characters (352 bits), but the code in `Program.cs` (lines 163-175) pads/truncates it to exactly 32 bytes (256 bits):

```csharp
var key = Encoding.UTF8.GetBytes(jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT secret is not configured"));

// Ensure key is exactly 32 bytes (256 bits)
if (key.Length != 32)
{
    var newKey = new byte[32];
    if (key.Length < 32)
    {
        Array.Copy(key, newKey, key.Length);  // Pads with zeros
    }
    else
    {
        Array.Copy(key, newKey, 32);  // Truncates
    }
    key = newKey;
}
```

**Impact**: 
- The 44-character secret gets truncated to 32 bytes
- Any token signed with the full 44-character secret cannot be validated
- This causes `IDX10618: Key unwrap failed` when OpenIddict tries to decrypt tokens

### Solution
Use a proper 32-byte (256-bit) secret from the start. The current secret when UTF-8 encoded is 44 bytes, which gets truncated.

**Recommended Fix**:
```json
"JwtSettings": {
    "Secret": "your-32-byte-secret-key-exactly-256bits",
    "ExpirationInMinutes": 120
}
```

Generate a proper 32-byte secret:
```csharp
// Generate a cryptographically secure 32-byte key
using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
{
    var keyBytes = new byte[32];
    rng.GetBytes(keyBytes);
    var base64Key = Convert.ToBase64String(keyBytes);
    // Use this base64Key in appsettings.json
}
```

---

## Issue 2: Encryption Key Configuration in OpenIddict

### Root Cause
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Program.cs` (lines 236-237)

The same symmetric key is used for both signing AND encryption:

```csharp
// Add symmetric signing key for access tokens, authorization codes, and refresh tokens
options.AddSigningKey(symmetricKey);

// Add encryption key
options.AddEncryptionKey(symmetricKey);
```

**Problem**: 
- OpenIddict expects separate keys for signing and encryption in production
- The key is being truncated/padded inconsistently
- Data Protection API keys are stored in `DataProtectionKeys` folder but might not be persisting correctly

### Solution
Use separate keys for signing and encryption, or ensure the key is properly sized from the start.

---

## Issue 3: Authorization Claims Not Propagated

### Root Cause
**Files**: 
- `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/AuthController.cs`
- `BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/Implementations/TokenStore.cs`

**Problem**:
1. The OAuth flow creates tokens but doesn't include permission/role claims
2. The `TokenStore.MapToDescriptor()` method doesn't restore all properties needed for authorization
3. Permission checks in controllers fail because claims are missing

### Evidence
From logs:
- `403 Forbidden` on endpoints requiring `users.view`, `employees.view`, `permissions.view`, `roles.view`
- These are permission claims that should be in the JWT but are missing

### Solution
Ensure that when tokens are created, they include:
1. Role claims
2. Permission claims
3. Scope claims

---

## Issue 4: PKCE Data Loss in Token Properties

### Root Cause
**File**: `BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/Implementations/TokenStore.cs` (lines 50-100)

The `MapToDescriptor()` method uses reflection to restore properties, but:
1. Properties might not be fully serialized/deserialized
2. PKCE data (code_challenge, code_challenge_method) might be lost
3. The bidirectional cache might have stale entries

### Solution
Ensure properties are properly persisted and restored with validation.

---

## Issue 5: Client-Side Token Handling

### Root Cause
**File**: `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity/Services/ApiClientService.cs`

The client correctly sets the Authorization header:
```csharp
if (!string.IsNullOrEmpty(_authToken))
{
    client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", _authToken);
}
```

**Problem**: 
- If the token is invalid or expired, the server returns 403
- The client doesn't have a mechanism to refresh tokens automatically on 403
- No retry logic for failed requests

### Solution
Implement automatic token refresh on 401/403 responses.

---

## Performance Issues

### Root Cause
Some API requests taking 10+ seconds:

1. **TokenStore polling** (lines 280-310): Waits up to 5 seconds for token confirmation
2. **SpacetimeDB queries**: Multiple sequential queries instead of batch operations
3. **No caching**: Permission checks query database every time

### Solution
1. Reduce polling timeout or use events instead
2. Batch database queries
3. Implement permission caching with TTL

---

## Recommended Fix Priority

### Priority 1 (Critical - Blocks Authentication)
1. Fix JWT secret key to be exactly 32 bytes
2. Ensure consistent key usage across signing and encryption
3. Verify Data Protection keys are persisting

### Priority 2 (High - Blocks Authorization)
1. Ensure permission/role claims are included in tokens
2. Fix token property serialization/deserialization
3. Implement proper claim restoration in TokenStore

### Priority 3 (Medium - Improves UX)
1. Add automatic token refresh on 401/403
2. Implement permission caching
3. Optimize database queries

### Priority 4 (Low - Performance)
1. Reduce TokenStore polling timeout
2. Batch SpacetimeDB operations
3. Add request caching

---

## Testing Checklist

- [ ] Generate a proper 32-byte JWT secret
- [ ] Update appsettings.json with new secret
- [ ] Verify token creation succeeds
- [ ] Verify token validation succeeds
- [ ] Verify permission claims are in token
- [ ] Verify 403 errors are resolved
- [ ] Verify API response times are under 2 seconds
- [ ] Test token refresh flow
- [ ] Test logout and re-login
