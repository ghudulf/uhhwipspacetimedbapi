using System.ComponentModel.DataAnnotations;

namespace BRU_AVTOPARK.Models.Requests;

/// <summary>
/// OAuth 2.0 token request - supports authorization_code and refresh_token grant types.
/// </summary>
public sealed record TokenRequest
{
    [Required]
    public required string GrantType { get; init; }

    public string? Code { get; init; }
    public string? RefreshToken { get; init; }

    [Required]
    public required string ClientId { get; init; }

    public string? ClientSecret { get; init; }
    public string? RedirectUri { get; init; }
}
