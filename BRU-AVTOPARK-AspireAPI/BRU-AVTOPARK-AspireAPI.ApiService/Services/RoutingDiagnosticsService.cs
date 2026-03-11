using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Services
{
    /// <summary>
    /// Diagnostic service that logs all discovered controllers and endpoints at startup.
    /// This helps debug routing issues where controllers aren't being discovered.
    /// </summary>
    public class RoutingDiagnosticsService : IHostedService
    {
        private readonly IActionDescriptorCollectionProvider _actionDescriptorProvider;
        private readonly ILogger<RoutingDiagnosticsService> _logger;

        public RoutingDiagnosticsService(
            IActionDescriptorCollectionProvider actionDescriptorProvider,
            ILogger<RoutingDiagnosticsService> logger)
        {
            _actionDescriptorProvider = actionDescriptorProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("========== ROUTING DIAGNOSTICS START ==========");
            
            var actions = _actionDescriptorProvider.ActionDescriptors.Items;
            _logger.LogInformation("Total actions discovered: {Count}", actions.Count);

            // Group by controller
            var controllerActions = actions
                .OfType<ControllerActionDescriptor>()
                .GroupBy(a => a.ControllerName)
                .OrderBy(g => g.Key);

            foreach (var controllerGroup in controllerActions)
            {
                _logger.LogInformation("Controller: {ControllerName} ({Count} actions)", 
                    controllerGroup.Key, controllerGroup.Count());

                foreach (var action in controllerGroup.OrderBy(a => a.ActionName))
                {
                    var route = action.AttributeRouteInfo?.Template ?? "No route";
                    
                    // Get HTTP methods from endpoint metadata instead of action constraints
                    var httpMethodMetadata = action.EndpointMetadata?
                        .OfType<HttpMethodMetadata>()
                        .FirstOrDefault();
                    var httpMethods = httpMethodMetadata != null 
                        ? string.Join(", ", httpMethodMetadata.HttpMethods)
                        : "ANY";
                    
                    var constraints = action.ActionConstraints?
                        .Select(c => c.GetType().Name)
                        .ToList() ?? new System.Collections.Generic.List<string>();

                    _logger.LogInformation(
                        "  Action: {ActionName}, Route: {Route}, Methods: {Methods}, Constraints: [{Constraints}]",
                        action.ActionName, route, httpMethods, string.Join(", ", constraints));
                }
            }

            // Specifically check for Auth controllers
            var authControllers = actions
                .OfType<ControllerActionDescriptor>()
                .Where(a => a.ControllerName.Contains("Auth", System.StringComparison.OrdinalIgnoreCase))
                .ToList();

            _logger.LogInformation("Auth-related controllers found: {Count}", authControllers.Count);
            foreach (var authAction in authControllers)
            {
                _logger.LogInformation(
                    "  Auth Action: {Controller}.{Action}, Route: {Route}",
                    authAction.ControllerName, authAction.ActionName, 
                    authAction.AttributeRouteInfo?.Template ?? "No route");
            }

            // Check for duplicate routes
            var duplicateRoutes = actions
                .OfType<ControllerActionDescriptor>()
                .Where(a => a.AttributeRouteInfo != null)
                .GroupBy(a => new { 
                    Route = a.AttributeRouteInfo!.Template, 
                    Method = string.Join(",", a.EndpointMetadata?
                        .OfType<HttpMethodMetadata>()
                        .FirstOrDefault()?.HttpMethods ?? new[] { "ANY" })
                })
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateRoutes.Any())
            {
                _logger.LogWarning("DUPLICATE ROUTES DETECTED ({Count}):", duplicateRoutes.Count);
                foreach (var group in duplicateRoutes)
                {
                    _logger.LogWarning(
                        "  Route: {Route} [{Method}] has {Count} actions:",
                        group.Key.Route, group.Key.Method, group.Count());
                    
                    foreach (var action in group)
                    {
                        _logger.LogWarning(
                            "    - {Controller}.{Action}",
                            action.ControllerName, action.ActionName);
                    }
                }
            }

            _logger.LogInformation("========== ROUTING DIAGNOSTICS END ==========");
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
