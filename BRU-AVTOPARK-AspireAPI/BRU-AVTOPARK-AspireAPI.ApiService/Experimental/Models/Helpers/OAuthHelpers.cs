namespace BRU_AVTOPARK.Models.Helpers;

/// <summary>
/// OpenID Connect authorization request parameters.
/// </summary>
public record OpenIdConnectRequest
{
    public string ClientId { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string ResponseType { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string? Nonce { get; init; }
}

/// <summary>
/// Authorization code data stored in cache during OAuth flow.
/// </summary>
public record AuthorizationCodeData
{
    public uint UserId { get; init; }
    public string[] Scopes { get; init; } = [];
    public string RedirectUri { get; init; } = string.Empty;
}
