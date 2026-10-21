using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

public sealed record QueryUpdate<T>(T? Data, bool IsSynchronized, string? Reason = null);

public sealed partial class RealtimeApplicationClient
{
    private readonly ConcurrentDictionary<string, Channel<SubscriptionInvalidation>> _subscriptions = new(StringComparer.Ordinal);

    /// <summary>One authorized snapshot lease at a time. Never merges incomplete event history into local rows.</summary>
    public async IAsyncEnumerable<QueryUpdate<TResponse>> WatchAsync<TRequest, TResponse>(string operation, TRequest payload,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsConnected || !_operations.Contains("subscriptions.open")) throw new OperationNotDispatchedException(operation);
        var id = Guid.NewGuid().ToString("N");
        var updates = Channel.CreateBounded<SubscriptionInvalidation>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        _subscriptions[id] = updates;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            while (true)
            {
                var snapshot = await CallAsync<SubscriptionRequest, SubscriptionSnapshot>("subscriptions.open",
                    new(id, operation, JsonSerializer.SerializeToElement(payload, ApiProtocol.Json)), null, lifetime.Token)
                    ?? throw new ApplicationCommandException("invalid_snapshot");
                if (snapshot.SubscriptionId != id || snapshot.Operation != operation || snapshot.Watermark.Sequence < 0)
                    throw new ApplicationCommandException("invalid_snapshot");
                // The receive loop can enqueue a racing invalidation before the awaiting caller resumes.
                // Do not briefly label that already-invalidated snapshot synchronized.
                if (!updates.Reader.TryRead(out var invalidation))
                {
                    yield return new(snapshot.Data.Deserialize<TResponse>(ApiProtocol.Json), true);
                    invalidation = await ReadInvalidationAsync(updates.Reader, lifetime.Token);
                }
                var reason = invalidation.From != snapshot.Watermark ? "resync_required" : invalidation.Reason;
                yield return new(default, false, reason);
                if (invalidation.Reason == "forbidden") throw new ApplicationCommandException("forbidden");
                // A replaced epoch or sequence gap always starts a new snapshot; no mutation is replayed.
            }
        }
        finally
        {
            _subscriptions.TryRemove(id, out _);
            if (IsConnected)
            {
                _ = CloseSubscriptionAsync(id);
            }
        }
    }

    private async Task CloseSubscriptionAsync(string id)
    {
        var dispatched = false;
        try
        {
            // Slot exhaustion is pre-dispatch and must never invalidate unrelated mutations.
            for (var attempt = 0; attempt < 100 && IsConnected; attempt++)
            {
                dispatched = false;
                try
                {
                    await CallCoreAsync<SubscriptionClose, JsonElement>("subscriptions.close", new(id), null, _lifetime.Token,
                        () => dispatched = true);
                    return;
                }
                catch (OperationNotDispatchedException) { await Task.Delay(100, _lifetime.Token); }
                catch (ApplicationCommandException ex) when (ex.Code is "busy" or "cancelled")
                {
                    dispatched = false; // Definitive rejection, not an indeterminate lease release.
                    await Task.Delay(100, _lifetime.Token);
                }
                catch (ApplicationCommandException ex) when (ex.Code is not ("connection_lost" or "commit_outcome_unknown")) { return; }
            }
        }
        catch (Exception)
        {
            // Background lease cleanup must never fault unobserved, including disposal races.
            if (dispatched)
            {
                try { _lifetime.Cancel(); }
                catch (Exception) { }
                try { _socket.Abort(); }
                catch (Exception) { }
            }
        }
    }
    private static async Task<SubscriptionInvalidation> ReadInvalidationAsync(ChannelReader<SubscriptionInvalidation> reader, CancellationToken ct)
    {
        try { return await reader.ReadAsync(ct); }
        catch (ChannelClosedException ex) when (ex.InnerException is ApplicationCommandException command) { throw command; }
    }
    private void ReceiveInvalidation(ResultEnvelope result)
    {
        var invalidation = result.Data?.Deserialize<SubscriptionInvalidation>(ApiProtocol.Json)
            ?? throw new JsonException("Missing invalidation.");
        if (_subscriptions.TryGetValue(invalidation.SubscriptionId, out var updates) && !updates.Writer.TryWrite(invalidation))
            throw new JsonException("Subscription event overflow; reconnect and resynchronize.");
    }
    private void EndSubscriptions(string code)
    {
        foreach (var entry in _subscriptions.ToArray())
            if (_subscriptions.TryRemove(entry.Key, out var updates)) updates.Writer.TryComplete(new ApplicationCommandException(code));
    }
}
