using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Filters;

public sealed class ApiMutationEventFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> MutationMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    private static readonly HashSet<string> ExcludedControllers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Auth",
        "AuthController",
        "AuthRefactored",
        "AuthControllerRefactored"
    };

    private readonly IRealtimeEventBus _eventBus;
    private readonly ILogger<ApiMutationEventFilter> _logger;

    public ApiMutationEventFilter(IRealtimeEventBus eventBus, ILogger<ApiMutationEventFilter> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executedContext = await next();
        var request = context.HttpContext.Request;

        if (!MutationMethods.Contains(request.Method) || executedContext.Exception is not null)
        {
            return;
        }

        var statusCode = ResolveStatusCode(executedContext.Result, context.HttpContext.Response.StatusCode);
        if (statusCode is < 200 or >= 400)
        {
            return;
        }

        var resource = context.RouteData.Values.TryGetValue("controller", out var controllerObj)
            ? controllerObj?.ToString() ?? "unknown"
            : "unknown";

        if (ExcludedControllers.Contains(resource))
        {
            return;
        }

        var user = context.HttpContext.User;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["action"] = context.ActionDescriptor.DisplayName ?? "unknown",
            ["path"] = request.Path.ToString(),
            ["query"] = request.QueryString.HasValue ? request.QueryString.Value ?? string.Empty : string.Empty,
            ["userAgent"] = request.Headers.UserAgent.ToString(),
            ["traceId"] = context.HttpContext.TraceIdentifier
        };

        var domainEvent = new ApiDomainEvent(
            EventName: $"{resource}.{request.Method}.completed",
            Resource: resource,
            HttpMethod: request.Method,
            StatusCode: statusCode,
            OccurredAt: DateTimeOffset.UtcNow,
            CorrelationId: context.HttpContext.TraceIdentifier,
            UserId: user.FindFirst("sub")?.Value ?? user.FindFirst("identity")?.Value,
            UserName: user.Identity?.Name,
            Tenant: user.FindFirst("tenant")?.Value,
            SourceIp: context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            Metadata: metadata);

        try
        {
            await _eventBus.PublishAsync(domainEvent, context.HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish mutation event for {Method} {Path}", request.Method, request.Path);
        }
    }

    private static int ResolveStatusCode(IActionResult? result, int fallback)
    {
        if (result is ObjectResult objectResult && objectResult.StatusCode.HasValue)
        {
            return objectResult.StatusCode.Value;
        }

        if (result is StatusCodeResult statusCodeResult)
        {
            return statusCodeResult.StatusCode;
        }

        return fallback;
    }
}
