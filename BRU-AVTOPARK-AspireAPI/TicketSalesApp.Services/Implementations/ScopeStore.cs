// --- File: ScopeStore.cs ---

using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using System.Diagnostics;

namespace TicketSalesApp.Services.Implementations
{
    public class ScopeStore : IOpenIddictScopeStore<OpenIddictScopeDescriptor>
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<ScopeStore> _logger;
        
        // CRITICAL: Client-side caching for scopes as failsafe
        // Maps OpenIddict scope ID (string) to internal SpacetimeDB ID (uint)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, uint> _scopeIdCache = new();
        
        // Maps scope name to OpenIddict scope ID for quick lookup
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _scopeNameToIdCache = new();
        
        // Pending scopes that are being created (not yet synced to database)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _pendingScopeIds = new();
        
        // Full scope descriptor cache for fast retrieval
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OpenIddictScopeDescriptor> _scopeDescriptorCache = new();

        public ScopeStore(ISpacetimeDBService spacetimeService, ILogger<ScopeStore> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("SpacetimeDB ScopeStore initialized.");
        }

        private DbConnection GetConnection()
        {
            var conn = _spacetimeService.GetConnection();
            if (conn == null) throw new InvalidOperationException("SpacetimeDB connection is not available.");
            return conn;
        }

        // --- Create ---
        public virtual ValueTask CreateAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            cancellationToken.ThrowIfCancellationRequested();

            var conn = GetConnection();

            // Ensure name is provided
            if (string.IsNullOrEmpty(descriptor.Name))
            {
                throw new ArgumentException("Scope name cannot be null or empty.", nameof(descriptor));
            }

            // Check if scope with the same name already exists in cache first
            if (_scopeNameToIdCache.TryGetValue(descriptor.Name, out var cachedId))
            {
                _logger.LogInformation("Scope '{ScopeName}' already exists in cache with ID {OidcScopeId}, skipping creation", descriptor.Name, cachedId);
                return default;
            }

            // Check if scope with the same name already exists in database
            var existingScope = conn.Db.OpenIddictSpacetimeScope.Iter().FirstOrDefault(s => s.Name == descriptor.Name);
            if (existingScope != null)
            {
                _logger.LogInformation("Scope '{ScopeName}' already exists with ID {OidcScopeId}, adding to cache", descriptor.Name, existingScope.OpenIddictScopeId);
                
                // Add to cache
                _scopeIdCache.TryAdd(existingScope.OpenIddictScopeId, existingScope.Id);
                _scopeNameToIdCache.TryAdd(descriptor.Name, existingScope.OpenIddictScopeId);
                _scopeDescriptorCache.TryAdd(existingScope.OpenIddictScopeId, MapToDescriptor(existingScope)!);
                
                // Store in pending dictionary so GetIdAsync can retrieve it immediately
                _pendingScopeIds[descriptor.Name] = existingScope.OpenIddictScopeId;
                return default;
            }

            var oidcScopeId = Guid.NewGuid().ToString(); // Generate unique ID for OpenIddict

            // Store in pending dictionary and cache so GetIdAsync can retrieve it immediately
            _pendingScopeIds[descriptor.Name] = oidcScopeId;
            _scopeNameToIdCache.TryAdd(descriptor.Name, oidcScopeId);
            _scopeDescriptorCache.TryAdd(oidcScopeId, descriptor);

            // Serialize complex properties
            string? descriptionsJson = SerializeJson(descriptor.Descriptions);
            string? displayNamesJson = SerializeJson(descriptor.DisplayNames);
            string? propertiesJson = SerializeJson(descriptor.Properties);
            string? resourcesJson = SerializeJson(descriptor.Resources);

