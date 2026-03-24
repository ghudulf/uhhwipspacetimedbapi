using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Middleware
{
    /// <summary>
    /// Middleware that logs which controller and action is being invoked for each request.
    /// This is especially useful for debugging feature flag routing between legacy and refactored controllers.
    /// </summary>
    public class ControllerLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ControllerLoggingMiddleware> _logger;

        public ControllerLoggingMiddleware(RequestDelegate next, ILogger<ControllerLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Call the next middleware first to allow routing to complete
            await _next(context);

            // After routing is complete, log which controller/action was selected
            var endpoint = context.GetEndpoint();
            if (endpoint != null)
            {
                var routeValues = context.Request.RouteValues;
                var controllerName = routeValues["controller"]?.ToString();
                var actionName = routeValues["action"]?.ToString();
                var httpMethod = context.Request.Method;
                var path = context.Request.Path;
                var statusCode = context.Response.StatusCode;

                // Get the actual controller type from the endpoint metadata
                var controllerActionDescriptor = endpoint.Metadata
                    .GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>();

                if (controllerActionDescriptor != null)
                {
                    var actualControllerType = controllerActionDescriptor.ControllerTypeInfo.Name;
                    var actualActionName = controllerActionDescriptor.ActionName;

                    _logger.LogInformation(
                        "🎯 REQUEST ROUTED: {HttpMethod} {Path} → Controller: {ControllerType}.{ActionName} | Status: {StatusCode}",
                        httpMethod,
                        path,
                        actualControllerType,
                        actualActionName,
                        statusCode
                    );

                    // Special logging for Auth controller
                    if (actualControllerType == "AuthController")
                    {
                        _logger.LogInformation(
                            "🔀 AUTH ROUTING: {HttpMethod} {Path} → {ControllerType}.{ActionName}",
                            httpMethod,
                            path,
                            actualControllerType,
                            actualActionName
                        );
                    }
                }
                else if (controllerName != null && actionName != null)
                {
                    // Fallback if ControllerActionDescriptor is not available
                    _logger.LogInformation(
                        "🎯 REQUEST ROUTED: {HttpMethod} {Path} → {Controller}.{Action} | Status: {StatusCode}",
                        httpMethod,
                        path,
                        controllerName,
                        actionName,
                        statusCode
                    );
                }
                else
                {
                    // No controller/action found (might be static file, etc.)
                    _logger.LogDebug(
                        "📄 REQUEST: {HttpMethod} {Path} → No controller/action | Status: {StatusCode}",
                        httpMethod,
                        path,
                        statusCode
                    );
                }
            }
            else
            {
                // No endpoint matched
                _logger.LogWarning(
                    "❌ NO ENDPOINT MATCHED: {HttpMethod} {Path} | Status: {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode
                );
            }
        }
    }

    /// <summary>
    /// Extension method to easily add the ControllerLoggingMiddleware to the pipeline
    /// </summary>
    public static class ControllerLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseControllerLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ControllerLoggingMiddleware>();
        }
    }
}
