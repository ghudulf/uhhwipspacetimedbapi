using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TicketSalesApp.AdminServer.Controllers;
using TicketSalesApp.Services.Interfaces;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FlexibleApiAccess")]
public sealed class RealtimeController : BaseController
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
            // NOTE: tickets and ticket-sales are advertised but read operations return null (SpacetimeDB direct access only)
            // TODO: Implement ITicketService.GetAllTicketsAsync() and ITicketSalesService.GetAllSalesAsync() or remove from handlers
            { "tickets", new ResourceHandler(typeof(ITicketService), "tickets") },
            { "ticketsales", new ResourceHandler(typeof(ITicketSalesService), "ticket-sales") },
            { "ticket-sales", new ResourceHandler(typeof(ITicketSalesService), "ticket-sales") }, // Alias
            { "users", new ResourceHandler(typeof(IUserService), "users") },
            { "system", new ResourceHandler(typeof(IRealtimeEventBus), "system") } // For BroadcastTest
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

        // CRITICAL: Validate authentication BEFORE accepting WebSocket connection using BaseController's ValidateOAuthTokenAsync
        var validatedClaims = await ValidateOAuthTokenAsync();
        if (validatedClaims == null)
        {
            _logger.LogWarning("Unauthenticated WebSocket connection attempt from {IP} - token validation failed", 
                HttpContext.Connection.RemoteIpAddress);
            HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await HttpContext.Response.WriteAsync("Unauthorized");
            return;
        }
        
        var userId = validatedClaims.TryGetValue("sub", out var subObj) ? subObj?.ToString() : null;
        _logger.LogInformation("WebSocket connection authenticated for user: {UserId} with {ClaimCount} claims", 
            userId, validatedClaims.Count);

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync("bru.events.v1");
        var connectionId = Guid.NewGuid().ToString();
        _logger.LogInformation("[{ConnectionId}] Universal stream WebSocket connection established for user: {UserId}", connectionId, userId);

        var buffer = new byte[8192];
        var subscriptions = new ConcurrentDictionary<string, byte>(); // Thread-safe subscription tracking
        var sendLock = new SemaphoreSlim(1, 1); // Ensure only one send at a time
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);

        try
        {
            // Start event broadcasting task
            var broadcastTask = BroadcastEventsAsync(webSocket, subscriptions, sendLock, connectionId, validatedClaims, linkedCts.Token);

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
                        
                        // Cancel background tasks before closing
                        linkedCts.Cancel();
                        try
                        {
                            await broadcastTask;
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected when canceling
                        }
                        
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

                    await HandleMessageAsync(webSocket, messageJson, subscriptions, sendLock, connectionId, validatedClaims, linkedCts.Token);
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
        Dictionary<string, object>? validatedClaims,
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
                await HandleSubscribeAsync(webSocket, root, requestId ?? Guid.NewGuid().ToString(), subscriptions, sendLock, connectionId, validatedClaims, cancellationToken);
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
                await HandleCrudAsync(webSocket, root, command, requestId ?? Guid.NewGuid().ToString(), sendLock, connectionId, validatedClaims, cancellationToken);
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
        Dictionary<string, object>? validatedClaims,
        CancellationToken cancellationToken)
    {
        var resource = root.TryGetProperty("resource", out var resEl) ? resEl.GetString() : null;

        if (string.IsNullOrEmpty(resource))
        {
            await SendErrorAsync(webSocket, requestId, "Resource name required for subscription", sendLock, cancellationToken);
            return;
        }

        // Normalize resource to lowercase for consistent event channel matching
        var normalizedResource = resource.ToLowerInvariant().Replace("_", "-");
        
        // CRITICAL: Check permissions for resource subscription (same pattern as individual controllers)
        // Subscribe requires "view" permission for the resource
        if (!await HasResourcePermissionAsync(normalizedResource, "view", validatedClaims))
        {
            var userId = validatedClaims?.TryGetValue("sub", out var subObj) == true ? subObj?.ToString() : null;
            _logger.LogWarning("[{ConnectionId}] User {UserId} denied subscription to resource: {Resource} (insufficient permissions)", 
                connectionId, userId, resource);
            await SendErrorAsync(webSocket, requestId, $"Forbidden: You do not have permission to view {normalizedResource}", sendLock, cancellationToken);
            return;
        }
        
        // Resolve to canonical event channel via handler lookup
        string eventChannel = normalizedResource;
        if (_resourceHandlers.TryGetValue(normalizedResource, out var handler))
        {
            eventChannel = handler.EventChannel;
        }

        subscriptions.TryAdd(eventChannel, 0);
        _logger.LogInformation("[{ConnectionId}] Subscribed to resource: {Resource} (channel: {EventChannel})", 
            connectionId, resource, eventChannel);

        var response = new
        {
            type = "subscribed",
            requestId,
            resource = eventChannel, // Return canonical channel name
            timestamp = DateTimeOffset.UtcNow
        };

        await SendJsonAsync(webSocket, response, sendLock, cancellationToken);
    }
    
    /// <summary>
    /// Validates that the user is authenticated and has permission to access the specified resource.
    /// Uses the same permission checking logic as individual controllers (BaseController pattern).
    /// Follows exact pattern: if (!await IsAdminAsync() && !HasPermission("resource.action", claims))
    /// </summary>
    /// <param name="resource">The resource name (e.g., "buses", "users").</param>
    /// <param name="action">The action being performed (e.g., "view", "create", "edit", "delete").</param>
    /// <param name="validatedClaims">Pre-validated OAuth claims from ValidateOAuthTokenAsync.</param>
    /// <returns>True if the user has permission; false otherwise.</returns>
    private async Task<bool> HasResourcePermissionAsync(string resource, string action, Dictionary<string, object>? validatedClaims)
    {
        try
        {
            // Build permission name in SpacetimeDB format: "resource.action"
            var permissionName = $"{resource}.{action}";
            
            // CRITICAL: Use exact pattern from individual controllers
            // Pattern: if (!await IsAdminAsync() && !HasPermission("permission.name", claims))
            // Admins bypass all permission checks
            if (await IsAdminAsync())
            {
                _logger.LogDebug("User is admin, granting access to {Resource}.{Action}", resource, action);
                return true;
            }
            
            // Use BaseController's HasPermission method with validated claims
            var hasPermission = HasPermission(permissionName, validatedClaims);
            
            if (!hasPermission)
            {
                _logger.LogWarning("User does not have permission {Permission}", permissionName);
            }
            
            return hasPermission;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking resource permission for {Resource}.{Action}", resource, action);
            return false;
        }
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

        // Normalize resource name and resolve to canonical event channel (same logic as HandleSubscribeAsync)
        var normalizedResource = resource.ToLowerInvariant().Replace("_", "-");
        
        // Resolve to canonical event channel via handler lookup
        string eventChannel = normalizedResource;
        if (_resourceHandlers.TryGetValue(normalizedResource, out var handler))
        {
            eventChannel = handler.EventChannel;
        }
        
        subscriptions.TryRemove(eventChannel, out _);
        _logger.LogInformation("[{ConnectionId}] Unsubscribed from resource: {Resource} (channel: {EventChannel})", 
            connectionId, resource, eventChannel);

        var response = new
        {
            type = "unsubscribed",
            requestId,
            resource = eventChannel,
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
        Dictionary<string, object>? validatedClaims,
        CancellationToken cancellationToken)
    {
        var resource = root.TryGetProperty("resource", out var resEl) ? resEl.GetString() : null;

        if (string.IsNullOrEmpty(resource))
        {
            await SendErrorAsync(webSocket, requestId, "Resource name required for CRUD operations", sendLock, cancellationToken);
            return;
        }

        // Normalize resource name for consistent lookup
        var normalizedResource = resource.ToLowerInvariant().Replace("_", "-");

        if (!_resourceHandlers.TryGetValue(normalizedResource, out var handler))
        {
            await SendErrorAsync(webSocket, requestId, $"Unknown resource: {resource}", sendLock, cancellationToken);
            return;
        }

        try
        {
            // CRITICAL: Map CRUD command to permission action (same pattern as individual controllers)
            var permissionAction = command switch
            {
                "read_all" => "view",
                "next_page" => "view",
                "prev_page" => "view",
                "first_page" => "view",
                "last_page" => "view",
                "goto_page" => "view",
                "read" => "view",
                "create" => "create",
                "update" => "edit",
                "delete" => "delete",
                _ => "view" // Default to view for unknown commands
            };

            // Check permission before executing command (same pattern as individual controllers)
            if (!await HasResourcePermissionAsync(normalizedResource, permissionAction, validatedClaims))
            {
                var userId = validatedClaims?.TryGetValue("sub", out var subObj) == true ? subObj?.ToString() : null;
                _logger.LogWarning("[{ConnectionId}] User {UserId} denied {Command} access to resource: {Resource}", 
                    connectionId, userId, command, resource);
                await SendErrorAsync(webSocket, requestId, $"Forbidden: You do not have permission to {permissionAction} {normalizedResource}", sendLock, cancellationToken);
                return;
            }

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
                    if (!root.TryGetProperty("payload", out var createPayloadEl))
                    {
                        await SendErrorAsync(webSocket, requestId, "Payload required for create operation", sendLock, cancellationToken);
                        return;
                    }
                    result = await ExecuteCreateAsync(service, resource, createPayloadEl, validatedClaims, cancellationToken);
                    success = result != null;
                    break;

                case "update":
                    var updateId = root.TryGetProperty("id", out var updateIdEl) ? updateIdEl.GetUInt32() : (uint?)null;
                    if (!updateId.HasValue)
                    {
                        await SendErrorAsync(webSocket, requestId, "ID required for update operation", sendLock, cancellationToken);
                        return;
                    }
                    if (!root.TryGetProperty("payload", out var updatePayloadEl))
                    {
                        await SendErrorAsync(webSocket, requestId, "Payload required for update operation", sendLock, cancellationToken);
                        return;
                    }
                    result = await ExecuteUpdateAsync(service, resource, updateId.Value, updatePayloadEl, cancellationToken);
                    success = result != null;
                    break;

                case "delete":
                    var deleteId = root.TryGetProperty("id", out var deleteIdEl) ? deleteIdEl.GetUInt32() : (uint?)null;
                    if (!deleteId.HasValue)
                    {
                        await SendErrorAsync(webSocket, requestId, "ID required for delete operation", sendLock, cancellationToken);
                        return;
                    }
                    await ExecuteDeleteAsync(service, resource, deleteId.Value, cancellationToken);
                    result = new { deleted = true, id = deleteId.Value };
                    break;

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
        // Normalize resource name for consistent switch matching
        var normalizedResource = resource.ToLowerInvariant().Replace("_", "-");
        
        object? allData = normalizedResource switch
        {
            "buses" => await ((IBusService)service).GetAllBusesAsync(),
            "employees" => await ((IEmployeeService)service).GetAllEmployeesAsync(),
            "jobs" => await ((IEmployeeService)service).GetAllJobsAsync(),
            "maintenance" => await ((IMaintenanceService)service).GetAllMaintenanceRecordsAsync(),
            "permissions" => await ((IPermissionService)service).GetAllPermissionsAsync(),
            "roles" => await ((IRoleService)service).GetAllRolesAsync(),
            "routes" => await ((IRouteService)service).GetAllRoutesAsync(),
            "route-schedules" or "routeschedules" => await ((IRouteScheduleService)service).GetAllSchedulesAsync(),
            "tickets" => null, // Tickets use SpacetimeDB directly
            "ticket-sales" or "ticketsales" => null, // TicketSales use SpacetimeDB directly
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

    private async Task<object?> ExecuteReadAsync(object service, string resource, uint id)
    {
        var normalizedResource = resource.ToLowerInvariant().Replace("_", "-");
        
        return normalizedResource switch
        {
            "buses" => await ((IBusService)service).GetBusByIdAsync(id),
            "employees" => await ((IEmployeeService)service).GetEmployeeByIdAsync(id),
            "jobs" => await ((IEmployeeService)service).GetJobByIdAsync(id),
            "maintenance" => await ((IMaintenanceService)service).GetMaintenanceByIdAsync(id),
            "permissions" => await ((IPermissionService)service).GetPermissionByIdAsync(id),
            "roles" => await ((IRoleService)service).GetRoleByIdAsync(id),
            "routes" => await ((IRouteService)service).GetRouteByIdAsync(id),
            "route-schedules" or "routeschedules" => await ((IRouteScheduleService)service).GetScheduleByIdAsync(id),
            "tickets" => null,
            "ticket-sales" or "ticketsales" => null,
            "users" => await ((IUserService)service).GetUserByIdAsync(id),
            _ => null
        };
    }

    private async Task<object?> ExecuteCreateAsync(object service, string resource, JsonElement payload, Dictionary<string, object>? validatedClaims, CancellationToken cancellationToken)
    {
        var normalizedResource = resource.ToLowerInvariant().Replace("_", "-");
        
        return normalizedResource switch
        {
            "buses" => await CreateBusAsync((IBusService)service, payload, validatedClaims),
            "employees" => await CreateEmployeeAsync((IEmployeeService)service, payload, validatedClaims),
            "jobs" => await CreateJobAsync((IEmployeeService)service, payload, validatedClaims),
            "maintenance" => await CreateMaintenanceAsync((IMaintenanceService)service, payload, validatedClaims),
            "permissions" => await CreatePermissionAsync((IPermissionService)service, payload, validatedClaims),
            "roles" => await CreateRoleAsync((IRoleService)service, payload, validatedClaims),
            "routes" => await CreateRouteAsync((IRouteService)service, payload, validatedClaims),
            "route-schedules" or "routeschedules" => await CreateRouteScheduleAsync((IRouteScheduleService)service, payload, validatedClaims),
            "tickets" => await CreateTicketAsync((ITicketService)service, payload, validatedClaims),
            "users" => await CreateUserAsync((IUserService)service, payload, validatedClaims),
            _ => throw new NotSupportedException($"Create not supported for resource: {resource}")
        };
    }

    private async Task<object?> ExecuteUpdateAsync(object service, string resource, uint id, JsonElement payload, CancellationToken cancellationToken)
    {
        var normalizedResource = resource.ToLowerInvariant().Replace("_", "-");
        
        bool success = normalizedResource switch
        {
            "buses" => await UpdateBusAsync((IBusService)service, id, payload),
            "employees" => await UpdateEmployeeAsync((IEmployeeService)service, id, payload),
            "jobs" => await UpdateJobAsync((IEmployeeService)service, id, payload),
            "maintenance" => await UpdateMaintenanceAsync((IMaintenanceService)service, id, payload),
            "permissions" => await UpdatePermissionAsync((IPermissionService)service, id, payload),
            "roles" => await UpdateRoleAsync((IRoleService)service, id, payload),
            "routes" => await UpdateRouteAsync((IRouteService)service, id, payload),
            "route-schedules" or "routeschedules" => await UpdateRouteScheduleAsync((IRouteScheduleService)service, id, payload),
            "tickets" => await UpdateTicketAsync((ITicketService)service, id, payload),
            "users" => await UpdateUserAsync((IUserService)service, id, payload),
            _ => throw new NotSupportedException($"Update not supported for resource: {resource}")
        };

        return success ? await ExecuteReadAsync(service, resource, id) : null;
    }

    private async Task ExecuteDeleteAsync(object service, string resource, uint id, CancellationToken cancellationToken)
    {
        var normalizedResource = resource.ToLowerInvariant().Replace("_", "-");
        
        bool success = normalizedResource switch
        {
            "buses" => await ((IBusService)service).DeleteBusAsync(id),
            "employees" => await ((IEmployeeService)service).DeleteEmployeeAsync(id),
            "jobs" => await ((IEmployeeService)service).DeleteJobAsync(id),
            "maintenance" => await ((IMaintenanceService)service).DeleteMaintenanceAsync(id),
            "permissions" => await ((IPermissionService)service).DeletePermissionAsync(id),
            "roles" => await ((IRoleService)service).DeleteRoleAsync(id),
            "routes" => await ((IRouteService)service).DeleteRouteAsync(id),
            "route-schedules" or "routeschedules" => await ((IRouteScheduleService)service).DeleteScheduleAsync(id),
            "tickets" => await ((ITicketService)service).DeleteTicketAsync(id),
            "users" => await ((IUserService)service).DeleteUserAsync(id),
            _ => throw new NotSupportedException($"Delete not supported for resource: {resource}")
        };

        if (!success)
        {
            throw new InvalidOperationException($"Failed to delete {resource} with id {id}");
        }
    }

    private SpacetimeDB.Identity ExtractIdentityFromClaims(Dictionary<string, object>? validatedClaims)
    {
        if (validatedClaims == null)
            return new SpacetimeDB.Identity();

        if (validatedClaims.TryGetValue("identity", out var identityObj) && identityObj is string identityStr && !string.IsNullOrEmpty(identityStr))
        {
            try
            {
                byte[] bytes = Convert.FromHexString(identityStr);
                return new SpacetimeDB.Identity(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Identity hex string '{IdentityHex}' from 'identity' claim", identityStr);
            }
        }

        if (validatedClaims.TryGetValue("sub", out var subObj) && subObj is string subStr && !string.IsNullOrEmpty(subStr))
        {
            try
            {
                byte[] bytes = Convert.FromHexString(subStr);
                return new SpacetimeDB.Identity(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Identity hex string '{IdentityHex}' from 'sub' claim", subStr);
            }
        }

        return new SpacetimeDB.Identity();
    }

    private async Task<Bus?> CreateBusAsync(IBusService service, JsonElement payload, Dictionary<string, object>? validatedClaims)
    {
        var model = payload.TryGetProperty("model", out var modelEl) ? modelEl.GetString() : null;
        var registrationNumber = payload.TryGetProperty("registrationNumber", out var regEl) ? regEl.GetString() : null;
        
        if (string.IsNullOrEmpty(model))
            throw new ArgumentException("Model is required");
            
        return await service.CreateBusAsync(model, registrationNumber);
    }

    private async Task<bool> UpdateBusAsync(IBusService service, uint id, JsonElement payload)
    {
        var model = payload.TryGetProperty("model", out var modelEl) ? modelEl.GetString() : null;
        var registrationNumber = payload.TryGetProperty("registrationNumber", out var regEl) ? regEl.GetString() : null;
        
        return await service.UpdateBusAsync(id, model, registrationNumber);
    }

    private async Task<UserProfile?> CreateUserAsync(IUserService service, JsonElement payload, Dictionary<string, object>? validatedClaims)
    {
        var login = payload.TryGetProperty("login", out var loginEl) ? loginEl.GetString() : null;
        var password = payload.TryGetProperty("password", out var passEl) ? passEl.GetString() : null;
        var role = payload.TryGetProperty("role", out var roleEl) ? roleEl.GetInt32() : 0;
        var email = payload.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
        var phoneNumber = payload.TryGetProperty("phoneNumber", out var phoneEl) ? phoneEl.GetString() : null;
        
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            throw new ArgumentException("Login and password are required");
            
        return await service.CreateUserAsync(login, password, role, email, phoneNumber);
    }

    private async Task<bool> UpdateUserAsync(IUserService service, uint id, JsonElement payload)
    {
        var login = payload.TryGetProperty("login", out var loginEl) ? loginEl.GetString() : null;
        var password = payload.TryGetProperty("password", out var passEl) ? passEl.GetString() : null;
        var role = payload.TryGetProperty("role", out var roleEl) ? (int?)roleEl.GetInt32() : null;
        var email = payload.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
        var phoneNumber = payload.TryGetProperty("phoneNumber", out var phoneEl) ? phoneEl.GetString() : null;
        var isActive = payload.TryGetProperty("isActive", out var activeEl) ? (bool?)activeEl.GetBoolean() : null;
        
        return await service.UpdateUserAsync(id, login, password, role, email, phoneNumber, isActive);
    }

    private async Task<object?> CreateRouteScheduleAsync(IRouteScheduleService service, JsonElement payload, Dictionary<string, object>? validatedClaims)
    {
        var routeId = payload.TryGetProperty("routeId", out var routeEl) ? (uint?)routeEl.GetUInt32() : null;
        var startPoint = payload.TryGetProperty("startPoint", out var startEl) ? startEl.GetString() : null;
        var endPoint = payload.TryGetProperty("endPoint", out var endEl) ? endEl.GetString() : null;
        var routeStops = payload.TryGetProperty("routeStops", out var stopsEl) && stopsEl.ValueKind == JsonValueKind.Array
            ? stopsEl.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).Cast<string>().ToList()
            : null;
        var departureTime = payload.TryGetProperty("departureTime", out var depEl) ? (ulong?)depEl.GetUInt64() : null;
        var arrivalTime = payload.TryGetProperty("arrivalTime", out var arrEl) ? (ulong?)arrEl.GetUInt64() : null;
        var price = payload.TryGetProperty("price", out var priceEl) ? (double?)priceEl.GetDouble() : null;
        var availableSeats = payload.TryGetProperty("availableSeats", out var seatsEl) ? (uint?)seatsEl.GetUInt32() : null;
        
        var scheduleId = await service.CreateScheduleAsync(
            routeId: routeId,
            startPoint: startPoint,
            endPoint: endPoint,
            routeStops: routeStops,
            departureTime: departureTime,
            arrivalTime: arrivalTime,
            price: price,
            availableSeats: availableSeats
        );
        return scheduleId.HasValue ? await service.GetScheduleByIdAsync(scheduleId.Value) : null;
    }

    private async Task<bool> UpdateRouteScheduleAsync(IRouteScheduleService service, uint id, JsonElement payload)
    {
        var routeId = payload.TryGetProperty("routeId", out var routeEl) ? (uint?)routeEl.GetUInt32() : null;
        var startPoint = payload.TryGetProperty("startPoint", out var startEl) ? startEl.GetString() : null;
        var endPoint = payload.TryGetProperty("endPoint", out var endEl) ? endEl.GetString() : null;
        var routeStops = payload.TryGetProperty("routeStops", out var stopsEl) && stopsEl.ValueKind == JsonValueKind.Array
            ? stopsEl.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).Cast<string>().ToList()
            : null;
        var departureTime = payload.TryGetProperty("departureTime", out var depEl) ? (ulong?)depEl.GetUInt64() : null;
        var arrivalTime = payload.TryGetProperty("arrivalTime", out var arrEl) ? (ulong?)arrEl.GetUInt64() : null;
        var price = payload.TryGetProperty("price", out var priceEl) ? (double?)priceEl.GetDouble() : null;
        var availableSeats = payload.TryGetProperty("availableSeats", out var seatsEl) ? (uint?)seatsEl.GetUInt32() : null;
        
        return await service.UpdateScheduleAsync(
            scheduleId: id,
            routeId: routeId,
            startPoint: startPoint,
            endPoint: endPoint,
            routeStops: routeStops,
            departureTime: departureTime,
            arrivalTime: arrivalTime,
            price: price,
            availableSeats: availableSeats
        );
    }

    private async Task<Employee?> CreateEmployeeAsync(IEmployeeService service, JsonElement payload, Dictionary<string, object>? validatedClaims)
    {
        var name = payload.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var surname = payload.TryGetProperty("surname", out var surnameEl) ? surnameEl.GetString() : null;
        var patronym = payload.TryGetProperty("patronym", out var patronymEl) ? patronymEl.GetString() : null;
        var jobId = payload.TryGetProperty("jobId", out var jobEl) ? jobEl.GetUInt32() : 0u;
        
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(surname))
            throw new ArgumentException("Name and surname are required");
            
        return await service.CreateEmployeeAsync(name, surname, patronym ?? "", jobId);
    }

    private async Task<bool> UpdateEmployeeAsync(IEmployeeService service, uint id, JsonElement payload)
    {
        var name = payload.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var surname = payload.TryGetProperty("surname", out var surnameEl) ? surnameEl.GetString() : null;
        var patronym = payload.TryGetProperty("patronym", out var patronymEl) ? patronymEl.GetString() : null;
        var jobId = payload.TryGetProperty("jobId", out var jobEl) ? (uint?)jobEl.GetUInt32() : null;
        
        return await service.UpdateEmployeeAsync(id, name, surname, patronym, jobId);
    }

    private async Task<object?> CreateJobAsync(IEmployeeService service, JsonElement payload, Dictionary<string, object>? validatedClaims)
    {
        var jobTitle = payload.TryGetProperty("jobTitle", out var titleEl) ? titleEl.GetString() : null;
        var internship = payload.TryGetProperty("internship", out var internEl) ? internEl.GetString() : null;
        
        if (string.IsNullOrEmpty(jobTitle))
            throw new ArgumentException("Job title is required");
            
        var success = await service.CreateJobAsync(jobTitle, internship ?? "");
        return success ? new { jobTitle, internship } : null;
    }

    private async Task<bool> UpdateJobAsync(IEmployeeService service, uint id, JsonElement payload)
    {
        var jobTitle = payload.TryGetProperty("jobTitle", out var titleEl) ? titleEl.GetString() : null;
        var internship = payload.TryGetProperty("internship", out var internEl) ? internEl.GetString() : null;
        
        return await service.UpdateJobAsync(id, jobTitle, internship);
    }

    private async Task<object?> CreateMaintenanceAsync(IMaintenanceService service, JsonElement payload, Dictionary<string, object>? validatedClaims)
    {
        var busId = payload.TryGetProperty("busId", out var busEl) ? busEl.GetUInt32() : 0u;
        var lastServiceDate = payload.TryGetProperty("lastServiceDate", out var lastEl) ? lastEl.GetUInt64() : 0ul;
        var serviceEngineer = payload.TryGetProperty("serviceEngineer", out var engEl) ? engEl.GetString() : null;
        var foundIssues = payload.TryGetProperty("foundIssues", out var issuesEl) ? issuesEl.GetString() : null;
        var nextServiceDate = payload.TryGetProperty("nextServiceDate", out var nextEl) ? nextEl.GetUInt64() : 0ul;
        var roadworthiness = payload.TryGetProperty("roadworthiness", out var roadEl) ? roadEl.GetString() : null;
        var maintenanceType = payload.TryGetProperty("maintenanceType", out var typeEl) ? typeEl.GetString() : null;
        
        if (busId == 0 || string.IsNullOrEmpty(serviceEngineer) || string.IsNullOrEmpty(foundIssues))
            throw new ArgumentException("BusId, serviceEngineer, and foundIssues are required");
            
        var success = await service.CreateMaintenanceAsync(busId, lastServiceDate, serviceEngineer, foundIssues, nextServiceDate, roadworthiness ?? "", maintenanceType ?? "");
        return success ? new { busId, lastServiceDate, serviceEngineer, foundIssues } : null;
    }

    private async Task<bool> UpdateMaintenanceAsync(IMaintenanceService service, uint id, JsonElement payload)
    {
        var busId = payload.TryGetProperty("busId", out var busEl) ? (uint?)busEl.GetUInt32() : null;
        var lastServiceDate = payload.TryGetProperty("lastServiceDate", out var lastEl) ? (ulong?)lastEl.GetUInt64() : null;
        var serviceEngineer = payload.TryGetProperty("serviceEngineer", out var engEl) ? engEl.GetString() : null;
        var foundIssues = payload.TryGetProperty("foundIssues", out var issuesEl) ? issuesEl.GetString() : null;
        var nextServiceDate = payload.TryGetProperty("nextServiceDate", out var nextEl) ? (ulong?)nextEl.GetUInt64() : null;
        var roadworthiness = payload.TryGetProperty("roadworthiness", out var roadEl) ? roadEl.GetString() : null;
        var maintenanceType = payload.TryGetProperty("maintenanceType", out var typeEl) ? typeEl.GetString() : null;
        var mileage = payload.TryGetProperty("mileage", out var mileageEl) ? mileageEl.GetString() : null;
        
        return await service.UpdateMaintenanceAsync(id, busId, lastServiceDate, serviceEngineer, foundIssues, nextServiceDate, roadworthiness, maintenanceType, mileage);
    }

    private async Task<Permission?> CreatePermissionAsync(IPermissionService service, JsonElement payload, Dictionary<string, object>? validatedClaims)
    {
        var name = payload.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var description = payload.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
        var category = payload.TryGetProperty("category", out var catEl) ? catEl.GetString() : null;
        
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(description) || string.IsNullOrEmpty(category))
            throw new ArgumentException("Name, description, and category are required");
            
        return await service.CreatePermissionAsync(name, description, category);
    }

    private async Task<bool> UpdatePermissionAsync(IPermissionService service, uint id, JsonElement payload)
    {
        var name = payload.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var description = payload.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
        var category = payload.TryGetProperty("category", out var catEl) ? catEl.GetString() : null;
        var isActive = payload.TryGetProperty("isActive", out var activeEl) ? (bool?)activeEl.GetBoolean() : null;
        
        return await service.UpdatePermissionAsync(id, name, description, category, isActive);
    }

    private async Task<Role?> CreateRoleAsync(IRoleService service, JsonElement payload, Dictionary<string, object>? validatedClaims)
    {
        var name = payload.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var description = payload.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
        var legacyRoleId = payload.TryGetProperty("legacyRoleId", out var legacyEl) ? legacyEl.GetInt32() : 0;
        var priority = payload.TryGetProperty("priority", out var prioEl) ? prioEl.GetUInt32() : 0u;
        var permissionIds = payload.TryGetProperty("permissionIds", out var permEl) && permEl.ValueKind == JsonValueKind.Array
            ? permEl.EnumerateArray().Select(e => e.GetUInt32()).ToList()
            : null;
        
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(description))
            throw new ArgumentException("Name and description are required");
            
        return await service.CreateRoleAsync(name, description, legacyRoleId, priority, permissionIds);
    }

    private async Task<bool> UpdateRoleAsync(IRoleService service, uint id, JsonElement payload)
    {
        var name = payload.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var description = payload.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
        var priority = payload.TryGetProperty("priority", out var prioEl) ? (uint?)prioEl.GetUInt32() : null;
        var permissionIds = payload.TryGetProperty("permissionIds", out var permEl) && permEl.ValueKind == JsonValueKind.Array
            ? permEl.EnumerateArray().Select(e => e.GetUInt32()).ToList()
            : null;
        
        return await service.UpdateRoleAsync(id, name, description, priority, permissionIds);
    }

    private async Task<object?> CreateRouteAsync(IRouteService service, JsonElement payload, Dictionary<string, object>? validatedClaims)
    {
        var startPoint = payload.TryGetProperty("startPoint", out var startEl) ? startEl.GetString() : null;
        var endPoint = payload.TryGetProperty("endPoint", out var endEl) ? endEl.GetString() : null;
        var driverId = payload.TryGetProperty("driverId", out var driverEl) ? driverEl.GetUInt32() : 0u;
        var busId = payload.TryGetProperty("busId", out var busEl) ? busEl.GetUInt32() : 0u;
        var travelTime = payload.TryGetProperty("travelTime", out var timeEl) ? timeEl.GetString() : null;
        var isActive = payload.TryGetProperty("isActive", out var activeEl) ? activeEl.GetBoolean() : true;
        
        if (string.IsNullOrEmpty(startPoint) || string.IsNullOrEmpty(endPoint))
            throw new ArgumentException("StartPoint and endPoint are required");
            
        var success = await service.CreateRouteAsync(startPoint, endPoint, driverId, busId, travelTime ?? "", isActive);
        return success ? new { startPoint, endPoint, driverId, busId, travelTime, isActive } : null;
    }

    private async Task<bool> UpdateRouteAsync(IRouteService service, uint id, JsonElement payload)
    {
        var startPoint = payload.TryGetProperty("startPoint", out var startEl) ? startEl.GetString() : null;
        var endPoint = payload.TryGetProperty("endPoint", out var endEl) ? endEl.GetString() : null;
        var driverId = payload.TryGetProperty("driverId", out var driverEl) ? (uint?)driverEl.GetUInt32() : null;
        var busId = payload.TryGetProperty("busId", out var busEl) ? (uint?)busEl.GetUInt32() : null;
        var travelTime = payload.TryGetProperty("travelTime", out var timeEl) ? timeEl.GetString() : null;
        var isActive = payload.TryGetProperty("isActive", out var activeEl) ? (bool?)activeEl.GetBoolean() : null;
        
        return await service.UpdateRouteAsync(id, startPoint, endPoint, driverId, busId, travelTime, isActive);
    }

    private async Task<object?> CreateTicketAsync(ITicketService service, JsonElement payload, Dictionary<string, object>? validatedClaims)
    {
        var routeId = payload.TryGetProperty("routeId", out var routeEl) ? routeEl.GetUInt32() : 0u;
        var seatNumber = payload.TryGetProperty("seatNumber", out var seatEl) ? seatEl.GetUInt32() : 0u;
        var ticketPrice = payload.TryGetProperty("ticketPrice", out var priceEl) ? priceEl.GetDouble() : 0.0;
        var paymentMethod = payload.TryGetProperty("paymentMethod", out var payEl) ? payEl.GetString() : null;
        var purchaseTime = payload.TryGetProperty("purchaseTime", out var timeEl) ? timeEl.GetUInt64() : (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        if (routeId == 0 || string.IsNullOrEmpty(paymentMethod))
            throw new ArgumentException("RouteId and paymentMethod are required");
        
        var userId = ExtractIdentityFromClaims(validatedClaims);
        var success = await service.CreateTicketAsync(routeId, seatNumber, ticketPrice, paymentMethod, purchaseTime, userId);
        return success ? new { routeId, seatNumber, ticketPrice, paymentMethod, purchaseTime } : null;
    }

    private async Task<bool> UpdateTicketAsync(ITicketService service, uint id, JsonElement payload)
    {
        var routeId = payload.TryGetProperty("routeId", out var routeEl) ? (uint?)routeEl.GetUInt32() : null;
        var ticketPrice = payload.TryGetProperty("ticketPrice", out var priceEl) ? (double?)priceEl.GetDouble() : null;
        var seatNumber = payload.TryGetProperty("seatNumber", out var seatEl) ? (uint?)seatEl.GetUInt32() : null;
        var paymentMethod = payload.TryGetProperty("paymentMethod", out var payEl) ? payEl.GetString() : null;
        var isActive = payload.TryGetProperty("isActive", out var activeEl) ? (bool?)activeEl.GetBoolean() : null;
        var updatedAt = payload.TryGetProperty("updatedAt", out var timeEl) ? (ulong?)timeEl.GetUInt64() : null;
        var updatedBy = payload.TryGetProperty("updatedBy", out var byEl) ? byEl.GetString() : null;
        
        return await service.UpdateTicketAsync(id, routeId, ticketPrice, seatNumber, paymentMethod, isActive, updatedAt, updatedBy);
    }


    private async Task BroadcastEventsAsync(
        WebSocket webSocket,
        ConcurrentDictionary<string, byte> subscriptions,
        SemaphoreSlim sendLock,
        string connectionId,
        Dictionary<string, object>? validatedClaims,
        CancellationToken cancellationToken)
    {
        // Track active channel subscriptions: EventChannel -> (IAsyncEnumerable, refCount)
        var activeChannelSubscriptions = new ConcurrentDictionary<string, (IAsyncEnumerable<ApiDomainEvent> Stream, int RefCount)>();
        var subscriptionLock = new SemaphoreSlim(1, 1);
        
        try
        {
            // Monitor subscription changes and dynamically manage channel subscriptions
            var previousSubscriptions = new HashSet<string>();
            
            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                // Get current subscriptions snapshot
                var currentSubscriptions = new HashSet<string>(subscriptions.Keys);
                
                // Determine which channels need to be subscribed/unsubscribed
                await subscriptionLock.WaitAsync(cancellationToken);
                try
                {
                    // Find new subscriptions
                    var newSubscriptions = currentSubscriptions.Except(previousSubscriptions).ToList();
                    foreach (var resource in newSubscriptions)
                    {
                        // Find the event channel for this resource
                        if (_resourceHandlers.TryGetValue(resource, out var handler))
                        {
                            var channel = handler.EventChannel;
                            
                            // Subscribe to channel if not already subscribed
                            if (!activeChannelSubscriptions.ContainsKey(channel))
                            {
                                var stream = _eventBus.SubscribeAsync(channel, cancellationToken);
                                activeChannelSubscriptions[channel] = (stream, 1);
                                _logger.LogDebug("[{ConnectionId}] Subscribed to channel {Channel} for resource {Resource}", 
                                    connectionId, channel, resource);
                            }
                            else
                            {
                                // Increment ref count
                                var (stream, refCount) = activeChannelSubscriptions[channel];
                                activeChannelSubscriptions[channel] = (stream, refCount + 1);
                            }
                        }
                    }
                    
                    // Find removed subscriptions
                    var removedSubscriptions = previousSubscriptions.Except(currentSubscriptions).ToList();
                    foreach (var resource in removedSubscriptions)
                    {
                        // Find the event channel for this resource
                        if (_resourceHandlers.TryGetValue(resource, out var handler))
                        {
                            var channel = handler.EventChannel;
                            
                            // Decrement ref count and unsubscribe if no more resources use this channel
                            if (activeChannelSubscriptions.TryGetValue(channel, out var entry))
                            {
                                var (stream, refCount) = entry;
                                if (refCount <= 1)
                                {
                                    activeChannelSubscriptions.TryRemove(channel, out _);
                                    _logger.LogDebug("[{ConnectionId}] Unsubscribed from channel {Channel} (no more resources)", 
                                        connectionId, channel);
                                }
                                else
                                {
                                    activeChannelSubscriptions[channel] = (stream, refCount - 1);
                                }
                            }
                        }
                    }
                    
                    previousSubscriptions = currentSubscriptions;
                }
                finally
                {
                    subscriptionLock.Release();
                }
                
                // Read events from active channels
                if (activeChannelSubscriptions.Any())
                {
                    var streams = activeChannelSubscriptions.Values.Select(v => v.Stream).ToList();
                    
                    // Use a short timeout to periodically check for subscription changes
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(1));
                    
                    try
                    {
                        await foreach (var evt in MergeEventStreams(streams, timeoutCts.Token))
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
                                    _logger.LogDebug("[{ConnectionId}] Broadcasted event {EventName} for {Resource}", 
                                        connectionId, evt.EventName, evt.Resource);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        // Timeout reached, loop will continue to check for subscription changes
                    }
                }
                else
                {
                    // No active subscriptions, wait before checking again
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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
        finally
        {
            subscriptionLock.Dispose();
        }
    }

    private async IAsyncEnumerable<ApiDomainEvent> MergeEventStreams(
        List<IAsyncEnumerable<ApiDomainEvent>> streams,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerators = streams.Select(s => s.GetAsyncEnumerator(cancellationToken)).ToList();
        var activeTasks = new Dictionary<IAsyncEnumerator<ApiDomainEvent>, Task<bool>>();
        var disposedEnumerators = new HashSet<IAsyncEnumerator<ApiDomainEvent>>();

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
                    await enumerator.DisposeAsync();
                    disposedEnumerators.Add(enumerator);
                    continue;
                }

                if (completed.IsCanceled)
                {
                    _logger.LogDebug("Enumerator MoveNextAsync was canceled in MergeEventStreams");
                    await enumerator.DisposeAsync();
                    disposedEnumerators.Add(enumerator);
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
                    disposedEnumerators.Add(enumerator);
                }
            }
        }
        finally
        {
            foreach (var enumerator in enumerators)
            {
                if (!disposedEnumerators.Contains(enumerator))
                {
                    await enumerator.DisposeAsync();
                }
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
        _logger.LogDebug(">>>> SENDING: {Json}", json);
        
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