            try
            {
                _logger.LogDebug("Calling CreateOidcScope reducer for Name: {ScopeName}, OIDC ID: {OidcScopeId}", descriptor.Name, oidcScopeId);
                conn.Reducers.CreateOidcScope(
                    oidcScopeId,
                    descriptor.Name,
                    descriptor.Description,
                    descriptionsJson,
                    descriptor.DisplayName,
                    displayNamesJson,
                    propertiesJson,
                    resourcesJson
                );
                
                _logger.LogInformation("Reducer called to create scope: {ScopeName} with ID: {OidcScopeId}", descriptor.Name, oidcScopeId);
                return default;
            }
            catch (Exception ex)
            {
                // Remove from pending and cache on error
                _pendingScopeIds.TryRemove(descriptor.Name, out _);
                _scopeNameToIdCache.TryRemove(descriptor.Name, out _);
                _scopeDescriptorCache.TryRemove(oidcScopeId, out _);
                _logger.LogError(ex, "Error creating scope {ScopeName} via reducer.", descriptor.Name);
                throw;
            }
        }

        // --- Delete ---
        public virtual async ValueTask DeleteAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            cancellationToken.ThrowIfCancellationRequested();

            var conn = GetConnection();

            // Find the internal SpacetimeDB entity by Name (safer than relying on ID from descriptor)
            var entity = conn.Db.OpenIddictSpacetimeScope.Iter().FirstOrDefault(s => s.Name == descriptor.Name);
            if (entity == null)
            {
                // Entity not found by name
                _logger.LogWarning("Scope '{ScopeName}' not found for deletion.", descriptor.Name);
                return; // OpenIddict might expect success if not found.
            }

            try
            {
                _logger.LogDebug("Calling DeleteOidcScope reducer for Internal ID {InternalId} (Name: {ScopeName})", entity.Id, entity.Name);
                conn.Reducers.DeleteOidcScope(entity.Id);
                _logger.LogInformation("Reducer called to delete scope: {ScopeName}", entity.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting scope {ScopeName} via reducer.", entity.Name);
                throw;
            }
        }

        // --- Find Methods (Read Operations) ---

        public virtual async ValueTask<OpenIddictScopeDescriptor?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Finding scope by OpenIddict ID: {OidcScopeId}", identifier);

            // CRITICAL: Check cache first for fast retrieval
            if (_scopeDescriptorCache.TryGetValue(identifier, out var cachedDescriptor))
            {
                _logger.LogDebug("Scope found in cache: {OidcScopeId}", identifier);
                return cachedDescriptor;
            }

            await Task.Yield(); // Simulate async if needed
            var conn = GetConnection();
            
            // Find using the OpenIddict string ID
            var entity = conn.Db.OpenIddictSpacetimeScope.Iter()
                            .FirstOrDefault(scope => scope != null && scope.OpenIddictScopeId == identifier);
            
            if (entity != null)
            {
                // Add to cache for future lookups
                var descriptor = MapToDescriptor(entity);
                if (descriptor != null)
                {
                    _scopeIdCache.TryAdd(entity.OpenIddictScopeId, entity.Id);
                    if (!string.IsNullOrEmpty(entity.Name))
                    {
                        _scopeNameToIdCache.TryAdd(entity.Name, entity.OpenIddictScopeId);
                    }
                    _scopeDescriptorCache.TryAdd(entity.OpenIddictScopeId, descriptor);
                }
                return descriptor;
            }
            
            return null;
        }

        public virtual async ValueTask<OpenIddictScopeDescriptor?> FindByNameAsync(string name, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name cannot be null or empty.", nameof(name));
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("=== [ScopeStore.FindByNameAsync] Searching for scope: {ScopeName} ===", name);

            // CRITICAL: Check cache first for fast retrieval
            if (_scopeNameToIdCache.TryGetValue(name, out var cachedOidcId))
            {
                if (_scopeDescriptorCache.TryGetValue(cachedOidcId, out var cachedDescriptor))
                {
                    _logger.LogInformation("[ScopeStore] ✓ FOUND scope in cache: {ScopeName}", name);
                    return cachedDescriptor;
                }
            }

            await Task.Yield();
            var conn = GetConnection();
            
            // Log ALL scopes in the database for debugging
            var allScopes = conn.Db.OpenIddictSpacetimeScope.Iter().ToList();
            _logger.LogInformation("[ScopeStore] Total scopes in OpenIddictSpacetimeScope table: {Count}", allScopes.Count);
            
            foreach (var s in allScopes)
            {
                _logger.LogInformation("[ScopeStore] - Scope: {ScopeName}, DisplayName: {DisplayName}, Description: {Description}", 
                    s.Name, s.DisplayName, s.Description);
            }
            
            // Now search for the specific scope
            var entity = conn.Db.OpenIddictSpacetimeScope.Iter()
                            .FirstOrDefault(scope => scope != null && scope.Name == name);
            
            if (entity != null)
            {
                _logger.LogInformation("[ScopeStore] ✓ FOUND scope in database: {ScopeName}", name);
                
                // Add to cache for future lookups
                var descriptor = MapToDescriptor(entity);
                if (descriptor != null)
                {
                    _scopeIdCache.TryAdd(entity.OpenIddictScopeId, entity.Id);
                    _scopeNameToIdCache.TryAdd(name, entity.OpenIddictScopeId);
                    _scopeDescriptorCache.TryAdd(entity.OpenIddictScopeId, descriptor);
                    
                    // Remove from pending since it's now in the database
                    _pendingScopeIds.TryRemove(name, out _);
                }
                
                return descriptor;
            }
            else
            {
                _logger.LogWarning("[ScopeStore] ✗ NOT FOUND: Scope {ScopeName} not in database or cache", name);
            }
            
            return null;
        }

        public virtual IAsyncEnumerable<OpenIddictScopeDescriptor> FindByNamesAsync(ImmutableArray<string> names, CancellationToken cancellationToken)
        {
            
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("=== [ScopeStore.FindByNamesAsync] Searching for scopes: {ScopeNames} ===", string.Join(", ", names));

            var conn = GetConnection();
            
            // CRITICAL: Add null safety - filter out null scopes before accessing properties
            var dbScopes = conn.Db.OpenIddictSpacetimeScope.Iter()
                            .Where(scope => scope != null && !string.IsNullOrEmpty(scope.Name) && names.Contains(scope.Name))
                            .Select(MapToDescriptor)
                            .Where(d => d != null)
                            .ToList();
            
            _logger.LogInformation("[ScopeStore] Found {Count} scopes in database out of {Requested} requested", dbScopes.Count, names.Length);
            
            // Check for pending scopes (just created, not yet synced)
            var foundNames = dbScopes.Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                                     .Select(s => s!.Name!)
                                     .ToHashSet();
            var pendingScopes = new List<OpenIddictScopeDescriptor>();
            
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue; // Skip null/empty names
                
                if (!foundNames.Contains(name) && _pendingScopeIds.TryGetValue(name, out var pendingId))
                {
                    _logger.LogInformation("[ScopeStore] Found pending scope: {ScopeName} (not yet synced to database)", name);
                    // Create a descriptor for the pending scope
                    var pendingDescriptor = new OpenIddictScopeDescriptor { Name = name };
                    pendingScopes.Add(pendingDescriptor);
                }
            }
            
            var allScopes = dbScopes.Concat(pendingScopes).Where(s => s != null);
            
            _logger.LogInformation("[ScopeStore] Returning {Total} scopes total ({DbCount} from DB + {PendingCount} pending)", 
                allScopes.Count(), dbScopes.Count, pendingScopes.Count);
            
            return GetAsyncEnumerable(allScopes!);
        }

        public virtual IAsyncEnumerable<OpenIddictScopeDescriptor> FindByResourceAsync(string resource, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(resource)) throw new ArgumentException("Resource cannot be null or empty.", nameof(resource));
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Finding scopes by Resource: {Resource}", resource);

            var conn = GetConnection();
            // Inefficient: Filters in memory after fetching all scopes.
            var candidates = conn.Db.OpenIddictSpacetimeScope.Iter().ToList();
            var results = candidates
                .Where(scope => {
                    var resources = DeserializeStringArray(scope.Resources);
                    return resources != null && resources.Value.Contains(resource, StringComparer.Ordinal);
                })
                .Select(MapToDescriptor)
                .Where(d => d != null)!;
            return GetAsyncEnumerable(results!);
        }

        // --- Get Properties ---
        public virtual ValueTask<string?> GetDescriptionAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return new ValueTask<string?>(descriptor.Description);
        }

        public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            // The descriptor holds the already deserialized map
            var descriptions = descriptor.Descriptions;
            return new ValueTask<ImmutableDictionary<CultureInfo, string>>(
                descriptions != null ? 
                    ImmutableDictionary.CreateRange(descriptions) : 
                    ImmutableDictionary<CultureInfo, string>.Empty);
        }

        public virtual ValueTask<string?> GetDisplayNameAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return new ValueTask<string?>(descriptor.DisplayName);
        }

        public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var displayNames = descriptor.DisplayNames;
            return new ValueTask<ImmutableDictionary<CultureInfo, string>>(
                displayNames != null ? 
                    ImmutableDictionary.CreateRange(displayNames) : 
                    ImmutableDictionary<CultureInfo, string>.Empty);
        }

        public virtual ValueTask<string?> GetIdAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            
            // First check if this is a pending scope (just created, not yet synced)
            if (!string.IsNullOrEmpty(descriptor.Name) && _pendingScopeIds.TryGetValue(descriptor.Name, out var pendingId))
            {
                _logger.LogTrace("Retrieved pending scope ID for {ScopeName}: {ScopeId}", descriptor.Name, pendingId);
                return new ValueTask<string?>(pendingId);
            }
            
            // Otherwise, look it up from the database by name
            var conn = GetConnection();
            var entity = conn.Db.OpenIddictSpacetimeScope.Iter().FirstOrDefault(s => s.Name == descriptor.Name);
            if (entity != null)
            {
                _logger.LogTrace("Retrieved scope ID from database for {ScopeName}: {ScopeId}", descriptor.Name, entity.OpenIddictScopeId);
                
                // Remove from pending since it's now in the database
                if (!string.IsNullOrEmpty(descriptor.Name))
                {
                    _pendingScopeIds.TryRemove(descriptor.Name, out _);
                }
                
                return new ValueTask<string?>(entity.OpenIddictScopeId);
            }
            
            _logger.LogWarning("Could not find scope ID for {ScopeName}", descriptor.Name);
            return new ValueTask<string?>((string?)null);
        }

        public virtual ValueTask<string?> GetNameAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return new ValueTask<string?>(descriptor.Name);
        }

        public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var properties = descriptor.Properties;
            return new ValueTask<ImmutableDictionary<string, JsonElement>>(
                properties != null ? 
                    ImmutableDictionary.CreateRange(properties) : 
                    ImmutableDictionary<string, JsonElement>.Empty);
        }

        public virtual ValueTask<ImmutableArray<string>> GetResourcesAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var resources = descriptor.Resources;
            return new ValueTask<ImmutableArray<string>>(
                resources != null ? 
                    resources.ToImmutableArray() : 
                    ImmutableArray<string>.Empty);
        }

        // --- Instantiate ---
        public virtual ValueTask<OpenIddictScopeDescriptor> InstantiateAsync(CancellationToken cancellationToken)
        {
            try {
                return new ValueTask<OpenIddictScopeDescriptor>(new OpenIddictScopeDescriptor());
            } catch (Exception exception) {
                return new ValueTask<OpenIddictScopeDescriptor>(Task.FromException<OpenIddictScopeDescriptor>(exception));
            }
        }

        // --- Count Methods ---
        public virtual async ValueTask<long> CountAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Counting all scopes");

            var conn = GetConnection();
            return conn.Db.OpenIddictSpacetimeScope.Iter().Count();
        }

        public virtual async ValueTask<long> CountAsync<TResult>(
            Func<IQueryable<OpenIddictScopeDescriptor>, IQueryable<TResult>> query,
            CancellationToken cancellationToken)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Counting scopes with custom query");

            var conn = GetConnection();
            var allDescriptors = conn.Db.OpenIddictSpacetimeScope.Iter()
                .Select(MapToDescriptor)
                .Where(d => d != null)!
                .AsQueryable();

            // Apply the query to our set of descriptors
            return query(allDescriptors).Count();
        }

        // --- Get Methods ---
        public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
            Func<IQueryable<OpenIddictScopeDescriptor>, TState, IQueryable<TResult>> query,
            TState state, CancellationToken cancellationToken)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Getting scope with custom query");

            var conn = GetConnection();
            var allDescriptors = conn.Db.OpenIddictSpacetimeScope.Iter()
                .Select(MapToDescriptor)
                .Where(d => d != null)!
                .AsQueryable();

            // Apply the query to our set of descriptors
            var result = query(allDescriptors, state).FirstOrDefault();
            return result;
        }

        // --- List ---
        public virtual IAsyncEnumerable<OpenIddictScopeDescriptor> ListAsync(int? count, int? offset, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Listing scopes with Count {Count}, Offset {Offset}", count, offset);
            var conn = GetConnection();
            // Use AsQueryable for Skip/Take. Ensure MapToDescriptor handles nulls.
            var query = conn.Db.OpenIddictSpacetimeScope.Iter()
                                .Select(MapToDescriptor)
                                .Where(d => d != null)!
                                .AsQueryable();

            if (offset.HasValue) query = query.Skip(offset.Value);
            if (count.HasValue) query = query.Take(count.Value);

            return GetAsyncEnumerable(query);
        }

        public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
            Func<IQueryable<OpenIddictScopeDescriptor>, TState, IQueryable<TResult>> query,
            TState state, CancellationToken cancellationToken)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));
            _logger.LogWarning("ListAsync with custom query delegate used - filtering applied in-memory.");

            var conn = GetConnection();
            var allDescriptors = conn.Db.OpenIddictSpacetimeScope.Iter()
                                   .Select(MapToDescriptor)
                                   .Where(d => d != null)!;
            var results = query(allDescriptors.AsQueryable(), state).ToList();

            return GetAsyncEnumerable(results);
        }

        // --- Set Properties (Act on the descriptor object) ---
        public virtual ValueTask SetDescriptionAsync(OpenIddictScopeDescriptor descriptor, string? description, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.Description = description; return default;
        }
        public virtual ValueTask SetDescriptionsAsync(OpenIddictScopeDescriptor descriptor, ImmutableDictionary<CultureInfo, string>? descriptions, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            // Can't directly assign to read-only properties, need to handle in Update/Create
            return default;
        }
        public virtual ValueTask SetDisplayNameAsync(OpenIddictScopeDescriptor descriptor, string? name, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.DisplayName = name; return default;
        }
        public virtual ValueTask SetDisplayNamesAsync(OpenIddictScopeDescriptor descriptor, ImmutableDictionary<CultureInfo, string>? names, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            // Can't directly assign to read-only properties, need to handle in Update/Create
            return default;
        }
        public virtual ValueTask SetNameAsync(OpenIddictScopeDescriptor descriptor, string? name, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.Name = name; return default;
        }
        public virtual ValueTask SetPropertiesAsync(OpenIddictScopeDescriptor descriptor, ImmutableDictionary<string, JsonElement>? properties, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            // Can't directly assign to read-only properties, need to handle in Update/Create
            return default;
        }
        public virtual ValueTask SetResourcesAsync(OpenIddictScopeDescriptor descriptor, ImmutableArray<string> resources, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            // Can't directly assign to read-only properties, need to handle in Update/Create
            return default;
        }

        // --- Update ---
        public virtual async ValueTask UpdateAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            cancellationToken.ThrowIfCancellationRequested();

            var conn = GetConnection();

            // Find the internal entity, preferably by Name for reliability during updates
            var entity = conn.Db.OpenIddictSpacetimeScope.Iter().FirstOrDefault(s => s.Name == descriptor.Name);
            if (entity == null) {
                throw new OpenIddictExceptions.ConcurrencyException($"Scope '{descriptor.Name}' not found for update.");
            }

            // Ensure Name isn't being changed to conflict with another scope
            if (!string.IsNullOrEmpty(descriptor.Name) && descriptor.Name != entity.Name) {
                if (conn.Db.OpenIddictSpacetimeScope.Iter().Any(s => s.Name == descriptor.Name)) {
                    throw new OpenIddictExceptions.ConcurrencyException($"A scope with the name '{descriptor.Name}' already exists.");
                }
                entity.Name = descriptor.Name; // Update name if changed
            }

            // Serialize complex properties for the reducer
            string? descriptionsJson = SerializeJson(descriptor.Descriptions);
            string? displayNamesJson = SerializeJson(descriptor.DisplayNames);
            string? propertiesJson = SerializeJson(descriptor.Properties);
            string? resourcesJson = SerializeJson(descriptor.Resources);

            try {
                _logger.LogDebug("Calling UpdateOidcScope reducer for Internal ID {InternalId} (Name: {ScopeName})", entity.Id, entity.Name);
                conn.Reducers.UpdateOidcScope(
                    entity.Id,
                    descriptor.Description, // Pass potentially updated simple props
                    descriptionsJson,
                    descriptor.DisplayName,
                    displayNamesJson,
                    propertiesJson,
                    resourcesJson
                    // Pass name if it was updated and reducer handles it? Or handle name updates separately.
                );
                _logger.LogInformation("Reducer called to update scope: {ScopeName}", entity.Name);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error updating scope {ScopeName} via reducer.", entity.Name);
                throw;
            }
        }

        // --- Helper Methods ---

        // Maps SpacetimeDB entity to OpenIddict Descriptor
        private static OpenIddictScopeDescriptor? MapToDescriptor(OpenIddictSpacetimeScope? entity)
        {
            if (entity == null) return null;

            var descriptor = new OpenIddictScopeDescriptor
            {
                Name = entity.Name,
                Description = entity.Description,
                DisplayName = entity.DisplayName
            };

            // We can't set read-only properties, but we can store them in a custom store 
            // and retrieve them using the appropriate methods

            return descriptor;
        }

        // Serializes Dictionary<CultureInfo, string> or Dictionary<string, JsonElement>
        private static string? SerializeJson(object? data) {
            if (data == null) return null;
            try {
                // Handle culture info keys for descriptions/displaynames
                if (data is ImmutableDictionary<CultureInfo, string> cultureMap) {
                    var stringKeyMap = cultureMap.ToDictionary(kvp => kvp.Key.Name, kvp => kvp.Value);
                    return JsonSerializer.Serialize(stringKeyMap, JsonOptions);
                }
                return JsonSerializer.Serialize(data, JsonOptions);
            } catch (Exception ex) { 
                Debug.WriteLine($"JSON Serialization failed: {ex.Message}");
                return null;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false // More compact for storage
        };

        // Deserializes JSON array string to ImmutableArray<string>
        private static ImmutableArray<string>? DeserializeStringArray(string? json) {
            if (string.IsNullOrEmpty(json)) return ImmutableArray<string>.Empty;
            try {
                var list = JsonSerializer.Deserialize<List<string>>(json);
                return list?.ToImmutableArray();
            } catch { return ImmutableArray<string>.Empty; } // Return empty on error
        }

        // Deserializes JSON map string to ImmutableDictionary<string, string>
        private static ImmutableDictionary<CultureInfo, string>? DeserializeStringMap(string? json) {
            if (string.IsNullOrEmpty(json)) return ImmutableDictionary<CultureInfo, string>.Empty;
            try {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict == null) return ImmutableDictionary<CultureInfo, string>.Empty;
                // Convert string keys back to CultureInfo
                var builder = ImmutableDictionary.CreateBuilder<CultureInfo, string>();
                foreach (var kvp in dict) {
                    try { builder.Add(CultureInfo.GetCultureInfo(kvp.Key), kvp.Value); }
                    catch (CultureNotFoundException) { /* Log or ignore invalid culture keys */ }
                }
                return builder.ToImmutable();
            } catch { return ImmutableDictionary<CultureInfo, string>.Empty; }
        }

        // Deserializes JSON map string to ImmutableDictionary<string, JsonElement>
        private static ImmutableDictionary<string, JsonElement>? DeserializeJsonMap(string? json) {
            if (string.IsNullOrEmpty(json)) return ImmutableDictionary<string, JsonElement>.Empty;
            try {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.EnumerateObject().ToImmutableDictionary(p => p.Name, p => p.Value.Clone()); // Clone needed
            } catch { return ImmutableDictionary<string, JsonElement>.Empty; }
        }

        // Helper to return IAsyncEnumerable
        private async IAsyncEnumerable<T> GetAsyncEnumerable<T>(IEnumerable<T> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Use ToList() to materialize before iterating if the source is lazy and might change
            foreach (var item in items.ToList()) {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield(); // Be a good async citizen
                yield return item;
            }
        }

        // --- Cache Management Methods ---
        
        /// <summary>
        /// Clears all cached scope data. Useful for testing or when database is reset.
        /// </summary>
        public static void ClearCache()
        {
            _scopeIdCache.Clear();
            _scopeNameToIdCache.Clear();
            _pendingScopeIds.Clear();
            _scopeDescriptorCache.Clear();
        }

        /// <summary>
        /// Gets the current size of the scope cache.
        /// </summary>
        public static int GetCacheSize()
        {
            return _scopeDescriptorCache.Count;
        }

        /// <summary>
        /// Removes a specific scope from the cache by OpenIddict ID.
        /// </summary>
        public static bool RemoveFromCache(string oidcScopeId)
        {
            if (string.IsNullOrEmpty(oidcScopeId)) return false;
            
            _scopeIdCache.TryRemove(oidcScopeId, out _);
            _scopeDescriptorCache.TryRemove(oidcScopeId, out var descriptor);
            
            if (descriptor != null && !string.IsNullOrEmpty(descriptor.Name))
            {
                _scopeNameToIdCache.TryRemove(descriptor.Name, out _);
                _pendingScopeIds.TryRemove(descriptor.Name, out _);
            }
            
            return descriptor != null;
        }
    }
}