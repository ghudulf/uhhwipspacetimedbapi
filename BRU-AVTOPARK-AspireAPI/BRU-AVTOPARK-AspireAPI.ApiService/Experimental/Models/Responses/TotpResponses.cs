namespace BRU_AVTOPARK.Models.Responses;

/// <summary>
/// TOTP verification response - confirms TOTP was successfully enabled.
/// </summary>
public sealed record VerifyTotpResponse
{
    public bool Enabled { get; init; }
}

/// <summary>
/// TOTP disable response - confirms TOTP was successfully disabled.
/// </summary>
public sealed record DisableTotpResponse
{
    public bool Disabled { get; init; }
}

/// <summary>
/// TOTP validation response - returns JWT token after successful TOTP 2FA validation.
/// </summary>
public sealed record ValidateTotpResponse
{
    public string Token { get; init; } = string.Empty;
    public UserDto User { get; init; } = new();
}
