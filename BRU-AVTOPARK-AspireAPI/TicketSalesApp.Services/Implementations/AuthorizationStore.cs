using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using SpacetimeDB;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces; // For ISpacetimeDBService
using System.Runtime.CompilerServices; // For EnumeratorCancellation
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Implementations
{
    public class AuthorizationStore : IOpenIddictAuthorizationStore<OpenIddictSpacetimeAuthorization>
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<AuthorizationStore> _logger;

        public AuthorizationStore(ISpacetimeDBService spacetimeService, ILogger<AuthorizationStore> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("SpacetimeDB AuthorizationStore initialized.");
        }

        private DbConnection GetConnection()
        {
            var conn = _spacetimeService.GetConnection();
            if (conn == null) throw new InvalidOperationException("SpacetimeDB connection is not available.");
            return conn;
        }

        // --- Create ---
        public virtual ValueTask CreateAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
        {
            if (authorization == null) throw new ArgumentNullException(nameof(authorization));
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var conn = GetConnection();
                var oidcAuthId = authorization.OpenIddictAuthorizationId ?? Guid.NewGuid().ToString();
                authorization.OpenIddictAuthorizationId = oidcAuthId;

                _logger.LogDebug("Calling CreateOidcAuthorization reducer for OIDC ID {OidcAuthId}", oidcAuthId);
                conn.Reducers.CreateOidcAuthorization(
                    oidcAuthId,
                    authorization.ApplicationClientId,
                    authorization.CreationDate,
                    authorization.Properties,
                    authorization.Scopes,
                    authorization.Status,
                    authorization.Subject,
                    authorization.Type);

                _logger.LogInformation("Reducer called to create authorization with OIDC ID: {OidcAuthId}", oidcAuthId);
                return default;
            }
            catch (Exception ex) { HandleError(ex, "creating"); throw; }
        }

        // --- Delete ---
        public virtual async ValueTask DeleteAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
        {
            if (authorization == null) throw new ArgumentNullException(nameof(authorization));
            cancellationToken.ThrowIfCancellationRequested();

            try {
                var conn = GetConnection();
                var entity = conn.Db.OpenIddictSpacetimeAuthorization.Iter()
                    .FirstOrDefault(a => a.OpenIddictAuthorizationId == authorization.OpenIddictAuthorizationId);
                
                if (entity == null) {
                    _logger.LogWarning("Attempted to delete authorization with OIDC ID {OidcAuthId}, but it wasn't found.", authorization.OpenIddictAuthorizationId);
                    return;
                }

                conn.Reducers.DeleteOidcAuthorization(entity.Id);
                _logger.LogInformation("Reducer called to delete authorization with ID: {Id}", entity.Id);
            }
            catch (Exception ex) { HandleError(ex, "deleting"); throw; }
        }

        // --- Find Methods ---
        public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindAsync(string subject, string client, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(subject)) throw new ArgumentException("Subject cannot be null or empty.", nameof(subject));
            if (string.IsNullOrEmpty(client)) throw new ArgumentException("Client cannot be null or empty.", nameof(client));
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Finding authorizations by Subject {Subject} and Client {Client}", subject, client);
            return ExecuteReadQuery(auth => auth.Subject == subject && auth.ApplicationClientId == client);
        }

        public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindAsync(string subject, string client, string status, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Finding authorizations by Subject {Subject}, Client {Client}, Status {Status}", subject, client, status);
            return ExecuteReadQuery(auth => auth.Subject == subject && auth.ApplicationClientId == client && auth.Status == status);
        }

        public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindAsync(string subject, string client, string status, string type, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Finding authorizations by Subject {Subject}, Client {Client}, Status {Status}, Type {Type}", subject, client, status, type);
            return ExecuteReadQuery(auth => auth.Subject == subject && auth.ApplicationClientId == client && auth.Status == status && auth.Type == type);
        }

        public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindAsync(string subject, string client, string status, string type, ImmutableArray<string> scopes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Finding authorizations by Subject {Subject}, Client {Client}, Status {Status}, Type {Type}, Scopes {Scopes}", subject, client, status, type, string.Join(",", scopes));

            var conn = GetConnection();
            var candidates = conn.Db.OpenIddictSpacetimeAuthorization.Iter()
                .Where(auth => auth.Subject == subject && auth.ApplicationClientId == client &&
                        auth.Status == status && auth.Type == type)
                .ToList();

            var results = candidates.Where(auth => {
                var storedScopes = DeserializeScopes(auth.Scopes);
                return scopes.All(requestedScope => storedScopes.Contains(requestedScope));
            });

            return GetAsyncEnumerable(results, cancellationToken);
        }

        public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Finding authorizations by ApplicationClientId {ClientId}", identifier);
            return ExecuteReadQuery(auth => auth.ApplicationClientId == identifier);
        }

        public virtual async ValueTask<OpenIddictSpacetimeAuthorization?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Finding authorization by OpenIddict ID {OidcAuthId}", identifier);
            try {
                await Task.Yield();
                var conn = GetConnection();
                return conn.Db.OpenIddictSpacetimeAuthorization.Iter()
                    .FirstOrDefault(auth => auth.OpenIddictAuthorizationId == identifier);
            }
            catch (Exception ex) { HandleError(ex, $"finding authorization by OIDC ID {identifier}"); throw; }
        }

        public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindBySubjectAsync(string subject, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(subject)) throw new ArgumentException("Subject cannot be null or empty.", nameof(subject));
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Finding authorizations by Subject {Subject}", subject);
            return ExecuteReadQuery(auth => auth.Subject == subject);
        }

        // --- Get Properties ---
        public virtual ValueTask<string?> GetApplicationIdAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
            => new(authorization?.ApplicationClientId);

        public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
            => new(authorization?.CreationDate != null ? DateTimeOffset.FromUnixTimeMilliseconds((long)authorization.CreationDate.Value) : null);

        public virtual ValueTask<string?> GetIdAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
            => new(authorization?.OpenIddictAuthorizationId);

        public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
            => new(DeserializeProperties(authorization?.Properties));

        public virtual ValueTask<ImmutableArray<string>> GetScopesAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
            => new(DeserializeScopes(authorization?.Scopes));

        public virtual ValueTask<string?> GetStatusAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
            => new(authorization?.Status);

        public virtual ValueTask<string?> GetSubjectAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
            => new(authorization?.Subject);

        public virtual ValueTask<string?> GetTypeAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
            => new(authorization?.Type);

        // --- Instantiate ---
        public virtual ValueTask<OpenIddictSpacetimeAuthorization> InstantiateAsync(CancellationToken cancellationToken)
        {
            try {
                return new ValueTask<OpenIddictSpacetimeAuthorization>(new OpenIddictSpacetimeAuthorization());
            }
            catch (Exception exception) {
                return new ValueTask<OpenIddictSpacetimeAuthorization>(Task.FromException<OpenIddictSpacetimeAuthorization>(exception));
            }
        }

        // --- List ---
        public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> ListAsync(int? count, int? offset, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Listing authorizations with Count {Count}, Offset {Offset}", count, offset);
            try {
                var conn = GetConnection();
                var query = conn.Db.OpenIddictSpacetimeAuthorization.Iter().AsQueryable();

                if (offset.HasValue) query = query.Skip(offset.Value);
                if (count.HasValue) query = query.Take(count.Value);

                var results = query.ToList();
                return GetAsyncEnumerable(results, cancellationToken);
            }
            catch (Exception ex) { HandleError(ex, "listing authorizations"); throw; }
        }

        public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<OpenIddictSpacetimeAuthorization>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)
        {
            _logger.LogWarning("ListAsync with custom query delegate is used - applying in memory. This can be inefficient.");
            try {
                var conn = GetConnection();
                var allAuthorizations = conn.Db.OpenIddictSpacetimeAuthorization.Iter().AsQueryable();
                var results = query(allAuthorizations, state).ToList();
                return GetAsyncEnumerable(results, cancellationToken);
            }
            catch (Exception ex) { HandleError(ex, "listing authorizations with custom query"); throw; }
        }

        // --- Prune ---
        public virtual ValueTask PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Pruning authorizations created before {Threshold}", threshold);
            try {
                var conn = GetConnection();
                ulong thresholdMs = (ulong)threshold.ToUnixTimeMilliseconds();
                conn.Reducers.PruneOidcAuthorizations(thresholdMs);
                _logger.LogInformation("Reducer called to prune authorizations before {Threshold}", threshold);
                return default;
            }
            catch(Exception ex) { HandleError(ex, $"pruning authorizations before {threshold}"); throw; }
        }

        // --- Set Properties ---
        public virtual ValueTask SetApplicationIdAsync(OpenIddictSpacetimeAuthorization authorization, string? identifier, CancellationToken cancellationToken)
        { authorization.ApplicationClientId = identifier; return default; }

        public virtual ValueTask SetCreationDateAsync(OpenIddictSpacetimeAuthorization authorization, DateTimeOffset? date, CancellationToken cancellationToken)
        { authorization.CreationDate = date != null ? (ulong)date.Value.ToUnixTimeMilliseconds() : null; return default; }

        public virtual ValueTask SetPropertiesAsync(OpenIddictSpacetimeAuthorization authorization, ImmutableDictionary<string, JsonElement>? properties, CancellationToken cancellationToken)
        { authorization.Properties = SerializeProperties(properties, authorization.OpenIddictAuthorizationId); return default; }

        public virtual ValueTask SetScopesAsync(OpenIddictSpacetimeAuthorization authorization, ImmutableArray<string> scopes, CancellationToken cancellationToken)
        { authorization.Scopes = SerializeScopes(scopes, authorization.OpenIddictAuthorizationId); return default; }

        public virtual ValueTask SetStatusAsync(OpenIddictSpacetimeAuthorization authorization, string? status, CancellationToken cancellationToken)
        { authorization.Status = status; return default; }

        public virtual ValueTask SetSubjectAsync(OpenIddictSpacetimeAuthorization authorization, string? subject, CancellationToken cancellationToken)
        { authorization.Subject = subject; return default; }

        public virtual ValueTask SetTypeAsync(OpenIddictSpacetimeAuthorization authorization, string? type, CancellationToken cancellationToken)
        { authorization.Type = type; return default; }

        // --- Update ---
        public virtual async ValueTask UpdateAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
        {
            if (authorization == null) throw new ArgumentNullException(nameof(authorization));
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var conn = GetConnection();
                var entity = conn.Db.OpenIddictSpacetimeAuthorization.Iter()
                    .FirstOrDefault(a => a.OpenIddictAuthorizationId == authorization.OpenIddictAuthorizationId);

                if (entity == null)
                {
                    _logger.LogError("Cannot update authorization: Authorization with OIDC ID {OidcAuthId} not found.", authorization.OpenIddictAuthorizationId);
                    throw new InvalidOperationException("Authorization not found for update.");
                }

                _logger.LogDebug("Calling UpdateOidcAuthorization reducer for ID {Id}", entity.Id);
                conn.Reducers.UpdateOidcAuthorization(entity.Id, authorization.Properties, authorization.Scopes, authorization.Status);
                _logger.LogInformation("Reducer called to update authorization with ID: {Id}", entity.Id);
            }
            catch (Exception ex) { HandleError(ex, "updating authorization"); throw; }
        }

        // --- Helper Methods ---
        private IAsyncEnumerable<OpenIddictSpacetimeAuthorization> ExecuteReadQuery(Func<OpenIddictSpacetimeAuthorization, bool> predicate)
        {
            try {
                var conn = GetConnection();
                var results = conn.Db.OpenIddictSpacetimeAuthorization.Iter().Where(predicate).ToList();
                return GetAsyncEnumerable(results);
            }
            catch (Exception ex) { HandleError(ex, "executing read query"); throw; }
        }

        private async IAsyncEnumerable<T> GetAsyncEnumerable<T>(IEnumerable<T> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var item in items) {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return item;
            }
        }

        private static ImmutableDictionary<string, JsonElement> DeserializeProperties(string? json)
        {
            if (string.IsNullOrEmpty(json)) return ImmutableDictionary<string, JsonElement>.Empty;
            try {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.EnumerateObject().ToImmutableDictionary(p => p.Name, p => p.Value.Clone());
            }
            catch (JsonException) { return ImmutableDictionary<string, JsonElement>.Empty; }
        }

        private static ImmutableArray<string> DeserializeScopes(string? json)
        {
            if (string.IsNullOrEmpty(json)) return ImmutableArray<string>.Empty;
            try {
                var list = JsonSerializer.Deserialize<List<string>>(json);
                return list?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
            }
            catch (JsonException) { return ImmutableArray<string>.Empty; }
        }

        private static string? SerializeProperties(ImmutableDictionary<string, JsonElement>? properties, string? authIdForLog)
        {
            if (properties == null || properties.IsEmpty) return null;
            try { return JsonSerializer.Serialize(properties); }
            catch (JsonException) { return null; }
        }

        private static string? SerializeScopes(ImmutableArray<string> scopes, string? authIdForLog)
        {
            if (scopes.IsDefaultOrEmpty) return null;
            try { return JsonSerializer.Serialize(scopes); }
            catch (JsonException) { return null; }
        }

        private void HandleError(Exception ex, string operation)
        {
            _logger.LogError(ex, "Error {Operation} in SpacetimeDB AuthorizationStore.", operation);
        }

        // Required interface methods
        public virtual ValueTask<long> CountAsync<TResult>(Func<IQueryable<OpenIddictSpacetimeAuthorization>, IQueryable<TResult>> query, CancellationToken cancellationToken)
        {
            try
            {
                var conn = GetConnection();
                var allAuthorizations = conn.Db.OpenIddictSpacetimeAuthorization.Iter().AsQueryable();
                return new ValueTask<long>(query(allAuthorizations).LongCount());
            }
            catch (Exception ex)
            {
                HandleError(ex, "counting authorizations");
                throw;
            }
        }

        public virtual ValueTask<TResult?> GetAsync<TState, TResult>(
            Func<IQueryable<OpenIddictSpacetimeAuthorization>, TState, IQueryable<TResult>> query,
            TState state, CancellationToken cancellationToken)
        {
            try
            {
                var conn = GetConnection();
                var allAuthorizations = conn.Db.OpenIddictSpacetimeAuthorization.Iter().AsQueryable();
                return new ValueTask<TResult?>(query(allAuthorizations, state).FirstOrDefault());
            }
            catch (Exception ex)
            {
                HandleError(ex, "getting authorization with custom query");
                throw;
            }
        }

        public virtual ValueTask<long> CountAsync(CancellationToken cancellationToken)
        {
            try
            {
                var conn = GetConnection();
                return new ValueTask<long>(conn.Db.OpenIddictSpacetimeAuthorization.Iter().LongCount());
            }
            catch (Exception ex)
            {
                HandleError(ex, "counting all authorizations");
                throw;
            }
        }
    }
}
