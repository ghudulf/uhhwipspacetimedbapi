using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Text;

namespace TicketSalesApp.Services.Implementations
{
    /// <summary>
    /// Implementation of the WebAuthn service
    /// </summary>
    public class WebAuthnService : IWebAuthnService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IFido2 _fido2;
        private readonly ILogger<WebAuthnService> _logger;

        public WebAuthnService(
            ISpacetimeDBService spacetimeService,
            IFido2 fido2,
            ILogger<WebAuthnService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _fido2 = fido2 ?? throw new ArgumentNullException(nameof(fido2));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets credential creation options for WebAuthn registration
        /// </summary>
        public async Task<(bool success, CredentialCreateOptions? options, string? errorMessage)> GetCredentialCreateOptionsAsync(Identity userId, string username)
        {
            try
            {
                _logger.LogInformation("Getting credential creation options for user: {Username}", username);

                var conn = _spacetimeService.GetConnection();
                
                // Check if user exists
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);
                
                if (user == null)
                {
                    _logger.LogWarning("User not found for WebAuthn registration: {UserId}", userId);
                    return (false, null, "User not found");
                }

                // Get existing credentials
                var existingCredentials = await GetUserCredentialsAsync(userId);
                var excludeCredentials = existingCredentials
                    .Select(c => new PublicKeyCredentialDescriptor(c.CredentialId.ToArray()))
                    .ToList();

                // Create options
                var authenticatorSelection = new AuthenticatorSelection
                {
                    RequireResidentKey = false,
                    UserVerification = UserVerificationRequirement.Preferred
                };

                var options = _fido2.RequestNewCredential(
                    new Fido2User
                    {
                        Id = System.Text.Encoding.UTF8.GetBytes(userId.ToString()),
                        Name = username,
                        DisplayName = username
                    },
                    excludeCredentials,
                    authenticatorSelection,
                    AttestationConveyancePreference.None,
                    new AuthenticationExtensionsClientInputs()
                );

                // Store challenge in SpacetimeDB
                conn.Reducers.StoreWebAuthnChallenge(
                    userId,
                    Convert.ToBase64String(options.Challenge),
                    (ulong)((DateTimeOffset)DateTime.UtcNow.AddMinutes(5)).ToUnixTimeMilliseconds()
                );

                return (true, options, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting credential creation options for user: {Username}", username);
                return (false, null, "An error occurred while creating WebAuthn registration options");
            }
        }

        /// <summary>
        /// Completes WebAuthn registration
        /// </summary>
        public async Task<(bool success, string? errorMessage)> CompleteRegistrationAsync(Identity userId, string username, AuthenticatorAttestationRawResponse attestationResponse)
        {
            try
            {
                _logger.LogInformation("Completing WebAuthn registration for user: {Username}", username);

                var conn = _spacetimeService.GetConnection();
                
                // Check if user exists
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);
                
                if (user == null)
                {
                    _logger.LogWarning("User not found for WebAuthn registration completion: {UserId}", userId);
                    return (false, "User not found");
                }

                // Get the challenge
                var challenge = conn.Db.WebAuthnChallenge.Iter()
                    .FirstOrDefault(c => c.UserId.Equals(userId) && c.ExpiresAt > (ulong)((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds());
                
                if (challenge == null)
                {
                    _logger.LogWarning("Challenge not found or expired for user: {Username}", username);
                    return (false, "Challenge not found or expired");
                }

                // Get existing credentials
                var existingCredentials = await GetUserCredentialsAsync(userId);
                var excludeCredentials = existingCredentials
                    .Select(c => new PublicKeyCredentialDescriptor(c.CredentialId.ToArray()))
                    .ToList();

                // Verify and make credential
                var success = await _fido2.MakeNewCredentialAsync(
                    attestationResponse,
                    new CredentialCreateOptions
                    {
                        User = new Fido2User
                        {
                            Id = System.Text.Encoding.UTF8.GetBytes(userId.ToString()),
                            Name = username,
                            DisplayName = username
                        },
                        Challenge = System.Text.Encoding.UTF8.GetBytes(challenge.Challenge),
                        ExcludeCredentials = excludeCredentials,
                        AuthenticatorSelection = new AuthenticatorSelection
                        {
                            RequireResidentKey = false,
                            UserVerification = UserVerificationRequirement.Preferred
                        },
                        Attestation = AttestationConveyancePreference.None
                    },
                    (_, _) => Task.FromResult(true)
                );

                // Store credential in SpacetimeDB
                conn.Reducers.RegisterWebAuthnCredential(
                    userId,
                    success.Result.CredentialId.ToList(),
                    Convert.ToBase64String(success.Result.PublicKey),
                    success.Result.Counter,
                    null // No device name provided
                );

                // Enable WebAuthn for the user
                conn.Reducers.EnableWebAuthn(userId);

                // Delete the challenge
                conn.Reducers.DeleteWebAuthnChallenge(challenge.Id);

                _logger.LogInformation("WebAuthn registration completed successfully for user: {Username}", username);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing WebAuthn registration for user: {Username}", username);
                return (false, "An error occurred while completing WebAuthn registration");
            }
        }

        /// <summary>
        /// Gets assertion options for WebAuthn authentication
        /// </summary>
        public async Task<(bool success, AssertionOptions? options, string? errorMessage)> GetAssertionOptionsAsync(string username)
        {
            try
            {
                _logger.LogInformation("Getting assertion options for user: {Username}", username);

                var conn = _spacetimeService.GetConnection();
                
                // Find user by username
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == username && u.IsActive);
                
                if (user == null)
                {
                    _logger.LogWarning("User not found for WebAuthn assertion: {Username}", username);
                    return (false, null, "User not found");
                }

                // Get user credentials
                var credentials = await GetUserCredentialsAsync(user.UserId);
                if (!credentials.Any())
                {
                    _logger.LogWarning("No WebAuthn credentials found for user: {Username}", username);
                    return (false, null, "No WebAuthn credentials found for this user");
                }

                var allowedCredentials = credentials
                    .Select(c => new PublicKeyCredentialDescriptor(c.CredentialId.ToArray()))
                    .ToList();

                // Create options
                var options = _fido2.GetAssertionOptions(
                    allowedCredentials,
                    UserVerificationRequirement.Preferred,
                    new AuthenticationExtensionsClientInputs()
                );

                // Store challenge in SpacetimeDB
                conn.Reducers.StoreWebAuthnChallenge(
                    user.UserId,
                    Convert.ToBase64String(options.Challenge),
                    (ulong)((DateTimeOffset)DateTime.UtcNow.AddMinutes(5)).ToUnixTimeMilliseconds()
                );

                return (true, options, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assertion options for user: {Username}", username);
                return (false, null, "An error occurred while creating WebAuthn assertion options");
            }
        }

        /// <summary>
        /// Completes WebAuthn authentication
        /// </summary>
        public async Task<(bool success, UserProfile? user, string? errorMessage)> CompleteAssertionAsync(string username, AuthenticatorAssertionRawResponse assertionResponse)
        {
            try
            {
                _logger.LogInformation("Completing WebAuthn assertion for user: {Username}", username);

                var conn = _spacetimeService.GetConnection();
                
                // Find user by username
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == username && u.IsActive);
                
                if (user == null)
                {
                    _logger.LogWarning("User not found for WebAuthn assertion completion: {Username}", username);
                    return (false, null, "User not found");
                }

                // Get the challenge
                var challenge = conn.Db.WebAuthnChallenge.Iter()
                    .FirstOrDefault(c => c.UserId.Equals(user.UserId) && c.ExpiresAt > (ulong)((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds());
                
                if (challenge == null)
                {
                    _logger.LogWarning("Challenge not found or expired for user: {Username}", username);
                    return (false, null, "Challenge not found or expired");
                }

                // Get the credential
                var credential = conn.Db.WebAuthnCredential.Iter()
                    .FirstOrDefault(c => c.UserId.Equals(user.UserId) && 
                                        c.CredentialId.SequenceEqual(assertionResponse.Id) && 
                                        c.IsActive);
                
                if (credential == null)
                {
                    _logger.LogWarning("Credential not found for user: {Username}", username);
                    return (false, null, "Credential not found");
                }

                // Verify the assertion
                var storedCounter = credential.Counter;
                var result = await _fido2.MakeAssertionAsync(
                    assertionResponse,
                    new AssertionOptions
                    {
                        Challenge = System.Text.Encoding.UTF8.GetBytes(challenge.Challenge),
                        AllowCredentials = new List<PublicKeyCredentialDescriptor>
                        {
                            new PublicKeyCredentialDescriptor(credential.CredentialId.ToArray())
                        },
                        UserVerification = UserVerificationRequirement.Preferred
                    },
                    Convert.FromBase64String(credential.PublicKey),
                    storedCounter,
                    (_, _) => Task.FromResult(true)
                );

                // Update the counter
                conn.Reducers.UpdateWebAuthnCredentialCounter(
                    credential.Id,
                    result.Counter
                );

                // Delete the challenge
                conn.Reducers.DeleteWebAuthnChallenge(challenge.Id);

                _logger.LogInformation("WebAuthn assertion completed successfully for user: {Username}", username);
                return (true, user, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing WebAuthn assertion for user: {Username}", username);
                return (false, null, "An error occurred while completing WebAuthn authentication");
            }
        }

        /// <summary>
        /// Removes a WebAuthn credential
        /// </summary>
        public async Task<(bool success, string? errorMessage)> RemoveCredentialAsync(Identity userId, string credentialId)
        {
            try
            {
                _logger.LogInformation("Removing WebAuthn credential for user ID: {UserId}", userId);

                var conn = _spacetimeService.GetConnection();
                
                // Check if user exists
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);
                
                if (user == null)
                {
                    _logger.LogWarning("User not found for WebAuthn credential removal: {UserId}", userId);
                    return (false, "User not found");
                }

                // Find the credential
                var credential = conn.Db.WebAuthnCredential.Iter()
                    .FirstOrDefault(c => c.UserId.Equals(userId) && 
                                        Convert.ToBase64String(c.CredentialId.ToArray()) == credentialId && 
                                        c.IsActive);
                
                if (credential == null)
                {
                    _logger.LogWarning("Credential not found for user ID: {UserId}", userId);
                    return (false, "Credential not found");
                }

                // Deactivate the credential
                conn.Reducers.DeactivateWebAuthnCredential(credential.Id);

                // Check if this was the last credential
                var remainingCredentials = conn.Db.WebAuthnCredential.Iter()
                    .Count(c => c.UserId.Equals(userId) && c.IsActive);
                
                if (remainingCredentials == 0)
                {
                    // Disable WebAuthn for the user
                    conn.Reducers.DisableWebAuthn(userId);
                }

                _logger.LogInformation("WebAuthn credential removed successfully for user ID: {UserId}", userId);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing WebAuthn credential for user ID: {UserId}", userId);
                return (false, "An error occurred while removing WebAuthn credential");
            }
        }

        /// <summary>
        /// Gets all WebAuthn credentials for a user
        /// </summary>
        public async Task<List<WebAuthnCredential>> GetUserCredentialsAsync(Identity userId)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                
                // Get all active credentials for the user
                var credentials = conn.Db.WebAuthnCredential.Iter()
                    .Where(c => c.UserId.Equals(userId) && c.IsActive)
                    .ToList();
                
                return credentials;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn credentials for user ID: {UserId}", userId);
                return new List<WebAuthnCredential>();
            }
        }

        /// <summary>
        /// Checks if WebAuthn is enabled for a user
        /// </summary>
        public async Task<bool> IsWebAuthnEnabledAsync(Identity userId)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                
                // Get user settings
                var userSettings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(userId));
                
                return userSettings?.WebAuthnEnabled ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if WebAuthn is enabled for user ID: {UserId}", userId);
                return false;
            }
        }
    }
}






