using System.Collections.Generic;
using System.Threading.Tasks;
using Fido2NetLib;
using Fido2NetLib.Objects;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for handling WebAuthn (Web Authentication) operations
    /// </summary>
    public interface IWebAuthnService
    {
        Task<UserProfile?> FindActiveUserAsync(string username);
        Task<byte[]> EnsureUserHandleAsync(Identity actor);
        Task<CredentialCreateOptions> CreateRegistrationOptionsAsync(Identity actor);
        Task<AssertionOptions> CreateAssertionOptionsAsync(string? username);
        Task VerifyRegistrationAsync(Identity actor, CredentialCreateOptions options, AuthenticatorAttestationRawResponse response);
        Task<UserProfile> VerifyAssertionAsync(AssertionOptions options, AuthenticatorAssertionRawResponse response, Identity? expectedActor = null);
        Task<(bool success, CredentialCreateOptions? options, string? errorMessage)> GetCredentialCreateOptionsAsync(Identity userId, string username);
        Task<(bool success, string? errorMessage)> CompleteRegistrationAsync(Identity userId, string username, AuthenticatorAttestationRawResponse attestationResponse);
        Task<(bool success, AssertionOptions? options, string? errorMessage)> GetAssertionOptionsAsync(string username);
        Task<(bool success, UserProfile? user, string? errorMessage)> CompleteAssertionAsync(string username, AuthenticatorAssertionRawResponse assertionResponse);
        Task<(bool success, string? errorMessage)> RemoveCredentialAsync(Identity userId, string credentialId);
        Task<List<WebAuthnCredential>> GetUserCredentialsAsync(Identity userId);
        Task<bool> IsWebAuthnEnabledAsync(Identity userId);
    }
}
