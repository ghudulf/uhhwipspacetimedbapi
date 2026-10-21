using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

public sealed class CatalogMutationExecutor(ISpacetimeDBService database, ILogger logger)
{
    public async Task<MutationReceipt> ExecuteAsync<T>(T reducer, Identity actor, string id, CancellationToken ct) where T : Reducer, IReducerArgs, new()
    {
        return await ExecuteResultAsync(reducer, actor, id, (_, receipt) => receipt, ct);
    }
    public async Task<TResult> ExecuteResultAsync<T, TResult>(T reducer, Identity actor, string id,
        Func<DbConnection, MutationReceipt, TResult> result, CancellationToken ct) where T : Reducer, IReducerArgs, new()
    {
        logger.LogInformation("Submitting {Reducer} operation {OperationId}", typeof(T).Name, id);
        var committed = await database.ExecuteReducerAsync(reducer, c =>
        {
            var receipt = c.Db.MutationReceipt.Key.Find(actor + ":" + id) ?? throw new InvalidOperationException("Committed receipt missing.");
            return (Receipt: receipt, Result: result(c, receipt));
        }, ct);
        logger.LogInformation("Committed {Operation}, entity {EntityId}, revision {Revision}, operation {OperationId}",
            committed.Receipt.Operation, committed.Receipt.EntityId, committed.Receipt.Revision, id);
        return committed.Result;
    }
    public static Identity RequireActor(Identity? actor) => actor ?? throw new UnauthorizedAccessException("Authenticated actor required.");
}
