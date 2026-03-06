using System.ComponentModel.DataAnnotations;

namespace BRU_AVTOPARK.Models.Requests;

/// <summary>
/// Standard login request supporting both JSON API and form-based browser submissions.
/// </summary>
public record LoginRequest
{
    [Required(ErrorMessage = "Username is required")]
    [StringLength(100, MinimumLength = 2)]
    public required string Username { get; init; }

    [Required(ErrorMessage = "Password is required")]
    [StringLength(256, MinimumLength = 6)]
    public required string Password { get; init; }

    /// <summary>When true, bypasses 2FA verification (e.g., during re-authentication flows).</summary>
    public bool SkipTwoFactor { get; init; }
}

/// <summary>
/// Admin-only user registration request. Role defaults to standard user (0).
/// </summary>
public record RegisterRequest
{
    [Required(ErrorMessage = "Username is required")]
    [StringLength(100, MinimumLength = 2)]
    public required string Username { get; init; }

    [Required(ErrorMessage = "Password is required")]
    [StringLength(256, MinimumLength = 8)]
    public required string Password { get; init; }

    /// <summary>Role ID: 0=User, 1=Admin, 2=Manager, 3=Driver, 4=Conductor, 5=Dispatcher.</summary>
    [Range(0, 5)]
    public int Role { get; init; }

    [EmailAddress]
    public string? Email { get; init; }

    [Phone]
    public string? PhoneNumber { get; init; }
}

/// <summary>
/// Account claim request for reactivating dormant or guest accounts.
/// </summary>
public record ClaimAccountRequest
{
    [Required]
    public required string Username { get; init; }

    [Required]
    [StringLength(256, MinimumLength = 6)]
    public required string Password { get; init; }

    /// <summary>Whether to generate a new SpacetimeDB identity for the claimed account.</summary>
    public bool GenerateNewIdentity { get; init; } = true;
}

/// <summary>
/// Magic link request - only requires an email address.
/// </summary>
public record MagicLinkRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; init; }
}

/// <summary>
/// TOTP verification during initial setup (proves the user scanned the QR correctly).
/// </summary>
public record VerifyTotpRequest
{
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public required string Code { get; init; }

    [Required]
    public required string SecretKey { get; init; }
}

/// <summary>
/// TOTP validation during login 2FA step (uses temporary token from initial authentication).
/// </summary>
public record ValidateTotpRequest
{
    [Required]
    public required string TempToken { get; init; }

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public required string Code { get; init; }
}

/// <summary>
/// OAuth authorization callback - submitted when user logs in during an OIDC authorization flow.
/// </summary>
public record AuthorizeCallbackRequest
{
    [Required]
    public required string RequestId { get; init; }

    [Required]
    public required string Username { get; init; }

    [Required]
    public required string Password { get; init; }
}

/// <summary>
/// OIDC client registration (admin API).
/// </summary>
public record RegisterClientRequest
{
    [Required]
    public required string ClientId { get; init; }

    [Required]
    public required string ClientSecret { get; init; }

    [Required]
    public required string DisplayName { get; init; }

    public string[] RedirectUris { get; init; } = [];
    public string[] PostLogoutRedirectUris { get; init; } = [];
    public string[] AllowedScopes { get; init; } = [];
    public bool RequireConsent { get; init; }
}

/// <summary>
/// OIDC client update (admin API). All fields optional for partial updates.
/// </summary>
public record UpdateClientRequest
{
    public string? ClientSecret { get; init; }
    public string? DisplayName { get; init; }
    public string[]? RedirectUris { get; init; }
    public string[]? PostLogoutRedirectUris { get; init; }
    public string[]? AllowedScopes { get; init; }
    public bool? RequireConsent { get; init; }
}

/// <summary>
/// Form-based client registration (browser submissions use string fields for URIs/scopes).
/// </summary>
public record RegisterClientFormRequest
{
    [Required] public required string ClientId { get; init; }
    [Required] public required string ClientSecret { get; init; }
    [Required] public required string DisplayName { get; init; }

    /// <summary>Newline-separated redirect URIs (from textarea).</summary>
    public string RedirectUris { get; init; } = "";
    public string PostLogoutRedirectUris { get; init; } = "";
    public string AllowedScopes { get; init; } = "";
    public bool RequireConsent { get; init; }
    public string? Token { get; init; }
}

/// <summary>
/// Form-based client update request (browser submissions).
/// </summary>
public record UpdateClientFormRequest
{
    public string? Token { get; init; }
    [Required] public required string DisplayName { get; init; }
    public string? ClientSecret { get; init; }
    public string RedirectUris { get; init; } = "";
    public string PostLogoutRedirectUris { get; init; } = "";
    public string AllowedScopes { get; init; } = "";
    public bool RequireConsent { get; init; }
}
