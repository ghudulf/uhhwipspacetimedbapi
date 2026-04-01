using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using Microsoft.Extensions.Logging;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Implements the WebSocket authentication session protocol.
/// Handles message dispatch, token validation, token refresh, QR-status polling,
/// and auth event publishing — all extracted from AuthControllerRefactored.
/// </summary>
public sealed class AuthWebSocketService : IAuthWebSocketService
{
    private static readonly JsonSerializerOptions WsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int MaxMessageBytes = 64 * 1024; // 64 KB
    private const int MaxQrSubscriptions = 50;
    private const int QrPollIntervalMs = 1500;
    private const int QrPollJitterMs = 300;
    private const int QrMaxPollSeconds = 300; // 5-minute QR expiry

    private readonly IAuthOrchestrationService _orchestration;
    private readonly IRealtimeEventBus _realtimeEventBus;
    private readonly IAuthWebSocketTokenValidator _tokenValidator;
    private readonly ILogger<AuthWebSocketService> _logger;

    public AuthWebSocketService(
        IAuthOrchestrationService orchestration,
        IRealtimeEventBus realtimeEventBus,
        IAuthWebSocketTokenValidator tokenValidator,
        ILogger<AuthWebSocketService> logger)
    {
        _orchestration = orchestration ?? throw new ArgumentNullException(nameof(orchestration));
        _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
        _tokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Public entry point ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task RunSessionAsync(
        WebSocket webSocket,
        string connectionId,
        Dictionary<string, object>? preValidatedClaims,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[AuthWS:{ConnId}] Session started. Pre-authenticated: {Auth}",
            connectionId, preValidatedClaims != null);

        var sendLock = new SemaphoreSlim(1, 1);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var qrSubscriptions = new ConcurrentDictionary<string, CancellationTokenSource>();
        var qrPollerTasks = new ConcurrentDictionary<string, Task>();
        var claimsHolder = new WsClaimsHolder { Claims = preValidatedClaims };
        var buffer = new byte[4096];

        try
        {
            await WsSendAsync(webSocket, new
            {
                type = "auth:connected",
                connectionId,
                authenticated = preValidatedClaims != null,
                serverTimeUtc = DateTimeOffset.UtcNow
            }, sendLock, cts.Token);

            while (webSocket.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await webSocket.ReceiveAsync(buffer, cts.Token);
                    if (ms.Length + result.Count > MaxMessageBytes)
                    {
                        _logger.LogWarning("[AuthWS:{ConnId}] Message exceeds {Max} bytes, closing", connectionId, MaxMessageBytes);
                        await webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var json = Encoding.UTF8.GetString(ms.ToArray());
                await HandleMessageAsync(webSocket, json, sendLock, connectionId,
                    claimsHolder, qrSubscriptions, qrPollerTasks, sourceIp, cts);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[AuthWS:{ConnId}] Cancelled", connectionId);
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogWarning("[AuthWS:{ConnId}] Connection closed prematurely", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AuthWS:{ConnId}] Unexpected error", connectionId);
        }
        finally
        {
            cts.Cancel();
            foreach (var sub in qrSubscriptions.Values)
                sub.Cancel();

            if (qrPollerTasks.Count > 0)
            {
                var allTasks = qrPollerTasks.Values.ToArray();
                try
                {
                    using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Task.WhenAny(Task.WhenAll(allTasks), Task.Delay(Timeout.Infinite, shutdownCts.Token));
                    var stuck = allTasks.Count(t => !t.IsCompleted);
                    if (stuck > 0)
                        _logger.LogWarning("[AuthWS:{ConnId}] {Count} QR poller(s) did not finish within shutdown timeout", connectionId, stuck);
                }
                catch { /* individual poller exceptions already logged */ }
            }

            sendLock.Dispose();
            _logger.LogInformation("[AuthWS:{ConnId}] Session closed", connectionId);
        }
    }

    // ── Message dispatcher ────────────────────────────────────────────────────

    private async Task HandleMessageAsync(
        WebSocket webSocket,
        string json,
        SemaphoreSlim sendLock,
        string connectionId,
        WsClaimsHolder claimsHolder,
        ConcurrentDictionary<string, CancellationTokenSource> qrSubscriptions,
        ConcurrentDictionary<string, Task> qrPollerTasks,
        string? sourceIp,
        CancellationTokenSource cts)
    {
        string? requestId = null;
        string? messageType = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            requestId = root.TryGetProperty("requestId", out var rid) ? (rid.ValueKind == JsonValueKind.String ? rid.GetString() : null) : null;
            messageType = root.TryGetProperty("type", out var t) ? (t.ValueKind == JsonValueKind.String ? t.GetString() : null) : null;

            if (messageType == null && root.TryGetProperty("type", out var tRaw) && tRaw.ValueKind != JsonValueKind.String)
            {
                await WsSendAsync(webSocket, new { type = "auth:error", requestId, error = "Protocol error: 'type' field must be a string" }, sendLock, cts.Token);
                return;
            }

            _logger.LogDebug("[AuthWS:{ConnId}] Received: {Type} (requestId={ReqId})",
                connectionId, SanitizeLogValue(messageType), SanitizeLogValue(requestId));

            switch (messageType?.ToLowerInvariant())
            {
                case "auth:validate":
                    await HandleValidateAsync(webSocket, root, requestId, sendLock,
                        connectionId, claimsHolder, sourceIp, cts.Token);
                    break;

                case "auth:refresh":
                    await HandleRefreshAsync(webSocket, root, requestId, sendLock,
                        connectionId, claimsHolder.Claims, sourceIp, cts.Token);
                    break;

                case "auth:qr-status":
                    if (claimsHolder.Claims == null)
                    {
                        await WsSendAsync(webSocket, new { type = "auth:error", requestId, error = "Authentication required for auth:qr-status" }, sendLock, cts.Token);
                        break;
                    }
                    await HandleQrStatusAsync(webSocket, root, requestId, sendLock,
                        connectionId, claimsHolder.Claims, qrSubscriptions, qrPollerTasks, sourceIp, cts);
                    break;

                case "auth:login":
                    await HandleLoginAsync(webSocket, root, requestId, sendLock,
                        connectionId, claimsHolder, sourceIp, cts.Token);
                    break;

                case "auth:ping":
                    await WsSendAsync(webSocket, new { type = "auth:pong", requestId, serverTimeUtc = DateTimeOffset.UtcNow }, sendLock, cts.Token);
                    break;

                default:
                    await WsSendAsync(webSocket, new { type = "auth:error", requestId, error = $"Unknown message type: {messageType}" }, sendLock, cts.Token);
                    break;
            }
        }
        catch (JsonException)
        {
            await WsSendAsync(webSocket, new { type = "auth:error", requestId, error = "Invalid JSON" }, sendLock, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AuthWS:{ConnId}] Error handling message type {Type}", connectionId, messageType);
            await WsSendAsync(webSocket, new { type = "auth:error", requestId, error = "Internal server error" }, sendLock, cts.Token);
        }
    }

    // ── auth:login ────────────────────────────────────────────────────────────

    private async Task HandleLoginAsync(
        WebSocket webSocket,
        JsonElement root,
        string? requestId,
        SemaphoreSlim sendLock,
        string connectionId,
        WsClaimsHolder claimsHolder,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        var username = root.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = root.TryGetProperty("password", out var p) ? p.GetString() : null;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            await WsSendAsync(webSocket, new { type = "auth:login-result", requestId, success = false, error = "username and password are required" }, sendLock, cancellationToken);
            return;
        }

        _logger.LogInformation("[AuthWS:{ConnId}] auth:login attempt for user {User}", connectionId, SanitizeLogValue(username));

        var result = await _orchestration.LoginAsync(username, password);

        if (!result.Success)
        {
            if (result.RequiresTwoFactor)
            {
                _logger.LogInformation("[AuthWS:{ConnId}] auth:login requires 2FA for {User}", connectionId, SanitizeLogValue(username));
                await WsSendAsync(webSocket, new
                {
                    type = "auth:login-result",
                    requestId,
                    success = false,
                    requiresTwoFactor = true,
                    twoFactorType = result.TwoFactorType,
                    tempToken = result.TempToken,
                    totpEnabled = result.TotpEnabled,
                    webAuthnEnabled = result.WebAuthnEnabled
                }, sendLock, cancellationToken);
            }
            else
            {
                _logger.LogWarning("[AuthWS:{ConnId}] auth:login failed for {User}: {Error}", connectionId, SanitizeLogValue(username), result.ErrorMessage);
                await WsSendAsync(webSocket, new { type = "auth:login-result", requestId, success = false, error = result.ErrorMessage ?? "Authentication failed" }, sendLock, cancellationToken);
            }
            return;
        }

        // Upgrade the connection's auth state with the new claims
        if (result.Claims != null)
            claimsHolder.Claims = result.Claims;

        await PublishAuthEventAsync("auth.login.websocket",
            result.User?.Id.ToString(),
            result.User?.Username,
            sourceIp,
            new Dictionary<string, string> { ["source"] = "websocket" });

        _logger.LogInformation("[AuthWS:{ConnId}] auth:login succeeded for {User}", connectionId, SanitizeLogValue(username));

        await WsSendAsync(webSocket, new
        {
            type = "auth:login-result",
            requestId,
            success = true,
            token = result.Token,
            user = result.User == null ? null : new
            {
                id = result.User.Id,
                username = result.User.Username,
                email = result.User.Email,
                role = result.User.Role
            },
            claims = result.Claims != null ? BuildSafeClaims(result.Claims) : null
        }, sendLock, cancellationToken);
    }

