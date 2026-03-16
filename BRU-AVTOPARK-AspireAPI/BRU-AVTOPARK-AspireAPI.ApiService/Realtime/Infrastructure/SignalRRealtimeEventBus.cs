using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Helpers;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Hubs;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Options;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Utilities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

public sealed class SignalRRealtimeEventBus : BackgroundService, IRealtimeEventBus, IAsyncDisposable
{
    private readonly record struct EventSubscriber(string Resource, Channel<ApiDomainEvent> Channel);

    private readonly Channel<ApiDomainEvent> _eventChannel;
    private readonly ConcurrentQueue<ApiDomainEvent> _recentEvents = new();
    private readonly ConcurrentDictionary<Guid, EventSubscriber> _subscribers = new();
    private readonly IHubContext<SystemEventsHub> _hubContext;
    private readonly ILogger<SignalRRealtimeEventBus> _logger;
    private readonly RealtimeEventOptions _options;
    private readonly SemaphoreSlim _disposalLock = new(1, 1);
    private int _disposed;
    private bool _stopping;

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
        if (_stopping || _disposed != 0)
        {
            throw new ObjectDisposedException(nameof(SignalRRealtimeEventBus), "Event bus is stopping or disposed");
        }
        
        // Sanitize and truncate event-derived fields to prevent log injection
        var sanitizedEventName = LogSanitizer.SanitizeLogField(domainEvent.EventName, maxLength: 100);
        var normalizedResource = ResourceNormalization.Normalize(domainEvent.Resource);
        var sanitizedResource = LogSanitizer.SanitizeLogField(normalizedResource, maxLength: 100);
        var sanitizedCorrelationId = LogSanitizer.SanitizeLogField(domainEvent.CorrelationId, maxLength: 100);

        _logger.LogInformation("[EventBus] Publishing event: {EventName} for resource: {Resource} (CorrelationId: {CorrelationId})",
            sanitizedEventName,
            sanitizedResource,
            sanitizedCorrelationId);
        
        await _eventChannel.Writer.WriteAsync(domainEvent, cancellationToken);
        EnqueueForHistory(domainEvent);
        
