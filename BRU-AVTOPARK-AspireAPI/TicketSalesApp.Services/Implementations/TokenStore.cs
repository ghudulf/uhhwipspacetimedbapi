using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

/// <summary>Only confirmed database rows are durable. No process-local ID or properties cache.</summary>
public sealed class TokenStore(ISpacetimeDBService database, ILogger<TokenStore> logger) : IOpenIddictTokenStore<OpenIddictToken>
{
    private static ulong? Timestamp(DateTimeOffset? date) => date.HasValue ? checked((ulong)date.Value.ToUnixTimeMilliseconds()) : null;
    private static DateTimeOffset? Date(ulong? value) => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(checked((long)value.Value)) : null;
    private static uint? Authorization(string? value) => value == null ? null : uint.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static OpenIddictToken Map(DbConnection connection, OpenIddictSpacetimeToken row) => new()
    {
        Id = row.OpenIddictTokenId, DatabaseId = row.Id,
        Revision = connection.Db.OidcTokenRevision.TokenId.Find(row.Id)?.Revision ?? 0,
        ApplicationId = row.ApplicationClientId, AuthorizationId = row.AuthorizationId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        CreationDate = Date(row.CreationDate), ExpirationDate = Date(row.ExpirationDate), RedemptionDate = Date(row.RedemptionDate),
        Payload = row.Payload, ReferenceId = row.ReferenceId, Status = row.Status, Subject = row.Subject, Type = row.Type,
        // JsonElement values created by Deserialize own their lifetime. Corrupt persisted metadata fails visibly.
        Properties = string.IsNullOrEmpty(row.Properties) ? ImmutableDictionary<string, JsonElement>.Empty :
            JsonSerializer.Deserialize<ImmutableDictionary<string, JsonElement>>(row.Properties) ?? ImmutableDictionary<string, JsonElement>.Empty
    };

    private Task<List<OpenIddictToken>> Snapshot(CancellationToken cancellationToken) => database.ReadAsync(
        connection => connection.Db.OpenIddictSpacetimeToken.Iter().OrderBy(t => t.Id).Select(t => Map(connection, t)).ToList(), cancellationToken);

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        await database.ReadAsync(c => c.Db.OpenIddictSpacetimeToken.Iter().LongCount(), cancellationToken);
    public async ValueTask<long> CountAsync<TResult>(Func<IQueryable<OpenIddictToken>, IQueryable<TResult>> query, CancellationToken cancellationToken) =>
        query((await Snapshot(cancellationToken)).AsQueryable()).LongCount();

