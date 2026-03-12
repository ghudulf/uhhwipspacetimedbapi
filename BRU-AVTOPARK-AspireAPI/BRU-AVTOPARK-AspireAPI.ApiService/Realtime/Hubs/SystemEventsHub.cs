using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Hubs;

[Authorize(Policy = "FlexibleApiAccess")]
public sealed class SystemEventsHub : Hub
{
    private const string AdminRoleValue = "1";
    
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<SystemEventsHub> _logger;

    public SystemEventsHub(IAuthorizationService authorizationService, ILogger<SystemEventsHub> logger)
    {
        _authorizationService = authorizationService;
        _logger = logger;
    }

    /// <summary>
    /// Handles a new client connection by adding the connection to the "system-events" group and sending the caller a "connectionEstablished" notification containing connection metadata.
    /// </summary>
    /// <returns>A task that completes when connection handling is finished.</returns>
    public override async Task OnConnectedAsync()
    {
        var userName = Context.User?.Identity?.Name ?? Context.UserIdentifier ?? "anonymous";
        
        // Attempt to get the actual negotiated transport from connection features
        // SignalR doesn't expose transport type directly, so we use "unknown" as fallback
        var transport = "unknown";
        try
        {
            // Try to infer transport from connection ID format or other indicators
            // This is a best-effort approach since SignalR abstracts transport details
            var connectionId = Context.ConnectionId;
            if (!string.IsNullOrEmpty(connectionId))
            {
                // Connection IDs often contain transport hints, but this is implementation-specific
                transport = "signalr"; // Generic indicator that SignalR is handling transport
            }
        }
        catch
        {
            // Silently fall back to unknown if any issues occur
        }

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

        // Check if user has permission to view this resource
        var permissionName = $"{normalized}.view";
        var authResult = await _authorizationService.AuthorizeAsync(Context.User, null, permissionName);
        
        if (!authResult.Succeeded)
        {
            // Check if user is admin (admins can view all resources)
            var isAdmin = Context.User?.FindFirst("primary_role")?.Value == AdminRoleValue ||
                         Context.User?.Claims.Any(c => c.Type == "role" && c.Value == AdminRoleValue) == true;
            
            if (!isAdmin)
            {
                _logger.LogWarning("User {User} attempted to subscribe to resource {Resource} without permission", 
                    Context.User?.Identity?.Name ?? "unknown", normalized);
                throw new HubException($"Access denied: You do not have permission to view '{normalized}' resources");
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"resource:{normalized}");
        _logger.LogInformation("User {User} subscribed to resource {Resource}", 
            Context.User?.Identity?.Name ?? "unknown", normalized);
    }

    /// <summary>
    /// Unsubscribes the current connection from the group associated with the specified resource.
    /// </summary>
    /// <param name="resourceName">The resource name to unsubscribe from. If null or whitespace, "all" is used; otherwise the name is trimmed and converted to lowercase.</param>
    /// <returns>A task that completes when the connection has been removed from the corresponding resource group.</returns>
    public async Task UnsubscribeResource(string resourceName)
    {
        var normalized = ResourceNormalization.Normalize(resourceName);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"resource:{normalized}");
        _logger.LogInformation("User {User} unsubscribed from resource {Resource}", 
            Context.User?.Identity?.Name ?? "unknown", normalized);
    }
}