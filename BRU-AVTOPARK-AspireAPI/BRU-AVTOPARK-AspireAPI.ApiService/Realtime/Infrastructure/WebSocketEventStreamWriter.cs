using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

public static class WebSocketEventStreamWriter
{
    public static async Task StreamAsync(
        HttpContext context,
        IAsyncEnumerable<ApiDomainEvent> events,
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

        await foreach (var evt in events.WithCancellation(cancellationToken))
        {
            if (webSocket.State != WebSocketState.Open)
            {
                break;
            }

            var payload = JsonSerializer.Serialize(evt);
            var buffer = Encoding.UTF8.GetBytes(payload);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
        }

        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stream completed", cancellationToken);
        }

        logger.LogDebug("WebSocket event stream completed for {Path}", context.Request.Path);
    }
}