    // ── auth:validate ─────────────────────────────────────────────────────────

    private async Task HandleValidateAsync(
        WebSocket webSocket,
        JsonElement root,
        string? requestId,
        SemaphoreSlim sendLock,
        string connectionId,
        WsClaimsHolder claimsHolder,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        string? inlineToken = root.TryGetProperty("token", out var tok) ? tok.GetString() : null;

        Dictionary<string, object>? claims = null;

        if (!string.IsNullOrWhiteSpace(inlineToken))
            claims = await _tokenValidator.ValidateTokenDirectAsync(inlineToken, cancellationToken);
        else
            claims = claimsHolder.Claims;

        if (claims == null || claims.Count == 0)
        {
            _logger.LogWarning("[AuthWS:{ConnId}] auth:validate failed – invalid or missing token", connectionId);
            await WsSendAsync(webSocket, new { type = "auth:validated", requestId, success = false, error = "Invalid or expired token" }, sendLock, cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(inlineToken))
        {
            claimsHolder.Claims = claims;
            _logger.LogInformation("[AuthWS:{ConnId}] Connection upgraded to authenticated via auth:validate", connectionId);
        }

        await PublishAuthEventAsync("auth.token.validated",
            claims.TryGetValue("sub", out var sub) ? sub?.ToString() : null,
            claims.TryGetValue("unique_name", out var name) ? name?.ToString() : null,
            sourceIp,
            new Dictionary<string, string> { ["source"] = "websocket" });

        await WsSendAsync(webSocket, new { type = "auth:validated", requestId, success = true, claims = BuildSafeClaims(claims) }, sendLock, cancellationToken);
    }

    // ── auth:refresh ──────────────────────────────────────────────────────────

    private async Task HandleRefreshAsync(
        WebSocket webSocket,
        JsonElement root,
        string? requestId,
        SemaphoreSlim sendLock,
        string connectionId,
        Dictionary<string, object>? validatedClaims,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        var refreshToken = root.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            await WsSendAsync(webSocket, new { type = "auth:refreshed", requestId, success = false, error = "refreshToken is required" }, sendLock, cancellationToken);
            return;
        }

        var result = await _orchestration.RefreshTokenAsync(refreshToken);
        if (!result.Success)
        {
            _logger.LogWarning("[AuthWS:{ConnId}] auth:refresh failed: {Error}", connectionId, result.ErrorMessage);
            await WsSendAsync(webSocket, new { type = "auth:refreshed", requestId, success = false, error = result.ErrorMessage ?? "Token refresh failed" }, sendLock, cancellationToken);
            return;
        }

        await PublishAuthEventAsync("auth.token.refreshed",
            validatedClaims?.TryGetValue("sub", out var sub) == true ? sub?.ToString() : null,
            validatedClaims?.TryGetValue("unique_name", out var name) == true ? name?.ToString() : null,
            sourceIp,
            new Dictionary<string, string> { ["source"] = "websocket" });

        await WsSendAsync(webSocket, new
        {
            type = "auth:refreshed",
            requestId,
            success = true,
            token = result.Token,
            refreshToken = result.RefreshToken,
            expiresAt = result.ExpiresAt
        }, sendLock, cancellationToken);
    }

