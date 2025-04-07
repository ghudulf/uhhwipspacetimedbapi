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
            _spacetimeService = spacetimeService;
            _logger = logger;
        }

        public ValueTask<long> CountAsync(CancellationToken cancellationToken)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                var count = conn.Db.OpenIdConnect.Iter().Count(c => c.IsActive);
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
            throw new NotSupportedException("Custom queries are not supported by this store.");
        }

        public ValueTask CreateAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrEmpty(application.ClientId))
                    throw new ArgumentException("Client ID cannot be null or empty.", nameof(application));

                var conn = _spacetimeService.GetConnection();
                
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
            try
            {
                if (string.IsNullOrEmpty(application.ClientId))
                    throw new ArgumentException("Client ID cannot be null or empty.", nameof(application));

                var conn = _spacetimeService.GetConnection();
                conn.Reducers.RevokeOpenIdClient(application.ClientId);
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
            try
            {
                var conn = _spacetimeService.GetConnection();
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
            return FindByClientIdAsync(identifier, cancellationToken);
        }

        public IAsyncEnumerable<OpenIddictApplication> FindByPostLogoutRedirectUriAsync(string address, CancellationToken cancellationToken)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
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
            try
            {
                var conn = _spacetimeService.GetConnection();
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
            throw new NotSupportedException("Custom queries are not supported by this store.");
        }

        public ValueTask<string?> GetClientIdAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<string?>(application.ClientId);
        }

        public ValueTask<string?> GetClientSecretAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<string?>(application.ClientSecret);
        }

        public ValueTask<string?> GetClientTypeAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<string?>(application.Type);
        }

        public ValueTask<string?> GetConsentTypeAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<string?>(application.ConsentType);
        }

        public ValueTask<string?> GetDisplayNameAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<string?>(application.DisplayName);
        }

        public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<ImmutableDictionary<CultureInfo, string>>(application.DisplayNames);
        }

        public ValueTask<string?> GetIdAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<string?>(application.Id);
        }

        public ValueTask<ImmutableArray<string>> GetPermissionsAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<ImmutableArray<string>>(application.Permissions);
        }

        public ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<ImmutableArray<string>>(application.PostLogoutRedirectUris);
        }

        public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<ImmutableDictionary<string, JsonElement>>(application.Properties);
        }

        public ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<ImmutableArray<string>>(application.RedirectUris);
        }

        public ValueTask<ImmutableArray<string>> GetRequirementsAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            return new ValueTask<ImmutableArray<string>>(application.Requirements);
        }

        public ValueTask<OpenIddictApplication> InstantiateAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<OpenIddictApplication>(new OpenIddictApplication());
        }

        public IAsyncEnumerable<OpenIddictApplication> ListAsync(int? count, int? offset, CancellationToken cancellationToken)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
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
                    query = query.Skip(offset.Value);
                }

                if (count.HasValue)
                {
                    query = query.Take(count.Value);
                }

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
            throw new NotSupportedException("Custom queries are not supported by this store.");
        }

        public ValueTask SetClientIdAsync(OpenIddictApplication application, string? identifier, CancellationToken cancellationToken)
        {
            application.ClientId = identifier ?? string.Empty;
            return default;
        }

        public ValueTask SetClientSecretAsync(OpenIddictApplication application, string? secret, CancellationToken cancellationToken)
        {
            application.ClientSecret = secret ?? string.Empty;
            return default;
        }

        public ValueTask SetClientTypeAsync(OpenIddictApplication application, string? type, CancellationToken cancellationToken)
        {
            application.Type = type ?? string.Empty;
            return default;
        }

        public ValueTask SetConsentTypeAsync(OpenIddictApplication application, string? type, CancellationToken cancellationToken)
        {
            application.ConsentType = type ?? string.Empty;
            return default;
        }

        public ValueTask SetDisplayNameAsync(OpenIddictApplication application, string? name, CancellationToken cancellationToken)
        {
            application.DisplayName = name ?? string.Empty;
            return default;
        }

        public ValueTask SetDisplayNamesAsync(OpenIddictApplication application, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
        {
            application.DisplayNames = names;
            return default;
        }

        public ValueTask SetPermissionsAsync(OpenIddictApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken)
        {
            application.Permissions = permissions;
            return default;
        }

        public ValueTask SetPostLogoutRedirectUrisAsync(OpenIddictApplication application, ImmutableArray<string> addresses, CancellationToken cancellationToken)
        {
            application.PostLogoutRedirectUris = addresses;
            return default;
        }

        public ValueTask SetPropertiesAsync(OpenIddictApplication application, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
        {
            application.Properties = properties;
            return default;
        }

        public ValueTask SetRedirectUrisAsync(OpenIddictApplication application, ImmutableArray<string> addresses, CancellationToken cancellationToken)
        {
            application.RedirectUris = addresses;
            return default;
        }

        public ValueTask SetRequirementsAsync(OpenIddictApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken)
        {
            application.Requirements = requirements;
            return default;
        }

        public ValueTask UpdateAsync(OpenIddictApplication application, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrEmpty(application.ClientId))
                    throw new ArgumentException("Client ID cannot be null or empty.", nameof(application));

                var conn = _spacetimeService.GetConnection();
                conn.Reducers.UpdateOpenIdClient(
                    application.ClientId,
                    application.ClientSecret,
                    application.DisplayName,
                    application.RedirectUris.ToList(),
                    application.PostLogoutRedirectUris.ToList(),
                    application.Permissions.ToList(),
                    application.ConsentType
                );

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

