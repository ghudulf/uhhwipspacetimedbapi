namespace BRU_AVTOPARK.Models.Responses;

/// <summary>
/// Standard API envelope for all JSON responses. Consistent shape allows
/// clients to deserialize any endpoint uniformly.
/// </summary>
public sealed record ApiResponse<T>
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
public sealed record UserDto
{
    public uint Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public int Role { get; init; }
}

/// <summary>Login success payload including JWT token and optional claims map.</summary>
public sealed record LoginResponse
{
    public string Token { get; init; } = string.Empty;
    public UserDto User { get; init; } = new();
    public Dictionary<string, object>? Claims { get; init; }
}

/// <summary>Registration success payload.</summary>
public sealed record RegisterResponse
{
    public UserDto User { get; init; } = new();
}

/// <summary>Returned when 2FA is required after initial password authentication.</summary>
public sealed record TwoFactorResponse
{
    public bool RequiresTwoFactor { get; init; }
    public string TwoFactorType { get; init; } = string.Empty;
    public string TempToken { get; init; } = string.Empty;
}

/// <summary>TOTP setup success with QR code URI and secret key for manual entry.</summary>
public sealed record TotpSetupResponse
{
    public string SecretKey { get; init; } = string.Empty;
    public string QrCodeUri { get; init; } = string.Empty;
}

/// <summary>Magic link sent confirmation.</summary>
public sealed record MagicLinkResponse
{
    public bool Sent { get; init; }
    public string Email { get; init; } = string.Empty;
}

/// <summary>QR code payload for QR login flow.</summary>
public sealed record QrCodeResponse
{
    public string QrCode { get; init; } = string.Empty;
    public string? RawData { get; init; }
}

/// <summary>OIDC client summary for list views.</summary>
public sealed record ClientDto
{
    public string? ClientId { get; init; }
    public string? DisplayName { get; init; }
}

/// <summary>Full OIDC client details for detail/edit views.</summary>
public sealed record GetClientResponse
{
    public string ClientId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string[] RedirectUris { get; init; } = [];
    public string[] PostLogoutRedirectUris { get; init; } = [];
    public string[] AllowedScopes { get; init; } = [];
    public bool RequireConsent { get; init; }
}

/// <summary>OAuth scope metadata for scope management views.</summary>
public sealed record ScopeDto
{
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string OidcId { get; init; } = string.Empty;
}

/// <summary>OpenID Connect UserInfo response matching OIDC standard claims.</summary>
public sealed record UserInfoResponse
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
