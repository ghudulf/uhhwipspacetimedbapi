using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections;
using System.Collections.Concurrent;
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
    
    // Max message size to prevent OOM
    private const int MaxIncomingMessageSize = 1 * 1024 * 1024; // 1MB

    public RealtimeController(
        IRealtimeEventBus eventBus,
        ILogger<RealtimeController> logger,
        IServiceProvider serviceProvider)
    {
        _eventBus = eventBus;
        _logger = logger;
        _serviceProvider = serviceProvider;

        // Initialize resource handlers for dynamic CRUD routing with normalized keys
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
            { "route-schedules", new ResourceHandler(typeof(IRouteScheduleService), "route-schedules") }, // Alias
            { "tickets", new ResourceHandler(typeof(ITicketService), "tickets") },
            { "ticketsales", new ResourceHandler(typeof(ITicketSalesService), "ticket-sales") },
            { "ticket-sales", new ResourceHandler(typeof(ITicketSalesService), "ticket-sales") }, // Alias
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
        var subscriptions = new ConcurrentDictionary<string, byte>(); // Thread-safe subscription tracking
        var sendLock = new SemaphoreSlim(1, 1); // Ensure only one send at a time
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);

        try
        {
            // Start event broadcasting task
            var broadcastTask = BroadcastEventsAsync(webSocket, subscriptions, sendLock, connectionId, linkedCts.Token);

            while (webSocket.State == WebSocketState.Open && !linkedCts.Token.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                int totalBytes = 0;

                do
                {
                    result = await webSocket.ReceiveAsync(buffer, linkedCts.Token);
                    ms.Write(buffer, 0, result.Count);
                    totalBytes += result.Count;
                    
                    // Check max size to prevent OOM
                    if (totalBytes > MaxIncomingMessageSize)
                    {
                        _logger.LogWarning("[{ConnectionId}] Message exceeds max size: {Size} bytes", connectionId, totalBytes);
                        await webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig, 
                            $"Message exceeds maximum size of {MaxIncomingMessageSize} bytes", 
                            CancellationToken.None);
                        return;
                    }
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
                    var sanitizedMessage = SanitizeMessageForLogging(messageJson);
                    _logger.LogDebug("[{ConnectionId}] <<<< RECEIVED: {Message}", connectionId, sanitizedMessage);

                    await HandleMessageAsync(webSocket, messageJson, subscriptions, sendLock, connectionId, linkedCts.Token);
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
                await SendJsonAsync(webSocket, errorResponse, sendLock, CancellationToken.None);
            }
        }
        finally
        {
            sendLock.Dispose();
        }
    }

    private async Task HandleMessageAsync(
        WebSocket webSocket,
        string messageJson,
        ConcurrentDictionary<string, byte> subscriptions,
        SemaphoreSlim sendLock,
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
                connectionId, messageType ?? "null", command ?? "null", requestId ?? "null");

            // Handle ping/pong
            if (messageType == "ping" || command == "ping")
            {
                _logger.LogInformation("[{ConnectionId}] Handling PING", connectionId);
                await HandlePingAsync(webSocket, requestId ?? Guid.NewGuid().ToString(), sendLock, cancellationToken);
                return;
            }

            // Handle subscription management
            if (command == "subscribe")
            {
                _logger.LogInformation("[{ConnectionId}] Handling SUBSCRIBE", connectionId);
                await HandleSubscribeAsync(webSocket, root, requestId ?? Guid.NewGuid().ToString(), subscriptions, sendLock, connectionId, cancellationToken);
                return;
            }

            if (command == "unsubscribe")
            {
                _logger.LogInformation("[{ConnectionId}] Handling UNSUBSCRIBE", connectionId);
                await HandleUnsubscribeAsync(webSocket, root, requestId ?? Guid.NewGuid().ToString(), subscriptions, sendLock, connectionId, cancellationToken);
                return;
            }

            // Handle CRUD operations
            if (IsCrudCommand(command))
            {
                _logger.LogInformation("[{ConnectionId}] Handling CRUD command: {Command}", connectionId, command);
                await HandleCrudAsync(webSocket, root, command, requestId ?? Guid.NewGuid().ToString(), sendLock, connectionId, cancellationToken);
                return;
            }

            // Unknown command
            _logger.LogWarning("[{ConnectionId}] Unknown command/type: {Command}/{Type}", connectionId, command, messageType);
            await SendErrorAsync(webSocket, requestId, $"Unknown command: {command ?? messageType}", sendLock, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] JSON parsing error. Message length: {Length}", connectionId, messageJson?.Length ?? 0);
            await SendErrorAsync(webSocket, null, "Invalid JSON format", sendLock, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] Error handling message. Message length: {Length}", connectionId, messageJson?.Length ?? 0);
            await SendErrorAsync(webSocket, null, "Error processing message", sendLock, cancellationToken);
        }
    }

    private async Task HandlePingAsync(WebSocket webSocket, string requestId, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var pong = new
        {
            type = "pong",
            requestId,
            timestamp = DateTimeOffset.UtcNow,
            serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await SendJsonAsync(webSocket, pong, sendLock, cancellationToken);
        _logger.LogInformation("Sent pong response for request {RequestId}", requestId);
    }

    private async Task HandleSubscribeAsync(
        WebSocket webSocket,
        JsonElement root,
        string requestId,
        ConcurrentDictionary<string, byte> subscriptions,
        SemaphoreSlim sendLock,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var resource = root.TryGetProperty("resource", out var resEl) ? resEl.GetString() : null;

        if (string.IsNullOrEmpty(resource))
        {
            await SendErrorAsync(webSocket, requestId, "Resource name required for subscription", sendLock, cancellationToken);
            return;
        }

        subscriptions.TryAdd(resource, 0);
        _logger.LogInformation("[{ConnectionId}] Subscribed to resource: {Resource}", connectionId, resource);

        var response = new
        {
            type = "subscribed",
            requestId,
            resource,
            timestamp = DateTimeOffset.UtcNow
        };

        await SendJsonAsync(webSocket, response, sendLock, cancellationToken);
    }

    private async Task HandleUnsubscribeAsync(
        WebSocket webSocket,
        JsonElement root,
        string requestId,
        ConcurrentDictionary<string, byte> subscriptions,
        SemaphoreSlim sendLock,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var resource = root.TryGetProperty("resource", out var resEl) ? resEl.GetString() : null;

        if (string.IsNullOrEmpty(resource))
        {
            await SendErrorAsync(webSocket, requestId, "Resource name required for unsubscription", sendLock, cancellationToken);
            return;
        }

        subscriptions.TryRemove(resource, out _);
        _logger.LogInformation("[{ConnectionId}] Unsubscribed from resource: {Resource}", connectionId, resource);

        var response = new
        {
            type = "unsubscribed",
            requestId,
            resource,
            timestamp = DateTimeOffset.UtcNow
        };

        await SendJsonAsync(webSocket, response, sendLock, cancellationToken);
    }

    private async Task HandleCrudAsync(
        WebSocket webSocket,
        JsonElement root,
        string? command,
        string requestId,
        SemaphoreSlim sendLock,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var resource = root.TryGetProperty("resource", out var resEl) ? resEl.GetString() : null;

        if (string.IsNullOrEmpty(resource))
        {
            await SendErrorAsync(webSocket, requestId, "Resource name required for CRUD operations", sendLock, cancellationToken);
            return;
        }

        if (!_resourceHandlers.TryGetValue(resource, out var handler))
        {
            await SendErrorAsync(webSocket, requestId, $"Unknown resource: {resource}", sendLock, cancellationToken);
            return;
        }

        try
        {
            // Get the service instance
            var service = _serviceProvider.GetService(handler.ServiceType);
            if (service == null)
            {
                await SendErrorAsync(webSocket, requestId, $"Service not available for resource: {resource}", sendLock, cancellationToken);
                return;
            }

            object? result = null;
            var success = true;
            int? totalCount = null;
            int? page = null;
            int? pageSize = null;
            int? totalPages = null;

            // Route to appropriate CRUD handler based on command
            switch (command)
            {
                case "read_all":
                case "next_page":
                case "prev_page":
                case "first_page":
                case "last_page":
                case "goto_page":
                    // Extract pagination parameters
                    var pageParam = root.TryGetProperty("page", out var pageEl) ? pageEl.GetInt32() : 1;
                    var pageSizeParam = root.TryGetProperty("pageSize", out var pageSizeEl) ? pageSizeEl.GetInt32() : 100;
                    
                    // Validate pagination parameters
                    if (pageParam < 1) pageParam = 1;
                    if (pageSizeParam < 1) pageSizeParam = 100;
                    if (pageSizeParam > 500) pageSizeParam = 500; // Max 500 items per page
                    
                    // Use paginatedResult.TotalCount instead of separate ExecuteCountAsync to avoid double materialization
                    var paginatedResult = await ExecuteReadAllAsync(service, resource, pageParam, pageSizeParam);
                    totalCount = paginatedResult.TotalCount;
                    
                    // Ensure totalPages is at least 1 to prevent page from being set to 0
                    totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSizeParam));
                    
                    // Determine target page based on navigation command
                    int targetPage = pageParam;
                    switch (command)
                    {
                        case "next_page":
                            targetPage = Math.Min(pageParam + 1, totalPages.Value);
                            break;
                        case "prev_page":
                            targetPage = Math.Max(pageParam - 1, 1);
                            break;
                        case "first_page":
                            targetPage = 1;
                            break;
                        case "last_page":
                            targetPage = totalPages.Value;
                            break;
                        case "goto_page":
                            targetPage = Math.Max(1, Math.Min(pageParam, totalPages.Value));
                            break;
                    }
                    
                    // If navigation changed the page, fetch again
                    if (targetPage != pageParam)
                    {
                        paginatedResult = await ExecuteReadAllAsync(service, resource, targetPage, pageSizeParam);
                    }
                    
                    result = paginatedResult.Data;
                    page = targetPage;
                    pageSize = pageSizeParam;
                    
                    _logger.LogInformation("[{ConnectionId}] {Command} {Resource} - Page {Page}/{TotalPages}, PageSize: {PageSize}, Total: {TotalCount}", 
                        connectionId, command, resource, page, totalPages, pageSize, totalCount);
                    break;

                case "read":
                    var id = root.TryGetProperty("id", out var idEl) ? idEl.GetUInt32() : (uint?)null;
                    if (!id.HasValue)
                    {
                        await SendErrorAsync(webSocket, requestId, "ID required for read operation", sendLock, cancellationToken);
                        return;
                    }
                    result = await ExecuteReadAsync(service, resource, id.Value);
                    break;

                case "create":
                case "update":
                case "delete":
                    await SendErrorAsync(webSocket, requestId, $"{command} operations not yet implemented via universal stream", sendLock, cancellationToken);
                    return;

                default:
                    await SendErrorAsync(webSocket, requestId, $"Unsupported CRUD command: {command}", sendLock, cancellationToken);
                    return;
            }

            // Send success response with pagination metadata
            var response = new
            {
                type = "result",
                requestId,
                command,
                resource,
                ok = success,
                data = result,
                pagination = totalCount.HasValue ? new
                {
                    page = page!.Value,
                    pageSize = pageSize!.Value,
                    totalCount = totalCount.Value,
                    totalPages = totalPages!.Value,
                    hasNextPage = page.Value < totalPages.Value,
                    hasPrevPage = page.Value > 1
                } : null,
                timestamp = DateTimeOffset.UtcNow
            };

            await SendJsonAsync(webSocket, response, sendLock, cancellationToken);
            _logger.LogInformation("[{ConnectionId}] CRUD {Command} on {Resource} completed successfully", connectionId, command, resource);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] Error executing CRUD {Command} on {Resource}", connectionId, command, resource);
            await SendErrorAsync(webSocket, requestId, "Failed to execute command", sendLock, cancellationToken);
        }
    }

    private async Task<(object? Data, int TotalCount)> ExecuteReadAllAsync(object service, string resource, int page, int pageSize)
    {
        object? allData = resource.ToLowerInvariant() switch
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

        if (allData == null)
        {
            return (null, 0);
        }

        // Materialize to list once to avoid multiple enumerations
        var materializedList = allData switch
        {
            IEnumerable<object> enumerable => enumerable.ToList(),
            IEnumerable enumerable => enumerable.Cast<object>().ToList(),
            _ => new List<object> { allData }
        };
        
        var totalCount = materializedList.Count;
        
        // Apply pagination
        var pagedData = materializedList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (pagedData, totalCount);
    }

    private async Task<int> ExecuteCountAsync(object service, string resource)
    {
        object? allData = resource.ToLowerInvariant() switch
        {
            "buses" => await ((IBusService)service).GetAllBusesAsync(),
            "employees" => await ((IEmployeeService)service).GetAllEmployeesAsync(),
            "jobs" => await ((IEmployeeService)service).GetAllJobsAsync(),
            "maintenance" => await ((IMaintenanceService)service).GetAllMaintenanceRecordsAsync(),
            "permissions" => await ((IPermissionService)service).GetAllPermissionsAsync(),
            "roles" => await ((IRoleService)service).GetAllRolesAsync(),
            "routes" => await ((IRouteService)service).GetAllRoutesAsync(),
            "routeschedules" => await ((IRouteScheduleService)service).GetAllSchedulesAsync(),
            "tickets" => null,
            "ticketsales" => null,
            "users" => await ((IUserService)service).GetAllUsersAsync(),
            _ => null
        };

        if (allData == null)
        {
            return 0;
        }

        // Materialize to get count
        var materializedList = allData switch
        {
            IEnumerable<object> enumerable => enumerable.ToList(),
            IEnumerable enumerable => enumerable.Cast<object>().ToList(),
            _ => new List<object> { allData }
        };
        
        return materializedList.Count;
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
        ConcurrentDictionary<string, byte> subscriptions,
        SemaphoreSlim sendLock,
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
                // Only send events for subscribed resources (thread-safe check)
                if (subscriptions.ContainsKey(evt.Resource))
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
                        await SendJsonAsync(webSocket, eventMessage, sendLock, cancellationToken);
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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerators = streams.Select(s => s.GetAsyncEnumerator(cancellationToken)).ToList();
        var activeTasks = new Dictionary<IAsyncEnumerator<ApiDomainEvent>, Task<bool>>();

        try
        {
            // Initialize all enumerators
            foreach (var enumerator in enumerators)
            {
                var moveTask = enumerator.MoveNextAsync().AsTask();
                activeTasks[enumerator] = moveTask;
            }

            while (activeTasks.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(activeTasks.Values);
                var enumerator = activeTasks.Single(kv => kv.Value == completed).Key;
                activeTasks.Remove(enumerator);

                // Check task status before accessing Result
                if (completed.IsFaulted)
                {
                    _logger.LogError(completed.Exception, "Enumerator MoveNextAsync faulted in MergeEventStreams");
                    // Dispose the faulted enumerator and continue without re-queuing
                    await enumerator.DisposeAsync();
                    continue;
                }

                if (completed.IsCanceled)
                {
                    _logger.LogDebug("Enumerator MoveNextAsync was canceled in MergeEventStreams");
                    await enumerator.DisposeAsync();
                    continue;
                }

                // Safe to access Result now that we know it's completed successfully
                if (completed.IsCompletedSuccessfully && completed.Result)
                {
                    yield return enumerator.Current;

                    // Re-queue this enumerator with a new task
                    var moveTask = enumerator.MoveNextAsync().AsTask();
                    activeTasks[enumerator] = moveTask;
                }
                else
                {
                    // Enumerator completed (no more items)
                    await enumerator.DisposeAsync();
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

    private async Task SendJsonAsync(WebSocket webSocket, object data, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        if (webSocket.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot send JSON - WebSocket state is {State}", webSocket.State);
            return;
        }

        var options = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            IncludeFields = true // CRITICAL: SpacetimeDB generated types use fields, not properties
        };
        
        var json = JsonSerializer.Serialize(data, options);
        _logger.LogInformation(">>>> SENDING: {Json}", json);
        
        var bytes = Encoding.UTF8.GetBytes(json);
        
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task SendErrorAsync(WebSocket webSocket, string? requestId, string message, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var error = new
        {
            type = "error",
            requestId,
            message,
            timestamp = DateTimeOffset.UtcNow
        };

        await SendJsonAsync(webSocket, error, sendLock, cancellationToken);
    }

    private static bool IsCrudCommand(string? command)
    {
        return command is "read_all" or "read" or "create" or "update" or "delete" or
            "next_page" or "prev_page" or "first_page" or "last_page" or "goto_page";
    }

    private static string SanitizeMessageForLogging(string messageJson)
    {
        if (string.IsNullOrEmpty(messageJson))
        {
            return messageJson ?? string.Empty;
        }

        const int maxLength = 200;
        const string redactedValue = "***REDACTED***";
        
        // List of sensitive keys to mask
        var sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password", "token", "access_token", "refresh_token", "auth", "authorization",
            "ssn", "creditCard", "credit_card", "cvv", "pin", "secret", "api_key", "apiKey",
            "private_key", "privateKey", "bearer"
        };

        try
        {
            // Try to parse as JSON and mask sensitive fields
            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;
            
            // Recursively mask sensitive values
            var sanitized = MaskSensitiveFields(root, sensitiveKeys, redactedValue);
            var sanitizedJson = JsonSerializer.Serialize(sanitized);
            
            // Apply truncation
            if (sanitizedJson.Length > maxLength)
            {
                return sanitizedJson.Substring(0, maxLength) + "... (truncated)";
            }
            return sanitizedJson;
        }
        catch
        {
            // Fallback: use heuristic string replacement if JSON parsing fails
            var sanitized = messageJson;
            foreach (var key in sensitiveKeys)
            {
                // Simple pattern: "key":"value" or "key": "value"
                var pattern = $"\"{key}\"\\s*:\\s*\"[^\"]*\"";
                sanitized = System.Text.RegularExpressions.Regex.Replace(
                    sanitized, 
                    pattern, 
                    $"\"{key}\":\"{redactedValue}\"",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            
            // Apply truncation
            if (sanitized.Length > maxLength)
            {
                return sanitized.Substring(0, maxLength) + "... (truncated)";
            }
            return sanitized;
        }
    }

    private static object MaskSensitiveFields(JsonElement element, HashSet<string> sensitiveKeys, string redactedValue)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object>();
                foreach (var prop in element.EnumerateObject())
                {
                    if (sensitiveKeys.Contains(prop.Name))
                    {
                        dict[prop.Name] = redactedValue;
                    }
                    else
                    {
                        dict[prop.Name] = MaskSensitiveFields(prop.Value, sensitiveKeys, redactedValue);
                    }
                }
                return dict;
            
            case JsonValueKind.Array:
                return element.EnumerateArray()
                    .Select(item => MaskSensitiveFields(item, sensitiveKeys, redactedValue))
                    .ToArray();
            
            case JsonValueKind.String:
                return element.GetString() ?? string.Empty;
            
            case JsonValueKind.Number:
                return element.TryGetInt64(out var longVal) ? longVal : element.GetDouble();
            
            case JsonValueKind.True:
                return true;
            
            case JsonValueKind.False:
                return false;
            
            case JsonValueKind.Null:
                return null!;
            
            default:
                return element.ToString();
        }
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