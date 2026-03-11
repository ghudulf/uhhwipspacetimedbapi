using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Hubs;

[Authorize(Policy = "FlexibleApiAccess")]
public sealed class SystemEventsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userName = Context.User?.Identity?.Name ?? Context.UserIdentifier ?? "anonymous";
        await Groups.AddToGroupAsync(Context.ConnectionId, "system-events");
        await Clients.Caller.SendAsync("connectionEstablished", new
        {
            ConnectionId = Context.ConnectionId,
            User = userName,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Protocol = Context.Protocol?.Name ?? "unknown"
        });

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "system-events");
        await base.OnDisconnectedAsync(exception);
    }

    public Task SubscribeResource(string resourceName)
    {
        var normalized = NormalizeResource(resourceName);
        return Groups.AddToGroupAsync(Context.ConnectionId, $"resource:{normalized}");
    }

    public Task UnsubscribeResource(string resourceName)
    {
        var normalized = NormalizeResource(resourceName);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"resource:{normalized}");
    }

    private static string NormalizeResource(string resourceName)
    {
        return string.IsNullOrWhiteSpace(resourceName)
            ? "all"
            : resourceName.Trim().ToLowerInvariant();
    }
}
