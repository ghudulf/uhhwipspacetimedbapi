using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class TokenStore : IOpenIddictTokenStore<OpenIddictTokenDescriptor>
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<TokenStore> _logger;

        public TokenStore(ISpacetimeDBService spacetimeService, ILogger<TokenStore> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private DbConnection GetConnection()
        {
            var conn = _spacetimeService.GetConnection();
            if (conn == null) throw new InvalidOperationException("SpacetimeDB connection is not available.");
            return conn;
        }

        private async Task<DbConnection> EnsureConnectedAsync()
        {
            return GetConnection();
        }

        private OpenIddictTokenDescriptor MapToDescriptor(OpenIddictSpacetimeToken token)
        {
            var properties = DeserializeProperties(token.Properties);
            return new OpenIddictTokenDescriptor
            {
                ApplicationId = token.ApplicationClientId,
                AuthorizationId = token.AuthorizationId?.ToString(),
                CreationDate = token.CreationDate.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)token.CreationDate.Value) : null,
                ExpirationDate = token.ExpirationDate.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)token.ExpirationDate.Value) : null,
                Payload = token.Payload,
                ReferenceId = token.ReferenceId,
                Status = token.Status,
                Subject = token.Subject,
                Type = token.Type
            };
        }

        private static ImmutableDictionary<string, JsonElement> DeserializeProperties(string? json)
        {
            if (string.IsNullOrEmpty(json)) return ImmutableDictionary<string, JsonElement>.Empty;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.EnumerateObject().ToImmutableDictionary(p => p.Name, p => p.Value.Clone());
            }
            catch (JsonException)
            {
                return ImmutableDictionary<string, JsonElement>.Empty;
            }
        }

        private static string? SerializeProperties(ImmutableDictionary<string, JsonElement>? properties)
        {
            if (properties == null || properties.IsEmpty) return null;
            try
            {
                return JsonSerializer.Serialize(properties);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
        {
            try
            {
                var conn = await EnsureConnectedAsync();
                return conn.Db.OpenIddictSpacetimeToken.Iter().LongCount();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error counting tokens");
                throw;
            }
        }

        public async ValueTask<long> CountAsync<TResult>(Func<IQueryable<OpenIddictTokenDescriptor>, IQueryable<TResult>> query, CancellationToken cancellationToken)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var conn = await EnsureConnectedAsync();
            var tokens = conn.Db.OpenIddictSpacetimeToken.Iter();
            var descriptors = tokens.Select(MapToDescriptor);
            return query(descriptors.AsQueryable()).LongCount();
        }

        public async ValueTask CreateAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            var conn = await EnsureConnectedAsync();
            var tokenId = Guid.NewGuid().ToString();

            // Convert dates to Unix timestamps with explicit casting
            ulong? creationDate = descriptor.CreationDate.HasValue ? (ulong?)descriptor.CreationDate.Value.ToUnixTimeMilliseconds() : null;
            ulong? expirationDate = descriptor.ExpirationDate.HasValue ? (ulong?)descriptor.ExpirationDate.Value.ToUnixTimeMilliseconds() : null;
            ulong? redemptionDate = descriptor.RedemptionDate.HasValue ? (ulong?)descriptor.RedemptionDate.Value.ToUnixTimeMilliseconds() : null;

            conn.Reducers.CreateOidcToken(
                tokenId,
                uint.TryParse(descriptor.AuthorizationId, out var authId) ? authId : null,
                descriptor.ApplicationId,
                creationDate,
                expirationDate,
                descriptor.Payload,
                SerializeProperties(descriptor.Properties.ToImmutableDictionary()),
                redemptionDate,
                descriptor.ReferenceId,
                descriptor.Status,
                descriptor.Subject,
                descriptor.Type
            );
        }

        public async ValueTask DeleteAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            var conn = await EnsureConnectedAsync();
            var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                .FirstOrDefault(t => t.OpenIddictTokenId == descriptor.AuthorizationId);

            if (token != null)
            {
                conn.Reducers.DeleteOidcToken(token.Id);
            }
        }

        public async IAsyncEnumerable<OpenIddictTokenDescriptor> FindAsync(string subject, string client, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(subject)) throw new ArgumentException("Subject cannot be null or empty.", nameof(subject));
            if (string.IsNullOrEmpty(client)) throw new ArgumentException("Client cannot be null or empty.", nameof(client));

            var conn = await EnsureConnectedAsync();
            var tokens = conn.Db.OpenIddictSpacetimeToken.Iter()
                .Where(t => t.Subject == subject && t.ApplicationClientId == client);

            foreach (var token in tokens)
            {
                yield return MapToDescriptor(token);
            }
        }

        public async IAsyncEnumerable<OpenIddictTokenDescriptor> FindAsync(string subject, string client, string status, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(subject)) throw new ArgumentException("Subject cannot be null or empty.", nameof(subject));
            if (string.IsNullOrEmpty(client)) throw new ArgumentException("Client cannot be null or empty.", nameof(client));
            if (string.IsNullOrEmpty(status)) throw new ArgumentException("Status cannot be null or empty.", nameof(status));

            var conn = await EnsureConnectedAsync();
            var tokens = conn.Db.OpenIddictSpacetimeToken.Iter()
                .Where(t => t.Subject == subject && t.ApplicationClientId == client && t.Status == status);

            foreach (var token in tokens)
            {
                yield return MapToDescriptor(token);
            }
        }

        public async IAsyncEnumerable<OpenIddictTokenDescriptor> FindAsync(string subject, string client, string status, string type, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(subject)) throw new ArgumentException("Subject cannot be null or empty.", nameof(subject));
            if (string.IsNullOrEmpty(client)) throw new ArgumentException("Client cannot be null or empty.", nameof(client));
            if (string.IsNullOrEmpty(status)) throw new ArgumentException("Status cannot be null or empty.", nameof(status));
            if (string.IsNullOrEmpty(type)) throw new ArgumentException("Type cannot be null or empty.", nameof(type));

            var conn = await EnsureConnectedAsync();
            var tokens = conn.Db.OpenIddictSpacetimeToken.Iter()
                .Where(t => t.Subject == subject && t.ApplicationClientId == client && t.Status == status && t.Type == type);

            foreach (var token in tokens)
            {
                yield return MapToDescriptor(token);
            }
        }

        public async IAsyncEnumerable<OpenIddictTokenDescriptor> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

            var conn = await EnsureConnectedAsync();
            var tokens = conn.Db.OpenIddictSpacetimeToken.Iter()
                .Where(t => t.ApplicationClientId == identifier);

            foreach (var token in tokens)
            {
                yield return MapToDescriptor(token);
            }
        }

        public async IAsyncEnumerable<OpenIddictTokenDescriptor> FindByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

            var conn = await EnsureConnectedAsync();
            if (uint.TryParse(identifier, out var authId))
            {
                var tokens = conn.Db.OpenIddictSpacetimeToken.Iter()
                    .Where(t => t.AuthorizationId == authId);

                foreach (var token in tokens)
                {
                    yield return MapToDescriptor(token);
                }
            }
        }

        public async ValueTask<OpenIddictTokenDescriptor?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

            var conn = await EnsureConnectedAsync();
            var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                .FirstOrDefault(t => t.OpenIddictTokenId == identifier);

            return token != null ? MapToDescriptor(token) : null;
        }

        public async ValueTask<OpenIddictTokenDescriptor?> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

            var conn = await EnsureConnectedAsync();
            var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                .FirstOrDefault(t => t.ReferenceId == identifier);

            return token != null ? MapToDescriptor(token) : null;
        }

        public async IAsyncEnumerable<OpenIddictTokenDescriptor> FindBySubjectAsync(string subject, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(subject)) throw new ArgumentException("Subject cannot be null or empty.", nameof(subject));

            var conn = await EnsureConnectedAsync();
            var tokens = conn.Db.OpenIddictSpacetimeToken.Iter()
                .Where(t => t.Subject == subject);

            foreach (var token in tokens)
            {
                yield return MapToDescriptor(token);
            }
        }

        public async ValueTask<TResult?> GetAsync<TState, TResult>(
            Func<IQueryable<OpenIddictTokenDescriptor>, TState, IQueryable<TResult>> query,
            TState state, CancellationToken cancellationToken)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var conn = await EnsureConnectedAsync();
            var tokens = conn.Db.OpenIddictSpacetimeToken.Iter();
            var descriptors = tokens.Select(MapToDescriptor);
            return query(descriptors.AsQueryable(), state).FirstOrDefault();
        }

        public async ValueTask<string?> GetApplicationIdAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.ApplicationId);
        }

        public async ValueTask<string?> GetAuthorizationIdAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.AuthorizationId);
        }

        public async ValueTask<DateTimeOffset?> GetCreationDateAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.CreationDate);
        }

        public async ValueTask<DateTimeOffset?> GetExpirationDateAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.ExpirationDate);
        }

        public async ValueTask<string?> GetIdAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.AuthorizationId);
        }

        public async ValueTask<string?> GetPayloadAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.Payload);
        }

        public async ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.Properties.ToImmutableDictionary());
        }

        public async ValueTask<DateTimeOffset?> GetRedemptionDateAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.RedemptionDate);
        }

        public async ValueTask<string?> GetReferenceIdAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.ReferenceId);
        }

        public async ValueTask<string?> GetStatusAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.Status);
        }

        public async ValueTask<string?> GetSubjectAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.Subject);
        }

        public async ValueTask<string?> GetTypeAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return await Task.FromResult(descriptor.Type);
        }

        public ValueTask<OpenIddictTokenDescriptor> InstantiateAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<OpenIddictTokenDescriptor>(new OpenIddictTokenDescriptor());
        }

        public async IAsyncEnumerable<OpenIddictTokenDescriptor> ListAsync(int? count, int? offset, CancellationToken cancellationToken)
        {
            var conn = await EnsureConnectedAsync();
            var query = conn.Db.OpenIddictSpacetimeToken.Iter().AsQueryable();

            if (offset.HasValue)
            {
                query = query.Skip(offset.Value);
            }

            if (count.HasValue)
            {
                query = query.Take(count.Value);
            }

            foreach (var token in query)
            {
                yield return MapToDescriptor(token);
            }
        }

        public async IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
            Func<IQueryable<OpenIddictTokenDescriptor>, TState, IQueryable<TResult>> query,
            TState state, CancellationToken cancellationToken)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var conn = await EnsureConnectedAsync();
            var tokens = conn.Db.OpenIddictSpacetimeToken.Iter();
            var descriptors = tokens.Select(MapToDescriptor);
            var results = query(descriptors.AsQueryable(), state);

            foreach (var result in results)
            {
                yield return result;
            }
        }

        public async ValueTask PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
        {
            var conn = await EnsureConnectedAsync();
            var thresholdMs = (ulong)threshold.ToUnixTimeMilliseconds();
            conn.Reducers.PruneOidcTokens(thresholdMs);
        }

        public async ValueTask UpdateAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            var conn = await EnsureConnectedAsync();
            var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                .FirstOrDefault(t => t.OpenIddictTokenId == descriptor.AuthorizationId);

            if (token == null) throw new InvalidOperationException("Token not found.");

            conn.Reducers.UpdateOidcToken(
                token.Id,
                descriptor.ExpirationDate?.ToUnixTimeMilliseconds() is long ms ? (ulong?)ms : null,
                descriptor.Payload,
                SerializeProperties(descriptor.Properties.ToImmutableDictionary()),
                descriptor.RedemptionDate?.ToUnixTimeMilliseconds() is long mss ? (ulong?)mss : null,
                descriptor.Status
            );
        }

        // Implement the remaining Set* methods following the same pattern
        public ValueTask SetApplicationIdAsync(OpenIddictTokenDescriptor descriptor, string? identifier, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }

        public ValueTask SetAuthorizationIdAsync(OpenIddictTokenDescriptor descriptor, string? identifier, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }

        public ValueTask SetCreationDateAsync(OpenIddictTokenDescriptor descriptor, DateTimeOffset? date, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }

        public ValueTask SetExpirationDateAsync(OpenIddictTokenDescriptor descriptor, DateTimeOffset? date, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }

        public ValueTask SetPayloadAsync(OpenIddictTokenDescriptor descriptor, string? payload, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }

        public ValueTask SetPropertiesAsync(OpenIddictTokenDescriptor descriptor, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }

        public ValueTask SetRedemptionDateAsync(OpenIddictTokenDescriptor descriptor, DateTimeOffset? date, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }

        public ValueTask SetReferenceIdAsync(OpenIddictTokenDescriptor descriptor, string? identifier, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }

        public ValueTask SetStatusAsync(OpenIddictTokenDescriptor descriptor, string? status, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }

        public ValueTask SetSubjectAsync(OpenIddictTokenDescriptor descriptor, string? subject, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }

        public ValueTask SetTypeAsync(OpenIddictTokenDescriptor descriptor, string? type, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return default;
        }
    }
}



