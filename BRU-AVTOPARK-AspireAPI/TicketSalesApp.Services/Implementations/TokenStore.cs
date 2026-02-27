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
        // Bidirectional cache for token ID mapping - STATIC to persist across requests
        // Maps: current ReferenceId (authorization code) → internal ID (GUID)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _referenceIdToInternalId = new();
        // Maps: internal ID (GUID) → current ReferenceId (authorization code)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _internalIdToReferenceId = new();
        // Maps: internal ID (GUID) → database ID (uint)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, uint> _internalIdToDatabaseId = new();
        // Maps: internal ID (GUID) → Properties (for tokens being created/updated)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImmutableDictionary<string, JsonElement>> _internalIdToProperties = new();

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
            
            // Build the properties dictionary first
            var propsBuilder = ImmutableDictionary.CreateBuilder<string, JsonElement>();
            if (properties != null && !properties.IsEmpty)
            {
                foreach (var prop in properties)
                {
                    propsBuilder.Add(prop.Key, prop.Value);
                }
            }
            
            var descriptor = new OpenIddictTokenDescriptor
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
            
            // CRITICAL: Use reflection to set the Properties since it's read-only
            // OpenIddict stores important metadata in Properties that it needs for validation (including PKCE data)
            if (propsBuilder.Count > 0)
            {
                _logger.LogInformation("MapToDescriptor: Attempting to set {Count} properties via reflection for token {RefId}", 
                    propsBuilder.Count, token.ReferenceId);
                
                // Log the properties being restored for PKCE debugging
                foreach (var kvp in propsBuilder)
                {
                    var valueStr = kvp.Value.ValueKind == JsonValueKind.String 
                        ? kvp.Value.GetString() 
                        : kvp.Value.ToString();
                    _logger.LogInformation("  Restoring property: {Key} = {Value}", kvp.Key, valueStr);
                }
                    
                var propsField = typeof(OpenIddictTokenDescriptor).GetProperty("Properties");
                if (propsField != null)
                {
                    _logger.LogInformation("MapToDescriptor: Found Properties property, checking if it has a setter");
                    // Properties has a private setter, we can use it via reflection
                    var immutableProps = propsBuilder.ToImmutable();
                    propsField.SetValue(descriptor, immutableProps);
                    _logger.LogInformation("MapToDescriptor: Successfully set properties via reflection");
                    
                    // Verify it worked
                    var verifyProps = descriptor.Properties;
                    _logger.LogInformation("MapToDescriptor: Verification - descriptor.Properties.Count = {Count}", verifyProps.Count);
                }
                else
                {
                    _logger.LogError("MapToDescriptor: Could not find Properties property via reflection!");
                }
            }
            else
            {
                _logger.LogInformation("MapToDescriptor: No properties to restore for token {RefId}", token.ReferenceId);
            }
            
            return descriptor;
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

            _logger.LogInformation("=== TokenStore.CreateAsync called ===");
            _logger.LogInformation("Token Type: {Type}, Status: {Status}, Subject: {Subject}", 
                descriptor.Type, descriptor.Status, descriptor.Subject);
            _logger.LogInformation("ApplicationId: {AppId}, AuthorizationId: {AuthId}", 
                descriptor.ApplicationId, descriptor.AuthorizationId);
            _logger.LogInformation("Payload length: {Length}, ReferenceId: {RefId}", 
                descriptor.Payload?.Length ?? 0, descriptor.ReferenceId);
            _logger.LogInformation("CreationDate: {Created}, ExpirationDate: {Expires}", 
                descriptor.CreationDate, descriptor.ExpirationDate);

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
            var internalId = tokenId; // Store the internal ID for later lookups
            
            // CRITICAL: Set the token ID on the descriptor so OpenIddict can extract it for caching
            // OpenIddict will call GetIdAsync on this descriptor after CreateAsync completes
            if (string.IsNullOrEmpty(descriptor.ReferenceId))
            {
                descriptor.ReferenceId = tokenId;
            }
            else
            {
                tokenId = descriptor.ReferenceId;
            }

            _logger.LogInformation("Creating token with ID: {TokenId}, Internal ID: {InternalId}", tokenId, internalId);
            
            // Cache the bidirectional mapping
            _referenceIdToInternalId[tokenId] = internalId;
            _internalIdToReferenceId[internalId] = tokenId;

            // Convert dates to Unix timestamps with explicit casting
            ulong? creationDate = descriptor.CreationDate.HasValue ? (ulong?)descriptor.CreationDate.Value.ToUnixTimeMilliseconds() : null;
            ulong? expirationDate = descriptor.ExpirationDate.HasValue ? (ulong?)descriptor.ExpirationDate.Value.ToUnixTimeMilliseconds() : null;
            ulong? redemptionDate = descriptor.RedemptionDate.HasValue ? (ulong?)descriptor.RedemptionDate.Value.ToUnixTimeMilliseconds() : null;

            // CRITICAL: Resolve AuthorizationId from GUID string to internal database ID
            uint? authorizationInternalId = null;
            if (!string.IsNullOrEmpty(descriptor.AuthorizationId))
            {
                _logger.LogInformation("Resolving AuthorizationId GUID: {AuthGuid}", descriptor.AuthorizationId);
                var authorization = conn.Db.OpenIddictSpacetimeAuthorization.Iter()
                    .ToList()
                    .FirstOrDefault(a => a.OpenIddictAuthorizationId == descriptor.AuthorizationId);
                
                if (authorization != null)
                {
                    authorizationInternalId = authorization.Id;
                    _logger.LogInformation("✓ Resolved AuthorizationId {AuthGuid} to internal ID: {InternalId}", 
                        descriptor.AuthorizationId, authorizationInternalId);
                }
                else
                {
                    _logger.LogError("✗ Authorization NOT found for GUID: {AuthGuid}", descriptor.AuthorizationId);
                }
            }
            
            _logger.LogInformation("Calling CreateOidcToken reducer with:");
            _logger.LogInformation("  - TokenId: {TokenId}", internalId);
            _logger.LogInformation("  - AuthorizationId (GUID): {AuthGuid}", descriptor.AuthorizationId);
            _logger.LogInformation("  - AuthorizationId (Internal DB ID): {AuthInternalId}", authorizationInternalId);
            _logger.LogInformation("  - ApplicationId: {AppId}", descriptor.ApplicationId);
            _logger.LogInformation("  - Subject: {Subject}", descriptor.Subject ?? "(null)");
            _logger.LogInformation("  - Type: {Type}", descriptor.Type);
            _logger.LogInformation("  - Status: {Status}", descriptor.Status);
            _logger.LogInformation("  - Payload present: {HasPayload}", !string.IsNullOrEmpty(descriptor.Payload));

            conn.Reducers.CreateOidcToken(
                internalId, // Use internal ID as the database ID
                authorizationInternalId, // Use the resolved internal database ID
                descriptor.ApplicationId,
                creationDate,
                expirationDate,
                descriptor.Payload,
                SerializeProperties(descriptor.Properties.ToImmutableDictionary()),
                redemptionDate,
                tokenId, // Store the token ID as ReferenceId
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
                
                try
                {
                    // Materialize to list to avoid "Collection was modified" exception
                    var token = conn.Db.OpenIddictSpacetimeToken.Iter().ToList().FirstOrDefault(t => t.ReferenceId == internalId);
                    
                    if (token != null)
                    {
                        _logger.LogInformation("Token confirmed in database after {Attempts} attempts ({Ms}ms): {TokenId}", 
                            attempt + 1, (attempt + 1) * delayMs, internalId);
                        _logger.LogInformation("Confirmed token - Type: {Type}, Subject: {Subject}, Payload length: {Length}", 
                            token.Type, token.Subject, token.Payload?.Length ?? 0);
                        
                        // Cache the database ID for future lookups
                        _internalIdToDatabaseId[internalId] = token.Id;
                        _logger.LogInformation("Cached database ID mapping: {InternalId} -> {DbId}", internalId, token.Id);
                        
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error querying database during polling attempt {Attempt}: {Message}", 
                        attempt + 1, ex.Message);
                    // Continue polling despite error
                }
            }
            
            _logger.LogWarning("Token not confirmed in database after {MaxAttempts} attempts ({Ms}ms): {TokenId}", 
                maxAttempts, maxAttempts * delayMs, internalId);
        }

        public async ValueTask DeleteAsync(OpenIddictTokenDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            var conn = await EnsureConnectedAsync();
            // Use ReferenceId to find the token since that's our token identifier
            var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                .ToList()
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

            _logger.LogInformation("=== TokenStore.FindByIdAsync called with identifier: {Identifier} ===", identifier);
            
            var conn = await EnsureConnectedAsync();
            
            // First try to find by ReferenceId directly
            var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                .ToList()
                .FirstOrDefault(t => t.ReferenceId == identifier);

            if (token != null)
            {
                _logger.LogInformation("Token found with ReferenceId: {ReferenceId}", identifier);
                _logger.LogInformation("Token details - Type: {Type}, Subject: {Subject}, Status: {Status}", 
                    token.Type, token.Subject, token.Status);
                _logger.LogInformation("Token Payload length: {Length}, AuthorizationId: {AuthId}, ApplicationId: {AppId}", 
                    token.Payload?.Length ?? 0, token.AuthorizationId, token.ApplicationClientId);
                
                var descriptor = MapToDescriptor(token);
                if (descriptor != null)
                {
                    _logger.LogInformation("Mapped descriptor - Type: {Type}, Subject: {Subject}, Status: {Status}", 
                        descriptor.Type, descriptor.Subject, descriptor.Status);
                    _logger.LogInformation("Descriptor Payload length: {Length}", descriptor.Payload?.Length ?? 0);
                }
                return descriptor;
            }
            
            // Strategy 2: Identifier might be an internal ID - look up the current ReferenceId
            _logger.LogInformation("Token not found by ReferenceId directly, checking if identifier is internal ID: {Identifier}", identifier);
            if (_internalIdToReferenceId.TryGetValue(identifier, out var currentReferenceId))
            {
                _logger.LogInformation("✓ Found mapping: Internal ID {InternalId} -> Current ReferenceId {CurrentRefId}", 
                    identifier, currentReferenceId);
                
                // Now search database using the current ReferenceId
                token = conn.Db.OpenIddictSpacetimeToken.Iter()
                    .ToList()
                    .FirstOrDefault(t => t.ReferenceId == currentReferenceId);
                    
                if (token != null)
                {
                    _logger.LogInformation("✓ Found token in database using current ReferenceId: {CurrentRefId}", currentReferenceId);
                    _logger.LogInformation("Token DB ID: {DbId}, Type: {Type}, Subject: {Subject}, Payload length: {Length}", 
                        token.Id, token.Type, token.Subject, token.Payload?.Length ?? 0);
                    
                    var descriptor = MapToDescriptor(token);
                    _logger.LogInformation("Returning descriptor with ReferenceId: {RefId}, Payload length: {Length}", 
                        descriptor.ReferenceId, descriptor.Payload?.Length ?? 0);
                    return descriptor;
                }
                else
                {
                    _logger.LogError("✗ Token NOT found in database even with current ReferenceId: {CurrentRefId}", currentReferenceId);
                }
            }
            else
            {
                _logger.LogWarning("✗ No mapping found for internal ID: {Identifier}", identifier);
            }
            
            _logger.LogWarning("Token not found with identifier: {Identifier}", identifier);
            return null;
        }

        public async ValueTask<OpenIddictTokenDescriptor?> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

            _logger.LogInformation("=== FindByReferenceIdAsync called with identifier: {Identifier} ===", identifier);

            var conn = await EnsureConnectedAsync();
            
            // Log all tokens to debug ReferenceId mismatch
            var allTokens = conn.Db.OpenIddictSpacetimeToken.Iter().ToList();
            _logger.LogInformation("Total tokens in database: {Count}", allTokens.Count);
            foreach (var t in allTokens)
            {
                _logger.LogInformation("Token DB ID: {DbId}, ReferenceId: {RefId}, Type: {Type}, Subject: {Subject}", 
                    t.Id, t.ReferenceId, t.Type, t.Subject);
            }
            
            var token = conn.Db.OpenIddictSpacetimeToken.Iter()
                .ToList()
                .FirstOrDefault(t => t.ReferenceId == identifier);

            if (token != null)
            {
                _logger.LogInformation("✓ Found token with ReferenceId: {RefId}", identifier);
                return MapToDescriptor(token);
            }
            else
            {
                _logger.LogWarning("✗ Token NOT found with ReferenceId: {RefId}", identifier);
                _logger.LogInformation("Checking cache for identifier: {Identifier}", identifier);
                if (_referenceIdToInternalId.TryGetValue(identifier, out var internalId))
                {
                    _logger.LogInformation("Found in cache, internal ID: {InternalId}, trying to find by internal ID", internalId);
                    token = conn.Db.OpenIddictSpacetimeToken.Iter()
                        .ToList()
                        .FirstOrDefault(t => t.ReferenceId == internalId);
                    if (token != null)
                    {
                        _logger.LogInformation("✓ Found token by internal ID from cache");
                        var descriptor = MapToDescriptor(token);
                        // CRITICAL: Update the descriptor's ReferenceId to match what was requested
                        // This is necessary because the database still has the old internal ID
                        // but OpenIddict expects the descriptor to have the new ReferenceId
                        descriptor.ReferenceId = identifier;
                        _logger.LogInformation("Updated descriptor ReferenceId from {OldRefId} to {NewRefId}", token.ReferenceId, identifier);
                        return descriptor;
                    }
                }
                return null;
            }
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
            // Return a descriptor with default values to pass validation
            // OpenIddict will populate the actual values via Set* methods before calling CreateAsync
            // Note: Properties is immutable and managed by OpenIddict internally
            var descriptor = new OpenIddictTokenDescriptor
            {
                // Set default values to pass OpenIddict's validation
                Type = "unknown", // Will be set to actual type by OpenIddict via SetTypeAsync
                Status = "valid"  // Will be set to actual status by OpenIddict via SetStatusAsync
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

            _logger.LogInformation("=== TokenStore.UpdateAsync called ===");
            _logger.LogInformation("Descriptor ReferenceId: {RefId}", descriptor.ReferenceId);
            _logger.LogInformation("Descriptor Payload length: {Length}", descriptor.Payload?.Length ?? 0);
            _logger.LogInformation("Descriptor Status: {Status}", descriptor.Status);
            _logger.LogInformation("Descriptor Type: {Type}", descriptor.Type);
            _logger.LogInformation("Descriptor Subject: {Subject}", descriptor.Subject);

            var conn = await EnsureConnectedAsync();
            
            // Try to get the internal database ID from cache first
            string? internalId = null;
            if (!string.IsNullOrEmpty(descriptor.ReferenceId) && _referenceIdToInternalId.TryGetValue(descriptor.ReferenceId, out var cachedId))
            {
                internalId = cachedId;
                _logger.LogInformation("Found internal ID in cache for ReferenceId {RefId}: {InternalId}", descriptor.ReferenceId, internalId);
            }
            
            // Find the token by internal ID (stored in ReferenceId field in database)
            var token = internalId != null 
                ? conn.Db.OpenIddictSpacetimeToken.Iter().ToList().FirstOrDefault(t => t.ReferenceId == internalId)
                : conn.Db.OpenIddictSpacetimeToken.Iter().ToList().FirstOrDefault(t => t.ReferenceId == descriptor.ReferenceId);

            if (token == null)
            {
                _logger.LogError("Token not found for update with ReferenceId: {ReferenceId}, InternalId: {InternalId}", 
                    descriptor.ReferenceId, internalId);
                throw new InvalidOperationException("Token not found.");
            }

            _logger.LogInformation("Found token in database - Database ID: {DbId}, Current Payload length: {CurrentLength}", 
                token.Id, token.Payload?.Length ?? 0);
            
            // Update the cache if ReferenceId changed (OpenIddict sets the actual token string)
            if (!string.IsNullOrEmpty(descriptor.ReferenceId) && internalId != null && descriptor.ReferenceId != internalId)
            {
                _referenceIdToInternalId[descriptor.ReferenceId] = internalId;
                _internalIdToReferenceId[internalId] = descriptor.ReferenceId;
                _logger.LogInformation("Updated bidirectional cache mapping: {NewRefId} <-> {InternalId}", descriptor.ReferenceId, internalId);
            }

            _logger.LogInformation("Calling UpdateOidcToken reducer with payload length: {Length}", descriptor.Payload?.Length ?? 0);

            // Check if we have cached properties to save - try multiple keys
            string? propertiesJson = null;
            ImmutableDictionary<string, JsonElement>? cachedProperties = null;
            
            // Try to find cached properties using: 1) new ReferenceId, 2) internal ID, 3) old ReferenceId
            if (!string.IsNullOrEmpty(descriptor.ReferenceId) && _internalIdToProperties.TryGetValue(descriptor.ReferenceId, out cachedProperties))
            {
                _logger.LogInformation("Found {Count} cached properties using new ReferenceId: {RefId}", cachedProperties.Count, descriptor.ReferenceId);
            }
            else if (internalId != null && _internalIdToProperties.TryGetValue(internalId, out cachedProperties))
            {
                _logger.LogInformation("Found {Count} cached properties using internal ID: {InternalId}", cachedProperties.Count, internalId);
            }
            else if (token.ReferenceId != null && _internalIdToProperties.TryGetValue(token.ReferenceId, out cachedProperties))
            {
                _logger.LogInformation("Found {Count} cached properties using old ReferenceId from DB: {RefId}", cachedProperties.Count, token.ReferenceId);
            }
            
            if (cachedProperties != null)
            {
                propertiesJson = SerializeProperties(cachedProperties);
                _logger.LogInformation("Serialized {Count} properties for database update", cachedProperties.Count);
            }
            else
            {
                propertiesJson = SerializeProperties(descriptor.Properties.ToImmutableDictionary());
                _logger.LogWarning("No cached properties found, using descriptor.Properties (count: {Count})", descriptor.Properties.Count);
            }

            // CRITICAL: DO NOT update ReferenceId in database - keep the internal ID
            // The cache handles all ReferenceId mappings (unencrypted code, encrypted code, etc.)
            // OpenIddict may call SetReferenceIdAsync multiple times with different values
            // but the database should maintain the stable internal ID
            conn.Reducers.UpdateOidcToken(
                token.Id,
                descriptor.ExpirationDate?.ToUnixTimeMilliseconds() is long ms ? (ulong?)ms : null,
                descriptor.Payload,
                propertiesJson,
                descriptor.RedemptionDate?.ToUnixTimeMilliseconds() is long mss ? (ulong?)mss : null,
                descriptor.Status,
                token.ReferenceId  // Keep the original internal ID in database
            );
            
            _logger.LogInformation("Token update reducer called - kept database ReferenceId as: {DbRefId}", token.ReferenceId);
            
            // CRITICAL: Wait for SpacetimeDB reducer to complete before returning
            // The payload contains PKCE data and MUST be persisted before the authorization code is sent to the client
            const int maxAttempts = 50; // 5 seconds total
            const int delayMs = 100;
            
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                await Task.Delay(delayMs, cancellationToken);
                
                try
                {
                    var updatedToken = conn.Db.OpenIddictSpacetimeToken.Iter().ToList().FirstOrDefault(t => t.Id == token.Id);
                    
                    if (updatedToken != null && updatedToken.Payload?.Length == descriptor.Payload?.Length)
                    {
                        _logger.LogInformation("Token update confirmed in database after {Attempts} attempts ({Ms}ms): Payload length {Length}", 
                            attempt + 1, (attempt + 1) * delayMs, updatedToken.Payload?.Length ?? 0);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error querying database during update polling attempt {Attempt}: {Message}", 
                        attempt + 1, ex.Message);
                }
            }
            
            _logger.LogWarning("Token update not confirmed in database after {MaxAttempts} attempts ({Ms}ms) - payload may not be persisted!", 
                maxAttempts, maxAttempts * delayMs);
        }

        // Implement the remaining Set* methods - these mutate the descriptor before CreateAsync is called
        public ValueTask SetApplicationIdAsync(OpenIddictTokenDescriptor descriptor, string? identifier, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.ApplicationId = identifier;
            return default;
        }

        public ValueTask SetAuthorizationIdAsync(OpenIddictTokenDescriptor descriptor, string? identifier, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.AuthorizationId = identifier;
            return default;
        }

        public ValueTask SetCreationDateAsync(OpenIddictTokenDescriptor descriptor, DateTimeOffset? date, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.CreationDate = date;
            return default;
        }

        public ValueTask SetExpirationDateAsync(OpenIddictTokenDescriptor descriptor, DateTimeOffset? date, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.ExpirationDate = date;
            return default;
        }

        public ValueTask SetPayloadAsync(OpenIddictTokenDescriptor descriptor, string? payload, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            _logger.LogInformation("=== SetPayloadAsync called ===");
            _logger.LogInformation("Payload length: {Length}", payload?.Length ?? 0);
            _logger.LogInformation("Current descriptor ReferenceId: {RefId}", descriptor.ReferenceId);
            
            // Log payload content for PKCE debugging (first 500 chars)
            if (!string.IsNullOrEmpty(payload))
            {
                var payloadPreview = payload.Length > 500 ? payload.Substring(0, 500) + "..." : payload;
                _logger.LogInformation("Payload preview: {Payload}", payloadPreview);
            }
            
            descriptor.Payload = payload;
            return default;
        }

        public ValueTask SetPropertiesAsync(OpenIddictTokenDescriptor descriptor, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            
            _logger.LogInformation("=== SetPropertiesAsync called ===");
            _logger.LogInformation("Properties count: {Count}", properties?.Count ?? 0);
            _logger.LogInformation("Current descriptor ReferenceId: {RefId}", descriptor.ReferenceId);
            
            // CRITICAL: Cache the properties using BOTH the current ReferenceId AND the internal ID
            // OpenIddict may call SetPropertiesAsync before or after SetReferenceIdAsync
            // We need to handle both cases to ensure PKCE data is preserved
            if (properties != null && !properties.IsEmpty)
            {
                // Log the property keys and values for PKCE debugging
                foreach (var kvp in properties)
                {
                    var valueStr = kvp.Value.ValueKind == JsonValueKind.String 
                        ? kvp.Value.GetString() 
                        : kvp.Value.ToString();
                    _logger.LogInformation("  Property: {Key} = {Value}", kvp.Key, valueStr);
                }
                
                // Cache using current ReferenceId
                if (!string.IsNullOrEmpty(descriptor.ReferenceId))
                {
                    _internalIdToProperties[descriptor.ReferenceId] = properties;
                    _logger.LogInformation("✓ Cached {Count} properties for ReferenceId: {RefId}", properties.Count, descriptor.ReferenceId);
                    
                    // Also cache using internal ID if we have the mapping
                    if (_referenceIdToInternalId.TryGetValue(descriptor.ReferenceId, out var internalId))
                    {
                        _internalIdToProperties[internalId] = properties;
                        _logger.LogInformation("✓ Also cached properties for internal ID: {InternalId}", internalId);
                    }
                }
            }
            else
            {
                _logger.LogWarning("✗ NOT caching properties - ReferenceId: {RefId}, Properties null: {IsNull}, Properties empty: {IsEmpty}", 
                    descriptor.ReferenceId, properties == null, properties?.IsEmpty ?? true);
            }
            
            return default;
        }

        public ValueTask SetRedemptionDateAsync(OpenIddictTokenDescriptor descriptor, DateTimeOffset? date, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.RedemptionDate = date;
            return default;
        }

        public ValueTask SetReferenceIdAsync(OpenIddictTokenDescriptor descriptor, string? identifier, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            
            // When OpenIddict sets the ReferenceId (the actual authorization code string),
            // we need to update our bidirectional cache
            // DO NOT update the database here - OpenIddict will call UpdateAsync with all data including properties
            var oldReferenceId = descriptor.ReferenceId;
            descriptor.ReferenceId = identifier;
            
            // Update bidirectional cache: new ReferenceId should map to the same internal ID as the old one
            if (!string.IsNullOrEmpty(oldReferenceId) && !string.IsNullOrEmpty(identifier) && oldReferenceId != identifier)
            {
                if (_referenceIdToInternalId.TryGetValue(oldReferenceId, out var internalId))
                {
                    // Update both directions in cache
                    _referenceIdToInternalId[identifier] = internalId;
                    _internalIdToReferenceId[internalId] = identifier;
                    _logger.LogInformation("SetReferenceIdAsync: Updated bidirectional cache {NewRefId} <-> {InternalId} (was {OldRefId})", 
                        identifier, internalId, oldReferenceId);
                    
                    // CRITICAL: Migrate cached properties from old ReferenceId to new ReferenceId
                    // This ensures PKCE data survives the ReferenceId change
                    if (_internalIdToProperties.TryGetValue(oldReferenceId, out var cachedProps))
                    {
                        _internalIdToProperties[identifier] = cachedProps;
                        _internalIdToProperties[internalId] = cachedProps; // Also keep under internal ID
                        _logger.LogInformation("✓ Migrated {Count} cached properties from {OldRefId} to {NewRefId}", 
                            cachedProps.Count, oldReferenceId, identifier);
                    }
                }
            }
            
            return default;
        }

        public ValueTask SetStatusAsync(OpenIddictTokenDescriptor descriptor, string? status, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.Status = status;
            return default;
        }

        public ValueTask SetSubjectAsync(OpenIddictTokenDescriptor descriptor, string? subject, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.Subject = subject;
            return default;
        }

        public ValueTask SetTypeAsync(OpenIddictTokenDescriptor descriptor, string? type, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.Type = type;
            return default;
        }
    }
}




