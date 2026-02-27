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
        
        // CRITICAL: Client-side caching for applications as failsafe
        // Maps ClientId to OpenIddictApplication for fast retrieval
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OpenIddictApplication> _applicationCache = new();
        
        // Pending applications that are being created (not yet synced to database)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OpenIddictApplication> _pendingApplications = new();

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
                _logger.LogInformation("=== [ApplicationStore.CreateAsync] Creating client: {ClientId} ===", application.ClientId);
                
                if (string.IsNullOrEmpty(application.ClientId))
                {
                    _logger.LogError("Client ID cannot be null or empty");
                    throw new ArgumentException("Client ID cannot be null or empty.", nameof(application));
                }

                // CRITICAL: Check cache first to avoid duplicate creation
                if (_applicationCache.TryGetValue(application.ClientId, out var cachedApp))
                {
                    _logger.LogInformation("[ApplicationStore.CreateAsync] Client {ClientId} already exists in cache, skipping creation", application.ClientId);
                    application.Id = cachedApp.Id;
                    return default;
                }

                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("Failed to get SpacetimeDB connection");
                    throw new InvalidOperationException("SpacetimeDB connection is null");
                }
                
                _logger.LogInformation("[ApplicationStore.CreateAsync] Client details:");
                _logger.LogInformation("  - ClientId: {ClientId}", application.ClientId);
                _logger.LogInformation("  - DisplayName: {DisplayName}", application.DisplayName);
                _logger.LogInformation("  - Type: {Type}", application.Type);
                _logger.LogInformation("  - ConsentType: {ConsentType}", application.ConsentType);
                _logger.LogInformation("  - RedirectUris: {Count} URIs", application.RedirectUris.Length);
                foreach (var uri in application.RedirectUris)
                {
                    _logger.LogInformation("    * {Uri}", uri);
                }
                _logger.LogInformation("  - Permissions/Scopes: {Count} permissions", application.Permissions.Length);
                foreach (var perm in application.Permissions)
                {
                    _logger.LogInformation("    * {Permission}", perm);
                }
                
                // Extract scope names from permissions (handle both "scp:" and "oc_scp:" prefixes)
                // OpenIddict can use either format depending on version/configuration
                var scopeNames = application.Permissions
                    .Where(p => p.StartsWith("scp:") || p.StartsWith("oc_scp:"))
                    .Select(p => {
                        if (p.StartsWith("oc_scp:"))
                            return p.Substring("oc_scp:".Length);
                        else if (p.StartsWith("scp:"))
                            return p.Substring("scp:".Length);
                        return p;
                    })
                    .ToList();
                
                _logger.LogInformation("  - Extracted {ScopeCount} scope names: [{Scopes}]", 
                    scopeNames.Count, 
                    string.Join(", ", scopeNames));
                
                if (scopeNames.Count == 0)
                {
                    _logger.LogError("[ApplicationStore.CreateAsync] ✗ NO SCOPES EXTRACTED! This will result in empty AllowedScopes in database!");
                    _logger.LogError("[ApplicationStore.CreateAsync] All permissions: [{Permissions}]", 
                        string.Join(", ", application.Permissions));
                    _logger.LogError("[ApplicationStore.CreateAsync] Checking for scope prefixes: scp: or oc_scp:");
                }
                
                // Set the ID to the ClientId so OpenIddict can cache it
                application.Id = application.ClientId;
                
                // CRITICAL: Add to pending cache immediately so FindByClientIdAsync can retrieve it
                _pendingApplications.TryAdd(application.ClientId, application);
                
                _logger.LogInformation("[ApplicationStore.CreateAsync] Calling SpacetimeDB reducer RegisterOpenIdClient...");
                _logger.LogInformation("[ApplicationStore.CreateAsync] Reducer parameters:");
                _logger.LogInformation("  - clientId: {ClientId}", application.ClientId);
                _logger.LogInformation("  - displayName: {DisplayName}", application.DisplayName);
                _logger.LogInformation("  - redirectUris: [{Uris}]", string.Join(", ", application.RedirectUris));
                _logger.LogInformation("  - postLogoutRedirectUris: [{Uris}]", string.Join(", ", application.PostLogoutRedirectUris));
                _logger.LogInformation("  - allowedScopes: [{Scopes}]", string.Join(", ", scopeNames));
                _logger.LogInformation("  - consentType: {ConsentType}", application.ConsentType);
                _logger.LogInformation("  - clientType: {Type}", application.Type);
                
                conn.Reducers.RegisterOpenIdClient(
                    application.ClientId,
                    application.ClientSecret,
                    application.DisplayName,
                    application.RedirectUris.ToList(),
                    application.PostLogoutRedirectUris.ToList(),
                    scopeNames,
                    application.ConsentType,
                    application.Type
                );

                _logger.LogInformation("[ApplicationStore.CreateAsync] ✓ Reducer call completed for client {ClientId}", application.ClientId);
                _logger.LogInformation("[ApplicationStore.CreateAsync] Note: Data may not be in local cache until FrameTick processes the response");
                
                return default;
            }
            catch (Exception ex)
            {
                // Remove from pending cache on error
                _pendingApplications.TryRemove(application.ClientId, out _);
                
                _logger.LogError(ex, "[ApplicationStore.CreateAsync] ✗ Error creating client {ClientId}: {Message}", 
                    application.ClientId, ex.Message);
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
                _logger.LogInformation("=== [ApplicationStore.FindByClientIdAsync] Searching for client: {ClientId} ===", identifier);
                
                // CRITICAL: Check cache first for fast retrieval
                if (_applicationCache.TryGetValue(identifier, out var cachedApp))
                {
                    _logger.LogInformation("[ApplicationStore] ✓ FOUND client in cache: {ClientId}", identifier);
                    return new ValueTask<OpenIddictApplication?>(cachedApp);
                }
                
                // Check pending applications (just created, not yet synced)
                if (_pendingApplications.TryGetValue(identifier, out var pendingApp))
                {
                    _logger.LogInformation("[ApplicationStore] ✓ FOUND client in pending cache: {ClientId}", identifier);
                    return new ValueTask<OpenIddictApplication?>(pendingApp);
                }
                
                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("Failed to get SpacetimeDB connection");
                    throw new InvalidOperationException("SpacetimeDB connection is null");
                }
                
                // Log ALL clients in the database for debugging
                var allClients = conn.Db.OpenIdConnect.Iter().ToList();
                _logger.LogInformation("[ApplicationStore] Total clients in OpenIdConnect table: {Count}", allClients.Count);
                
                foreach (var c in allClients)
                {
                    _logger.LogInformation("[ApplicationStore] - Client: {ClientId}, IsActive: {IsActive}, DisplayName: {DisplayName}", 
                        c.ClientId, c.IsActive, c.DisplayName);
                }
                
                // Now search for the specific client
                var client = conn.Db.OpenIdConnect.Iter()
                    .Where(c => c.ClientId == identifier && c.IsActive)
                    .Select(c =>
                    {
                        // Convert scope names to OpenIddict permission format and add endpoint/grant permissions
                        var permissions = new List<string>();
                        
                        // Add endpoint permissions (using short prefixes that match runtime values)
                        permissions.Add("ept:authorization");  // Permissions.Endpoints.Authorization
                        permissions.Add("ept:token");          // Permissions.Endpoints.Token
                        permissions.Add("ept:logout");         // Permissions.Endpoints.Logout
                        permissions.Add("ept:revocation");     // Permissions.Endpoints.Revocation
                        
                        // Add grant type permissions
                        permissions.Add("gt:authorization_code");  // Permissions.GrantTypes.AuthorizationCode
                        permissions.Add("gt:refresh_token");       // Permissions.GrantTypes.RefreshToken
                        permissions.Add("gt:client_credentials");  // Permissions.GrantTypes.ClientCredentials
                        
                        // Add response type permissions
                        permissions.Add("rst:code");  // Permissions.ResponseTypes.Code
                        
                        // Add scope permissions (using short prefix that matches runtime values)
                        foreach (var scope in c.AllowedScopes)
                        {
                            permissions.Add($"scp:{scope}");
                        }
                        
                        return new OpenIddictApplication
                        {
                            Id = c.ClientId,
                            ClientId = c.ClientId,
                            ClientSecret = c.ClientSecret,
                            PostLogoutRedirectUris = ImmutableArray.Create(c.PostLogoutRedirectUris.ToArray()),
                            RedirectUris = ImmutableArray.Create(c.RedirectUris.ToArray()),
                            ConsentType = c.ConsentType,
                            Type = c.ClientType,
                            DisplayName = c.DisplayName,
                            Permissions = ImmutableArray.Create(permissions.ToArray()),
                        };
                    })
                    .FirstOrDefault();

                if (client != null)
                {
                    _logger.LogInformation("[ApplicationStore] ✓ FOUND client {ClientId} with {RedirectUriCount} redirect URIs and {ScopeCount} scopes", 
                        identifier, client.RedirectUris.Length, client.Permissions.Length);
                    
                    // CRITICAL: Add to cache for future lookups
                    _applicationCache.TryAdd(identifier, client);
                    
                    // Remove from pending since it's now in the database
                    _pendingApplications.TryRemove(identifier, out _);
                }
                else
                {
                    _logger.LogWarning("[ApplicationStore] ✗ NOT FOUND: Client {ClientId} not in database or not active", identifier);
                    
                    // Check if it exists but is inactive
                    var inactiveClient = conn.Db.OpenIdConnect.Iter()
                        .FirstOrDefault(c => c.ClientId == identifier);
                    
                    if (inactiveClient != null)
                    {
                        _logger.LogWarning("[ApplicationStore] Client {ClientId} exists but IsActive={IsActive}", 
                            identifier, inactiveClient.IsActive);
                    }
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
            
            // CRITICAL: For public clients (like desktop/mobile apps), return null for client secret
            // This tells OpenIddict not to require client authentication
            // Public clients use PKCE (code_challenge/code_verifier) for security instead
            if (application.Type == OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Public)
            {
                _logger.LogDebug("Client {ClientId} is public - returning null secret (PKCE will be used)", application.ClientId);
                return ValueTask.FromResult<string?>(null);
            }
            
            return ValueTask.FromResult<string?>(application.ClientSecret);
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
                
                _logger.LogInformation("[ApplicationStore.UpdateAsync] Client details:");
                _logger.LogInformation("  - ClientId: {ClientId}", application.ClientId);
                _logger.LogInformation("  - DisplayName: {DisplayName}", application.DisplayName);
                _logger.LogInformation("  - RedirectUris: {Count} URIs", application.RedirectUris.Length);
                _logger.LogInformation("  - Permissions: {Count} permissions", application.Permissions.Length);
                
                // Extract scope names from permissions (handle both "scp:" and "oc_scp:" prefixes)
                // OpenIddict can use either format depending on version/configuration
                var scopeNames = application.Permissions
                    .Where(p => p.StartsWith("scp:") || p.StartsWith("oc_scp:"))
                    .Select(p => {
                        if (p.StartsWith("oc_scp:"))
                            return p.Substring("oc_scp:".Length);
                        else if (p.StartsWith("scp:"))
                            return p.Substring("scp:".Length);
                        return p;
                    })
                    .ToList();
                
                _logger.LogInformation("  - Extracted {ScopeCount} scope names: [{Scopes}]", 
                    scopeNames.Count, 
                    string.Join(", ", scopeNames));
                
                if (scopeNames.Count == 0)
                {
                    _logger.LogError("[ApplicationStore.UpdateAsync] ✗ NO SCOPES EXTRACTED! This will result in empty AllowedScopes in database!");
                    _logger.LogError("[ApplicationStore.UpdateAsync] All permissions: [{Permissions}]", 
                        string.Join(", ", application.Permissions));
                    _logger.LogError("[ApplicationStore.UpdateAsync] Checking for scope prefixes: scp: or oc_scp:");
                }
                
                _logger.LogInformation("[ApplicationStore.UpdateAsync] Calling SpacetimeDB reducer UpdateOpenIdClient...");
                _logger.LogInformation("[ApplicationStore.UpdateAsync] Reducer parameters:");
                _logger.LogInformation("  - clientId: {ClientId}", application.ClientId);
                _logger.LogInformation("  - displayName: {DisplayName}", application.DisplayName);
                _logger.LogInformation("  - redirectUris: [{Uris}]", string.Join(", ", application.RedirectUris));
                _logger.LogInformation("  - postLogoutRedirectUris: [{Uris}]", string.Join(", ", application.PostLogoutRedirectUris));
                _logger.LogInformation("  - allowedScopes: [{Scopes}]", string.Join(", ", scopeNames));
                _logger.LogInformation("  - consentType: {ConsentType}", application.ConsentType);
                
                conn.Reducers.UpdateOpenIdClient(
                    application.ClientId,
                    application.ClientSecret,
                    application.DisplayName,
                    application.RedirectUris.ToList(),
                    application.PostLogoutRedirectUris.ToList(),
                    scopeNames,
                    application.ConsentType
                );

                // CRITICAL: Invalidate cache entry so it gets refreshed on next lookup
                _applicationCache.TryRemove(application.ClientId, out _);

                _logger.LogInformation("Successfully updated OpenID Connect client {ClientId}", application.ClientId);
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating OpenID Connect client {ClientId}", application.ClientId);
                throw;
            }
        }

        // --- Cache Management Methods ---
        
        /// <summary>
        /// Clears all cached application data. Useful for testing or when database is reset.
        /// </summary>
        public static void ClearCache()
        {
            _applicationCache.Clear();
            _pendingApplications.Clear();
        }

        /// <summary>
        /// Gets the current size of the application cache.
        /// </summary>
        public static int GetCacheSize()
        {
            return _applicationCache.Count;
        }

        /// <summary>
        /// Removes a specific application from the cache by ClientId.
        /// </summary>
        public static bool RemoveFromCache(string clientId)
        {
            if (string.IsNullOrEmpty(clientId)) return false;
            
            var removed = _applicationCache.TryRemove(clientId, out _);
            _pendingApplications.TryRemove(clientId, out _);
            
            return removed;
        }
    }
}
