using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Filters;

public sealed class ApiMutationEventFilter : IAsyncActionFilter
{
    private const string TenantClaimType = "tenant";
    private const int MaxMetadataStringLength = 200;

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
        "AuthRefactored",
        "Realtime"
    };

    private readonly IRealtimeEventBus _eventBus;
    private readonly ILogger<ApiMutationEventFilter> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ApiMutationEventFilter"/> with required dependencies.
    /// </summary>
    /// <param name="eventBus">The realtime event bus used to publish API domain events.</param>
    /// <param name="logger">The logger used for warnings and diagnostic messages.</param>
    public ApiMutationEventFilter(IRealtimeEventBus eventBus, ILogger<ApiMutationEventFilter> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// Publishes an ApiDomainEvent for successful HTTP mutation actions (POST, PUT, PATCH, DELETE) unless the request or controller is excluded.
    /// </summary>
    /// <param name="context">The current action executing context containing HttpContext, route data, and user information.</param>
    /// <param name="next">Delegate to invoke the next action/middleware in the pipeline.</param>
    /// <returns>A task that completes after the action has executed and any event publish attempt has finished.</returns>
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

        // Skip RealtimeController to prevent duplicate events (it publishes its own)
        if (ExcludedControllers.Contains(resource))
        {
            return;
        }

        var user = context.HttpContext.User;
        var sanitizedPath = TruncateIfNeeded(request.Path.ToString(), MaxMetadataStringLength);
        var sanitizedUserAgent = TruncateIfNeeded(request.Headers.UserAgent.ToString(), MaxMetadataStringLength);
        
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["action"] = context.ActionDescriptor.DisplayName ?? "unknown",
            ["path"] = sanitizedPath,
            ["userAgent"] = sanitizedUserAgent,
            ["traceId"] = context.HttpContext.TraceIdentifier
            // Query string intentionally omitted to prevent PII/token leakage
        };

        var domainEvent = new ApiDomainEvent(
            EventName: $"{resource}.{request.Method}.completed",
            Resource: resource,
            HttpMethod: request.Method,
            StatusCode: statusCode,
            OccurredAt: DateTimeOffset.UtcNow,
            CorrelationId: context.HttpContext.TraceIdentifier,
            UserId: user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value,
            UserName: user.Identity?.Name,
            Tenant: user.FindFirst(TenantClaimType)?.Value,
            SourceIp: context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            Metadata: metadata);

        try
        {
            await _eventBus.PublishAsync(domainEvent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish mutation event for {Method} {Path}", request.Method, sanitizedPath);
        }
    }

    /// <summary>
    /// Truncates a string to the specified maximum length if it exceeds that length.
    /// </summary>
    /// <param name="value">The string to truncate.</param>
    /// <param name="maxLength">The maximum allowed length.</param>
    /// <returns>The original string if it's within the limit, otherwise a truncated version.</returns>
    private static string TruncateIfNeeded(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }
        return value.Substring(0, maxLength);
    }

    /// <summary>
    /// Determine the HTTP status code represented by an <see cref="IActionResult"/>, using the provided fallback when the result does not expose a status code.
    /// </summary>
    /// <param name="result">The action result to inspect; may be null.</param>
    /// <param name="fallback">The value to return when the result does not provide a status code.</param>
    /// <returns>The HTTP status code extracted from <paramref name="result"/> if available; otherwise <paramref name="fallback"/>.</returns>
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
