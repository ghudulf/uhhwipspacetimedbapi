using System.ComponentModel.DataAnnotations;

namespace BRU_AVTOPARK.Models.Requests;

/// <summary>
/// WebAuthn registration completion request containing the attestation response from the authenticator.
/// </summary>
public record WebAuthnRegisterCompleteRequest
{
    [Required]
    public required string AttestationResponse { get; init; }
}

/// <summary>
/// WebAuthn login options request - initiates the WebAuthn login flow for a specific user.
/// </summary>
public record WebAuthnLoginOptionsRequest
{
    [Required]
    public required string Username { get; init; }
}

/// <summary>
/// WebAuthn login completion request containing the assertion response from the authenticator.
/// </summary>
public record WebAuthnLoginCompleteRequest
{
    [Required]
    public required string Username { get; init; }

    [Required]
    public required string AssertionResponse { get; init; }
}

/// <summary>
/// WebAuthn 2FA validation request - validates WebAuthn assertion during 2FA step.
/// </summary>
public record WebAuthnValidateRequest
{
    [Required]
    public required string TempToken { get; init; }

    [Required]
    public required string AssertionResponse { get; init; }
}
