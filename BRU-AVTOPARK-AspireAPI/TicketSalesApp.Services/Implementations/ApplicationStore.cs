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
        public ulong Revision { get; set; }
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

        public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
        {
            return await _spacetimeService.ReadAsync(c => c.Db.OpenIdConnect.Iter().LongCount(x => x.IsActive), cancellationToken);
        }

        public ValueTask<long> CountAsync<TResult>(Func<IQueryable<OpenIddictApplication>, IQueryable<TResult>> query, CancellationToken cancellationToken)
        {
            _logger.LogWarning("CountAsync with custom query was called but is not supported");
            throw new NotSupportedException("Custom queries are not supported by this store.");
        }

        public async ValueTask CreateAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(application);
            ArgumentException.ThrowIfNullOrEmpty(application.ClientId);
            _logger.LogDebug("Creating OIDC application {ClientId}; redirectCount {RedirectCount}, permissionCount {PermissionCount}",
                application.ClientId, application.RedirectUris.Length, application.Permissions.Length);
            await PersistAsync(application, create: true, cancellationToken);
            application.Id = application.ClientId;
            _logger.LogInformation("OIDC application creation committed {ClientId}", application.ClientId);
        }

        public async ValueTask DeleteAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(application);
            await _spacetimeService.ExecuteReducerAsync(new Reducer.RevokeOpenIdClient(application.ClientId), _ => true, cancellationToken);
            _logger.LogInformation("OIDC application revocation committed {ClientId}", application.ClientId);
        }

        public async ValueTask<OpenIddictApplication?> FindByClientIdAsync(string identifier, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(identifier);
            var application = await _spacetimeService.ReadAsync(c => Map(c, c.Db.OpenIdConnect.ClientId.Find(identifier)), cancellationToken);
            _logger.LogDebug("OIDC application lookup {ClientId}; found {Found}, source committed subscription", identifier, application != null);
            return application;
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

        public async IAsyncEnumerable<OpenIddictApplication> FindByPostLogoutRedirectUriAsync(string address, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(address);
            var applications = await _spacetimeService.ReadAsync(c => c.Db.OpenIdConnect.Iter()
                .Where(x => x.IsActive && x.PostLogoutRedirectUris.Contains(address, StringComparer.Ordinal)).Select(x => Map(c, x)!).ToArray(), cancellationToken);
            _logger.LogDebug("OIDC PostLogoutRedirectUris match count {Count}", applications.Length);
            foreach (var application in applications) { cancellationToken.ThrowIfCancellationRequested(); yield return application; }
        }

        public async IAsyncEnumerable<OpenIddictApplication> FindByRedirectUriAsync(string address, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(address);
            var applications = await _spacetimeService.ReadAsync(c => c.Db.OpenIdConnect.Iter()
                .Where(x => x.IsActive && x.RedirectUris.Contains(address, StringComparer.Ordinal)).Select(x => Map(c, x)!).ToArray(), cancellationToken);
            _logger.LogDebug("OIDC RedirectUris match count {Count}", applications.Length);
            foreach (var application in applications) { cancellationToken.ThrowIfCancellationRequested(); yield return application; }
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

        public async IAsyncEnumerable<OpenIddictApplication> ListAsync(int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (count < 0 || offset < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var applications = await _spacetimeService.ReadAsync(c => c.Db.OpenIdConnect.Iter()
                .Where(x => x.IsActive).OrderBy(x => x.ClientId, StringComparer.Ordinal)
                .Skip(offset ?? 0).Take(count ?? int.MaxValue).Select(x => Map(c, x)!).ToArray(), cancellationToken);
            foreach (var application in applications) { cancellationToken.ThrowIfCancellationRequested(); yield return application; }
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
            application.Properties = properties.ToImmutableDictionary(x => x.Key, x => x.Value.Clone());
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

        public async ValueTask UpdateAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(application);
            await PersistAsync(application, create: false, cancellationToken);
            _logger.LogInformation("OIDC application update committed {ClientId}", application.ClientId);
        }

        private async Task PersistAsync(OpenIddictApplication application, bool create, CancellationToken cancellationToken)
        {
            try
            {
                application.Revision = await _spacetimeService.ExecuteReducerAsync(new Reducer.ReplaceOidcApplication(
                    application.ClientId, application.ClientSecret, application.DisplayName, application.RedirectUris.ToList(),
                    application.PostLogoutRedirectUris.ToList(), ScopeNames(application), application.ConsentType, application.Type,
                    JsonSerializer.Serialize(application.Permissions), JsonSerializer.Serialize(application.Requirements),
                    JsonSerializer.Serialize(application.DisplayNames.ToDictionary(x => x.Key.Name, x => x.Value)),
                    JsonSerializer.Serialize(application.Properties), application.Revision, create),
                    c => c.Db.OidcApplicationMetadata.ClientId.Find(application.ClientId)!.Revision, cancellationToken);
            }
            catch (ReducerRejectedException)
            {
                throw new OpenIddictExceptions.ConcurrencyException("Application update was rejected; reload the committed record.");
            }
        }

        private static List<string> ScopeNames(OpenIddictApplication application) => application.Permissions
            .Where(p => p.StartsWith("scp:", StringComparison.Ordinal) || p.StartsWith("oc_scp:", StringComparison.Ordinal))
            .Select(p => p[(p.IndexOf(':') + 1)..]).Distinct(StringComparer.Ordinal).ToList();

        private static OpenIddictApplication? Map(DbConnection connection, OpenIdConnect? client)
        {
            if (client is not { IsActive: true }) return null;
            // Preserve the existing 4.1 endpoint/grant contract for legacy registrations.
            var permissions = new[] { "ept:authorization", "ept:token", "ept:logout", "ept:revocation",
                "gt:authorization_code", "gt:refresh_token", "rst:code" }
                .Concat(client.AllowedScopes.Select(scope => "scp:" + scope)).ToImmutableArray();
            var metadata = connection.Db.OidcApplicationMetadata.ClientId.Find(client.ClientId);
            return new OpenIddictApplication
            {
                Id = client.ClientId, ClientId = client.ClientId, ClientSecret = client.ClientSecret,
                DisplayName = client.DisplayName, ConsentType = client.ConsentType, Type = client.ClientType,
                RedirectUris = client.RedirectUris.ToImmutableArray(), PostLogoutRedirectUris = client.PostLogoutRedirectUris.ToImmutableArray(),
                Revision = metadata?.Revision ?? 0,
                Permissions = string.IsNullOrEmpty(metadata?.Permissions) ? permissions : JsonSerializer.Deserialize<ImmutableArray<string>>(metadata.Permissions),
                Requirements = metadata == null ? [] : JsonSerializer.Deserialize<ImmutableArray<string>>(metadata.Requirements),
                DisplayNames = metadata == null ? ImmutableDictionary<CultureInfo, string>.Empty :
                    JsonSerializer.Deserialize<Dictionary<string, string>>(metadata.DisplayNames)!.ToImmutableDictionary(x => CultureInfo.GetCultureInfo(x.Key), x => x.Value),
                Properties = metadata == null ? ImmutableDictionary<string, JsonElement>.Empty :
                    JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metadata.Properties)!.ToImmutableDictionary(x => x.Key, x => x.Value.Clone())
            };
        }
    }
}
