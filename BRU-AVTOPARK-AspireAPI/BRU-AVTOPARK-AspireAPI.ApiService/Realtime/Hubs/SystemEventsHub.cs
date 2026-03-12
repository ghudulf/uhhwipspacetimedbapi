using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Hubs;

[Authorize(Policy = "FlexibleApiAccess")]
public sealed class SystemEventsHub : Hub
{
    /// <summary>
    /// Handles a new client connection by adding the connection to the "system-events" group and sending the caller a "connectionEstablished" notification containing connection metadata.
    /// </summary>
    /// <returns>A task that completes when connection handling is finished.</returns>
    public override async Task OnConnectedAsync()
    {
        var userName = Context.User?.Identity?.Name ?? Context.UserIdentifier ?? "anonymous";
        var transport = "websocket"; // Default transport type for SignalR

        await Groups.AddToGroupAsync(Context.ConnectionId, "system-events");
        await Clients.Caller.SendAsync("connectionEstablished", new
        {
            ConnectionId = Context.ConnectionId,
            User = userName,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Transport = transport
        });

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Handles a client disconnection by removing the connection from the "system-events" group and delegating remaining cleanup to the base hub.
    /// </summary>
    /// <param name="exception">The exception that triggered the disconnection, if any; passed to the base handler.</param>
    /// <returns>A Task that completes when group removal and base disconnection handling have finished.</returns>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "system-events");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribes the current connection to updates for the specified resource.
    /// </summary>
    /// <param name="resourceName">The resource name to subscribe to; null or whitespace subscribes to the "all" resource. The value is trimmed and converted to lower-case.</param>
    /// <returns>A task that completes when the connection has been added to the corresponding resource group.</returns>
    public async Task SubscribeResource(string resourceName)
    {
        var normalized = ResourceNormalization.Normalize(resourceName);

        // TODO: Implement resource-scoped permission validation
        // Verify that Context.User has access to the specific normalized resource
        // Example: Check if user has permission for the resource via IAuthorizationService
        // If unauthorized, throw HubException or return failed Task

        await Groups.AddToGroupAsync(Context.ConnectionId, $"resource:{normalized}");
    }

    /// <summary>
    /// Unsubscribes the current connection from the group associated with the specified resource.
    /// </summary>
    /// <param name="resourceName">The resource name to unsubscribe from. If null or whitespace, "all" is used; otherwise the name is trimmed and converted to lowercase.</param>
    /// <returns>A task that completes when the connection has been removed from the corresponding resource group.</returns>
    public async Task UnsubscribeResource(string resourceName)
    {
        var normalized = ResourceNormalization.Normalize(resourceName);

        // TODO: Implement resource-scoped permission validation
        // Verify that Context.User has access to the specific normalized resource
        // Example: Check if user has permission for the resource via IAuthorizationService
        // If unauthorized, throw HubException or return failed Task

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"resource:{normalized}");
    }
}