    // ── auth:qr-status ────────────────────────────────────────────────────────

    private async Task HandleQrStatusAsync(
        WebSocket webSocket,
        JsonElement root,
        string? requestId,
        SemaphoreSlim sendLock,
        string connectionId,
        Dictionary<string, object>? validatedClaims,
        ConcurrentDictionary<string, CancellationTokenSource> qrSubscriptions,
        ConcurrentDictionary<string, Task> qrPollerTasks,
        string? sourceIp,
        CancellationTokenSource connectionCts)
    {
        var deviceId = root.TryGetProperty("deviceId", out var did) ? did.GetString() : null;

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            await WsSendAsync(webSocket, new { type = "auth:error", requestId, error = "deviceId is required for auth:qr-status" }, sendLock, connectionCts.Token);
            return;
        }

        // Cancel any existing subscription for this deviceId
        if (qrSubscriptions.TryRemove(deviceId, out var existing))
            existing.Cancel();

        if (qrSubscriptions.Count >= MaxQrSubscriptions)
        {
            await WsSendAsync(webSocket, new { type = "auth:qr-error", requestId, deviceId, reason = "Too many concurrent QR subscriptions. Please try again later." }, sendLock, connectionCts.Token);
            return;
        }

        var subCts = CancellationTokenSource.CreateLinkedTokenSource(connectionCts.Token);
        qrSubscriptions[deviceId] = subCts;