    public async ValueTask CreateAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.DatabaseId.HasValue) throw new InvalidOperationException("Token already persisted.");
        var properties = JsonSerializer.Serialize(token.Properties);
        logger.LogDebug("OIDC create; stable ID {TokenId}, type {TokenType}, property count {PropertyCount}, payload present {HasPayload}",
            token.Id, token.Type, token.Properties.Count, token.Payload != null);
        token.DatabaseId = await database.ExecuteReducerAsync(new Reducer.CreateOidcToken(token.Id,
            Authorization(token.AuthorizationId), token.ApplicationId, Timestamp(token.CreationDate), Timestamp(token.ExpirationDate),
            token.Payload, properties, Timestamp(token.RedemptionDate), token.ReferenceId, token.Status, token.Subject, token.Type),
            c => c.Db.OpenIddictSpacetimeToken.Iter().Single(t => t.OpenIddictTokenId == token.Id).Id, cancellationToken);
        token.Revision = 0;
        logger.LogInformation("OIDC create confirmed: stable ID {TokenId}, database ID {DatabaseId}", token.Id, token.DatabaseId);
    }

    public async ValueTask UpdateAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        var id = token.DatabaseId ?? throw new InvalidOperationException("Token has not been persisted.");
        var revision = token.Revision;
        logger.LogDebug("OIDC update: database ID {DatabaseId}, expected revision {Revision}, property count {PropertyCount}", id, revision, token.Properties.Count);
        try
        {
            token.Revision = await database.ExecuteReducerAsync(new Reducer.ReplaceOidcToken(id, revision,
                Authorization(token.AuthorizationId), token.ApplicationId, Timestamp(token.CreationDate), Timestamp(token.ExpirationDate),
                token.Payload, JsonSerializer.Serialize(token.Properties), Timestamp(token.RedemptionDate), token.ReferenceId,
                token.Status, token.Subject, token.Type),
                c => c.Db.OidcTokenRevision.TokenId.Find(id)?.Revision ?? throw new InvalidOperationException("Committed token revision missing."), cancellationToken);
        }
        catch (ReducerRejectedException ex)
        {
            // OpenIddict treats a lost optimistic concurrency race as a failed redemption.
            throw new OpenIddictExceptions.ConcurrencyException("Token update rejected; reload persisted state.", ex);
        }
        logger.LogInformation("OIDC update confirmed: database ID {DatabaseId}, revision {Revision}", id, token.Revision);
    }

    public async ValueTask DeleteAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.DatabaseId is not uint id) return;
        await database.ExecuteReducerAsync(new Reducer.DeleteOidcToken(id), c => c.Db.OpenIddictSpacetimeToken.Id.Find(id) == null, cancellationToken);
        logger.LogInformation("OIDC delete confirmed: database ID {DatabaseId}", id);
    }

    public async ValueTask PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken) =>
        _ = await database.ExecuteReducerAsync(new Reducer.PruneOidcTokens(Timestamp(threshold)!.Value), _ => true, cancellationToken);

    public async ValueTask<OpenIddictToken?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var token = await database.ReadAsync(c => c.Db.OpenIddictSpacetimeToken.Iter().FirstOrDefault(t => t.OpenIddictTokenId == identifier) is { } row ? Map(c, row) : null, cancellationToken);
        logger.LogDebug("OIDC stable ID lookup: found {Found}", token != null);
        return token;
    }

    public async ValueTask<OpenIddictToken?> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        var token = await database.ReadAsync(c => c.Db.OpenIddictSpacetimeToken.Iter().FirstOrDefault(t => t.ReferenceId == identifier) is { } row ? Map(c, row) : null, cancellationToken);
        logger.LogDebug("OIDC reference lookup from committed storage: found {Found}, database ID {DatabaseId}", token != null, token?.DatabaseId);
        return token;
    }

    private async IAsyncEnumerable<OpenIddictToken> Filter(Func<OpenIddictToken, bool> predicate, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var token in await Snapshot(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate(token)) yield return token;
        }
    }
    public IAsyncEnumerable<OpenIddictToken> FindAsync(string subject, string client, CancellationToken cancellationToken) =>
        Filter(t => t.Subject == subject && t.ApplicationId == client, cancellationToken);
    public IAsyncEnumerable<OpenIddictToken> FindAsync(string subject, string client, string status, CancellationToken cancellationToken) =>
        Filter(t => t.Subject == subject && t.ApplicationId == client && t.Status == status, cancellationToken);
    public IAsyncEnumerable<OpenIddictToken> FindAsync(string subject, string client, string status, string type, CancellationToken cancellationToken) =>
        Filter(t => t.Subject == subject && t.ApplicationId == client && t.Status == status && t.Type == type, cancellationToken);
    public IAsyncEnumerable<OpenIddictToken> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken) => Filter(t => t.ApplicationId == identifier, cancellationToken);
    public IAsyncEnumerable<OpenIddictToken> FindByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken) => Filter(t => t.AuthorizationId == identifier, cancellationToken);
    public IAsyncEnumerable<OpenIddictToken> FindBySubjectAsync(string subject, CancellationToken cancellationToken) => Filter(t => t.Subject == subject, cancellationToken);

    public ValueTask<OpenIddictToken> InstantiateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new OpenIddictToken());
    }
    public async ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<OpenIddictToken>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        query((await Snapshot(cancellationToken)).AsQueryable(), state).FirstOrDefault();
    public async IAsyncEnumerable<OpenIddictToken> ListAsync(int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (count < 0 || offset < 0) throw new ArgumentOutOfRangeException(nameof(count));
        foreach (var token in (await Snapshot(cancellationToken)).Skip(offset ?? 0).Take(count ?? int.MaxValue))
        { cancellationToken.ThrowIfCancellationRequested(); yield return token; }
    }
    public async IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<OpenIddictToken>, TState, IQueryable<TResult>> query, TState state, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in query((await Snapshot(cancellationToken)).AsQueryable(), state))
        { cancellationToken.ThrowIfCancellationRequested(); yield return item; }
    }
    public ValueTask<string?> GetApplicationIdAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(token.ApplicationId);
    }
    public ValueTask SetApplicationIdAsync(OpenIddictToken token, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.ApplicationId = value;
        return ValueTask.CompletedTask;
    }
    public ValueTask<string?> GetAuthorizationIdAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(token.AuthorizationId);
    }
    public ValueTask SetAuthorizationIdAsync(OpenIddictToken token, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.AuthorizationId = value;
        return ValueTask.CompletedTask;
    }
    public ValueTask<DateTimeOffset?> GetCreationDateAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<DateTimeOffset?>(token.CreationDate);
    }
    public ValueTask SetCreationDateAsync(OpenIddictToken token, DateTimeOffset? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.CreationDate = value;
        return ValueTask.CompletedTask;
    }
    public ValueTask<DateTimeOffset?> GetExpirationDateAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<DateTimeOffset?>(token.ExpirationDate);
    }
    public ValueTask SetExpirationDateAsync(OpenIddictToken token, DateTimeOffset? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.ExpirationDate = value;
        return ValueTask.CompletedTask;
    }
    public ValueTask<string?> GetPayloadAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(token.Payload);
    }
    public ValueTask SetPayloadAsync(OpenIddictToken token, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.Payload = value;
        return ValueTask.CompletedTask;
    }
    public ValueTask<DateTimeOffset?> GetRedemptionDateAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<DateTimeOffset?>(token.RedemptionDate);
    }
    public ValueTask SetRedemptionDateAsync(OpenIddictToken token, DateTimeOffset? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.RedemptionDate = value;
        return ValueTask.CompletedTask;
    }
    public ValueTask<string?> GetReferenceIdAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(token.ReferenceId);
    }
    public ValueTask SetReferenceIdAsync(OpenIddictToken token, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.ReferenceId = value;
        return ValueTask.CompletedTask;
    }
    public ValueTask<string?> GetStatusAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(token.Status);
    }
    public ValueTask SetStatusAsync(OpenIddictToken token, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.Status = value;
        return ValueTask.CompletedTask;
    }
    public ValueTask<string?> GetSubjectAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(token.Subject);
    }
    public ValueTask SetSubjectAsync(OpenIddictToken token, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.Subject = value;
        return ValueTask.CompletedTask;
    }
    public ValueTask<string?> GetTypeAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(token.Type);
    }
    public ValueTask SetTypeAsync(OpenIddictToken token, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.Type = value;
        return ValueTask.CompletedTask;
    }
    public ValueTask<string?> GetIdAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(token.Id);
    }
    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ImmutableDictionary<string, JsonElement>>(token.Properties);
    }
    public ValueTask SetPropertiesAsync(OpenIddictToken token, ImmutableDictionary<string, JsonElement> value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        token.Properties = value.ToImmutableDictionary(p => p.Key, p => p.Value.Clone());
        return ValueTask.CompletedTask;
    }
}
