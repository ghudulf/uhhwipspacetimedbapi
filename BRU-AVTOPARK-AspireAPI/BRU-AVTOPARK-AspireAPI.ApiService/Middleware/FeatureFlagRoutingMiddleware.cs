using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using TicketSalesApp.AdminServer.Configuration;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Middleware
{
    /// <summary>
    /// Middleware that intercepts requests and dynamically routes them to legacy or refactored
    /// controllers based on feature flags. This runs BEFORE endpoint routing to prevent
    /// ambiguous match exceptions.
    /// </summary>
    public class FeatureFlagRoutingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<FeatureFlagRoutingMiddleware> _logger;
        private readonly IOptions<FeatureFlagOptions> _featureFlags;

        // Route mapping: path pattern -> feature flag property name
        private static readonly Dictionary<string, string> RouteToFeatureFlagMap = new()
        {
            // Auth endpoints
            { "GET:/api/auth/login", nameof(FeatureFlagOptions.EnableLoginRefactoring) },
            { "POST:/api/auth/login", nameof(FeatureFlagOptions.EnableLoginRefactoring) },
            { "GET:/api/auth/register", nameof(FeatureFlagOptions.EnableRegisterRefactoring) },
            { "POST:/api/auth/register", nameof(FeatureFlagOptions.EnableRegisterRefactoring) },
            { "GET:/api/auth/logout", nameof(FeatureFlagOptions.EnableLoginRefactoring) },
            { "POST:/api/auth/logout", nameof(FeatureFlagOptions.EnableLoginRefactoring) },
            
            // OAuth/OIDC endpoints - API
            { "POST:/api/auth/connect/clients", nameof(FeatureFlagOptions.EnableOAuthClientRegisterRefactoring) },
            { "GET:/api/auth/connect/clients", nameof(FeatureFlagOptions.EnableOAuthClientListRefactoring) },
            { "PUT:/api/auth/connect/clients/{id}", nameof(FeatureFlagOptions.EnableOAuthClientUpdateRefactoring) },
            { "DELETE:/api/auth/connect/clients/{id}", nameof(FeatureFlagOptions.EnableOAuthClientDeleteRefactoring) },
            { "GET:/api/auth/connect/scopes", nameof(FeatureFlagOptions.EnableOAuthScopesRefactoring) },
            { "POST:/api/auth/connect/clients/{id}/regenerate-secret", nameof(FeatureFlagOptions.EnableOAuthClientRegenerateSecretRefactoring) },
            
            // Profile endpoints
            { "GET:/api/auth/profile", nameof(FeatureFlagOptions.EnableProfileRefactoring) },
            { "PUT:/api/auth/profile", nameof(FeatureFlagOptions.EnableProfileUpdateRefactoring) },
            
            // Utility pages
            { "GET:/api/auth/success", nameof(FeatureFlagOptions.EnableSuccessPageRefactoring) },
            { "GET:/api/auth/error", nameof(FeatureFlagOptions.EnableErrorPageRefactoring) },
            { "GET:/api/auth/claim-account", nameof(FeatureFlagOptions.EnableClaimAccountPageRefactoring) },
            { "GET:/api/auth/webauthn/register", nameof(FeatureFlagOptions.EnableWebAuthnRegisterPageRefactoring) },
        };

        public FeatureFlagRoutingMiddleware(
            RequestDelegate next,
            ILogger<FeatureFlagRoutingMiddleware> logger,
            IOptions<FeatureFlagOptions> featureFlags)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _featureFlags = featureFlags ?? throw new ArgumentNullException(nameof(featureFlags));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                var path = context.Request.Path.Value?.ToLower() ?? "";
                var method = context.Request.Method.ToUpper();
                var routeKey = $"{method}:{path}";

                _logger.LogDebug("FeatureFlagRoutingMiddleware: Processing {Method} {Path}", method, path);

                // Check if this route has feature flag routing
                string? featureFlagProperty = null;
                
                // Try exact match first
                if (RouteToFeatureFlagMap.TryGetValue(routeKey, out featureFlagProperty))
                {
                    _logger.LogInformation("FeatureFlagRoutingMiddleware: Exact match found for {RouteKey} -> {FeatureFlag}", 
                        routeKey, featureFlagProperty);
                }
                else
                {
                    // Try pattern matching for routes with parameters
                    foreach (var kvp in RouteToFeatureFlagMap)
                    {
                        if (MatchesRoutePattern(routeKey, kvp.Key))
                        {
                            featureFlagProperty = kvp.Value;
                            _logger.LogInformation("FeatureFlagRoutingMiddleware: Pattern match found for {RouteKey} -> {Pattern} -> {FeatureFlag}", 
                                routeKey, kvp.Key, featureFlagProperty);
                            break;
                        }
                    }
                }

                if (featureFlagProperty != null)
                {
                    try
                    {
                        // Get the feature flag value
                        var flagProperty = typeof(FeatureFlagOptions).GetProperty(featureFlagProperty);
                        if (flagProperty != null)
                        {
                            var flagValue = (bool?)flagProperty.GetValue(_featureFlags.Value) ?? false;
                            
                            _logger.LogInformation(
                                "FeatureFlagRoutingMiddleware: Route {Method} {Path} -> FeatureFlag {Flag} = {Value} -> Controller: {Controller}",
                                method, path, featureFlagProperty, flagValue, flagValue ? "Refactored" : "Legacy");

                            // Store the routing decision in HttpContext.Items for logging/debugging
                            context.Items["FeatureFlagRouting_Flag"] = featureFlagProperty;
                            context.Items["FeatureFlagRouting_Value"] = flagValue;
                            context.Items["FeatureFlagRouting_Controller"] = "AuthController";
                        }
                        else
                        {
                            _logger.LogWarning("FeatureFlagRoutingMiddleware: Feature flag property {Property} not found", featureFlagProperty);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "FeatureFlagRoutingMiddleware: Error reading feature flag {Property}", featureFlagProperty);
                        // Continue anyway - let the action constraint handle it
                    }
                }

                // Continue to next middleware
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FeatureFlagRoutingMiddleware: Unhandled exception processing request {Method} {Path}", 
                    context.Request.Method, context.Request.Path);
                
                // Always continue to next middleware even on error
                await _next(context);
            }
        }

        /// <summary>
        /// Matches a route key against a pattern that may contain {id} or other parameters
        /// </summary>
        private bool MatchesRoutePattern(string routeKey, string pattern)
        {
            try
            {
                // Split into method and path
                var routeParts = routeKey.Split(':', 2);
                var patternParts = pattern.Split(':', 2);

                if (routeParts.Length != 2 || patternParts.Length != 2)
                    return false;

                // Method must match exactly
                if (routeParts[0] != patternParts[0])
                    return false;

                // Match path with parameters
                var routeSegments = routeParts[1].Split('/', StringSplitOptions.RemoveEmptyEntries);
                var patternSegments = patternParts[1].Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (routeSegments.Length != patternSegments.Length)
                    return false;

                for (int i = 0; i < routeSegments.Length; i++)
                {
                    // Pattern segment can be a parameter like {id} or exact match
                    if (patternSegments[i].StartsWith("{") && patternSegments[i].EndsWith("}"))
                    {
                        // This is a parameter, matches any value
                        continue;
                    }
                    
                    // Must match exactly
                    if (routeSegments[i] != patternSegments[i])
                        return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FeatureFlagRoutingMiddleware: Error in MatchesRoutePattern for {RouteKey} vs {Pattern}", routeKey, pattern);
                return false;
            }
        }
    }

    /// <summary>
    /// Extension method to register the FeatureFlagRoutingMiddleware
    /// </summary>
    public static class FeatureFlagRoutingMiddlewareExtensions
    {
        public static IApplicationBuilder UseFeatureFlagRouting(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<FeatureFlagRoutingMiddleware>();
        }
    }
}
