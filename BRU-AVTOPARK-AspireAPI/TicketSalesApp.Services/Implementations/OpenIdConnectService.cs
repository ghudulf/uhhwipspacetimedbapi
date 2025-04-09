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
using System.Collections.Immutable;
using OpenIddict.Core;
using Microsoft.Extensions.DependencyInjection;

namespace TicketSalesApp.Services.Implementations
{
    /// <summary>
    /// Implementation of the OpenID Connect service
    /// </summary>
    public class OpenIdConnectService : IOpenIdConnectService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OpenIdConnectService> _logger;

        public OpenIdConnectService(
            ISpacetimeDBService spacetimeService,
            IServiceProvider serviceProvider,
            ILogger<OpenIdConnectService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private IOpenIddictApplicationManager GetApplicationManager()
        {
            return _serviceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        }

        private IOpenIddictAuthorizationManager GetAuthorizationManager()
        {
            return _serviceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        }

        private IOpenIddictScopeManager GetScopeManager()
        {
            return _serviceProvider.GetRequiredService<IOpenIddictScopeManager>();
        }

        /// <summary>
        /// Gets an application by client ID
        /// </summary>
        public async Task<(bool success, object? application, string? errorMessage)> GetApplicationByClientIdAsync(string clientId)
        {
            try
            {
                _logger.LogInformation("Getting application by client ID: {ClientId}", clientId);

                var applicationManager = GetApplicationManager();
                var application = await applicationManager.FindByClientIdAsync(clientId);
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
        public async Task<(bool success, List<object>? authorizations, string? errorMessage)> GetAuthorizationsAsync(string subject, object application, string status, string type, string[] scopes)
        {
            try
            {
                _logger.LogInformation("Getting authorizations for subject: {Subject}", subject);
                var applicationManager = GetApplicationManager();
                var authorizationManager = GetAuthorizationManager();
                
                var applicationId = await applicationManager.GetIdAsync(application);
                var authorizationsQuery = authorizationManager.FindAsync(
                    subject: subject,
                    client: applicationId,
                    status: status,
                    type: type,
                    scopes: ImmutableArray.Create(scopes));
                
                var authorizations = new List<object>();
                await foreach (var authorization in authorizationsQuery)
                {
                    authorizations.Add(authorization);
                }

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
        public async Task<(bool success, ClaimsIdentity? identity, string? errorMessage)> CreateIdentityFromUserAsync(UserProfile user, string[] scopes)
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
        public async Task<(bool success, object? authorization, string? errorMessage)> CreateAuthorizationAsync(ClaimsIdentity identity, string subject, object application, string type, string[] scopes)
        {
            try
            {
                _logger.LogInformation("Creating authorization for subject: {Subject}", subject);

                var applicationManager = GetApplicationManager();
                var authorizationManager = GetAuthorizationManager();
                
                var applicationId = await applicationManager.GetIdAsync(application);
                var authorization = await authorizationManager.CreateAsync(
                    identity: identity,
                    subject: subject,
                    client: applicationId,
                    type: type,
                    scopes: ImmutableArray.Create(scopes));

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
                var authorizationManager = GetAuthorizationManager();
                var id = await authorizationManager.GetIdAsync(authorization);
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
        public async Task<(bool success, List<string>? resources, string? errorMessage)> GetResourcesAsync(string[] scopes)
        {
            try
            {
                var scopeManager = GetScopeManager();
                var resourcesAsync = scopeManager.ListResourcesAsync(ImmutableArray.Create(scopes));
                var resources = new List<string>();
                await foreach (var resource in resourcesAsync)
                {
                    resources.Add(resource);
                }
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
                var applicationManager = GetApplicationManager();
                var existingApp = await applicationManager.FindByClientIdAsync(clientId);
                if (existingApp != null)
                {
                    _logger.LogWarning("An application with this client ID already exists: {ClientId}", clientId);
                    return (false, "An application with this client ID already exists");
                }
                
                var conn = _spacetimeService.GetConnection();
                
                // Register the client in SpacetimeDB
                conn.Reducers.RegisterOpenIdClient(
                    clientId,
                    clientSecret,
                    displayName,
                    redirectUris.ToList(),
                    postLogoutRedirectUris.ToList(),
                    allowedScopes.ToList(),
                    requireConsent ? "explicit" : "implicit",
                    "public"
                );
                
                // Create a new OpenIddict application
                var application = await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
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
                    }
                });

                // Add redirect URIs
                foreach (var uri in redirectUris ?? Array.Empty<string>())
                {
                    ((OpenIddictApplicationDescriptor)application).RedirectUris.Add(new Uri(uri));
                }

                // Add post-logout redirect URIs
                foreach (var uri in postLogoutRedirectUris)
                {
                    ((OpenIddictApplicationDescriptor)application).PostLogoutRedirectUris.Add(new Uri(uri));
                }

                // Set consent type
                ((OpenIddictApplicationDescriptor)application).ConsentType = requireConsent ? 
                    ConsentTypes.Explicit : 
                    ConsentTypes.Implicit;

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

                var applicationManager = GetApplicationManager();
                var application = await applicationManager.FindByClientIdAsync(clientId);
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
                conn.Reducers.UpdateOpenIdClient(
                    clientId,
                    clientSecret ?? client.ClientSecret,
                    displayName ?? client.DisplayName,
                    (redirectUris ?? client.RedirectUris.ToArray()).ToList(),
                    (postLogoutRedirectUris ?? client.PostLogoutRedirectUris.ToArray()).ToList(),
                    (allowedScopes ?? client.AllowedScopes.ToArray()).ToList(),
                    requireConsent.HasValue ? (requireConsent.Value ? "explicit" : "implicit") : client.ConsentType
                );
                
                // Update the application in OpenIddict
                var descriptor = new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    DisplayName = displayName ?? await applicationManager.GetDisplayNameAsync(application),
                    ConsentType = requireConsent.HasValue ? 
                        (requireConsent.Value ? ConsentTypes.Explicit : ConsentTypes.Implicit) : 
                        await applicationManager.GetConsentTypeAsync(application)
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
                    var existingUris = await applicationManager.GetRedirectUrisAsync(application);
                    foreach (var uri in existingUris)
                    {
                        descriptor.RedirectUris.Add(new Uri(uri));
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
                    var existingUris = await applicationManager.GetPostLogoutRedirectUrisAsync(application);
                    foreach (var uri in existingUris)
                    {
                        descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
                    }
                }
                
                // Copy existing permissions
                foreach (var permission in await applicationManager.GetPermissionsAsync(application))
                {
                    descriptor.Permissions.Add(permission);
                }
                
                // Update the application
                await applicationManager.UpdateAsync(application, descriptor);
                
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

                var applicationManager = GetApplicationManager();
                var application = await applicationManager.FindByClientIdAsync(clientId);
                if (application == null)
                {
                    _logger.LogWarning("Application not found with client ID: {ClientId}", clientId);
                    return (false, "Application not found");
                }
                
                var conn = _spacetimeService.GetConnection();
                
                // Revoke the client in SpacetimeDB
                 conn.Reducers.RevokeOpenIdClient(clientId);

                 //stupid ai made all the calls awaited - you  cant await a reducer
                
                // Delete the application in OpenIddict
                await applicationManager.DeleteAsync(application);
                
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

                var applicationManager = GetApplicationManager();
                var applications = new List<object>();
                await foreach (var application in applicationManager.ListAsync())
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

                var applicationManager = GetApplicationManager();
                var application = await applicationManager.FindByClientIdAsync(clientId);
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






