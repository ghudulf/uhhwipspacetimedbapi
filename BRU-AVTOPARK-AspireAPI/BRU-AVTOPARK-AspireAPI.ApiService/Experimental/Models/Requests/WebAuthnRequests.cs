using Fido2NetLib.Objects;
using System.ComponentModel.DataAnnotations;

namespace BRU_AVTOPARK.Models.Requests;

/// <summary>
/// WebAuthn registration completion request containing the attestation response from the authenticator.
/// </summary>
public sealed record WebAuthnRegisterCompleteRequest
{
    [Required]
    public required AuthenticatorAttestationRawResponse AttestationResponse { get; init; }
}

/// <summary>
/// WebAuthn login options request - initiates the WebAuthn login flow for a specific user.
/// </summary>
public sealed record WebAuthnLoginOptionsRequest
{
    [Required]
    public required string Username { get; init; }
}

/// <summary>
/// WebAuthn login completion request containing the assertion response from the authenticator.
/// </summary>
public sealed record WebAuthnLoginCompleteRequest
{
    [Required]
    public required string Username { get; init; }

    [Required]
    public required AuthenticatorAssertionRawResponse AssertionResponse { get; init; }
}

/// <summary>
/// WebAuthn 2FA validation request - validates WebAuthn assertion during 2FA step.
/// </summary>
public sealed record WebAuthnValidateRequest
{
    [Required]
    public required string TempToken { get; init; }

    [Required]
    public required AuthenticatorAssertionRawResponse AssertionResponse { get; init; }
}
