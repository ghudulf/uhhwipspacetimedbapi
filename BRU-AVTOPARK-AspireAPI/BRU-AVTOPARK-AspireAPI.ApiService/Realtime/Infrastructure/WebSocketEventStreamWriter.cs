using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

public sealed record RealtimeCrudRequest(
    string Command,
    string? RequestId,
    uint? Id,
    JsonElement? Payload);

public static class WebSocketEventStreamWriter
{
    private const int MaxIncomingMessageSize = 1024 * 1024; // 1 MB

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

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
                }, sendLock, linkedCts.Token);
            }
        }, linkedCts.Token);

        try
        {
            while (webSocket.State == WebSocketState.Open && !linkedCts.Token.IsCancellationRequested)
            {
                var request = await ReceiveRequestAsync(webSocket, linkedCts.Token);
                if (request is null)
                {
                    break;
                }

                // Prepare response payload (isolate business logic from transport)
                object payload;
                try
                {
                    var data = await requestHandler(request, linkedCts.Token);
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
                    logger.LogError(ex, "WebSocket CRUD command failed: {Command}", request.Command);

                    // Determine client-facing error message (sanitize sensitive information)
                    var clientErrorMessage = ex switch
                    {
                        UnauthorizedAccessException => ex.Message,
                        InvalidOperationException => ex.Message,
                        ArgumentException => "Bad request",
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
                await SendJsonAsync(webSocket, payload, sendLock, linkedCts.Token);
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

    private static async Task<RealtimeCrudRequest?> ReceiveRequestAsync(WebSocket webSocket, CancellationToken cancellationToken)
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

        // Wrap JSON deserialization in try-catch to handle invalid JSON
        try
        {
            return JsonSerializer.Deserialize<RealtimeCrudRequest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            await webSocket.CloseAsync(
                WebSocketCloseStatus.InvalidPayloadData,
                $"Invalid JSON: {ex.Message}",
                CancellationToken.None);
            return null;
        }
    }

    private static async Task SendJsonAsync(WebSocket socket, object payload, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }
}