using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FlexibleApiAccess")]
public sealed class RealtimeController : ControllerBase
{
    private readonly IRealtimeEventBus _eventBus;
    private readonly ILogger<RealtimeController> _logger;
    private readonly IServiceProvider _serviceProvider;

    // Map resource names to their service interfaces and event channels
    private readonly Dictionary<string, ResourceHandler> _resourceHandlers;

    public RealtimeController(
        IRealtimeEventBus eventBus,
        ILogger<RealtimeController> logger,
        IServiceProvider serviceProvider)
    {
        _eventBus = eventBus;
        _logger = logger;
        _serviceProvider = serviceProvider;

        // Initialize resource handlers for dynamic CRUD routing
        _resourceHandlers = new Dictionary<string, ResourceHandler>(StringComparer.OrdinalIgnoreCase)
        {
            { "buses", new ResourceHandler(typeof(IBusService), "buses") },
            { "employees", new ResourceHandler(typeof(IEmployeeService), "employees") },
            { "jobs", new ResourceHandler(typeof(IEmployeeService), "jobs") },
            { "maintenance", new ResourceHandler(typeof(IMaintenanceService), "maintenance") },
            { "permissions", new ResourceHandler(typeof(IPermissionService), "permissions") },
            { "roles", new ResourceHandler(typeof(IRoleService), "roles") },
            { "routes", new ResourceHandler(typeof(IRouteService), "routes") },
            { "routeschedules", new ResourceHandler(typeof(IRouteScheduleService), "route-schedules") },
            { "tickets", new ResourceHandler(typeof(ITicketService), "tickets") },
            { "ticketsales", new ResourceHandler(typeof(ITicketSalesService), "ticket-sales") },
            { "users", new ResourceHandler(typeof(IUserService), "users") }
        };
    }

    /// <summary>
    /// Retrieve recent realtime domain events from the event bus.
    /// </summary>
    /// <param name="maxCount">Maximum number of events to return. Defaults to 100.</param>
    /// <returns>An OkObjectResult containing a collection of recent domain events, limited to <paramref name="maxCount"/> items.</returns>
    [HttpGet("events")]
    public IActionResult GetRecentEvents([FromQuery] int maxCount = 100)
    {
        return Ok(_eventBus.GetRecentEvents(maxCount));
    }

    /// <summary>
    /// Publishes a synthetic "system.broadcast-test" domain event to the realtime event bus and responds with an acknowledgement.
    /// </summary>
    /// <param name="cancellationToken">Token forwarded to the event bus publish operation to cancel the request.</param>
    /// <returns>An HTTP 202 Accepted response containing a JSON payload with `message` and `correlationId`.</returns>
    [HttpPost("broadcast-test")]
    public async Task<IActionResult> BroadcastTest(CancellationToken cancellationToken)
    {
        var evt = new ApiDomainEvent(
            EventName: "system.broadcast-test",
            Resource: "system",
            HttpMethod: HttpMethods.Post,
            StatusCode: StatusCodes.Status200OK,
            OccurredAt: DateTimeOffset.UtcNow,
            CorrelationId: HttpContext.TraceIdentifier,
            UserId: User.FindFirst("sub")?.Value,
            UserName: User.Identity?.Name,
            Tenant: User.FindFirst("tenant")?.Value,
            SourceIp: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            Metadata: new Dictionary<string, string>
            {
                ["purpose"] = "connectivity-check",
                ["path"] = HttpContext.Request.Path,
                ["trigger"] = "manual"
            });

        await _eventBus.PublishAsync(evt, cancellationToken);
        return Accepted(new { message = "Realtime event enqueued.", correlationId = evt.CorrelationId });
    }

    /// <summary>
    /// Universal WebSocket stream endpoint with dynamic CRUD routing and event handling.
    /// Supports: ping/pong, CRUD operations for all resources, event subscriptions, and broadcasting.
    /// For controller-specific CRUD event streams, use individual controller WebSocket endpoints.
    /// </summary>
    [HttpGet("stream")]
    public async Task StreamEvents()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsync("WebSocket connection required");
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync("bru.events.v1");
        var connectionId = Guid.NewGuid().ToString();
        _logger.LogInformation("[{ConnectionId}] Universal stream WebSocket connection established", connectionId);

