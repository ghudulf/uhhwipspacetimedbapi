using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Utilities;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

public sealed record RealtimeCrudRequest(
    string Command,
    string? RequestId,
    uint? Id,
    JsonElement? Payload)
{
    public int? Page { get; init; }
    public int? PageSize { get; init; }
};

public static class WebSocketEventStreamWriter
{
    private const int MaxIncomingMessageSize = 1024 * 1024; // 1 MB

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true // CRITICAL: SpacetimeDB generated types use fields, not properties
    };

    /// <summary>
    /// Manages a WebSocket session that streams domain events to the client and processes incoming CRUD-style requests.
    /// </summary>
    /// <remarks>
    /// Upgrades the given HTTP context to a WebSocket session (expects subprotocol "bru.events.v1"), sends each <paramref name="events"/> item as an event message, invokes <paramref name="requestHandler"/> for incoming JSON requests, and sends structured result messages. The session ends when the socket closes, cancellation is requested, or a transport error occurs; the provided <paramref name="cancellationToken"/> is linked into the session lifetime.
    /// </remarks>
    /// <param name="context">HTTP context used to accept the WebSocket upgrade and perform the session handshake.</param>
    /// <param name="events">Asynchronous sequence of domain events to stream to the connected client.</param>
    /// <param name="requestHandler">Handler invoked for each received RealtimeCrudRequest; should process the request and return a serializable result object.</param>
    /// <param name="logger">Logger used to record errors and unexpected conditions during the session.</param>
    /// <param name="cancellationToken">Token that can be used to cancel the entire session from the caller side.</param>
    public static async Task StreamCrudSessionAsync(
        HttpContext context,
        IAsyncEnumerable<ApiDomainEvent> events,
        Func<RealtimeCrudRequest, CancellationToken, Task<object>> requestHandler,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket upgrade request required.", cancellationToken);
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync("bru.events.v1");
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var sendLock = new SemaphoreSlim(1, 1);

        var eventPumpTask = Task.Run(async () =>
        {
            await foreach (var evt in events.WithCancellation(linkedCts.Token))
            {
                if (webSocket.State != WebSocketState.Open)
                {
                    break;
                }

                await SendJsonAsync(webSocket, new
                {
                    type = "event",
                    eventName = evt.EventName,
                    resource = evt.Resource,
                    data = evt
                }, sendLock, logger, linkedCts.Token);
            }
        }, linkedCts.Token);

        try
        {
            while (webSocket.State == WebSocketState.Open && !linkedCts.Token.IsCancellationRequested)
            {
                var request = await ReceiveRequestAsync(webSocket, logger, linkedCts.Token);
                if (request is null)
                {
                    break;
                }

                // Prepare response payload (isolate business logic from transport)
                object payload;
                try
                {
                    var sanitizedCommand = LogSanitizer.SanitizeLogField(request.Command ?? "", 100);
                    var sanitizedRequestId = LogSanitizer.SanitizeLogField(request.RequestId ?? "", 50);
                    
                    logger.LogInformation("[WebSocketEventStreamWriter] Processing request - Command: {Command}, RequestId: {RequestId}", 
                        sanitizedCommand, sanitizedRequestId);
                    var data = await requestHandler(request, linkedCts.Token);
                    logger.LogInformation("[WebSocketEventStreamWriter] Request handler completed successfully");
                    
                    payload = new
                    {
                        type = "result",
                        requestId = request.RequestId,
                        ok = true,
                        data
                    };
                }
                catch (Exception ex)
                {
                    var sanitizedCommand = LogSanitizer.SanitizeLogField(request.Command ?? "", 100);
                    logger.LogError(ex, "WebSocket CRUD command failed: {Command}", sanitizedCommand);

                    // Determine client-facing error message (sanitize sensitive information)
                    var clientErrorMessage = ex switch
                    {
                        UnauthorizedAccessException => "Unauthorized",
                        InvalidOperationException => "Invalid operation",
                        ArgumentException => "Bad request",
                        JsonException => "Bad request",
                        _ => "Internal server error"
                    };

                    payload = new
                    {
                        type = "result",
                        requestId = request.RequestId,
                        ok = false,
                        error = clientErrorMessage
                    };
                }

                // Send response (transport errors will propagate and close session)
                await SendJsonAsync(webSocket, payload, sendLock, logger, linkedCts.Token);
            }
        }
        finally
        {
            linkedCts.Cancel();

            try
            {
                await eventPumpTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                // Log unexpected exceptions from event pump (e.g., from WithCancellation or SendJsonAsync)
                logger.LogError(ex, "Event pump task faulted with unexpected exception: {Message}", ex.Message);
            }

            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session completed", CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Reads one text message from the provided WebSocket, parses the message as JSON, and returns a deserialized RealtimeCrudRequest.
    /// </summary>
    /// <param name="webSocket">The active WebSocket to receive the message from.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    /// <param name="cancellationToken">Token to observe for cancellation of the receive operation.</param>
    /// <returns>
    /// A <see cref="RealtimeCrudRequest"/> parsed from the received JSON text message, or <c>null</c> if the connection was closed, the message was rejected (non-text, too large, or invalid JSON), or the close handshake completed.
    /// </returns>
    /// <remarks>
    /// On protocol or payload errors this method will initiate a close using an appropriate WebSocket close status (for example InvalidMessageType, MessageTooBig, or InvalidPayloadData).
    /// </remarks>
    private static async Task<RealtimeCrudRequest?> ReceiveRequestAsync(WebSocket webSocket, ILogger logger, CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024];
        using var messageBuffer = new MemoryStream();

        while (true)
        {
            var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Complete the close handshake
                if (webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseOutputAsync(
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription ?? "Client requested close",
                        CancellationToken.None);
                }
                return null;
            }

            // Reject non-text frames
            if (result.MessageType != WebSocketMessageType.Text)
            {
                logger.LogWarning("[WebSocketEventStreamWriter] Rejecting non-text frame: {MessageType}", result.MessageType);
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.InvalidMessageType,
                    "Only text frames are supported",
                    CancellationToken.None);
                return null;
            }

            if (result.Count > 0)
            {
                messageBuffer.Write(buffer, 0, result.Count);

                if (messageBuffer.Length > MaxIncomingMessageSize)
                {
                    logger.LogWarning("[WebSocketEventStreamWriter] Message too large: {Size} bytes (max: {MaxSize})", 
                        messageBuffer.Length, MaxIncomingMessageSize);
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        $"Message exceeds maximum size of {MaxIncomingMessageSize} bytes",
                        CancellationToken.None);
                    return null;
                }
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
        logger.LogDebug("[WebSocketEventStreamWriter] <<<< RECEIVED RAW: {Json}", json);

        // Wrap JSON deserialization in try-catch to handle invalid JSON
        try
        {
            var request = JsonSerializer.Deserialize<RealtimeCrudRequest>(json, JsonOptions);
            
            var sanitizedCommand = LogSanitizer.SanitizeLogField(request?.Command ?? "", 100);
            var sanitizedRequestId = LogSanitizer.SanitizeLogField(request?.RequestId ?? "", 50);
            var sanitizedId = request?.Id?.ToString() ?? "null";
            
            logger.LogInformation("[WebSocketEventStreamWriter] Parsed request - Command: {Command}, RequestId: {RequestId}, Id: {Id}",
                sanitizedCommand, sanitizedRequestId, sanitizedId);

            // Validate the deserialized request
            if (request == null || string.IsNullOrWhiteSpace(request.Command))
            {
                logger.LogWarning("[WebSocketEventStreamWriter] Malformed envelope - null request or empty Command field");
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.InvalidPayloadData,
                    "Malformed envelope: Command field is required",
                    CancellationToken.None);
                return null;
            }

            return request;
        }
        catch (JsonException ex)
        {
            var sanitizedJsonError = LogSanitizer.SanitizeLogField(ex.Message ?? string.Empty, 200);
            logger.LogError(ex, "[WebSocketEventStreamWriter] JSON parse error: {Message}", sanitizedJsonError);
            await webSocket.CloseAsync(
                WebSocketCloseStatus.InvalidPayloadData,
                "Invalid JSON payload",
                CancellationToken.None);
            return null;
        }
    }

    /// <summary>
    /// Serialize an object to JSON and send it as a text WebSocket message while ensuring only one send occurs at a time.
    /// </summary>
    /// <param name="socket">The target WebSocket to send the JSON text frame to.</param>
    /// <param name="payload">The object to serialize to JSON for transmission.</param>
    /// <param name="sendLock">A semaphore used to enforce single-concurrent-send semantics.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    /// <param name="cancellationToken">Token to observe for cancellation of wait or send operations.</param>
    private static async Task SendJsonAsync(WebSocket socket, object payload, SemaphoreSlim sendLock, ILogger logger, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        logger.LogDebug("[WebSocketEventStreamWriter] >>>> SENDING: {Json}", json);
        
        var bytes = Encoding.UTF8.GetBytes(json);

        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            logger.LogDebug("[WebSocketEventStreamWriter] Message sent successfully ({ByteCount} bytes)", bytes.Length);
        }
        catch (Exception ex)
        {
            // Sanitize exception message to prevent log injection
            var sanitizedMessage = LogSanitizer.SanitizeLogField(ex.Message, 200);
            logger.LogError(ex, "[WebSocketEventStreamWriter] Send error: {ErrorType}: {Message}",
                ex.GetType().Name, sanitizedMessage);
            throw;
        }
        finally
        {
            sendLock.Release();
        }
    }
}