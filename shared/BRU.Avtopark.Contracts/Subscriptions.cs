using System.Text.Json;
namespace BRU.Avtopark.Contracts;

public sealed record SubscriptionRequest(string SubscriptionId, string Operation, JsonElement Payload);
public sealed record SubscriptionClose(string SubscriptionId);
public sealed record SnapshotWatermark(string Epoch, long Sequence);
public sealed record SubscriptionSnapshot(string SubscriptionId, string Operation, JsonElement Data, SnapshotWatermark Watermark);
/// <summary>Invalidates the query, requiring a fresh authoritative snapshot. Sequence ranges may coalesce changes.</summary>
public sealed record SubscriptionInvalidation(string SubscriptionId, SnapshotWatermark From, SnapshotWatermark Watermark, string Reason);
