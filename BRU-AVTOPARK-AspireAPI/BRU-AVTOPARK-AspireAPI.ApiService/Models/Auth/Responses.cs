using Fido2NetLib;
using Fido2NetLib.Objects;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Models.Auth
{
    // ── Generic Wrapper ─────────────────────────────────────────────────

    public sealed class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string>? Errors { get; set; }
        public T? Data { get; set; }
    }

    // ── User ────────────────────────────────────────────────────────────

    public sealed class UserDto
    {
        public uint Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public int Role { get; set; }
    }

    // ── Login / Registration ────────────────────────────────────────────

    public sealed class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new();
        public Dictionary<string, object>? Claims { get; set; }
    }

    public sealed class RegisterResponse
    {
        public UserDto User { get; set; } = new();
    }

    // ── Two-Factor ──────────────────────────────────────────────────────

    public class TwoFactorResponse
    {
        public bool RequiresTwoFactor { get; set; }
        public string TwoFactorType { get; set; } = string.Empty;
        public string TempToken { get; set; } = string.Empty;
    }

    public sealed class WebAuthnTwoFactorResponse : TwoFactorResponse
    {
        public AssertionOptions? Options { get; set; }
    }

    // ── TOTP ────────────────────────────────────────────────────────────

    public sealed class TotpSetupResponse
    {
        public string SecretKey { get; set; } = string.Empty;
        public string QrCodeUri { get; set; } = string.Empty;
    }

    public sealed class VerifyTotpResponse
    {
        public bool Enabled { get; set; }
    }

    public sealed class DisableTotpResponse
    {
        public bool Disabled { get; set; }
    }

    public sealed class ValidateTotpResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new();
    }

    // ── WebAuthn ────────────────────────────────────────────────────────

    public sealed class WebAuthnRegisterOptionsResponse
    {
        public CredentialCreateOptions Options { get; set; } = new();
    }

    public sealed class WebAuthnRegisterCompleteResponse
    {
        public bool Registered { get; set; }
    }

    public sealed class WebAuthnLoginOptionsResponse
    {
        public AssertionOptions Options { get; set; } = new();
    }

    public sealed class WebAuthnLoginCompleteResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new();
    }

    public sealed class WebAuthnValidateResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new();
    }

    public sealed class WebAuthnCredentialDto
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public sealed class WebAuthnCredentialsResponse
    {
        public List<WebAuthnCredentialDto> Credentials { get; set; } = new();
    }

    public sealed class WebAuthnRemoveCredentialResponse
    {
        public bool Removed { get; set; }
    }

    // ── Magic Link ──────────────────────────────────────────────────────

    public sealed class MagicLinkResponse
    {
        public bool Sent { get; set; }
        public string Email { get; set; } = string.Empty;
    }

    public sealed class ValidateMagicLinkResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new();
    }

    // ── QR Code ─────────────────────────────────────────────────────────

    public sealed class QrCodeResponse
    {
        public string QrCode { get; set; } = string.Empty;
        public string? RawData { get; set; }
    }

    public sealed class QrLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new();
    }

    public sealed class DirectQrCodeResponse
    {
        public string QrCode { get; set; } = string.Empty;
        public string? RawData { get; set; }
    }

    public sealed class DirectQrLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public UserDto User { get; set; } = new();
    }

    public sealed class CheckQrLoginResponse
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
    }

    // ── OpenID Connect ──────────────────────────────────────────────────

    public sealed class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "Bearer";
        public int ExpiresIn { get; set; }
        public string? RefreshToken { get; set; }
        public string? IdToken { get; set; }
        public string Scope { get; set; } = string.Empty;
        public Dictionary<string, object>? Claims { get; set; }
    }

    public sealed class UserInfoResponse
    {
        public string Sub { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PreferredUsername { get; set; } = string.Empty;
        public string? Email { get; set; }
        public bool EmailVerified { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneNumberVerified { get; set; }
        public List<string> Roles { get; set; } = new();
    }

    public sealed class RegisterClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public sealed class UpdateClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public sealed class DeleteClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public bool Deleted { get; set; }
    }

    public sealed class ClientDto
    {
        public string? ClientId { get; set; }
        public string? DisplayName { get; set; }
    }

    public sealed class GetClientsResponse
    {
        public List<ClientDto> Clients { get; set; } = new();
    }

    public sealed class GetClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string[] RedirectUris { get; set; } = Array.Empty<string>();
        public string[] PostLogoutRedirectUris { get; set; } = Array.Empty<string>();
        public string[] AllowedScopes { get; set; } = Array.Empty<string>();
        public bool RequireConsent { get; set; }
    }

    public sealed class ScopeDto
    {
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string OidcId { get; set; } = string.Empty;
    }

    public sealed class GetScopesResponse
    {
        public List<ScopeDto> Scopes { get; set; } = new();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public sealed class OpenIdConnectRequestModel
    {
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string ResponseType { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? Nonce { get; set; }
    }

    public sealed class AuthorizationCodeData
    {
        public uint UserId { get; set; }
        public string[] Scopes { get; set; } = Array.Empty<string>();
        public string RedirectUri { get; set; } = string.Empty;
    }
}
