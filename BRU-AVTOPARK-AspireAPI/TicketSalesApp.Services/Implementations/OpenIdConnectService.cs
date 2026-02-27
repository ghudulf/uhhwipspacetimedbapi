using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using SpacetimeDB;
using SpacetimeDB.Types;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TicketSalesApp.Services.Implementations
{
    /// <summary>
    /// Implementation of the OpenID Connect service
    /// </summary>
    public class OpenIdConnectService : IOpenIdConnectService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IOpenIddictApplicationManager _applicationManager;
        private readonly IOpenIddictAuthorizationManager _authorizationManager;
        private readonly IOpenIddictScopeManager _scopeManager;
        private readonly ILogger<OpenIdConnectService> _logger;

        public OpenIdConnectService(
            ISpacetimeDBService spacetimeService,
            IOpenIddictApplicationManager applicationManager,
            IOpenIddictAuthorizationManager authorizationManager,
            IOpenIddictScopeManager scopeManager,
            ILogger<OpenIdConnectService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _applicationManager = applicationManager ?? throw new ArgumentNullException(nameof(applicationManager));
            _authorizationManager = authorizationManager ?? throw new ArgumentNullException(nameof(authorizationManager));
            _scopeManager = scopeManager ?? throw new ArgumentNullException(nameof(scopeManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets an application by client ID
        /// </summary>
        public async Task<(bool success, object? application, string? errorMessage)> GetApplicationByClientIdAsync(string clientId)
        {
            try
            {
                _logger.LogInformation("Getting application by client ID: {ClientId}", clientId);

                var application = await _applicationManager.FindByClientIdAsync(clientId);
                if (application == null)
                {
                    _logger.LogWarning("Application not found with client ID: {ClientId}", clientId);
                    return (false, null, "Application not found");
                }

                return (true, application, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting application by client ID: {ClientId}", clientId);
                return (false, null, "An error occurred while getting the application");
            }
        }

        /// <summary>
        /// Gets authorizations for a user and application
        /// </summary>
        public async Task<(bool success, List<object>? authorizations, string? errorMessage)> GetAuthorizationsAsync(string subject, object application, string status, string type, IEnumerable<string> scopes)
        {
            try
            {
                _logger.LogInformation("Getting authorizations for subject: {Subject}", subject);

                var applicationId = await _applicationManager.GetIdAsync(application);
                var authorizations = await _authorizationManager.FindAsync(
                    subject: subject,
                    client: applicationId,
                    status: status,
                    type: type,
                    scopes: scopes).ToListAsync();

                return (true, authorizations, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting authorizations for subject: {Subject}", subject);
                return (false, null, "An error occurred while getting authorizations");
            }
        }

        /// <summary>
        /// Creates an identity from a user
        /// </summary>
        public async Task<(bool success, ClaimsIdentity? identity, string? errorMessage)> CreateIdentityFromUserAsync(UserProfile user, IEnumerable<string> scopes)
        {
            try
            {
                _logger.LogInformation("Creating identity for user: {Login}", user.Login);

                var conn = _spacetimeService.GetConnection();
                
                // Create the claims-based identity
                var identity = new ClaimsIdentity(
                    authenticationType: "Bearer",
                    nameType: Claims.Name,
                    roleType: Claims.Role);

                // Add the claims that will be persisted in the tokens
                identity.SetClaim(Claims.Subject, user.LegacyUserId.ToString())
                        .SetClaim(Claims.Email, user.Email)
                        .SetClaim(Claims.Name, user.Login)
                        .SetClaim(Claims.PreferredUsername, user.Login);

                // Add role claims
                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.UserId.Equals(user.UserId))
                    .Join(conn.Db.Role.Iter(), ur => ur.RoleId, r => r.RoleId, (ur, r) => r.Name)
                    .ToList();
                
                foreach (var role in userRoles)
                {
                    identity.AddClaim(Claims.Role, role);
                }

                // Set the scopes
                identity.SetScopes(scopes);

                return (true, identity, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating identity for user: {Login}", user.Login);
                return (false, null, "An error occurred while creating identity");
            }
        }

        /// <summary>
        /// Creates an authorization
        /// </summary>
        public async Task<(bool success, object? authorization, string? errorMessage)> CreateAuthorizationAsync(ClaimsIdentity identity, string subject, object application, string type, IEnumerable<string> scopes)
        {
            try
            {
                _logger.LogInformation("Creating authorization for subject: {Subject}", subject);

                var applicationId = await _applicationManager.GetIdAsync(application);
                var authorization = await _authorizationManager.CreateAsync(
                    identity: identity,
                    subject: subject,
                    client: applicationId,
                    type: type,
                    scopes: scopes);

                return (true, authorization, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating authorization for subject: {Subject}", subject);
                return (false, null, "An error occurred while creating authorization");
            }
        }

        /// <summary>
        /// Gets the ID of an authorization
        /// </summary>
        public async Task<(bool success, string? id, string? errorMessage)> GetAuthorizationIdAsync(object authorization)
        {
            try
            {
                var id = await _authorizationManager.GetIdAsync(authorization);
                return (true, id, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting authorization ID");
                return (false, null, "An error occurred while getting authorization ID");
            }
        }

        /// <summary>
        /// Gets resources for scopes
        /// </summary>
        public async Task<(bool success, List<string>? resources, string? errorMessage)>
        /// <summary>
        /// Gets resources for scopes
        /// </summary>
        public async Task<(bool success, List<string>? resources, string? errorMessage)> GetResourcesAsync(IEnumerable<string> scopes)
        {
            try
            {
                var resources = await _scopeManager.ListResourcesAsync(scopes).ToListAsync();
                return (true, resources, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting resources for scopes");
                return (false, null, "An error occurred while getting resources");
            }
        }

        /// <summary>
        /// Registers a new client application
        /// </summary>
        public async Task<(bool success, string? errorMessage)> RegisterClientApplicationAsync(string clientId, string clientSecret, string displayName, string[] redirectUris, string[] postLogoutRedirectUris, string[] allowedScopes, bool requireConsent)
        {
            try
            {
                _logger.LogInformation("Registering client application: {ClientId}", clientId);

                // Check if application with the same client ID already exists
                var existingApp = await _applicationManager.FindByClientIdAsync(clientId);
                if (existingApp != null)
                {
                    _logger.LogWarning("An application with this client ID already exists: {ClientId}", clientId);
                    return (false, "An application with this client ID already exists");
                }
                
                var conn = _spacetimeService.GetConnection();
                
                // Register the client in SpacetimeDB
                await conn.Reducers.RegisterOpenIdClientAsync(
                    clientId,
                    clientSecret,
                    displayName,
                    redirectUris,
                    postLogoutRedirectUris,
                    allowedScopes,
                    requireConsent ? "explicit" : "implicit",
                    "public"
                );
                
                // Create a new OpenIddict application
                var application = await _applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    DisplayName = displayName,
                    Permissions =
                    {
                        Permissions.Endpoints.Authorization,
                        Permissions.Endpoints.Token,
                        Permissions.Endpoints.Logout,
                        Permissions.Endpoints.Revocation,
                        Permissions.GrantTypes.AuthorizationCode,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.GrantTypes.RefreshToken,
                        Permissions.GrantTypes.Password,
                        Permissions.ResponseTypes.Code,
                        Permissions.Scopes.Email,
                        Permissions.Scopes.Profile,
                        Permissions.Scopes.Roles
                    },
                    RedirectUris = redirectUris.Select(uri => new Uri(uri)).ToList(),
                    PostLogoutRedirectUris = postLogoutRedirectUris.Select(uri => new Uri(uri)).ToList(),
                    ConsentType = requireConsent ? 
                        ConsentTypes.Explicit : 
                        ConsentTypes.Implicit
                });
                
                _logger.LogInformation("Client application registered successfully: {ClientId}", clientId);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering client application: {ClientId}", clientId);
                return (false, "An error occurred while registering the client application");
            }
        }

        /// <summary>
        /// Updates a client application
        /// </summary>
        public async Task<(bool success, string? errorMessage)> UpdateClientApplicationAsync(string clientId, string? clientSecret, string? displayName, string[]? redirectUris, string[]? postLogoutRedirectUris, string[]? allowedScopes, bool? requireConsent)
        {
            try
            {
                _logger.LogInformation("Updating client application: {ClientId}", clientId);

                var application = await _applicationManager.FindByClientIdAsync(clientId);
                if (application == null)
                {
                    _logger.LogWarning("Application not found with client ID: {ClientId}", clientId);
                    return (false, "Application not found");
                }
                
                var conn = _spacetimeService.GetConnection();
                
                // Get the current client from SpacetimeDB
                var client = conn.Db.OpenIdConnect.Iter()
                    .FirstOrDefault(c => c.ClientId == clientId && c.IsActive);
                
                if (client == null)
                {
                    _logger.LogWarning("Client not found in SpacetimeDB with client ID: {ClientId}", clientId);
                    return (false, "Client not found");
                }
                
                // Update the client in SpacetimeDB
                await conn.Reducers.UpdateOpenIdClientAsync(
                    clientId,
                    clientSecret ?? client.ClientSecret,
                    displayName ?? client.DisplayName,
                    redirectUris ?? client.RedirectUris,
                    postLogoutRedirectUris ?? client.PostLogoutRedirectUris,
                    allowedScopes ?? client.AllowedScopes,
                    requireConsent.HasValue ? (requireConsent.Value ? "explicit" : "implicit") : client.ConsentType
                );
                
                // Update the application in OpenIddict
                var descriptor = new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    DisplayName = displayName ?? await _applicationManager.GetDisplayNameAsync(application),
                    ConsentType = requireConsent.HasValue ? 
                        (requireConsent.Value ? ConsentTypes.Explicit : ConsentTypes.Implicit) : 
                        await _applicationManager.GetConsentTypeAsync(application)
                };
                
                // Update client secret if provided
                if (!string.IsNullOrEmpty(clientSecret))
                {
                    descriptor.ClientSecret = clientSecret;
                }
                
                // Update redirect URIs if provided
                if (redirectUris != null)
                {
                    foreach (var uri in redirectUris)
                    {
                        descriptor.RedirectUris.Add(new Uri(uri));
                    }
                }
                else
                {
                    foreach (var uri in await _applicationManager.GetRedirectUrisAsync(application))
                    {
                        descriptor.RedirectUris.Add(uri);
                    }
                }
                
                // Update post-logout redirect URIs if provided
                if (postLogoutRedirectUris != null)
                {
                    foreach (var uri in postLogoutRedirectUris)
                    {
                        descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
                    }
                }
                else
                {
                    foreach (var uri in await _applicationManager.GetPostLogoutRedirectUrisAsync(application))
                    {
                        descriptor.PostLogoutRedirectUris.Add(uri);
                    }
                }
                
                // Copy existing permissions
                foreach (var permission in await _applicationManager.GetPermissionsAsync(application))
                {
                    descriptor.Permissions.Add(permission);
                }
                
                // Update the application
                await _applicationManager.UpdateAsync(application, descriptor);
                
                _logger.LogInformation("Client application updated successfully: {ClientId}", clientId);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating client application: {ClientId}", clientId);
                return (false, "An error occurred while updating the client application");
            }
        }

        /// <summary>
        /// Deletes a client application
        /// </summary>
        public async Task<(bool success, string? errorMessage)> DeleteClientApplicationAsync(string clientId)
        {
            try
            {
                _logger.LogInformation("Deleting client application: {ClientId}", clientId);

                var application = await _applicationManager.FindByClientIdAsync(clientId);
                if (application == null)
                {
                    _logger.LogWarning("Application not found with client ID: {ClientId}", clientId);
                    return (false, "Application not found");
                }
                
                var conn = _spacetimeService.GetConnection();
                
                // Revoke the client in SpacetimeDB
                await conn.Reducers.RevokeOpenIdClientAsync(clientId);
                
                // Delete the application in OpenIddict
                await _applicationManager.DeleteAsync(application);
                
                _logger.LogInformation("Client application deleted successfully: {ClientId}", clientId);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting client application: {ClientId}", clientId);
                return (false, "An error occurred while deleting the client application");
            }
        }

        /// <summary>
        /// Gets all client applications
        /// </summary>
        public async Task<(bool success, List<object>? applications, string? errorMessage)> GetAllClientApplicationsAsync()
        {
            try
            {
                _logger.LogInformation("Getting all client applications");

                var applications = new List<object>();
                await foreach (var application in _applicationManager.ListAsync())
                {
                    applications.Add(application);
                }

                return (true, applications, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all client applications");
                return (false, null, "An error occurred while getting client applications");
            }
        }

        /// <summary>
        /// Gets a client application
        /// </summary>
        public async Task<(bool success, object? application, string? errorMessage)> GetClientApplicationAsync(string clientId)
        {
            try
            {
                _logger.LogInformation("Getting client application: {ClientId}", clientId);

                var application = await _applicationManager.FindByClientIdAsync(clientId);
                if (application == null)
                {
                    _logger.LogWarning("Application not found with client ID: {ClientId}", clientId);
                    return (false, null, "Application not found");
                }

                return (true, application, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client application: {ClientId}", clientId);
                return (false, null, "An error occurred while getting the client application");
            }
        }

        /// <summary>
        /// Determines the destinations for claims
        /// </summary>
        public IEnumerable<string> GetDestinations(Claim claim)
        {
            // Note: by default, claims are NOT automatically included in the access and identity tokens.
            // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
            // whether they should be included in access tokens, in identity tokens or in both.

            switch (claim.Type)
            {
                case Claims.Name or Claims.PreferredUsername:
                    yield return Destinations.AccessToken;

                    if (claim.Subject.HasScope(Scopes.Profile))
                        yield return Destinations.IdentityToken;

                    yield break;

                case Claims.Email:
                    yield return Destinations.AccessToken;

                    if (claim.Subject.HasScope(Scopes.Email))
                        yield return Destinations.IdentityToken;

                    yield break;

                case Claims.Role:
                    yield return Destinations.AccessToken;

                    if (claim.Subject.HasScope(Scopes.Roles))
                        yield return Destinations.IdentityToken;

                    yield break;

                // Never include the security stamp in the access and identity tokens, as it's a secret value.
                case "AspNet.Identity.SecurityStamp": yield break;

                default:
                    yield return Destinations.AccessToken;
                    yield break;
            }
        }
    }
}
