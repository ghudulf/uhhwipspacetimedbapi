using Serilog.Context;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Middleware;

/// <summary>Correlates all downstream logs and responses without changing protocol body shapes.</summary>
public sealed class RequestTraceMiddleware(RequestDelegate next, ILogger<RequestTraceMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = context.TraceIdentifier;
        context.Response.Headers["Trace-Id"] = traceId;
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId });
        using var property = LogContext.PushProperty("TraceId", traceId);
        await next(context);
    }
}
