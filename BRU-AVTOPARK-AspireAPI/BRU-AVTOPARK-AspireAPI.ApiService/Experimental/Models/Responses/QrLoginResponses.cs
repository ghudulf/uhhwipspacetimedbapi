namespace BRU_AVTOPARK.Models.Responses;

/// <summary>
/// QR login response - returns JWT token after successful QR code authentication.
/// </summary>
public sealed record QrLoginResponse
{
    public string Token { get; init; } = string.Empty;
    public UserDto User { get; init; } = new();
}

/// <summary>
/// Direct QR code response - contains QR code data for display.
/// </summary>
public sealed record DirectQrCodeResponse
{
    public string QrCode { get; init; } = string.Empty;
    public string? RawData { get; init; }
}

/// <summary>
/// Direct QR login response - returns JWT token and device ID after successful authentication.
/// </summary>
public sealed record DirectQrLoginResponse
{
    public string Token { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public UserDto User { get; init; } = new();
}

/// <summary>
/// Check QR login response - polls for QR code scan status.
/// </summary>
public sealed record CheckQrLoginResponse
{
    public bool Success { get; init; }
    public string? Token { get; init; }
}

/// <summary>
/// Magic link validation response - returns JWT token after successful magic link validation.
/// </summary>
public sealed record ValidateMagicLinkResponse
{
    public string Token { get; init; } = string.Empty;
    public UserDto User { get; init; } = new();
}
