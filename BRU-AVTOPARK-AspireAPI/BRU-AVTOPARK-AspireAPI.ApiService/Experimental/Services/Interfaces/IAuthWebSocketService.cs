using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace BRU_AVTOPARK.Services.Interfaces;

/// <summary>
/// Handles the real-time WebSocket authentication protocol for /api/auth/ws.
/// Extracted from AuthControllerRefactored to keep the controller thin and
/// allow independent testing of the WebSocket message-handling logic.
/// </summary>
public interface IAuthWebSocketService
{
    /// <summary>
    /// Runs the full WebSocket session loop: reads messages, dispatches handlers,
    /// manages QR-status subscriptions, and cleans up on disconnect.
    /// </summary>
    /// <param name="webSocket">The accepted WebSocket connection.</param>
    /// <param name="connectionId">Short unique ID for log correlation.</param>
    /// <param name="preValidatedClaims">Claims already validated at connection time (may be null for anonymous connections).</param>
    /// <param name="sourceIp">Remote IP captured before any background tasks.</param>
    /// <param name="cancellationToken">Linked to HttpContext.RequestAborted.</param>
    Task RunSessionAsync(
        WebSocket webSocket,
        string connectionId,
        Dictionary<string, object>? preValidatedClaims,
        string? sourceIp,
        CancellationToken cancellationToken);
}
