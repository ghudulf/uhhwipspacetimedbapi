using Fido2NetLib;
using Fido2NetLib.Objects;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Models.Auth
{
    // ── Login / Registration ────────────────────────────────────────────

    public sealed class LoginRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public bool SkipTwoFactor { get; set; }
    }

    public sealed class RegisterRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public int Role { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
    }

    public sealed class ClaimAccountRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public bool GenerateNewIdentity { get; set; } = true;
    }

    // ── TOTP ────────────────────────────────────────────────────────────

    public sealed class VerifyTotpRequest
    {
        public required string Code { get; set; }
        public required string SecretKey { get; set; }
    }

    public sealed class ValidateTotpRequest
    {
        public required string TempToken { get; set; }
        public required string Code { get; set; }
    }

    // ── WebAuthn ────────────────────────────────────────────────────────

    public sealed class WebAuthnRegisterCompleteRequest
    {
        public required AuthenticatorAttestationRawResponse AttestationResponse { get; set; }
    }

    public sealed class WebAuthnLoginOptionsRequest
    {
        public required string Username { get; set; }
    }

    public sealed class WebAuthnLoginCompleteRequest
    {
        public required string Username { get; set; }
        public required AuthenticatorAssertionRawResponse AssertionResponse { get; set; }
    }

    public sealed class WebAuthnValidateRequest
    {
        public required string TempToken { get; set; }
        public required AuthenticatorAssertionRawResponse AssertionResponse { get; set; }
    }

    // ── Magic Link ──────────────────────────────────────────────────────

    public sealed class MagicLinkRequest
    {
        public required string Email { get; set; }
    }

    public sealed class ValidateMagicLinkRequest
    {
        public required string Token { get; set; }
    }

    // ── QR Code ─────────────────────────────────────────────────────────

    public sealed class QrLoginRequest
    {
        public required string Username { get; set; }
        public required string Token { get; set; }
    }

    public sealed class DirectQrLoginRequest
    {
        public required string Token { get; set; }
        public required string DeviceType { get; set; }
        public bool IsDesktopLogin { get; set; }
    }

    // ── OpenID Connect ──────────────────────────────────────────────────

    public sealed class TokenRequest
    {
        public required string GrantType { get; set; }
        public string? Code { get; set; }
        public string? RefreshToken { get; set; }
        public required string ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? RedirectUri { get; set; }
    }

    public sealed class AuthorizeCallbackRequest
    {
        public required string RequestId { get; set; }
        public required string Username { get; set; }
        public required string Password { get; set; }
    }

    public sealed class RegisterClientRequest
    {
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
        public required string DisplayName { get; set; }
        public required string[] RedirectUris { get; set; }
        public required string[] PostLogoutRedirectUris { get; set; }
        public required string[] AllowedScopes { get; set; }
        public bool RequireConsent { get; set; }
    }

    public sealed class UpdateClientRequest
    {
        public string? ClientSecret { get; set; }
        public string? DisplayName { get; set; }
        public string[]? RedirectUris { get; set; }
        public string[]? PostLogoutRedirectUris { get; set; }
        public string[]? AllowedScopes { get; set; }
        public bool? RequireConsent { get; set; }
    }

    public sealed class RegisterClientFormRequest
    {
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
        public required string DisplayName { get; set; }
        public string? RedirectUris { get; set; }
        public string? PostLogoutRedirectUris { get; set; }
        public string? AllowedScopes { get; set; }
        public bool RequireConsent { get; set; }
    }

    public sealed class UpdateClientFormRequest
    {
        public string? ClientSecret { get; set; }
        public string? DisplayName { get; set; }
        public string? RedirectUris { get; set; }
        public string? PostLogoutRedirectUris { get; set; }
        public string? AllowedScopes { get; set; }
        public bool? RequireConsent { get; set; }
    }
}