        await WsSendAsync(webSocket, new { type = "auth:qr-subscribed", requestId, deviceId }, sendLock, connectionCts.Token);

        var pollerKey = $"{deviceId}_{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<Task>();
        qrPollerTasks[pollerKey] = tcs.Task.Unwrap();

        var pollerTask = Task.Run(async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(QrMaxPollSeconds);

            try
            {
                while (!subCts.Token.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
                {
                    string? pollStatus;
                    string? pollToken;

                    try
                    {
                        var status = await _orchestration.CheckQRLoginStatusAsync(deviceId);
                        if (status == null)
                        {
                            if (qrSubscriptions.TryGetValue(deviceId, out var cur) && ReferenceEquals(cur, subCts))
                            {
                                await WsSendAsync(webSocket, new { type = "auth:qr-error", deviceId, reason = "QR status check returned null" }, sendLock, subCts.Token);
                                await PublishAuthEventAsync("auth.qr.error", null, null, sourceIp, new Dictionary<string, string> { ["deviceId"] = deviceId, ["reason"] = "null_status" });
                            }
                            break;
                        }
                        if (!status.Success)
                        {
                            if (qrSubscriptions.TryGetValue(deviceId, out var cur) && ReferenceEquals(cur, subCts))
                            {
                                await WsSendAsync(webSocket, new { type = "auth:qr-error", deviceId, reason = "QR status check indicated failure" }, sendLock, subCts.Token);
                                await PublishAuthEventAsync("auth.qr.error", null, null, sourceIp, new Dictionary<string, string> { ["deviceId"] = deviceId, ["reason"] = "status_not_success" });
                            }
                            break;
                        }
                        pollStatus = status.Status;
                        pollToken = status.Token;
                    }
                    catch (Exception pollEx)
                    {
                        _logger.LogError(pollEx, "[AuthWS:{ConnId}] QR poll exception for device {DeviceId}", connectionId, deviceId);
                        if (qrSubscriptions.TryGetValue(deviceId, out var cur) && ReferenceEquals(cur, subCts))
                        {
                            await WsSendAsync(webSocket, new { type = "auth:qr-error", deviceId, reason = "Internal error during QR status check" }, sendLock, subCts.Token);
                            await PublishAuthEventAsync("auth.qr.error", null, null, sourceIp, new Dictionary<string, string> { ["deviceId"] = deviceId, ["reason"] = "poll_exception" });
                        }
                        break;
                    }

                    if (pollStatus == "completed" && !string.IsNullOrEmpty(pollToken))
                    {
                        if (qrSubscriptions.TryGetValue(deviceId, out var cur) && ReferenceEquals(cur, subCts))
                        {
                            await WsSendAsync(webSocket, new { type = "auth:qr-completed", deviceId, token = pollToken }, sendLock, subCts.Token);
                            await PublishAuthEventAsync("auth.qr.completed", null, null, sourceIp, new Dictionary<string, string> { ["deviceId"] = deviceId, ["source"] = "websocket" });
                        }
                        break;
                    }

                    if (pollStatus is "failed" or "cancelled" or "expired")
                    {
                        if (qrSubscriptions.TryGetValue(deviceId, out var cur) && ReferenceEquals(cur, subCts))
                        {
                            await WsSendAsync(webSocket, new { type = "auth:qr-failed", deviceId, reason = pollStatus }, sendLock, subCts.Token);
                            await PublishAuthEventAsync("auth.qr.failed", null, null, sourceIp, new Dictionary<string, string> { ["deviceId"] = deviceId, ["reason"] = pollStatus!, ["source"] = "websocket" });
                        }
                        break;
                    }

                    var jitter = Random.Shared.Next(-QrPollJitterMs, QrPollJitterMs);
                    await Task.Delay(QrPollIntervalMs + jitter, subCts.Token);
                }

                // Timed out
                if (!subCts.Token.IsCancellationRequested && DateTimeOffset.UtcNow >= deadline)
                {
                    if (qrSubscriptions.TryGetValue(deviceId, out var cur) && ReferenceEquals(cur, subCts))
                    {
                        await WsSendAsync(webSocket, new { type = "auth:qr-failed", deviceId, reason = "timeout" }, sendLock, subCts.Token);
                        await PublishAuthEventAsync("auth.qr.failed", null, null, sourceIp, new Dictionary<string, string> { ["deviceId"] = deviceId, ["reason"] = "timeout", ["source"] = "websocket" });
                    }
                }
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthWS:{ConnId}] QR status polling error for device {DeviceId}", connectionId, deviceId);
            }
            finally
            {
                if (qrSubscriptions.TryGetValue(deviceId, out var current) && ReferenceEquals(current, subCts))
                    qrSubscriptions.TryRemove(deviceId, out _);
                subCts?.Dispose();
                qrPollerTasks.TryRemove(pollerKey, out _);
            }
        });

        tcs.SetResult(pollerTask);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task PublishAuthEventAsync(
        string eventName,
        string? userId,
        string? userName,
        string? sourceIp,
        Dictionary<string, string>? metadata = null)
    {
        try
        {
            var domainEvent = new ApiDomainEvent(
                EventName: eventName,
                Resource: "auth",
                HttpMethod: "WS",
                StatusCode: 200,
                OccurredAt: DateTimeOffset.UtcNow,
                CorrelationId: Guid.NewGuid().ToString("N"),
                UserId: userId,
                UserName: userName,
                Tenant: null,
                SourceIp: sourceIp ?? "unknown",
                Metadata: (metadata ?? new Dictionary<string, string>()).AsReadOnly());

            await _realtimeEventBus.PublishAsync(domainEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish auth event {EventName} to realtime bus", eventName);
        }
    }

    private static Dictionary<string, object> BuildSafeClaims(Dictionary<string, object> claims)
    {
        var sensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "oi_tkn_id", "oi_au_id", "oi_app_id"
        };
        return claims
            .Where(kv => !sensitive.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static string SanitizeLogValue(string? value, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(none)";
        var sanitized = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (sanitized.Length == 0) return "(none)";
        return sanitized.Length > maxLength ? sanitized[..maxLength] + "…" : sanitized;
    }

    private async Task WsSendAsync(WebSocket webSocket, object payload, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        if (webSocket.State != WebSocketState.Open)
            return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, WsJsonOptions);

        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }
}

/// <summary>
/// Mutable wrapper for validated claims, allowing async WebSocket handlers
/// to upgrade the connection's authentication state without ref parameters.
/// Single-writer assumption: only mutated by HandleValidateAsync sequentially.
/// </summary>
public sealed class WsClaimsHolder
{
    public Dictionary<string, object>? Claims { get; set; }
}
