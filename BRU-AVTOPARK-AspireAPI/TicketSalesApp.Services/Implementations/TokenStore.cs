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

            _logger.LogInformation("TokenStore.CreateAsync called");
            _logger.LogInformation("Token Type: {Type}, Status: {Status}, Subject: {Subject}", 
                descriptor.Type, descriptor.Status, descriptor.Subject);

            if (string.IsNullOrEmpty(descriptor.Type))
            {
                _logger.LogError("Token Type is null or empty - this should have been set by OpenIddict");
                throw new InvalidOperationException("Token Type must be set before creating a token");
            }

            if (string.IsNullOrEmpty(descriptor.Status))
            {
                _logger.LogError("Token Status is null or empty - this should have been set by OpenIddict");
                throw new InvalidOperationException("Token Status must be set before creating a token");
            }

            var conn = await EnsureConnectedAsync();
            var tokenId = Guid.NewGuid().ToString();
            
            // CRITICAL: Set the token ID on the descriptor so OpenIddict can extract it for caching
            // OpenIddict will call GetIdAsync on this descriptor after CreateAsync completes
            // We store the ID in the ReferenceId property which OpenIddict uses as the identifier
            if (string.IsNullOrEmpty(descriptor.ReferenceId))
            {
                descriptor.ReferenceId = tokenId;
            }
            else
            {
                tokenId = descriptor.ReferenceId;
            }

            _logger.LogInformation("Creating token with ID: {TokenId}", tokenId);

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
            
            _logger.LogInformation("Token reducer called, waiting for SpacetimeDB to confirm creation...");

            // CRITICAL: Wait for SpacetimeDB reducer to complete before returning
            // Poll the database to confirm the token exists before OpenIddict tries to use it
            const int maxAttempts = 50; // 5 seconds total (50 * 100ms)
            const int delayMs = 100;
            
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                await Task.Delay(delayMs, cancellationToken);
                
                var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                    .FirstOrDefault(t => t.ReferenceId == tokenId);
                
                if (token != null)
                {
                    _logger.LogInformation("Token confirmed in database after {Attempts} attempts ({Ms}ms): {TokenId}", 
                        attempt + 1, (attempt + 1) * delayMs, tokenId);
                    return;
                }
            }
            
            _logger.LogWarning("Token not confirmed in database after {MaxAttempts} attempts ({Ms}ms): {TokenId}", 
                maxAttempts, maxAttempts * delayMs, tokenId);
        }

        public async ValueTask DeleteAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            var conn = await EnsureConnectedAsync();
            // Use ReferenceId to find the token since that's our token identifier
            var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                .FirstOrDefault(t => t.ReferenceId == descriptor.ReferenceId);

            if (token != null)
            {
                _logger.LogInformation("Deleting token with ReferenceId: {ReferenceId}", descriptor.ReferenceId);
                conn.Reducers.DeleteOidcToken(token.Id);
            }
            else
            {
                _logger.LogWarning("Token not found for deletion with ReferenceId: {ReferenceId}", descriptor.ReferenceId);
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

            _logger.LogDebug("FindByIdAsync called with identifier: {Identifier}", identifier);
            
            var conn = await EnsureConnectedAsync();
            // Use ReferenceId to find tokens since that's what we use as the token ID
            var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                .FirstOrDefault(t => t.ReferenceId == identifier);

            if (token != null)
            {
                _logger.LogDebug("Token found with ReferenceId: {ReferenceId}", identifier);
            }
            else
            {
                _logger.LogDebug("Token not found with ReferenceId: {ReferenceId}", identifier);
            }

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
            // CRITICAL: Return ReferenceId which contains the token ID, not AuthorizationId
            // OpenIddict uses this to extract the token identifier for caching
            var tokenId = descriptor.ReferenceId;
            _logger.LogDebug("GetIdAsync called - returning token ID: {TokenId}", tokenId);
            return await Task.FromResult(tokenId);
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
            // Return a descriptor with default values
            // OpenIddict will populate the actual values before calling CreateAsync
            var descriptor = new OpenIddictTokenDescriptor
            {
                // Set default values to pass OpenIddict's validation
                // These will be overridden by OpenIddict's handlers
                Type = "unknown", // Will be set to actual type by OpenIddict
                Status = "valid"  // Will be set to actual status by OpenIddict
            };
            
            return new ValueTask<OpenIddictTokenDescriptor>(descriptor);
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
            // Use ReferenceId to find the token since that's our token identifier
            var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                .FirstOrDefault(t => t.ReferenceId == descriptor.ReferenceId);

            if (token == null)
            {
                _logger.LogError("Token not found for update with ReferenceId: {ReferenceId}", descriptor.ReferenceId);
                throw new InvalidOperationException("Token not found.");
            }

            _logger.LogInformation("Updating token with ReferenceId: {ReferenceId}", descriptor.ReferenceId);

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



