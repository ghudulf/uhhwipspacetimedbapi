using Fido2NetLib;
using Fido2NetLib.Objects;

namespace BRU_AVTOPARK.Models.Responses;

/// <summary>
/// WebAuthn 2FA response - extends TwoFactorResponse with WebAuthn assertion options.
/// </summary>
public record WebAuthnTwoFactorResponse : TwoFactorResponse
{
    public AssertionOptions? Options { get; init; }
}

/// <summary>
/// WebAuthn registration options response - contains credential creation options for the client.
/// </summary>
public record WebAuthnRegisterOptionsResponse
{
    public CredentialCreateOptions Options { get; init; } = new();
}

/// <summary>
/// WebAuthn registration completion response - confirms successful credential registration.
/// </summary>
public record WebAuthnRegisterCompleteResponse
{
    public bool Registered { get; init; }
}

/// <summary>
/// WebAuthn login options response - contains assertion options for authentication.
/// </summary>
public record WebAuthnLoginOptionsResponse
{
    public AssertionOptions Options { get; init; } = new();
}

/// <summary>
/// WebAuthn login completion response - returns JWT token after successful authentication.
/// </summary>
public record WebAuthnLoginCompleteResponse
{
    public string Token { get; init; } = string.Empty;
    public UserDto User { get; init; } = new();
}

/// <summary>
/// WebAuthn 2FA validation response - returns JWT token after successful 2FA validation.
/// </summary>
public record WebAuthnValidateResponse
{
    public string Token { get; init; } = string.Empty;
    public UserDto User { get; init; } = new();
}

/// <summary>
/// WebAuthn credential DTO for listing user's registered credentials.
/// </summary>
public record WebAuthnCredentialDto
{
    public string Id { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// WebAuthn credentials list response - returns all credentials for a user.
/// </summary>
public record WebAuthnCredentialsResponse
{
    public List<WebAuthnCredentialDto> Credentials { get; init; } = [];
}

/// <summary>
/// WebAuthn credential removal response - confirms successful credential deletion.
/// </summary>
public record WebAuthnRemoveCredentialResponse
{
    public bool Removed { get; init; }
}
