using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenIddict.Abstractions;
using TicketSalesApp.Services.Interfaces;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;

namespace TicketSalesApp.Services.Implementations
{
    public class OpenIddictApplication
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string ConsentType { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public ImmutableDictionary<CultureInfo, string> DisplayNames { get; set; } = ImmutableDictionary<CultureInfo, string>.Empty;
        public string Type { get; set; } = string.Empty;
        public ImmutableArray<string> Permissions { get; set; } = ImmutableArray<string>.Empty;
        public ImmutableArray<string> PostLogoutRedirectUris { get; set; } = ImmutableArray<string>.Empty;
        public ImmutableArray<string> RedirectUris { get; set; } = ImmutableArray<string>.Empty;
        public ImmutableArray<string> Requirements { get; set; } = ImmutableArray<string>.Empty;
        public ImmutableDictionary<string, JsonElement> Properties { get; set; } = ImmutableDictionary<string, JsonElement>.Empty;
    }

    public class ApplicationStore : IOpenIddictApplicationStore<OpenIddictApplication>
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<ApplicationStore> _logger;

        public ApplicationStore(ISpacetimeDBService spacetimeService, ILogger<ApplicationStore> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("ApplicationStore initialized");
        }

        public ValueTask<long> CountAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Counting OpenID Connect clients");
                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("Failed to get SpacetimeDB connection");
                    throw new InvalidOperationException("SpacetimeDB connection is null");
                }

                var count = conn.Db.OpenIdConnect.Iter().Count(c => c.IsActive);
                _logger.LogDebug("Found {Count} active OpenID Connect clients", count);
                return new ValueTask<long>(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error counting OpenID Connect clients");
                throw;
            }
        }

        public ValueTask<long> CountAsync<TResult>(Func<IQueryable<OpenIddictApplication>, IQueryable<TResult>> query, CancellationToken cancellationToken)
        {
            _logger.LogWarning("CountAsync with custom query was called but is not supported");
            throw new NotSupportedException("Custom queries are not supported by this store.");
        }

        public ValueTask CreateAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot create null application");
                throw new ArgumentNullException(nameof(application));
            }

            try
            {
                _logger.LogInformation("Creating OpenID Connect client with ID {ClientId}", application.ClientId);
                
                if (string.IsNullOrEmpty(application.ClientId))
                {
                    _logger.LogError("Client ID cannot be null or empty");
                    throw new ArgumentException("Client ID cannot be null or empty.", nameof(application));
                }

                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("Failed to get SpacetimeDB connection");
                    throw new InvalidOperationException("SpacetimeDB connection is null");
                }
                
                _logger.LogDebug("Registering client {ClientId} with {RedirectUriCount} redirect URIs and {ScopeCount} scopes", 
                    application.ClientId, 
                    application.RedirectUris.Length, 
                    application.Permissions.Length);
                
                conn.Reducers.RegisterOpenIdClient(
                    application.ClientId,
                    application.ClientSecret,
                    application.DisplayName,
                    application.RedirectUris.ToList(),
                    application.PostLogoutRedirectUris.ToList(),
                    application.Permissions.ToList(),
                    application.ConsentType,
                    application.Type
                );

                _logger.LogInformation("Successfully created OpenID Connect client {ClientId}", application.ClientId);
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating OpenID Connect client {ClientId}", application.ClientId);
                throw;
            }
        }

        public ValueTask DeleteAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot delete null application");
                throw new ArgumentNullException(nameof(application));
            }

            try
            {
                _logger.LogInformation("Deleting OpenID Connect client {ClientId}", application.ClientId);
                
                if (string.IsNullOrEmpty(application.ClientId))
                {
                    _logger.LogError("Client ID cannot be null or empty");
                    throw new ArgumentException("Client ID cannot be null or empty.", nameof(application));
                }

                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("Failed to get SpacetimeDB connection");
                    throw new InvalidOperationException("SpacetimeDB connection is null");
                }
                
                conn.Reducers.RevokeOpenIdClient(application.ClientId);
                _logger.LogInformation("Successfully deleted OpenID Connect client {ClientId}", application.ClientId);
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting OpenID Connect client {ClientId}", application.ClientId);
                throw;
            }
        }

        public ValueTask<OpenIddictApplication?> FindByClientIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                _logger.LogError("Client identifier cannot be null or empty");
                throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));
            }

            try
            {
                _logger.LogDebug("Finding OpenID Connect client by ID {ClientId}", identifier);
                
                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("Failed to get SpacetimeDB connection");
                    throw new InvalidOperationException("SpacetimeDB connection is null");
                }
                
                var client = conn.Db.OpenIdConnect.Iter()
                    .Where(c => c.ClientId == identifier && c.IsActive)
                    .Select(c => new OpenIddictApplication
                    {
                        Id = c.ClientId,
                        ClientId = c.ClientId,
                        ClientSecret = c.ClientSecret,
                        PostLogoutRedirectUris = ImmutableArray.Create(c.PostLogoutRedirectUris.ToArray()),
                        RedirectUris = ImmutableArray.Create(c.RedirectUris.ToArray()),
                        ConsentType = c.ConsentType,
                        Type = c.ClientType,
                        DisplayName = c.DisplayName,
                        Permissions = ImmutableArray.Create(c.AllowedScopes.ToArray()),
                    })
                    .FirstOrDefault();

                if (client != null)
                {
                    _logger.LogDebug("Found OpenID Connect client {ClientId}", identifier);
                }
                else
                {
                    _logger.LogWarning("OpenID Connect client {ClientId} not found", identifier);
                }

                return new ValueTask<OpenIddictApplication?>(client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding OpenID Connect client by ID {ClientId}", identifier);
                throw;
            }
        }

        public ValueTask<OpenIddictApplication?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                _logger.LogError("Identifier cannot be null or empty");
                throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));
            }

            _logger.LogDebug("Finding OpenID Connect client by ID {ClientId} (delegating to FindByClientIdAsync)", identifier);
            return FindByClientIdAsync(identifier, cancellationToken);
        }

        public IAsyncEnumerable<OpenIddictApplication> FindByPostLogoutRedirectUriAsync(string address, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(address))
            {
                _logger.LogError("Post-logout redirect URI cannot be null or empty");
                throw new ArgumentException("Address cannot be null or empty.", nameof(address));
            }

            try
            {
                _logger.LogDebug("Finding OpenID Connect clients by post-logout redirect URI {Address}", address);
                
                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("Failed to get SpacetimeDB connection");
                    throw new InvalidOperationException("SpacetimeDB connection is null");
                }
                
                var clients = conn.Db.OpenIdConnect.Iter()
                    .Where(c => c.RedirectUris.Contains(address) && c.IsActive)
                    .Select(c => new OpenIddictApplication
                    {
                        Id = c.ClientId,
                        ClientId = c.ClientId,
                        ClientSecret = c.ClientSecret,
                        RedirectUris = ImmutableArray.Create(c.RedirectUris.ToArray()),
                        Permissions = ImmutableArray.Create(c.AllowedScopes.ToArray()),
                        Type = "public",
                        ConsentType = "explicit",
                        DisplayName = c.ClientId
                    });

                _logger.LogDebug("Found {Count} OpenID Connect clients with post-logout redirect URI {Address}", clients.Count(), address);
                return GetAsyncEnumerable(clients);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding OpenID Connect clients by post-logout redirect URI {Address}", address);
                throw;
            }
        }

        public IAsyncEnumerable<OpenIddictApplication> FindByRedirectUriAsync(string address, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(address))
            {
                _logger.LogError("Redirect URI cannot be null or empty");
                throw new ArgumentException("Address cannot be null or empty.", nameof(address));
            }

            try
            {
                _logger.LogDebug("Finding OpenID Connect clients by redirect URI {Address}", address);
                
                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("Failed to get SpacetimeDB connection");
                    throw new InvalidOperationException("SpacetimeDB connection is null");
                }
                
                var clients = conn.Db.OpenIdConnect.Iter()
                    .Where(c => c.RedirectUris.Contains(address) && c.IsActive)
                    .Select(c => new OpenIddictApplication
                    {
                        Id = c.ClientId,
                        ClientId = c.ClientId,
                        ClientSecret = c.ClientSecret,
                        RedirectUris = ImmutableArray.Create(c.RedirectUris.ToArray()),
                        Permissions = ImmutableArray.Create(c.AllowedScopes.ToArray()),
                        Type = "public",
                        ConsentType = "explicit",
                        DisplayName = c.ClientId
                    });

                _logger.LogDebug("Found {Count} OpenID Connect clients with redirect URI {Address}", clients.Count(), address);
                return GetAsyncEnumerable(clients);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding OpenID Connect clients by redirect URI {Address}", address);
                throw;
            }
        }

        private async IAsyncEnumerable<OpenIddictApplication> GetAsyncEnumerable(IEnumerable<OpenIddictApplication> applications)
        {
            foreach (var application in applications)
            {
                yield return application;
            }
        }

        ValueTask<TResult> IOpenIddictApplicationStore<OpenIddictApplication>.GetAsync<TState, TResult>(
            Func<IQueryable<OpenIddictApplication>, TState, IQueryable<TResult>> query,
            TState state, CancellationToken cancellationToken)
        {
            _logger.LogWarning("GetAsync with custom query was called but is not supported");
            throw new NotSupportedException("Custom queries are not supported by this store.");
        }

        public ValueTask<string?> GetClientIdAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get client ID from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting client ID for application {ClientId}", application.ClientId);
            return new ValueTask<string?>(application.ClientId);
        }

        public ValueTask<string?> GetClientSecretAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get client secret from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting client secret for application {ClientId}", application.ClientId);
            return new ValueTask<string?>(application.ClientSecret);
        }

        public ValueTask<string?> GetClientTypeAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get client type from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting client type for application {ClientId}", application.ClientId);
            return new ValueTask<string?>(application.Type);
        }

        public ValueTask<string?> GetConsentTypeAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get consent type from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting consent type for application {ClientId}", application.ClientId);
            return new ValueTask<string?>(application.ConsentType);
        }

        public ValueTask<string?> GetDisplayNameAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get display name from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting display name for application {ClientId}", application.ClientId);
            return new ValueTask<string?>(application.DisplayName);
        }

        public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get display names from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting display names for application {ClientId}", application.ClientId);
            return new ValueTask<ImmutableDictionary<CultureInfo, string>>(application.DisplayNames);
        }

        public ValueTask<string?> GetIdAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get ID from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting ID for application {ClientId}", application.ClientId);
            return new ValueTask<string?>(application.Id);
        }

        public ValueTask<ImmutableArray<string>> GetPermissionsAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get permissions from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting permissions for application {ClientId}", application.ClientId);
            return new ValueTask<ImmutableArray<string>>(application.Permissions);
        }

        public ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get post-logout redirect URIs from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting post-logout redirect URIs for application {ClientId}", application.ClientId);
            return new ValueTask<ImmutableArray<string>>(application.PostLogoutRedirectUris);
        }

        public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get properties from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting properties for application {ClientId}", application.ClientId);
            return new ValueTask<ImmutableDictionary<string, JsonElement>>(application.Properties);
        }

        public ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get redirect URIs from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting redirect URIs for application {ClientId}", application.ClientId);
            return new ValueTask<ImmutableArray<string>>(application.RedirectUris);
        }

        public ValueTask<ImmutableArray<string>> GetRequirementsAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot get requirements from null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Getting requirements for application {ClientId}", application.ClientId);
            return new ValueTask<ImmutableArray<string>>(application.Requirements);
        }

        public ValueTask<OpenIddictApplication> InstantiateAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Instantiating new OpenIddictApplication");
                return new ValueTask<OpenIddictApplication>(new OpenIddictApplication());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error instantiating OpenIddictApplication");
                throw;
            }
        }

        public IAsyncEnumerable<OpenIddictApplication> ListAsync(int? count, int? offset, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Listing OpenID Connect clients with count: {Count}, offset: {Offset}", count, offset);
                
                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("Failed to get SpacetimeDB connection");
                    throw new InvalidOperationException("SpacetimeDB connection is null");
                }
                
                var query = conn.Db.OpenIdConnect.Iter()
                    .Where(c => c.IsActive)
                    .Select(c => new OpenIddictApplication
                    {
                        Id = c.ClientId,
                        ClientId = c.ClientId,
                        ClientSecret = c.ClientSecret,
                        RedirectUris = ImmutableArray.Create(c.RedirectUris.ToArray()),
                        Permissions = ImmutableArray.Create(c.AllowedScopes.ToArray()),
                        Type = "public",
                        ConsentType = "explicit",
                        DisplayName = c.ClientId
                    });

                if (offset.HasValue)
                {
                    _logger.LogTrace("Applying offset {Offset}", offset.Value);
                    query = query.Skip(offset.Value);
                }

                if (count.HasValue)
                {
                    _logger.LogTrace("Applying count limit {Count}", count.Value);
                    query = query.Take(count.Value);
                }

                _logger.LogDebug("Found {Count} OpenID Connect clients", query.Count());
                return GetAsyncEnumerable(query);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing OpenID Connect clients");
                throw;
            }
        }

        private async IAsyncEnumerable<OpenIddictApplication> GetAsyncEnumerable(IQueryable<OpenIddictApplication> applications)
        {
            foreach (var application in applications)
            {
                yield return application;
            }
        }

        public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
            Func<IQueryable<OpenIddictApplication>, TState, IQueryable<TResult>> query,
            TState state, CancellationToken cancellationToken)
        {
            _logger.LogWarning("ListAsync with custom query was called but is not supported");
            throw new NotSupportedException("Custom queries are not supported by this store.");
        }

        public ValueTask SetClientIdAsync(OpenIddictApplication application, string? identifier, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set client ID on null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Setting client ID to {ClientId} for application", identifier);
            application.ClientId = identifier ?? string.Empty;
            return default;
        }

        public ValueTask SetClientSecretAsync(OpenIddictApplication application, string? secret, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set client secret on null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Setting client secret for application {ClientId}", application.ClientId);
            application.ClientSecret = secret ?? string.Empty;
            return default;
        }

        public ValueTask SetClientTypeAsync(OpenIddictApplication application, string? type, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set client type on null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Setting client type to {ClientType} for application {ClientId}", type, application.ClientId);
            application.Type = type ?? string.Empty;
            return default;
        }

        public ValueTask SetConsentTypeAsync(OpenIddictApplication application, string? type, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set consent type on null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Setting consent type to {ConsentType} for application {ClientId}", type, application.ClientId);
            application.ConsentType = type ?? string.Empty;
            return default;
        }

        public ValueTask SetDisplayNameAsync(OpenIddictApplication application, string? name, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set display name on null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Setting display name to {DisplayName} for application {ClientId}", name, application.ClientId);
            application.DisplayName = name ?? string.Empty;
            return default;
        }

        public ValueTask SetDisplayNamesAsync(OpenIddictApplication application, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set display names on null application");
                throw new ArgumentNullException(nameof(application));
            }

            if (names == null)
            {
                _logger.LogError("Cannot set null display names on application {ClientId}", application.ClientId);
                throw new ArgumentNullException(nameof(names));
            }

            _logger.LogTrace("Setting {Count} display names for application {ClientId}", names.Count, application.ClientId);
            application.DisplayNames = names;
            return default;
        }

        public ValueTask SetPermissionsAsync(OpenIddictApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set permissions on null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Setting {Count} permissions for application {ClientId}", permissions.Length, application.ClientId);
            application.Permissions = permissions;
            return default;
        }

        public ValueTask SetPostLogoutRedirectUrisAsync(OpenIddictApplication application, ImmutableArray<string> addresses, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set post-logout redirect URIs on null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Setting {Count} post-logout redirect URIs for application {ClientId}", addresses.Length, application.ClientId);
            application.PostLogoutRedirectUris = addresses;
            return default;
        }

        public ValueTask SetPropertiesAsync(OpenIddictApplication application, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set properties on null application");
                throw new ArgumentNullException(nameof(application));
            }

            if (properties == null)
            {
                _logger.LogError("Cannot set null properties on application {ClientId}", application.ClientId);
                throw new ArgumentNullException(nameof(properties));
            }

            _logger.LogTrace("Setting {Count} properties for application {ClientId}", properties.Count, application.ClientId);
            application.Properties = properties;
            return default;
        }

        public ValueTask SetRedirectUrisAsync(OpenIddictApplication application, ImmutableArray<string> addresses, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set redirect URIs on null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Setting {Count} redirect URIs for application {ClientId}", addresses.Length, application.ClientId);
            application.RedirectUris = addresses;
            return default;
        }

        public ValueTask SetRequirementsAsync(OpenIddictApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot set requirements on null application");
                throw new ArgumentNullException(nameof(application));
            }

            _logger.LogTrace("Setting {Count} requirements for application {ClientId}", requirements.Length, application.ClientId);
            application.Requirements = requirements;
            return default;
        }

        public ValueTask UpdateAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            if (application == null)
            {
                _logger.LogError("Cannot update null application");
                throw new ArgumentNullException(nameof(application));
            }

            try
            {
                _logger.LogInformation("Updating OpenID Connect client {ClientId}", application.ClientId);
                
                if (string.IsNullOrEmpty(application.ClientId))
                {
                    _logger.LogError("Client ID cannot be null or empty");
                    throw new ArgumentException("Client ID cannot be null or empty.", nameof(application));
                }

                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("Failed to get SpacetimeDB connection");
                    throw new InvalidOperationException("SpacetimeDB connection is null");
                }
                
                _logger.LogDebug("Updating client {ClientId} with {RedirectUriCount} redirect URIs and {ScopeCount} scopes", 
                    application.ClientId, 
                    application.RedirectUris.Length, 
                    application.Permissions.Length);
                
                conn.Reducers.UpdateOpenIdClient(
                    application.ClientId,
                    application.ClientSecret,
                    application.DisplayName,
                    application.RedirectUris.ToList(),
                    application.PostLogoutRedirectUris.ToList(),
                    application.Permissions.ToList(),
                    application.ConsentType
                );

                _logger.LogInformation("Successfully updated OpenID Connect client {ClientId}", application.ClientId);
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating OpenID Connect client {ClientId}", application.ClientId);
                throw;
            }
        }
    }
}
