using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

public class AuthorizationStore(ISpacetimeDBService spacetimeService, ILogger<AuthorizationStore> logger)
    : IOpenIddictAuthorizationStore<OpenIddictSpacetimeAuthorization>
{
    public virtual async ValueTask CreateAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (string.IsNullOrEmpty(authorization.OpenIddictAuthorizationId)) throw new InvalidOperationException("Authorization ID required before create.");
        var id = authorization.OpenIddictAuthorizationId;
        var committed = await spacetimeService.ExecuteReducerAsync(new Reducer.CreateOidcAuthorization(id,
            authorization.ApplicationClientId, authorization.CreationDate, authorization.Properties, authorization.Scopes,
            authorization.Status, authorization.Subject, authorization.Type),
            c => c.Db.OpenIddictSpacetimeAuthorization.Iter().Single(a => a.OpenIddictAuthorizationId == id), cancellationToken);
        authorization.Id = committed.Id;
        logger.LogInformation("OIDC grant creation committed");
    }

    public virtual async ValueTask DeleteAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var entity = await FindByIdAsync(authorization.OpenIddictAuthorizationId, cancellationToken);
        if (entity != null) await spacetimeService.ExecuteReducerAsync(new Reducer.DeleteOidcAuthorization(entity.Id), _ => true, cancellationToken);
    }

    public virtual async ValueTask UpdateAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var entity = await FindByIdAsync(authorization.OpenIddictAuthorizationId, cancellationToken)
            ?? throw new InvalidOperationException("Authorization not found for update.");
        await spacetimeService.ExecuteReducerAsync(new Reducer.UpdateOidcAuthorization(entity.Id,
            authorization.Properties, authorization.Scopes, authorization.Status), _ => true, cancellationToken);
    }

    public virtual async ValueTask PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken) =>
        await spacetimeService.ExecuteReducerAsync(new Reducer.PruneOidcAuthorizations((ulong)threshold.ToUnixTimeMilliseconds()), _ => true, cancellationToken);

    public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindAsync(string subject, string client, CancellationToken cancellationToken) =>
        Query(a => a.Subject == subject && a.ApplicationClientId == client, cancellationToken);
    public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindAsync(string subject, string client, string status, CancellationToken cancellationToken) =>
        Query(a => a.Subject == subject && a.ApplicationClientId == client && a.Status == status, cancellationToken);
    public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindAsync(string subject, string client, string status, string type, CancellationToken cancellationToken) =>
        Query(a => a.Subject == subject && a.ApplicationClientId == client && a.Status == status && a.Type == type, cancellationToken);
    public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindAsync(string subject, string client, string status, string type, ImmutableArray<string> scopes, CancellationToken cancellationToken) =>
        Query(a => a.Subject == subject && a.ApplicationClientId == client && a.Status == status && a.Type == type && scopes.All(s => DeserializeScopes(a.Scopes).Contains(s)), cancellationToken);
    public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken) => Query(a => a.ApplicationClientId == identifier, cancellationToken);
    public virtual IAsyncEnumerable<OpenIddictSpacetimeAuthorization> FindBySubjectAsync(string subject, CancellationToken cancellationToken) => Query(a => a.Subject == subject, cancellationToken);
    public virtual async ValueTask<OpenIddictSpacetimeAuthorization?> FindByIdAsync(string identifier, CancellationToken cancellationToken) =>
        await spacetimeService.ReadAsync(c => c.Db.OpenIddictSpacetimeAuthorization.Iter().FirstOrDefault(a => a.OpenIddictAuthorizationId == identifier), cancellationToken);

    private async IAsyncEnumerable<OpenIddictSpacetimeAuthorization> Query(Func<OpenIddictSpacetimeAuthorization, bool> predicate, [EnumeratorCancellation] CancellationToken ct)
    {
        var rows = await spacetimeService.ReadAsync(c => c.Db.OpenIddictSpacetimeAuthorization.Iter().Where(predicate).ToList(), ct);
        foreach (var row in rows) { ct.ThrowIfCancellationRequested(); yield return row; }
    }

    private Task<List<OpenIddictSpacetimeAuthorization>> Rows(CancellationToken ct) =>
        spacetimeService.ReadAsync(c => c.Db.OpenIddictSpacetimeAuthorization.Iter().ToList(), ct);
    public virtual async IAsyncEnumerable<OpenIddictSpacetimeAuthorization> ListAsync(int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await Rows(cancellationToken);
        foreach (var row in rows.Skip(offset ?? 0).Take(count ?? int.MaxValue)) { cancellationToken.ThrowIfCancellationRequested(); yield return row; }
    }
    public virtual async IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<OpenIddictSpacetimeAuthorization>, TState, IQueryable<TResult>> query,
        TState state, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await Rows(cancellationToken);
        foreach (var result in query(rows.AsQueryable(), state)) { cancellationToken.ThrowIfCancellationRequested(); yield return result; }
    }
    public virtual async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        await spacetimeService.ReadAsync(c => c.Db.OpenIddictSpacetimeAuthorization.Iter().LongCount(), cancellationToken);
    public virtual async ValueTask<long> CountAsync<TResult>(Func<IQueryable<OpenIddictSpacetimeAuthorization>, IQueryable<TResult>> query, CancellationToken cancellationToken) =>
        query((await Rows(cancellationToken)).AsQueryable()).LongCount();
    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<OpenIddictSpacetimeAuthorization>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        query((await Rows(cancellationToken)).AsQueryable(), state).FirstOrDefault();

    public virtual ValueTask<OpenIddictSpacetimeAuthorization> InstantiateAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return new(new OpenIddictSpacetimeAuthorization { OpenIddictAuthorizationId = Guid.NewGuid().ToString() }); }
    public virtual ValueTask<string?> GetApplicationIdAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken) => new(authorization.ApplicationClientId);
    public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken) => new(authorization.CreationDate.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)authorization.CreationDate.Value) : null);
    public virtual ValueTask<string?> GetIdAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken) => new(authorization.OpenIddictAuthorizationId);
    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(authorization.Properties)) return new(ImmutableDictionary<string, JsonElement>.Empty);
        try { using var doc = JsonDocument.Parse(authorization.Properties); return new(doc.RootElement.EnumerateObject().ToImmutableDictionary(p => p.Name, p => p.Value.Clone())); }
        catch (JsonException) { return new(ImmutableDictionary<string, JsonElement>.Empty); }
    }
    public virtual ValueTask<ImmutableArray<string>> GetScopesAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken) => new(DeserializeScopes(authorization.Scopes));
    public virtual ValueTask<string?> GetStatusAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken) => new(authorization.Status);
    public virtual ValueTask<string?> GetSubjectAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken) => new(authorization.Subject);
    public virtual ValueTask<string?> GetTypeAsync(OpenIddictSpacetimeAuthorization authorization, CancellationToken cancellationToken) => new(authorization.Type);
    public virtual ValueTask SetApplicationIdAsync(OpenIddictSpacetimeAuthorization authorization, string? identifier, CancellationToken cancellationToken) { authorization.ApplicationClientId = identifier; return default; }
    public virtual ValueTask SetCreationDateAsync(OpenIddictSpacetimeAuthorization authorization, DateTimeOffset? date, CancellationToken cancellationToken) { authorization.CreationDate = date.HasValue ? (ulong)date.Value.ToUnixTimeMilliseconds() : null; return default; }
    public virtual ValueTask SetPropertiesAsync(OpenIddictSpacetimeAuthorization authorization, ImmutableDictionary<string, JsonElement>? properties, CancellationToken cancellationToken)
    { authorization.Properties = properties == null || properties.IsEmpty ? null : JsonSerializer.Serialize(properties); return default; }
    public virtual ValueTask SetScopesAsync(OpenIddictSpacetimeAuthorization authorization, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    { authorization.Scopes = scopes.IsDefaultOrEmpty ? null : JsonSerializer.Serialize(scopes); return default; }
    public virtual ValueTask SetStatusAsync(OpenIddictSpacetimeAuthorization authorization, string? status, CancellationToken cancellationToken) { authorization.Status = status; return default; }
    public virtual ValueTask SetSubjectAsync(OpenIddictSpacetimeAuthorization authorization, string? subject, CancellationToken cancellationToken) { authorization.Subject = subject; return default; }
    public virtual ValueTask SetTypeAsync(OpenIddictSpacetimeAuthorization authorization, string? type, CancellationToken cancellationToken) { authorization.Type = type; return default; }
    private static ImmutableArray<string> DeserializeScopes(string? json)
    {
        if (string.IsNullOrEmpty(json)) return ImmutableArray<string>.Empty;
        try { return JsonSerializer.Deserialize<List<string>>(json)?.ToImmutableArray() ?? ImmutableArray<string>.Empty; }
        catch (JsonException) { return ImmutableArray<string>.Empty; }
    }

    // Compatibility maintenance APIs: mappings are now read from committed storage, not a process cache.
    public void ClearCache() { }
    public int GetCacheSize() => 0;
    public bool RemoveFromCache(string oidcAuthId) => false;
}
