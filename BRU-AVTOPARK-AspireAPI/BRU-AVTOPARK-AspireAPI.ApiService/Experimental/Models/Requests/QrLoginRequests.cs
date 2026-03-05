using System.ComponentModel.DataAnnotations;

namespace BRU_AVTOPARK.Models.Requests;

/// <summary>
/// QR login request - authenticates a user via QR code scan.
/// </summary>
public sealed record QrLoginRequest
{
    [Required]
    public required string Username { get; init; }

    [Required]
    public required string Token { get; init; }
}

/// <summary>
/// Direct QR login request - used for desktop/mobile QR code authentication flows.
/// </summary>
public sealed record DirectQrLoginRequest
{
    [Required]
    public required string Token { get; init; }

    [Required]
    public required string DeviceType { get; init; }

    public bool IsDesktopLogin { get; init; }
}

/// <summary>
/// Magic link validation request - validates a magic link token sent via email.
/// </summary>
public sealed record ValidateMagicLinkRequest
{
    [Required]
    public required string Token { get; init; }
}
