namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

public interface IRealtimeEventBus
{
    /// <summary>
/// Publishes the provided <see cref="ApiDomainEvent"/> to interested subscribers.
/// </summary>
/// <param name="domainEvent">The domain event to publish.</param>
/// <param name="cancellationToken">A token to cancel the publish operation.</param>
/// <returns>A <see cref="ValueTask"/> that completes when the publish operation has finished.</returns>
ValueTask PublishAsync(ApiDomainEvent domainEvent, CancellationToken cancellationToken = default);
    /// <summary>
/// Retrieves a read-only collection of recently published ApiDomainEvent instances.
/// </summary>
/// <param name="maxCount">The maximum number of events to return; defaults to 250.</param>
/// <returns>A read-only collection containing up to <paramref name="maxCount"/> of the most recent ApiDomainEvent instances (may be empty).</returns>
IReadOnlyCollection<ApiDomainEvent> GetRecentEvents(int maxCount = 250);
    /// <summary>
/// Subscribes to domain events for the specified resource.
/// </summary>
/// <param name="resource">The resource identifier whose domain events should be delivered.</param>
/// <param name="cancellationToken">Token to cancel the subscription enumeration.</param>
/// <returns>An async sequence of <see cref="ApiDomainEvent"/> instances for the specified resource; enumeration ends when the stream completes or the token is canceled.</returns>
IAsyncEnumerable<ApiDomainEvent> SubscribeAsync(string resource, CancellationToken cancellationToken = default);
}
