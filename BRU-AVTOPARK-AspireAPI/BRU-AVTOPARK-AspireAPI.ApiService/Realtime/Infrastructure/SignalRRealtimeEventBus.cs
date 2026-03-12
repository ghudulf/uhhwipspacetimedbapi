using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Helpers;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Hubs;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

public sealed class SignalRRealtimeEventBus : BackgroundService, IRealtimeEventBus
{
    private readonly record struct EventSubscriber(string Resource, Channel<ApiDomainEvent> Channel);

    private readonly Channel<ApiDomainEvent> _eventChannel;
    private readonly ConcurrentQueue<ApiDomainEvent> _recentEvents = new();
    private readonly ConcurrentDictionary<Guid, EventSubscriber> _subscribers = new();
    private readonly IHubContext<SystemEventsHub> _hubContext;
    private readonly ILogger<SignalRRealtimeEventBus> _logger;
    private readonly RealtimeEventOptions _options;

    /// <summary>
    /// Initializes a SignalRRealtimeEventBus with the provided SignalR hub context, configuration options, and logger.
    /// </summary>
    /// <param name="hubContext">SignalR hub context used to broadcast events to connected clients.</param>
    /// <param name="options">Configuration options for buffering, recent-event limits, and publish timeouts.</param>
    /// <param name="logger">Logger for diagnostic messages produced by the event bus.</param>
    public SignalRRealtimeEventBus(
        IHubContext<SystemEventsHub> hubContext,
        IOptions<RealtimeEventOptions> options,
        ILogger<SignalRRealtimeEventBus> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
        _options = options.Value;

        _eventChannel = Channel.CreateBounded<ApiDomainEvent>(new BoundedChannelOptions(Math.Max(128, _options.BufferCapacity))
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>
    /// Publishes a domain event for local buffering and delivery to subscribers and SignalR clients.
    /// </summary>
    /// <param name="domainEvent">The domain event to publish and record in the recent-events history.</param>
    /// <param name="cancellationToken">A token to cancel the publish operation while queuing the event.</param>
    /// <returns>A ValueTask that completes when the event has been queued for dispatch; blocks if the channel is full to apply backpressure.</returns>
    public async ValueTask PublishAsync(ApiDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[EventBus] Publishing event: {EventName} for resource: {Resource} (CorrelationId: {CorrelationId})",
            domainEvent.EventName,
            domainEvent.Resource,
            domainEvent.CorrelationId);
        
        await _eventChannel.Writer.WriteAsync(domainEvent, cancellationToken);
        EnqueueForHistory(domainEvent);
        
        _logger.LogDebug("[EventBus] Event queued successfully: {EventName}", domainEvent.EventName);
    }

    /// <summary>
    /// Retrieves a snapshot of the most recent domain events, ordered from newest to oldest.
    /// </summary>
    /// <param name="maxCount">Maximum number of events to return; the value will be clamped to the service's configured recent-event limit.</param>
    /// <returns>An array containing up to the requested number of most recent events in reverse chronological order.</returns>
    public IReadOnlyCollection<ApiDomainEvent> GetRecentEvents(int maxCount = 250)
    {
        var safeCount = Math.Clamp(maxCount, 1, Math.Max(1, _options.RecentEventLimit));
        return _recentEvents.Reverse().Take(safeCount).ToArray();
    }

    /// <summary>
    /// Streams domain events to the caller, filtered by the specified resource.
    /// </summary>
    /// <param name="resource">The resource name to subscribe to. If null, empty, or whitespace, the subscription receives events for all resources.</param>
    /// <param name="cancellationToken">A token to cancel the subscription and stop the event stream.</param>
    /// <returns>An asynchronous sequence of <see cref="ApiDomainEvent"/> instances that match the requested resource.</returns>
    public async IAsyncEnumerable<ApiDomainEvent> SubscribeAsync(string resource, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalizedResource = ResourceNormalization.Normalize(resource);
        var subscriptionChannel = Channel.CreateBounded<ApiDomainEvent>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });

        var subscriberId = Guid.NewGuid();
        _subscribers[subscriberId] = new EventSubscriber(normalizedResource, subscriptionChannel);

        try
        {
            await foreach (var evt in subscriptionChannel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            _subscribers.TryRemove(subscriberId, out _);
            subscriptionChannel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Continuously processes inbound domain events by forwarding each event to local subscribers and broadcasting it to SignalR groups ("system-events" and "resource:&lt;resource&gt;") using the configured publish timeout.
    /// </summary>
    /// <remarks>
    /// Processing stops when <paramref name="stoppingToken"/> is canceled; timeouts and failures during SignalR dispatch are logged.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var domainEvent in _eventChannel.Reader.ReadAllAsync(stoppingToken))
        {
            DispatchToLocalSubscribers(domainEvent);

            using var timeoutCts = new CancellationTokenSource(Math.Max(25, _options.PublishTimeoutMs));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

            try
            {
                var normalizedResource = ResourceNormalization.Normalize(domainEvent.Resource);
                await _hubContext.Clients.Group("system-events").SendAsync("domainEvent", domainEvent, linkedCts.Token);
                await _hubContext.Clients.Group($"resource:{normalizedResource}")
                    .SendAsync("resourceEvent", domainEvent, linkedCts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
            {
                _logger.LogWarning("Realtime event dispatch timed out for event {EventName} ({CorrelationId})",
                    domainEvent.EventName,
                    domainEvent.CorrelationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Realtime event dispatch failed for {EventName} ({CorrelationId})",
                    domainEvent.EventName,
                    domainEvent.CorrelationId);
            }
        }
    }

    /// <summary>
    /// Sends the given domain event to all local subscribers whose subscribed resource matches the event's resource or who are subscribed to "all".
    /// </summary>
    /// <param name="domainEvent">The domain event to dispatch to matching local subscribers.</param>
    private void DispatchToLocalSubscribers(ApiDomainEvent domainEvent)
    {
        var normalized = ResourceNormalization.Normalize(domainEvent.Resource);

        foreach (var (_, subscriber) in _subscribers)
        {
            if (subscriber.Resource != "all" && subscriber.Resource != normalized)
            {
                continue;
            }

            subscriber.Channel.Writer.TryWrite(domainEvent);
        }
    }

    /// <summary>
    /// Adds a domain event to the in-memory recent-events history and ensures the history does not grow beyond the configured limit.
    /// </summary>
    /// <param name="domainEvent">The domain event to record in the recent-events buffer.</param>
    private void EnqueueForHistory(ApiDomainEvent domainEvent)
    {
        _recentEvents.Enqueue(domainEvent);
        var max = Math.Max(50, _options.RecentEventLimit);

        while (_recentEvents.Count > max && _recentEvents.TryDequeue(out _))
        {
        }
    }
}