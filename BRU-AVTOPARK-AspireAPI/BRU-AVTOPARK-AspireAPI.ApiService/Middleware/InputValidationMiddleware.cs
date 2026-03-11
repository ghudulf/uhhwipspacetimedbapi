using System.Text.Json;
using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BRU_AVTOPARK.Middleware;

/// <summary>
/// Middleware that validates and sanitizes incoming request data to prevent injection attacks.
/// Provides defense-in-depth protection by checking inputs before they reach controllers.
/// </summary>
public class InputValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<InputValidationMiddleware> _logger;

    public InputValidationMiddleware(RequestDelegate next, ILogger<InputValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IInputSanitizationService sanitizationService)
    {
        // Only validate POST, PUT, PATCH requests with JSON content
        if ((context.Request.Method == HttpMethods.Post || 
             context.Request.Method == HttpMethods.Put || 
             context.Request.Method == HttpMethods.Patch) &&
            context.Request.ContentType?.Contains("application/json") == true)
        {
            // Enable buffering to allow reading the body multiple times
            context.Request.EnableBuffering();

            try
            {
                // Read the request body
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0; // Reset stream position

                if (!string.IsNullOrWhiteSpace(body))
                {
                    // Check for suspicious patterns in the raw body
                    if (sanitizationService.ContainsSuspiciousPatterns(body))
                    {
                        _logger.LogWarning("Suspicious patterns detected in request body from {IP} to {Path}", 
                            context.Connection.RemoteIpAddress, 
                            context.Request.Path);

                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        context.Response.ContentType = "application/json";
                        
                        var errorResponse = JsonSerializer.Serialize(new
                        {
                            success = false,
                            message = "Invalid input detected. Request contains potentially dangerous content."
                        });
                        
                        await context.Response.WriteAsync(errorResponse);
                        return;
                    }

                    // Parse JSON and validate specific fields
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        var root = doc.RootElement;

                        // Validate common authentication fields
                        if (root.TryGetProperty("username", out var username))
                        {
                            var usernameStr = username.GetString();
                            if (!string.IsNullOrEmpty(usernameStr) && !sanitizationService.IsValidUsername(usernameStr))
                            {
                                _logger.LogWarning("Invalid username format detected from {IP}", context.Connection.RemoteIpAddress);
                                
                                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                context.Response.ContentType = "application/json";
                                
                                var errorResponse = JsonSerializer.Serialize(new
                                {
                                    success = false,
                                    message = "Invalid username format. Only alphanumeric characters, underscores, hyphens, and dots are allowed."
                                });
                                
                                await context.Response.WriteAsync(errorResponse);
                                return;
                            }
                        }

                        // Validate email fields
                        if (root.TryGetProperty("email", out var email))
                        {
                            var emailStr = email.GetString();
                            if (!string.IsNullOrEmpty(emailStr) && !sanitizationService.IsValidEmail(emailStr))
                            {
                                _logger.LogWarning("Invalid email format detected from {IP}", context.Connection.RemoteIpAddress);
                                
                                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                context.Response.ContentType = "application/json";
                                
                                var errorResponse = JsonSerializer.Serialize(new
                                {
                                    success = false,
                                    message = "Invalid email format."
                                });
                                
                                await context.Response.WriteAsync(errorResponse);
                                return;
                            }
                        }

                        // Validate phone number fields
                        if (root.TryGetProperty("phoneNumber", out var phone))
                        {
                            var phoneStr = phone.GetString();
                            if (!string.IsNullOrEmpty(phoneStr) && !sanitizationService.IsValidPhoneNumber(phoneStr))
                            {
                                _logger.LogWarning("Invalid phone number format detected from {IP}", context.Connection.RemoteIpAddress);
                                
                                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                context.Response.ContentType = "application/json";
                                
                                var errorResponse = JsonSerializer.Serialize(new
                                {
                                    success = false,
                                    message = "Invalid phone number format."
                                });
                                
                                await context.Response.WriteAsync(errorResponse);
                                return;
                            }
                        }

                        // Validate client ID fields
                        if (root.TryGetProperty("clientId", out var clientId))
                        {
                            var clientIdStr = clientId.GetString();
                            if (!string.IsNullOrEmpty(clientIdStr) && sanitizationService.ContainsSuspiciousPatterns(clientIdStr))
                            {
                                _logger.LogWarning("Suspicious client ID detected from {IP}", context.Connection.RemoteIpAddress);
                                
                                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                context.Response.ContentType = "application/json";
                                
                                var errorResponse = JsonSerializer.Serialize(new
                                {
                                    success = false,
                                    message = "Invalid client ID format."
                                });
                                
                                await context.Response.WriteAsync(errorResponse);
                                return;
                            }
                        }

                        // Validate URL arrays (redirect URIs, etc.)
                        if (root.TryGetProperty("redirectUris", out var redirectUris) && redirectUris.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var uri in redirectUris.EnumerateArray())
                            {
                                var uriStr = uri.GetString();
                                if (!string.IsNullOrEmpty(uriStr) && !sanitizationService.IsValidUrl(uriStr))
                                {
                                    _logger.LogWarning("Invalid redirect URI detected from {IP}: {Uri}", 
                                        context.Connection.RemoteIpAddress, uriStr);
                                    
                                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                    context.Response.ContentType = "application/json";
                                    
                                    var errorResponse = JsonSerializer.Serialize(new
                                    {
                                        success = false,
                                        message = "Invalid redirect URI format."
                                    });
                                    
                                    await context.Response.WriteAsync(errorResponse);
                                    return;
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // If JSON parsing fails, let it continue to the controller where proper error handling occurs
                        _logger.LogDebug("JSON parsing failed in validation middleware, continuing to controller");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in input validation middleware");
                // Continue to next middleware even if validation fails
            }
        }

        // Validate query string parameters
        foreach (var param in context.Request.Query)
        {
            if (sanitizationService.ContainsSuspiciousPatterns(param.Value.ToString()))
            {
                _logger.LogWarning("Suspicious query parameter detected from {IP}: {Param}", 
                    context.Connection.RemoteIpAddress, param.Key);
                
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";
                
                var errorResponse = JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "Invalid query parameter detected."
                });
                
                await context.Response.WriteAsync(errorResponse);
                return;
            }
        }

        await _next(context);
    }
}
