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
        
        // Tracks identity only for live descriptor objects; no row or credential cache.
        private readonly ConditionalWeakTable<OpenIddictScopeDescriptor, ScopeIdentity> _identities = new();
        private sealed record ScopeIdentity(uint DatabaseId, string Id, string Name, ulong Revision);

        public ScopeStore(ISpacetimeDBService spacetimeService, ILogger<ScopeStore> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("SpacetimeDB ScopeStore initialized.");
        }

        public virtual async ValueTask CreateAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentException.ThrowIfNullOrEmpty(descriptor.Name);
            var id = Guid.NewGuid().ToString("N");
            var identity = await _spacetimeService.ExecuteReducerAsync(new Reducer.CreateOidcScope(
                id, descriptor.Name, descriptor.Description, SerializeCultures(descriptor.Descriptions),
                descriptor.DisplayName, SerializeCultures(descriptor.DisplayNames),
                JsonSerializer.Serialize(descriptor.Properties), JsonSerializer.Serialize(descriptor.Resources)), c =>
                {
                    var row = c.Db.OpenIddictSpacetimeScope.Iter().Single(x => x.OpenIddictScopeId == id);
                    return new ScopeIdentity(row.Id, row.OpenIddictScopeId, row.Name, c.Db.OidcScopeRevision.ScopeId.Find(row.Id)?.Revision ?? 0);
                }, cancellationToken);
            _identities.AddOrUpdate(descriptor, identity);
            _logger.LogInformation("OIDC scope creation committed {ScopeId}", id);
        }

        public virtual async ValueTask DeleteAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            var identity = await IdentityAsync(descriptor, cancellationToken);
            if (identity == null) return;
            await _spacetimeService.ExecuteReducerAsync(new Reducer.DeleteOidcScope(identity.DatabaseId), _ => true, cancellationToken);
            _identities.Remove(descriptor);
            _logger.LogInformation("OIDC scope deletion committed {ScopeId}", identity.Id);
        }

        public virtual async ValueTask<OpenIddictScopeDescriptor?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(identifier);
            return await _spacetimeService.ReadAsync(c => MapToDescriptor(c, c.Db.OpenIddictSpacetimeScope.Iter()
                .FirstOrDefault(x => x.OpenIddictScopeId == identifier)), cancellationToken);
        }

        public virtual async ValueTask<OpenIddictScopeDescriptor?> FindByNameAsync(string name, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            var descriptor = await _spacetimeService.ReadAsync(c => MapToDescriptor(c, c.Db.OpenIddictSpacetimeScope.Iter()
                .FirstOrDefault(x => x.Name == name)), cancellationToken);
            _logger.LogDebug("OIDC scope lookup; found {Found}, source committed subscription", descriptor != null);
            return descriptor;
        }

        private async Task<OpenIddictScopeDescriptor[]> SnapshotAsync(CancellationToken cancellationToken) =>
            await _spacetimeService.ReadAsync(c => c.Db.OpenIddictSpacetimeScope.Iter().OrderBy(x => x.OpenIddictScopeId, StringComparer.Ordinal)
                .Select(x => MapToDescriptor(c, x)!).ToArray(), cancellationToken);

        public virtual async IAsyncEnumerable<OpenIddictScopeDescriptor> FindByNamesAsync(ImmutableArray<string> names, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in await SnapshotAsync(cancellationToken))
            { cancellationToken.ThrowIfCancellationRequested(); if (names.Contains(item.Name, StringComparer.Ordinal)) yield return item; }
        }

        public virtual async IAsyncEnumerable<OpenIddictScopeDescriptor> FindByResourceAsync(string resource, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(resource);
            foreach (var item in await SnapshotAsync(cancellationToken))
            { cancellationToken.ThrowIfCancellationRequested(); if (item.Resources.Contains(resource)) yield return item; }
        }

        private async Task<ScopeIdentity?> IdentityAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            cancellationToken.ThrowIfCancellationRequested();
            if (_identities.TryGetValue(descriptor, out var known)) return known;
            return await _spacetimeService.ReadAsync(c =>
            {
                var row = c.Db.OpenIddictSpacetimeScope.Iter().FirstOrDefault(x => x.Name == descriptor.Name);
                if (row == null) return null;
                var identity = new ScopeIdentity(row.Id, row.OpenIddictScopeId, row.Name, c.Db.OidcScopeRevision.ScopeId.Find(row.Id)?.Revision ?? 0);
                _identities.AddOrUpdate(descriptor, identity);
                return identity;
            }, cancellationToken);
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

        public virtual async ValueTask<string?> GetIdAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            return (await IdentityAsync(descriptor, cancellationToken))?.Id;
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

        public virtual async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
            await _spacetimeService.ReadAsync(c => c.Db.OpenIddictSpacetimeScope.Iter().LongCount(), cancellationToken);

        public virtual async ValueTask<long> CountAsync<TResult>(Func<IQueryable<OpenIddictScopeDescriptor>, IQueryable<TResult>> query, CancellationToken cancellationToken) =>
            query((await SnapshotAsync(cancellationToken)).AsQueryable()).LongCount();

        public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<OpenIddictScopeDescriptor>, TState, IQueryable<TResult>> query,
            TState state, CancellationToken cancellationToken) => query((await SnapshotAsync(cancellationToken)).AsQueryable(), state).FirstOrDefault();

        public virtual async IAsyncEnumerable<OpenIddictScopeDescriptor> ListAsync(int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (count < 0 || offset < 0) throw new ArgumentOutOfRangeException(nameof(count));
            foreach (var item in (await SnapshotAsync(cancellationToken)).Skip(offset ?? 0).Take(count ?? int.MaxValue))
            { cancellationToken.ThrowIfCancellationRequested(); yield return item; }
        }

        public virtual async IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<OpenIddictScopeDescriptor>, TState, IQueryable<TResult>> query,
            TState state, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in query((await SnapshotAsync(cancellationToken)).AsQueryable(), state))
            { cancellationToken.ThrowIfCancellationRequested(); yield return item; }
        }

        // --- Set Properties (Act on the descriptor object) ---
        public virtual ValueTask SetDescriptionAsync(OpenIddictScopeDescriptor descriptor, string? description, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.Description = description; return default;
        }
        public virtual ValueTask SetDescriptionsAsync(OpenIddictScopeDescriptor descriptor, ImmutableDictionary<CultureInfo, string>? descriptions, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            cancellationToken.ThrowIfCancellationRequested();
            descriptor.Descriptions.Clear();
            if (descriptions != null) foreach (var entry in descriptions) descriptor.Descriptions.Add(entry.Key, entry.Value);
            return default;
        }
        public virtual ValueTask SetDisplayNameAsync(OpenIddictScopeDescriptor descriptor, string? name, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.DisplayName = name; return default;
        }
        public virtual ValueTask SetDisplayNamesAsync(OpenIddictScopeDescriptor descriptor, ImmutableDictionary<CultureInfo, string>? names, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            cancellationToken.ThrowIfCancellationRequested();
            descriptor.DisplayNames.Clear();
            if (names != null) foreach (var entry in names) descriptor.DisplayNames.Add(entry.Key, entry.Value);
            return default;
        }
        public virtual ValueTask SetNameAsync(OpenIddictScopeDescriptor descriptor, string? name, CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            descriptor.Name = name; return default;
        }
        public virtual ValueTask SetPropertiesAsync(OpenIddictScopeDescriptor descriptor, ImmutableDictionary<string, JsonElement>? properties, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            cancellationToken.ThrowIfCancellationRequested();
            descriptor.Properties.Clear();
            if (properties != null) foreach (var entry in properties) descriptor.Properties.Add(entry.Key, entry.Value.Clone());
            return default;
        }
        public virtual ValueTask SetResourcesAsync(OpenIddictScopeDescriptor descriptor, ImmutableArray<string> resources, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            cancellationToken.ThrowIfCancellationRequested();
            descriptor.Resources.Clear();
            if (!resources.IsDefault) foreach (var resource in resources) descriptor.Resources.Add(resource);
            return default;
        }

        public virtual async ValueTask UpdateAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            var identity = await IdentityAsync(descriptor, cancellationToken)
                ?? throw new OpenIddictExceptions.ConcurrencyException("Scope no longer exists.");
            var revision = await _spacetimeService.ExecuteReducerAsync(new Reducer.ReplaceOidcScope(identity.DatabaseId, identity.Revision, descriptor.Name!,
                descriptor.Description, SerializeCultures(descriptor.Descriptions), descriptor.DisplayName,
                SerializeCultures(descriptor.DisplayNames), JsonSerializer.Serialize(descriptor.Properties),
                JsonSerializer.Serialize(descriptor.Resources)), c => c.Db.OidcScopeRevision.ScopeId.Find(identity.DatabaseId)!.Revision, cancellationToken);
            _identities.AddOrUpdate(descriptor, identity with { Name = descriptor.Name!, Revision = revision });
            _logger.LogInformation("OIDC scope update committed {ScopeId}", identity.Id);
        }

        private static string SerializeCultures(IDictionary<CultureInfo, string> values) =>
            JsonSerializer.Serialize(values.ToDictionary(x => x.Key.Name, x => x.Value));

        private OpenIddictScopeDescriptor? MapToDescriptor(DbConnection connection, OpenIddictSpacetimeScope? entity)
        {
            if (entity == null) return null;
            var descriptor = new OpenIddictScopeDescriptor { Name = entity.Name, Description = entity.Description, DisplayName = entity.DisplayName };
            foreach (var pair in Deserialize<Dictionary<string, string>>(entity.Descriptions) ?? new())
                descriptor.Descriptions.Add(CultureInfo.GetCultureInfo(pair.Key), pair.Value);
            foreach (var pair in Deserialize<Dictionary<string, string>>(entity.DisplayNames) ?? new())
                descriptor.DisplayNames.Add(CultureInfo.GetCultureInfo(pair.Key), pair.Value);
            foreach (var pair in Deserialize<Dictionary<string, JsonElement>>(entity.Properties) ?? new())
                descriptor.Properties.Add(pair.Key, pair.Value.Clone());
            foreach (var resource in Deserialize<string[]>(entity.Resources) ?? []) descriptor.Resources.Add(resource);
            _identities.Add(descriptor, new ScopeIdentity(entity.Id, entity.OpenIddictScopeId, entity.Name, connection.Db.OidcScopeRevision.ScopeId.Find(entity.Id)?.Revision ?? 0));
            return descriptor;
        }

        // Malformed durable properties must fail visibly rather than silently changing scope semantics.
        private static T? Deserialize<T>(string? json) => string.IsNullOrEmpty(json) ? default : JsonSerializer.Deserialize<T>(json);
    }
}
