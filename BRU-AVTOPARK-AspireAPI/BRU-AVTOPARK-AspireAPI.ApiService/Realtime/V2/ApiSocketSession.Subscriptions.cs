using System.Text.Json;
using System.Threading.Channels;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using SpacetimeDB;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.V2;

public sealed partial class ApiSocketSession
{
    private sealed record Subscription(string Id, string Operation, string[] Resources, DatabaseWatermark Watermark);
    private readonly Dictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);
    private int _tickQueued;
    // Explicit query dependencies. Authentication/receipt operations can never become subscriptions.
    private static readonly Dictionary<string, string[]> QueryResources = new(StringComparer.Ordinal)
    {
        ["buses.list"] = ["buses", "routes", "maintenance", "access"],
        ["sales.list"] = ["sales", "tickets", "users", "access"],
        ["reports.sales"] = ["sales", "tickets", "routes", "schedules", "buses", "access"],
        ["reports.income"] = ["sales", "tickets", "access"],
        ["session.access"] = ["access"],
        ["routes.list"] = ["routes", "buses", "employees", "tickets", "access"],
        ["employees.list"] = ["employees", "jobs", "access"],
        ["employees.drivers"] = ["employees", "jobs", "access"],
        ["jobs.list"] = ["jobs", "employees", "access"],
        ["maintenance.list"] = ["maintenance", "buses", "access"],
        ["tickets.list"] = ["tickets", "routes", "access"],
        ["schedules.list"] = ["schedules", "routes", "buses", "employees", "access"],
        ["users.list"] = ["users", "roles", "access"],
        ["users.get"] = ["users", "roles", "access"],
        ["roles.list"] = ["roles", "permissions", "users", "access"],
        ["permissions.list"] = ["permissions", "access"]
    };
    private static void ValidateSubscriptionId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new OperationException("validation_failed");
    }
    private async Task<object> OpenSubscriptionAsync(CommandEnvelope command, Identity actor, CancellationToken ct)
    {
        var request = command.Payload.Deserialize<SubscriptionRequest>(ApiProtocol.Json) ?? throw new OperationException("validation_failed");
        ValidateSubscriptionId(request.SubscriptionId);
        if (request.Operation == null || !QueryResources.TryGetValue(request.Operation, out var resources))
            throw new OperationException("unsupported_subscription");
        if (!_subscriptions.ContainsKey(request.SubscriptionId) && _subscriptions.Count >= 16) throw new OperationException("subscription_limit");
        if (database.State != DatabaseConnectionState.Ready) throw new OperationException("database_unavailable");
        // Capture BEFORE querying. Changes between this watermark and the snapshot are replayed as
        // invalidations, so no update can fall between snapshot acquisition and subscription installation.
        var watermark = database.Changes.Current;
        var snapshot = await operations.ExecuteAsync(request.Operation, actor, request.Payload, null, ct);
        if (database.State != DatabaseConnectionState.Ready || database.Changes.Current.Epoch != watermark.Epoch)
            throw new OperationException("resync_required");
        await operations.AuthorizeAsync(request.Operation, actor, ct);
        _subscriptions[request.SubscriptionId] = new(request.SubscriptionId, request.Operation, resources, watermark);
        logger.LogDebug("Subscription opened {SubscriptionId} {Operation} Epoch={Epoch} Watermark={Watermark}", request.SubscriptionId, request.Operation, watermark.Epoch, watermark.Sequence);
        return new SubscriptionSnapshot(request.SubscriptionId, request.Operation,
            JsonSerializer.SerializeToElement(snapshot, ApiProtocol.Json), ToContract(watermark));
    }
    private static SnapshotWatermark ToContract(DatabaseWatermark watermark) => new(watermark.Epoch, watermark.Sequence);
    private async Task TickAsync(ChannelWriter<QueuedCommand> commands, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct))
                if (Interlocked.CompareExchange(ref _tickQueued, 1, 0) == 0 && !commands.TryWrite(new(null, 0)))
                    Interlocked.Exchange(ref _tickQueued, 0);
        }
        catch (OperationCanceledException) { }
    }
    private async Task PumpSubscriptionsAsync(ChannelWriter<ResultEnvelope> output, CancellationToken ct)
    {
        if (_subscriptions.Count == 0) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var actor = _token == null ? null : await ResolveActorAsync(_token, deadline.Token);
            if (actor == null)
            {
                ResetSession("expired");
                if (!output.TryWrite(Result(null, false, null, "session_expired", "session"))) throw new OperationException("event_overflow");
                return;
            }
            foreach (var subscription in _subscriptions.Values.ToArray())
            {
                var reason = "changed";
                try { await operations.AuthorizeAsync(subscription.Operation, actor.Value, deadline.Token); }
                catch (OperationException ex) when (ex.Code == "forbidden") { reason = "forbidden"; }
                var batch = database.Changes.Read(subscription.Watermark);
                if (reason != "forbidden" && (batch.Gap || database.State != DatabaseConnectionState.Ready)) reason = "resync_required";
                if (reason == "changed" && !batch.Changes.Any(c => subscription.Resources.Contains(c.Resource))) continue;
                var invalidation = new SubscriptionInvalidation(subscription.Id, ToContract(subscription.Watermark), ToContract(batch.Watermark), reason);
                // An invalidation ends this snapshot lease. The client obtains a fresh authorized snapshot
                // before receiving further data. Queue overflow closes the connection: it never silently drops events.
                if (!output.TryWrite(Result(null, true, invalidation, type: "invalidation"))) throw new OperationException("event_overflow");
                _subscriptions.Remove(subscription.Id);
                logger.LogInformation("Subscription {SubscriptionId} {Operation} invalidated: {Reason}; watermark {Sequence}",
                    subscription.Id, subscription.Operation, reason, batch.Watermark.Sequence);
            }
        }
        catch (Exception ex) when (ex is DatabaseUnavailableException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            // Failed reauthorization cannot keep a snapshot live. Reconnect requires fresh authentication and snapshots.
            throw new OperationException("resync_required");
        }
    }
}