        // Use sanitized event name to avoid log forging in debug logs as well
        _logger.LogDebug("[EventBus] Event queued successfully: {EventName}", sanitizedEventName);
    }

    /// <summary>
    /// Retrieves a snapshot of the most recent domain events, ordered from newest to oldest.
    /// </summary>
    /// <param name="maxCount">Maximum number of events to return; the value will be clamped to the service's configured recent-event limit.</param>
    /// <returns>An array containing up to the requested number of most recent events in reverse chronological order.</returns>
    public IReadOnlyCollection<ApiDomainEvent> GetRecentEvents(int maxCount = 250)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        
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

        // Atomically check the shutdown gate and register the subscriber so that
        // a concurrent StopAsync/DisposeAsync cannot miss this subscriber between
        // the check and the add (TOCTOU).
        await _disposalLock.WaitAsync(cancellationToken);
        try
        {
            if (_stopping || _disposed != 0)
            {
                throw new ObjectDisposedException(nameof(SignalRRealtimeEventBus), "Event bus is stopping or disposed");
            }
            _subscribers[subscriberId] = new EventSubscriber(normalizedResource, subscriptionChannel);
        }
        finally
        {
            _disposalLock.Release();
        }

        _logger.LogInformation("[EventBus] New subscription created: {SubscriberId} for resource: {Resource}", 
            subscriberId, LogSanitizer.SanitizeLogField(normalizedResource, 100));

        try
        {
            await foreach (var evt in subscriptionChannel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            // Ensure cleanup happens even if enumeration is abandoned
            if (_subscribers.TryRemove(subscriberId, out var removedSubscriber))
            {
                removedSubscriber.Channel.Writer.TryComplete();
                var boundedSanitizedResource = LogSanitizer.SanitizeLogField(normalizedResource, 200);
                _logger.LogInformation("[EventBus] Subscription disposed: {SubscriberId} for resource: {Resource}", subscriberId, boundedSanitizedResource);
            }
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
        _logger.LogInformation("[EventBus] Background event processing started");
        
        try
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
                    if (timeoutCts.IsCancellationRequested)
                    {
                        _logger.LogWarning("Realtime event dispatch timed out for event {EventName} ({CorrelationId})",
                            LogSanitizer.SanitizeLogField(domainEvent.EventName, 100),
                            LogSanitizer.SanitizeLogField(domainEvent.CorrelationId, 100));
                    }
                    // else: shutdown cancellation – swallow silently
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Realtime event dispatch failed for {EventName} ({CorrelationId})",
                        LogSanitizer.SanitizeLogField(domainEvent.EventName, 100),
                        LogSanitizer.SanitizeLogField(domainEvent.CorrelationId, 100));
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[EventBus] Background event processing cancelled");
        }
        finally
        {
            _logger.LogInformation("[EventBus] Background event processing stopped");
        }
    }

    /// <summary>
    /// Stops the background service and ensures all resources are properly disposed.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[EventBus] Stopping event bus and cleaning up resources");
        
        // Set stopping flag first
        _stopping = true;

        // Complete the event channel to stop accepting new events
        _eventChannel.Writer.TryComplete();

        // Wait for background processing to complete
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            // Dispose all active subscriptions even if base.StopAsync throws or is canceled
            await DisposeAllSubscriptionsAsync();
        }

        _logger.LogInformation("[EventBus] Event bus stopped successfully");
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

    /// <summary>
    /// Disposes all active subscriptions and completes their channels.
    /// </summary>
    private void DisposeAllSubscriptionsCore()
    {
        var subscriberCount = _subscribers.Count;
        if (subscriberCount > 0)
        {
            _logger.LogInformation("[EventBus] Disposing {Count} active subscriptions", subscriberCount);

            foreach (var (subscriberId, subscriber) in _subscribers)
            {
                subscriber.Channel.Writer.TryComplete();
                _logger.LogDebug("[EventBus] Completed channel for subscription: {SubscriberId}", subscriberId);
            }

            _subscribers.Clear();
            _logger.LogInformation("[EventBus] All subscriptions disposed");
        }
    }

    /// <summary>
    /// Disposes all active subscriptions with lock acquisition.
    /// </summary>
    private async Task DisposeAllSubscriptionsAsync()
    {
        await _disposalLock.WaitAsync();
        try
        {
            DisposeAllSubscriptionsCore();
        }
        finally
        {
            _disposalLock.Release();
        }
    }

    /// <summary>
    /// Core teardown logic shared by both DisposeAsync and Dispose.
    /// </summary>
    private void DisposeCoreLogic()
    {
        // Set stopping flag to fail-fast concurrent operations
        _stopping = true;

        _logger.LogInformation("[EventBus] Disposing event bus");

        // Complete the event channel
        _eventChannel.Writer.TryComplete();

        // Clear recent events
        _recentEvents.Clear();

        _logger.LogInformation("[EventBus] Event bus disposed successfully");
    }

    /// <summary>
    /// Asynchronously disposes the event bus and all its resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Use Interlocked.CompareExchange to ensure only one thread disposes
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
        {
            return;
        }

        await _disposalLock.WaitAsync();
        try
        {
            // Shared core teardown logic
            DisposeCoreLogic();

            // Dispose all subscriptions (call core method directly since we already hold the lock)
            DisposeAllSubscriptionsCore();
        }
        finally
        {
            _disposalLock.Release();
            _disposalLock.Dispose();
        }

        base.Dispose();
        GC.SuppressFinalize(this);
    }

    public override void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            // Synchronous disposal follows same teardown path
            _disposalLock.Wait();
            try
            {
                DisposeCoreLogic();
                DisposeAllSubscriptionsCore();
            }
            finally
            {
                _disposalLock.Release();
                _disposalLock.Dispose();
            }
        }

        base.Dispose();
        GC.SuppressFinalize(this);
    }
}