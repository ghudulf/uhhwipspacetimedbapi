using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
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
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    }

    public ValueTask PublishAsync(ApiDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        EnqueueForHistory(domainEvent);
        return _eventChannel.Writer.WriteAsync(domainEvent, cancellationToken);
    }

    public IReadOnlyCollection<ApiDomainEvent> GetRecentEvents(int maxCount = 250)
    {
        var safeCount = Math.Clamp(maxCount, 1, Math.Max(1, _options.RecentEventLimit));
        return _recentEvents.Reverse().Take(safeCount).ToArray();
    }

    public async IAsyncEnumerable<ApiDomainEvent> SubscribeAsync(string resource, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalizedResource = NormalizeResource(resource);
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var domainEvent in _eventChannel.Reader.ReadAllAsync(stoppingToken))
        {
            using var timeoutCts = new CancellationTokenSource(Math.Max(25, _options.PublishTimeoutMs));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

            try
            {
                await _hubContext.Clients.Group("system-events").SendAsync("domainEvent", domainEvent, linkedCts.Token);
                await _hubContext.Clients.Group($"resource:{domainEvent.Resource.ToLowerInvariant()}")
                    .SendAsync("resourceEvent", domainEvent, linkedCts.Token);

                DispatchToLocalSubscribers(domainEvent);
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

    private void DispatchToLocalSubscribers(ApiDomainEvent domainEvent)
    {
        var normalized = NormalizeResource(domainEvent.Resource);

        foreach (var (_, subscriber) in _subscribers)
        {
            if (subscriber.Resource != "all" && subscriber.Resource != normalized)
            {
                continue;
            }

            subscriber.Channel.Writer.TryWrite(domainEvent);
        }
    }

    private void EnqueueForHistory(ApiDomainEvent domainEvent)
    {
        _recentEvents.Enqueue(domainEvent);
        var max = Math.Max(50, _options.RecentEventLimit);

        while (_recentEvents.Count > max && _recentEvents.TryDequeue(out _))
        {
        }
    }

    private static string NormalizeResource(string resource)
    {
        return string.IsNullOrWhiteSpace(resource)
            ? "all"
            : resource.Trim().ToLowerInvariant();
    }
}
