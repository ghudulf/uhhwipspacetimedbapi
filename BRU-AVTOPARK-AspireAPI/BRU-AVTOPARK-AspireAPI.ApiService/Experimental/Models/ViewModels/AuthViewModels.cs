namespace BRU_AVTOPARK.Models.ViewModels;

/// <summary>
/// View model for the login page.
/// </summary>
public record LoginViewModel
{
    public string? Error { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// View model for the registration page.
/// </summary>
public record RegisterViewModel
{
    public string? Error { get; init; }
    public string? Message { get; init; }
    public int? AdminCheckAttempt { get; init; }
    public bool IsAdmin { get; init; }
}

/// <summary>
/// View model for the profile page.
/// </summary>
public record ProfileViewModel
{
    public required UserProfileViewModel User { get; init; }
    public bool TotpEnabled { get; init; }
    public bool WebAuthnEnabled { get; init; }
    public List<WebAuthnCredentialViewModel> WebAuthnCredentials { get; init; } = [];
    public List<RoleViewModel> Roles { get; init; } = [];
    public List<PermissionViewModel> Permissions { get; init; } = [];
    public bool IsAdmin => Roles.Any(r => r.LegacyRoleId == 1);
}

/// <summary>
/// User profile information for display.
/// </summary>
public record UserProfileViewModel
{
    public string UserId { get; init; } = string.Empty;
    public uint LegacyUserId { get; init; }
    public string Login { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool? EmailConfirmed { get; init; }
    public bool? PhoneNumberConfirmed { get; init; }
    public bool IsActive { get; init; }
    public string? Xuid { get; init; }
    public string AvatarUrl { get; init; } = "https://avatars.mds.yandex.net/get-yapic/0/0-0/islands-200";
}

/// <summary>
/// WebAuthn credential for display.
/// </summary>
public record WebAuthnCredentialViewModel
{
    public string Id { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// Role information for display.
/// </summary>
public record RoleViewModel
{
    public int LegacyRoleId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Priority { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// Permission information for display.
/// </summary>
public record PermissionViewModel
{
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

/// <summary>
/// View model for status messages (errors/success).
/// </summary>
public record StatusViewModel(string? Error, string? Message);

/// <summary>
/// View model for form fields (reusable partial).
/// </summary>
public record FormFieldViewModel(
    string Id,
    string Label,
    string Type = "text",
    string? Placeholder = null,
    string? Value = null,
    bool Required = false);

/// <summary>
/// View model for TOTP setup page.
/// </summary>
public record TotpSetupViewModel
{
    public string QrCodeUri { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
}

/// <summary>
/// View model for WebAuthn registration page.
/// </summary>
public record WebAuthnRegistrationViewModel
{
    public string OptionsJson { get; init; } = string.Empty;
}

/// <summary>
/// View model for magic link request page.
/// </summary>
public record MagicLinkViewModel
{
    public string? Error { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// View model for QR login page.
/// </summary>
public record QrLoginViewModel
{
    public string QrCodeBase64 { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
}

/// <summary>
/// View model for OAuth login page during authorization flow.
/// </summary>
public record OAuthLoginViewModel
{
    public string RequestId { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string[] Scopes { get; init; } = [];
    public string? Error { get; init; }
}

/// <summary>
/// View model for success page.
/// </summary>
public record SuccessViewModel
{
    public string Token { get; init; } = string.Empty;
    public string Message { get; init; } = "Login successful!";
}

/// <summary>
/// View model for error page.
/// </summary>
public record ErrorViewModel
{
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// View model for account claim page.
/// </summary>
public record ClaimAccountViewModel
{
    public string? Error { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// View model for OIDC clients list page.
/// </summary>
public record OidcClientsListViewModel
{
    public List<ClientViewModel> Clients { get; init; } = [];
    public string? Token { get; init; }
}

/// <summary>
/// Client information for display in lists.
/// </summary>
public record ClientViewModel
{
    public string ClientId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
}

/// <summary>
/// View model for OIDC scopes list page.
/// </summary>
public record OidcScopesListViewModel
{
    public List<ScopeViewModel> Scopes { get; init; } = [];
    public string? Token { get; init; }
}

/// <summary>
/// Scope information for display.
/// </summary>
public record ScopeViewModel
{
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string OidcId { get; init; } = string.Empty;
}

/// <summary>
/// View model for OIDC client details page.
/// </summary>
public record OidcClientDetailsViewModel
{
    public string ClientId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string[] RedirectUris { get; init; } = [];
    public string[] PostLogoutRedirectUris { get; init; } = [];
    public string[] AllowedScopes { get; init; } = [];
    public bool RequireConsent { get; init; }
    public string? Token { get; init; }
}

/// <summary>
/// View model for OIDC client form (create/edit).
/// </summary>
public record OidcClientFormViewModel
{
    public string? ClientId { get; init; }
    public string? DisplayName { get; init; }
    public string? ClientSecret { get; init; }
    public string RedirectUris { get; init; } = string.Empty;
    public string PostLogoutRedirectUris { get; init; } = string.Empty;
    public string AllowedScopes { get; init; } = string.Empty;
    public bool RequireConsent { get; init; }
    public string? Token { get; init; }
    public bool IsEdit => !string.IsNullOrEmpty(ClientId);
}

