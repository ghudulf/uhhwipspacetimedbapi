namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

public interface IRealtimeEventBus
{
    ValueTask PublishAsync(ApiDomainEvent domainEvent, CancellationToken cancellationToken = default);
    IReadOnlyCollection<ApiDomainEvent> GetRecentEvents(int maxCount = 250);
    IAsyncEnumerable<ApiDomainEvent> SubscribeAsync(string resource, CancellationToken cancellationToken = default);
}
