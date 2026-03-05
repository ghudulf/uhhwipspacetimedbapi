namespace BRU_AVTOPARK.Models.Responses;

/// <summary>
/// OAuth 2.0 token response - standard OAuth token endpoint response.
/// </summary>
public sealed record TokenResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string TokenType { get; init; } = "Bearer";
    public int ExpiresIn { get; init; }
    public string? RefreshToken { get; init; }
    public string? IdToken { get; init; }
    public string Scope { get; init; } = string.Empty;
    public Dictionary<string, object>? Claims { get; init; }
}

/// <summary>
/// OIDC client registration response - confirms successful client creation.
/// </summary>
public sealed record RegisterClientResponse
{
    public string ClientId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// OIDC client update response - confirms successful client update.
/// </summary>
public sealed record UpdateClientResponse
{
    public string ClientId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// OIDC client deletion response - confirms successful client deletion.
/// </summary>
public sealed record DeleteClientResponse
{
    public string ClientId { get; init; } = string.Empty;
    public bool Deleted { get; init; }
}

/// <summary>
/// OIDC clients list response - returns all registered clients.
/// </summary>
public sealed record GetClientsResponse
{
    public List<ClientDto> Clients { get; init; } = [];
}

/// <summary>
/// OAuth scopes list response - returns all registered scopes.
/// </summary>
public sealed record GetScopesResponse
{
    public List<ScopeDto> Scopes { get; init; } = [];
}