        var buffer = new byte[8192];
        var subscriptions = new HashSet<string>();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);

        try
        {
            // Start event broadcasting task
            var broadcastTask = BroadcastEventsAsync(webSocket, subscriptions, connectionId, linkedCts.Token);

            while (webSocket.State == WebSocketState.Open && !linkedCts.Token.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await webSocket.ReceiveAsync(buffer, linkedCts.Token);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                    _logger.LogInformation("[{ConnectionId}] WebSocket closed by client", connectionId);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var messageJson = Encoding.UTF8.GetString(ms.ToArray());
                    _logger.LogInformation("[{ConnectionId}] <<<< RECEIVED RAW: {Message}", connectionId, messageJson);

                    await HandleMessageAsync(webSocket, messageJson, subscriptions, connectionId, linkedCts.Token);
                }
            }

            linkedCts.Cancel();
            await broadcastTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[{ConnectionId}] Operation cancelled", connectionId);
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogWarning("[{ConnectionId}] Connection closed prematurely", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] Unexpected error in universal stream", connectionId);
            
            if (webSocket.State == WebSocketState.Open)
            {
                var errorResponse = new
                {
                    type = "error",
                    message = "Internal server error",
                    timestamp = DateTimeOffset.UtcNow
                };
                await SendJsonAsync(webSocket, errorResponse, CancellationToken.None);
            }
        }
        finally
        {
            linkedCts.Dispose();
        }
    }

    private async Task HandleMessageAsync(
        WebSocket webSocket,
        string messageJson,
        HashSet<string> subscriptions,
        string connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[{ConnectionId}] Parsing message...", connectionId);
            
            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;

            // Extract message type/command
            var messageType = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            var command = root.TryGetProperty("command", out var cmdEl) ? cmdEl.GetString()?.ToLowerInvariant() : null;
            var requestId = root.TryGetProperty("requestId", out var reqIdEl) ? reqIdEl.GetString() : Guid.NewGuid().ToString();

            _logger.LogInformation("[{ConnectionId}] Parsed - Type: {Type}, Command: {Command}, RequestId: {RequestId}", 
                connectionId, messageType ?? "null", command ?? "null", requestId);

            // Handle ping/pong
            if (messageType == "ping" || command == "ping")
            {
                _logger.LogInformation("[{ConnectionId}] Handling PING", connectionId);
                await HandlePingAsync(webSocket, requestId, cancellationToken);
                return;
            }

            // Handle subscription management
            if (command == "subscribe")
            {
                _logger.LogInformation("[{ConnectionId}] Handling SUBSCRIBE", connectionId);
                await HandleSubscribeAsync(webSocket, root, subscriptions, requestId, connectionId, cancellationToken);
                return;
            }

            if (command == "unsubscribe")
            {
                _logger.LogInformation("[{ConnectionId}] Handling UNSUBSCRIBE", connectionId);
                await HandleUnsubscribeAsync(webSocket, root, subscriptions, requestId, connectionId, cancellationToken);
                return;
            }

            // Handle CRUD operations
            if (IsCrudCommand(command))
            {
                _logger.LogInformation("[{ConnectionId}] Handling CRUD command: {Command}", connectionId, command);
                await HandleCrudAsync(webSocket, root, command, requestId, connectionId, cancellationToken);
                return;
            }

            // Unknown command
            _logger.LogWarning("[{ConnectionId}] Unknown command/type: {Command}/{Type}", connectionId, command, messageType);
            await SendErrorAsync(webSocket, requestId, $"Unknown command: {command ?? messageType}", cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] JSON parsing error. Raw message: {Message}", connectionId, messageJson);
            await SendErrorAsync(webSocket, null, "Invalid JSON format", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] Error handling message. Raw: {Message}", connectionId, messageJson);
            await SendErrorAsync(webSocket, null, "Error processing message", cancellationToken);
        }
    }

    private async Task HandlePingAsync(WebSocket webSocket, string requestId, CancellationToken cancellationToken)
    {
        var pong = new
        {
            type = "pong",
            requestId,
            timestamp = DateTimeOffset.UtcNow,
            serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await SendJsonAsync(webSocket, pong, cancellationToken);
        _logger.LogInformation("Sent pong response for request {RequestId}", requestId);
    }

    private async Task HandleSubscribeAsync(
        WebSocket webSocket,
        JsonElement root,
        HashSet<string> subscriptions,
        string requestId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var resource = root.TryGetProperty("resource", out var resEl) ? resEl.GetString() : null;

        if (string.IsNullOrEmpty(resource))
        {
            await SendErrorAsync(webSocket, requestId, "Resource name required for subscription", cancellationToken);
            return;
        }

        subscriptions.Add(resource);
        _logger.LogInformation("[{ConnectionId}] Subscribed to resource: {Resource}", connectionId, resource);

        var response = new
        {
            type = "subscribed",
            requestId,
            resource,
            timestamp = DateTimeOffset.UtcNow
        };

        await SendJsonAsync(webSocket, response, cancellationToken);
    }

    private async Task HandleUnsubscribeAsync(
        WebSocket webSocket,
        JsonElement root,
        HashSet<string> subscriptions,
        string requestId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var resource = root.TryGetProperty("resource", out var resEl) ? resEl.GetString() : null;

        if (string.IsNullOrEmpty(resource))
        {
            await SendErrorAsync(webSocket, requestId, "Resource name required for unsubscription", cancellationToken);
            return;
        }

        subscriptions.Remove(resource);
        _logger.LogInformation("[{ConnectionId}] Unsubscribed from resource: {Resource}", connectionId, resource);

        var response = new
        {
            type = "unsubscribed",
            requestId,
            resource,
            timestamp = DateTimeOffset.UtcNow
        };

        await SendJsonAsync(webSocket, response, cancellationToken);
    }

    private async Task HandleCrudAsync(
        WebSocket webSocket,
        JsonElement root,
        string? command,
        string requestId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var resource = root.TryGetProperty("resource", out var resEl) ? resEl.GetString() : null;

        if (string.IsNullOrEmpty(resource))
        {
            await SendErrorAsync(webSocket, requestId, "Resource name required for CRUD operations", cancellationToken);
            return;
        }

        if (!_resourceHandlers.TryGetValue(resource, out var handler))
        {
            await SendErrorAsync(webSocket, requestId, $"Unknown resource: {resource}", cancellationToken);
            return;
        }

        try
        {
            // Get the service instance
            var service = _serviceProvider.GetService(handler.ServiceType);
            if (service == null)
            {
                await SendErrorAsync(webSocket, requestId, $"Service not available for resource: {resource}", cancellationToken);
                return;
            }

            object? result = null;
            var success = true;

            // Route to appropriate CRUD handler based on command
            switch (command)
            {
                case "read_all":
                    result = await ExecuteReadAllAsync(service, resource);
                    break;

                case "read":
                    var id = root.TryGetProperty("id", out var idEl) ? idEl.GetUInt32() : (uint?)null;
                    if (!id.HasValue)
                    {
                        await SendErrorAsync(webSocket, requestId, "ID required for read operation", cancellationToken);
                        return;
                    }
                    result = await ExecuteReadAsync(service, resource, id.Value);
                    break;

                case "create":
                case "update":
                case "delete":
                    await SendErrorAsync(webSocket, requestId, $"{command} operations not yet implemented via universal stream", cancellationToken);
                    return;

                default:
                    await SendErrorAsync(webSocket, requestId, $"Unsupported CRUD command: {command}", cancellationToken);
                    return;
            }

            // Send success response
            var response = new
            {
                type = "result",
                requestId,
                command,
                resource,
                ok = success,
                data = result,
                timestamp = DateTimeOffset.UtcNow
            };

            await SendJsonAsync(webSocket, response, cancellationToken);
            _logger.LogInformation("[{ConnectionId}] CRUD {Command} on {Resource} completed successfully", connectionId, command, resource);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] Error executing CRUD {Command} on {Resource}", connectionId, command, resource);
            await SendErrorAsync(webSocket, requestId, $"Error executing {command}: {ex.Message}", cancellationToken);
        }
    }

    private async Task<object?> ExecuteReadAllAsync(object service, string resource)
    {
        return resource.ToLowerInvariant() switch
        {
            "buses" => await ((IBusService)service).GetAllBusesAsync(),
            "employees" => await ((IEmployeeService)service).GetAllEmployeesAsync(),
            "jobs" => await ((IEmployeeService)service).GetAllJobsAsync(),
            "maintenance" => await ((IMaintenanceService)service).GetAllMaintenanceRecordsAsync(),
            "permissions" => await ((IPermissionService)service).GetAllPermissionsAsync(),
            "roles" => await ((IRoleService)service).GetAllRolesAsync(),
            "routes" => await ((IRouteService)service).GetAllRoutesAsync(),
            "routeschedules" => await ((IRouteScheduleService)service).GetAllSchedulesAsync(),
            "tickets" => null, // Tickets use SpacetimeDB directly
            "ticketsales" => null, // TicketSales use SpacetimeDB directly
            "users" => await ((IUserService)service).GetAllUsersAsync(),
            _ => null
        };
    }

    private async Task<object?> ExecuteReadAsync(object service, string resource, uint id)
    {
        return resource.ToLowerInvariant() switch
        {
            "buses" => await ((IBusService)service).GetBusByIdAsync(id),
            "employees" => await ((IEmployeeService)service).GetEmployeeByIdAsync(id),
            "jobs" => await ((IEmployeeService)service).GetJobByIdAsync(id),
            "maintenance" => await ((IMaintenanceService)service).GetMaintenanceByIdAsync(id),
            "permissions" => await ((IPermissionService)service).GetPermissionByIdAsync(id),
            "roles" => await ((IRoleService)service).GetRoleByIdAsync(id),
            "routes" => await ((IRouteService)service).GetRouteByIdAsync(id),
            "routeschedules" => await ((IRouteScheduleService)service).GetScheduleByIdAsync(id),
            "tickets" => null, // Tickets use SpacetimeDB directly
            "ticketsales" => null, // TicketSales use SpacetimeDB directly
            "users" => await ((IUserService)service).GetUserByIdAsync(id),
            _ => null
        };
    }

    private async Task BroadcastEventsAsync(
        WebSocket webSocket,
        HashSet<string> subscriptions,
        string connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Subscribe to all resource channels
            var eventStreams = new List<IAsyncEnumerable<ApiDomainEvent>>();
            
            foreach (var handler in _resourceHandlers.Values.DistinctBy(h => h.EventChannel))
            {
                eventStreams.Add(_eventBus.SubscribeAsync(handler.EventChannel, cancellationToken));
            }

            // Merge all event streams and broadcast to client
            await foreach (var evt in MergeEventStreams(eventStreams, cancellationToken))
            {
                // Only send events for subscribed resources
                if (subscriptions.Contains(evt.Resource))
                {
                    var eventMessage = new
                    {
                        type = "event",
                        eventName = evt.EventName,
                        resource = evt.Resource,
                        timestamp = evt.OccurredAt,
                        metadata = evt.Metadata
                    };

                    if (webSocket.State == WebSocketState.Open)
                    {
                        await SendJsonAsync(webSocket, eventMessage, cancellationToken);
                        _logger.LogDebug("[{ConnectionId}] Broadcasted event {EventName} for {Resource}", connectionId, evt.EventName, evt.Resource);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[{ConnectionId}] Event broadcasting cancelled", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] Error in event broadcasting", connectionId);
        }
    }

    private async IAsyncEnumerable<ApiDomainEvent> MergeEventStreams(
        List<IAsyncEnumerable<ApiDomainEvent>> streams,
        CancellationToken cancellationToken)
    {
        var enumerators = streams.Select(s => s.GetAsyncEnumerator(cancellationToken)).ToList();
        var activeTasks = new Dictionary<Task<bool>, IAsyncEnumerator<ApiDomainEvent>>();

        try
        {
            // Initialize all enumerators
            foreach (var enumerator in enumerators)
            {
                var moveTask = enumerator.MoveNextAsync().AsTask();
                activeTasks[moveTask] = enumerator;
            }

            while (activeTasks.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(activeTasks.Keys);
                var enumerator = activeTasks[completed];
                activeTasks.Remove(completed);

                if (await completed)
                {
                    yield return enumerator.Current;

                    // Re-queue this enumerator
                    var moveTask = enumerator.MoveNextAsync().AsTask();
                    activeTasks[moveTask] = enumerator;
                }
            }
        }
        finally
        {
            foreach (var enumerator in enumerators)
            {
                await enumerator.DisposeAsync();
            }
        }
    }

    private async Task SendJsonAsync(WebSocket webSocket, object data, CancellationToken cancellationToken)
    {
        if (webSocket.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot send JSON - WebSocket state is {State}", webSocket.State);
            return;
        }

        var options = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        
        var json = JsonSerializer.Serialize(data, options);
        _logger.LogInformation(">>>> SENDING: {Json}", json);
        
        var bytes = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task SendErrorAsync(WebSocket webSocket, string? requestId, string message, CancellationToken cancellationToken)
    {
        var error = new
        {
            type = "error",
            requestId,
            message,
            timestamp = DateTimeOffset.UtcNow
        };

        await SendJsonAsync(webSocket, error, cancellationToken);
    }

    private static bool IsCrudCommand(string? command)
    {
        return command is "read_all" or "read" or "create" or "update" or "delete";
    }

    private class ResourceHandler
    {
        public Type ServiceType { get; }
        public string EventChannel { get; }

        public ResourceHandler(Type serviceType, string eventChannel)
        {
            ServiceType = serviceType;
            EventChannel = eventChannel;
        }
    }
}
