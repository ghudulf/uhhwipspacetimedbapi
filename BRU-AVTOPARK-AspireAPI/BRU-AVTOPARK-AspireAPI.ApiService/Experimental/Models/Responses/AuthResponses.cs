using Fido2NetLib;
using Fido2NetLib.Objects;

namespace BRU_AVTOPARK.Models.Responses;

/// <summary>
/// Standard API envelope for all JSON responses. Consistent shape allows
/// clients to deserialize any endpoint uniformly.
/// </summary>
public record ApiResponse<T>
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public List<string>? Errors { get; init; }
    public T? Data { get; init; }

    /// <summary>Factory for success responses.</summary>
    public static ApiResponse<T> Ok(T data, string message = "") =>
        new() { Success = true, Data = data, Message = message };

    /// <summary>Factory for failure responses.</summary>
    public static ApiResponse<T> Fail(string message, List<string>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors };
}

/// <summary>Lightweight user DTO returned in login/register responses.</summary>
public record UserDto
{
    public uint Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public int Role { get; init; }
}

/// <summary>Login success payload including JWT token and optional claims map.</summary>
public record LoginResponse
{
    public string Token { get; init; } = string.Empty;
    public UserDto User { get; init; } = new();
    public Dictionary<string, object>? Claims { get; init; }
}

/// <summary>Registration success payload.</summary>
public record RegisterResponse
{
    public UserDto User { get; init; } = new();
}

/// <summary>Returned when 2FA is required after initial password authentication.</summary>
public record TwoFactorResponse
{
    public bool RequiresTwoFactor { get; init; }
    public string? TwoFactorType { get; init; }
    public string? TempToken { get; init; }
}

/// <summary>TOTP setup success with QR code URI and secret key for manual entry.</summary>
public record TotpSetupResponse
{
    public string SecretKey { get; init; } = string.Empty;
    public string QrCodeUri { get; init; } = string.Empty;
}

/// <summary>Magic link sent confirmation.</summary>
public record MagicLinkResponse
{
    public bool Sent { get; init; }
    public string Email { get; init; } = string.Empty;
}

/// <summary>QR code payload for QR login flow.</summary>
public record QrCodeResponse
{
    public string QrCode { get; init; } = string.Empty;
    public string? RawData { get; init; }
}

/// <summary>OIDC client summary for list views.</summary>
public record ClientDto
{
    public string? ClientId { get; init; }
    public string? DisplayName { get; init; }
}

/// <summary>Full OIDC client details for detail/edit views.</summary>
public record GetClientResponse
{
    public string ClientId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string[] RedirectUris { get; init; } = [];
    public string[] PostLogoutRedirectUris { get; init; } = [];
    public string[] AllowedScopes { get; init; } = [];
    public bool RequireConsent { get; init; }
}

/// <summary>OAuth scope metadata for scope management views.</summary>
public record ScopeDto
{
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string OidcId { get; init; } = string.Empty;
}

/// <summary>OpenID Connect UserInfo response matching OIDC standard claims.</summary>
public record UserInfoResponse
{
    public string Sub { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string PreferredUsername { get; init; } = string.Empty;
    public string? Email { get; init; }
    public bool EmailVerified { get; init; }
    public string? PhoneNumber { get; init; }
    public bool PhoneNumberVerified { get; init; }
    public List<string> Roles { get; init; } = [];
}

/// <summary>QR login response with token and QR code data.</summary>
public record QRLoginResponse
{
    public string Token { get; init; } = string.Empty;
    public string QrCodeData { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public Dictionary<string, object>? Claims { get; init; }
}

/// <summary>QR login status response.</summary>
public record QRLoginStatusResponse
{
    public string Status { get; init; } = string.Empty;
    public string? Token { get; init; }
    public UserDto? User { get; init; }
}

/// <summary>Refresh token response.</summary>
public record RefreshTokenResponse
{
    public string Token { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}

/// <summary>User settings DTO.</summary>
public record UserSettingsDto
{
    public bool TotpEnabled { get; init; }
    public bool WebAuthnEnabled { get; init; }
    public bool EmailNotifications { get; init; }
    public bool SmsNotifications { get; init; }
}

/// <summary>Auth status response.</summary>
public record AuthStatusResponse
{
    public bool IsAuthenticated { get; init; }
    public UserDto? User { get; init; }
    public string? Username { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

/// <summary>OAuth scope DTO for scope management views.</summary>
public record OAuthScopeDto
{
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
}

/// <summary>OAuth client secret response.</summary>
public record OAuthClientSecretDto
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
}
