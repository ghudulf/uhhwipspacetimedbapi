using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Diagnostics;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

/// <summary>Shared legacy/browser verifier and committed credential storage. Never logs ceremony payloads.</summary>
public sealed class WebAuthnService(ISpacetimeDBService database, IFido2 fido2, ILogger<WebAuthnService> logger) : IWebAuthnService
{
    private void Failure(Exception ex) => logger.LogWarning("WebAuthn operation rejected; failure type {FailureType}", ex.GetType().Name);
    public Task<UserProfile?> FindActiveUserAsync(string username) => database.ReadAsync(c => c.Db.UserProfile.Iter()
        .FirstOrDefault(user => user.IsActive && string.Equals(user.Login, username, StringComparison.OrdinalIgnoreCase)));
    private async Task<UserProfile> Active(Identity actor) => await database.ReadAsync(c => c.Db.UserProfile.UserId.Find(actor)) is { IsActive: true } user
        ? user : throw new InvalidOperationException("Active account required.");
    public Task<List<WebAuthnCredential>> GetUserCredentialsAsync(Identity userId) => database.ReadAsync(c => c.Db.WebAuthnCredential.Iter().Where(credential => credential.UserId == userId && credential.IsActive).ToList());
    public async Task<byte[]> EnsureUserHandleAsync(Identity actor)
    {
        await database.ExecuteReducerAsync(new Reducer.EnsureWebAuthnUserHandle(actor), _ => true);
        return await database.ReadAsync(c => c.Db.WebAuthnUserHandle.UserId.Find(actor)?.Handle.ToArray() ?? throw new InvalidOperationException("Handle missing after commit."));
    }
    public async Task<CredentialCreateOptions> CreateRegistrationOptionsAsync(Identity actor)
    {
        _ = await Active(actor);
        var handle = await EnsureUserHandleAsync(actor);
        var credentials = await GetUserCredentialsAsync(actor);
        // Opaque labels avoid sending usernames/emails/identities to the browser or authenticator.
        return fido2.RequestNewCredential(new Fido2User { Id = handle, Name = "Ключ доступа", DisplayName = "БРУ Автопарк" },
            credentials.Select(credential => new PublicKeyCredentialDescriptor(credential.CredentialId.ToArray())).ToList(),
            // Fido2 3.0.1 exposes only the legacy resident-key boolean. The browser adapter
            // adds residentKey:"preferred" while leaving requireResidentKey false.
            new AuthenticatorSelection { RequireResidentKey = false, UserVerification = UserVerificationRequirement.Required },
            AttestationConveyancePreference.None, new AuthenticationExtensionsClientInputs());
    }
    public async Task<AssertionOptions> CreateAssertionOptionsAsync(string? username)
    {
        var user = username == null ? null : await FindActiveUserAsync(username);
        var credentials = user == null ? [] : await GetUserCredentialsAsync(user.UserId);
        return fido2.GetAssertionOptions(credentials.Select(credential => new PublicKeyCredentialDescriptor(credential.CredentialId.ToArray())).ToList(),
            UserVerificationRequirement.Required, new AuthenticationExtensionsClientInputs());
    }
    public async Task VerifyRegistrationAsync(Identity actor, CredentialCreateOptions options, AuthenticatorAttestationRawResponse response)
    {
        _ = await Active(actor);
        var handle = await EnsureUserHandleAsync(actor);
        if (!options.User.Id.SequenceEqual(handle) || options.AuthenticatorSelection.UserVerification != UserVerificationRequirement.Required)
            throw new InvalidOperationException("Registration binding rejected.");
        var verified = await fido2.MakeNewCredentialAsync(response, options, async (args, _) =>
            !await database.ReadAsync(c => c.Db.WebAuthnCredential.Iter().Any(credential => credential.CredentialId.SequenceEqual(args.CredentialId))));
        var credential = verified.Result ?? throw new InvalidOperationException("Registration verification returned no credential.");
        await database.ExecuteReducerAsync(new Reducer.RegisterWebAuthnCredential(actor, credential.CredentialId.ToList(),
            Convert.ToBase64String(credential.PublicKey), credential.Counter, "Ключ доступа"), _ => true);
        await database.ExecuteReducerAsync(new Reducer.EnableWebAuthn(actor), _ => true);
    }
    public async Task<UserProfile> VerifyAssertionAsync(AssertionOptions options, AuthenticatorAssertionRawResponse response, Identity? expectedActor = null)
    {
        if (options.UserVerification != UserVerificationRequirement.Required) throw new InvalidOperationException("User verification required.");
        var credential = await database.ReadAsync(c =>
        {
            // Historical rows may predate reducer uniqueness checks; never choose an arbitrary owner.
            var matches = c.Db.WebAuthnCredential.Iter().Where(item => item.IsActive && item.CredentialId.SequenceEqual(response.Id)).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        })
            ?? throw new InvalidOperationException("Assertion rejected.");
        var user = await Active(credential.UserId);
        if (expectedActor != null && user.UserId != expectedActor) throw new InvalidOperationException("Assertion rejected.");
        try
        {
            var result = await fido2.MakeAssertionAsync(response, options, Convert.FromBase64String(credential.PublicKey), credential.Counter,
                async (args, _) => await database.ReadAsync(c => c.Db.WebAuthnUserHandle.UserId.Find(credential.UserId) is { } handle &&
                    handle.Handle.SequenceEqual(args.UserHandle) && credential.CredentialId.SequenceEqual(args.CredentialId)));
            // The module independently rejects a stale/nonmonotonic writer, even if two verifiers raced.
            await database.ExecuteReducerAsync(new Reducer.UpdateWebAuthnCounter(credential.CredentialId.ToList(), result.Counter), _ => true);
            _ = await Active(user.UserId);
            return user;
        }
        catch
        {
            logger.LogWarning("WebAuthn assertion/counter rejected; credential {CredentialFingerprint}", SafeDiagnostics.Fingerprint(Convert.ToBase64String(credential.CredentialId.ToArray())));
            throw;
        }
    }
    private async Task<WebAuthnChallenge> Challenge(Identity actor) => await database.ReadAsync(c => c.Db.WebAuthnChallenge.Iter()
        .Where(item => item.UserId == actor && item.ExpiresAt > (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).OrderByDescending(item => item.Id).FirstOrDefault())
        ?? throw new InvalidOperationException("Challenge unavailable.");
    private static byte[] DecodeChallenge(string value) => Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(value);
    public async Task<(bool success, CredentialCreateOptions? options, string? errorMessage)> GetCredentialCreateOptionsAsync(Identity userId, string username)
    {
        try
        {
            var options = await CreateRegistrationOptionsAsync(userId);
            await database.ExecuteReducerAsync(new Reducer.StoreWebAuthnChallenge(userId, Convert.ToBase64String(options.Challenge),
                (ulong)DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()), _ => true);
            return (true, options, null);
        }
        catch (Exception ex) { Failure(ex); return (false, null, "WebAuthn registration unavailable"); }
    }
    public async Task<(bool success, string? errorMessage)> CompleteRegistrationAsync(Identity userId, string username, AuthenticatorAttestationRawResponse response)
    {
        try
        {
            var challenge = await Challenge(userId);
            var options = await CreateRegistrationOptionsAsync(userId);
            options.Challenge = DecodeChallenge(challenge.Challenge);
            // Committed consume before verification prevents replay, including concurrent legacy submissions.
            await database.ExecuteReducerAsync(new Reducer.DeleteWebAuthnChallenge(challenge.Id), _ => true);
            await VerifyRegistrationAsync(userId, options, response);
            return (true, null);
        }
        catch (Exception ex) { Failure(ex); return (false, "WebAuthn registration rejected"); }
    }
    public async Task<(bool success, AssertionOptions? options, string? errorMessage)> GetAssertionOptionsAsync(string username)
    {
        try
        {
            var user = await FindActiveUserAsync(username);
            var options = await CreateAssertionOptionsAsync(username);
            if (user != null) await database.ExecuteReducerAsync(new Reducer.StoreWebAuthnChallenge(user.UserId, Convert.ToBase64String(options.Challenge),
                (ulong)DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()), _ => true);
            return (true, options, null);
        }
        catch (Exception ex) { Failure(ex); return (false, null, "WebAuthn unavailable"); }
    }
    public async Task<(bool success, UserProfile? user, string? errorMessage)> CompleteAssertionAsync(string username, AuthenticatorAssertionRawResponse response)
    {
        try
        {
            var actor = await FindActiveUserAsync(username) ?? throw new InvalidOperationException("Assertion rejected.");
            var challenge = await Challenge(actor.UserId);
            var options = await CreateAssertionOptionsAsync(username);
            options.Challenge = DecodeChallenge(challenge.Challenge);
            await database.ExecuteReducerAsync(new Reducer.DeleteWebAuthnChallenge(challenge.Id), _ => true);
            return (true, await VerifyAssertionAsync(options, response, actor.UserId), null);
        }
        catch (Exception ex) { Failure(ex); return (false, null, "WebAuthn assertion rejected"); }
    }
    public async Task<(bool success, string? errorMessage)> RemoveCredentialAsync(Identity userId, string credentialId)
    {
        try
        {
            // URL-safe IDs avoid encoded '/' route ambiguity; retain standard Base64 compatibility.
            var id = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(credentialId);
            var credential = await database.ReadAsync(c => c.Db.WebAuthnCredential.Iter().FirstOrDefault(item => item.UserId == userId && item.IsActive && item.CredentialId.SequenceEqual(id)))
                ?? throw new InvalidOperationException("Own credential required.");
            await database.ExecuteReducerAsync(new Reducer.RemoveOwnWebAuthnCredential(credential.Id, userId), _ => true);
            return (true, null);
        }
        catch (Exception ex) { Failure(ex); return (false, "WebAuthn removal rejected"); }
    }
    public Task<bool> IsWebAuthnEnabledAsync(Identity userId) => database.ReadAsync(c => c.Db.UserSettings.Iter().Any(item => item.UserId == userId && item.WebAuthnEnabled));
}
