using System.Diagnostics;
using BRU.Avtopark.Contracts;
using TicketSalesApp.Services.Diagnostics;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Middleware;

public sealed class TrafficLoggingMiddleware(RequestDelegate next, ILogger<TrafficLoggingMiddleware> logger, IConfiguration configuration)
{
    private readonly TrafficOptions options = TrafficDiagnostics.ServerOptions(configuration);
    public async Task InvokeAsync(HttpContext context)
    {
        if (options.Level == TrafficLevel.Off || context.Request.Path.Value?.ToLowerInvariant() is "/health" or "/alive" or "/metrics")
        { await next(context); return; }
        var started = Stopwatch.GetTimestamp();
        var request = context.Request;
        var upgrade = context.WebSockets.IsWebSocketRequest || request.Headers.Upgrade == "websocket";
        var requestBody = "[skipped]";
        if (!upgrade && TrafficDiagnostics.IsText(request.ContentType))
        {
            var original = request.Body;
            request.EnableBuffering();
            var prefix = new byte[options.MaxBodyBytes];
            var count = await request.Body.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, context.RequestAborted);
            Array.Resize(ref prefix, count);
            // Do not keep extending FileBufferingReadStream when the endpoint reads a large request.
            request.Body = new TrafficReplayStream(prefix, original, leaveOpen: true);
            requestBody = TrafficDiagnostics.Body(prefix, request.ContentLength ?? count, request.ContentType, options, request.Path.StartsWithSegments("/api/auth"));
        }
        else if (!upgrade && request.ContentLength is { } length) requestBody = DebugPayload.Binary(length, options.Level);
        var requestHeaders = DebugPayload.Headers(request.Headers.Where(h => Selected(h.Key)).Select(h =>
            new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value.Select(v => v ?? ""))), options.Level);
        var url = DebugPayload.Url(request.PathBase + request.Path + request.QueryString, options.Level);
        logger.LogDebug("HTTP request {Method} {Url} TraceId={TraceId} Headers={Headers} Body={Body}", request.Method, url, context.TraceIdentifier, System.Text.Json.JsonSerializer.Serialize(requestHeaders), requestBody);
        var originalResponse = context.Response.Body;
        bool Capture() => !upgrade && !ExcludedBody(context, options);
        using var tee = new TrafficCaptureStream(originalResponse, options.MaxBodyBytes, Capture);
        context.Response.Body = tee;
        try { await next(context); }
        finally
        {
            context.Response.Body = originalResponse;
            var body = Capture() ? TrafficDiagnostics.Body(tee.Preview, tee.TotalBytes, context.Response.ContentType, options, request.Path.StartsWithSegments("/api/auth")) : "[skipped streaming/HTML/file/upgrade]";
            var headers = DebugPayload.Headers(context.Response.Headers.Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value.Select(v => v ?? ""))), options.Level);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            logger.LogDebug("HTTP response {Method} {Url} Status={StatusCode} TraceId={TraceId} ElapsedMs={ElapsedMs} Headers={Headers} Body={Body}", request.Method, url, context.Response.StatusCode, context.TraceIdentifier, elapsed, System.Text.Json.JsonSerializer.Serialize(headers), body);
            if (context.Response.StatusCode >= 400)
                logger.LogWarning("HTTP failed {Method} {Url} Status={StatusCode} TraceId={TraceId} ElapsedMs={ElapsedMs} RequestHeaders={RequestHeaders} RequestBody={RequestBody} Headers={Headers} Body={Body}", request.Method, url, context.Response.StatusCode, context.TraceIdentifier, elapsed, System.Text.Json.JsonSerializer.Serialize(requestHeaders), requestBody, System.Text.Json.JsonSerializer.Serialize(headers), body);
        }
    }
    private static bool Selected(string name) => name.StartsWith("X-BRU-", StringComparison.OrdinalIgnoreCase) ||
        new[] { "Content-Type", "Content-Length", "User-Agent", "Origin", "Accept", "Authorization", "Cookie", "X-Api-Key" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    private static bool ExcludedBody(HttpContext context, TrafficOptions options)
    {
        var type = context.Response.ContentType ?? "";
        // Authentication HTML can embed protocol secrets and is never captured, even in full mode.
        if (type.Contains("html", StringComparison.OrdinalIgnoreCase) && (context.Request.Path.StartsWithSegments("/api/auth/passkey") || context.Request.Path.StartsWithSegments("/api/auth/entra"))) return true;
        if (options.Level == TrafficLevel.Full) return false;
        return !TrafficDiagnostics.IsText(type) || type.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("html", StringComparison.OrdinalIgnoreCase) || context.Response.Headers.ContainsKey("Content-Disposition");
    }
}
