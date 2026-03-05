using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Helper methods for working with OpenIddict application objects and OAuth-related utilities.
/// Extracted from AuthController helper methods.
/// </summary>
public sealed class OidcHelperService : IOidcHelperService
{
    private readonly ILogger<OidcHelperService> _logger;

    public OidcHelperService(ILogger<OidcHelperService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> GetClientIdAsync(object application)
    {
        var propertyInfo = application.GetType().GetProperty("ClientId");
        if (propertyInfo != null)
        {
            var value = propertyInfo.GetValue(application)?.ToString();
            return value ?? string.Empty;
        }
        return string.Empty;
    }

    /// <inheritdoc />
    public async Task<string> GetDisplayNameAsync(object application)
    {
        var propertyInfo = application.GetType().GetProperty("DisplayName");
        if (propertyInfo != null)
        {
            var value = propertyInfo.GetValue(application)?.ToString();
            return value ?? string.Empty;
        }
        return string.Empty;
    }

    /// <inheritdoc />
    public async Task<List<string>> GetRedirectUrisAsync(object application)
    {
        var result = new List<string>();
        try
        {
            var propertyInfo = application.GetType().GetProperty("RedirectUris");
            if (propertyInfo != null)
            {
                var value = propertyInfo.GetValue(application);
                if (value is IEnumerable<string> uris)
                {
                    result.AddRange(uris);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting redirect URIs from application object");
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<List<string>> GetPostLogoutRedirectUrisAsync(object application)
    {
        var result = new List<string>();
        try
        {
            var propertyInfo = application.GetType().GetProperty("PostLogoutRedirectUris");
            if (propertyInfo != null)
            {
                var value = propertyInfo.GetValue(application);
                if (value is IEnumerable<string> uris)
                {
                    result.AddRange(uris);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting post-logout redirect URIs from application object");
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<List<string>> GetPermissionsAsync(object application)
    {
        var result = new List<string>();
        try
        {
            var propertyInfo = application.GetType().GetProperty("Permissions");
            if (propertyInfo != null)
            {
                var value = propertyInfo.GetValue(application);
                if (value is IEnumerable<string> permissions)
                {
                    result.AddRange(permissions);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting permissions from application object");
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<string> GetConsentTypeAsync(object application)
    {
        try
        {
            var propertyInfo = application.GetType().GetProperty("ConsentType");
            if (propertyInfo != null)
            {
                var value = propertyInfo.GetValue(application)?.ToString();
                return value ?? "implicit";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting consent type from application object");
        }
        return "implicit";
    }

    /// <inheritdoc />
    public string[] SplitTextareaInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return Array.Empty<string>();

        return input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .Where(s => !string.IsNullOrEmpty(s))
                   .ToArray();
    }

    /// <inheritdoc />
    public string GetScopeIcon(string scope)
    {
        return scope.ToLower() switch
        {
            "openid" => "👤",
            "profile" => "📋",
            "email" => "📧",
            "phone" => "📱",
            "roles" => "🎭",
            "offline_access" => "🔄",
            "api" => "🔌",
            _ => "✓"
        };
    }

    /// <inheritdoc />
    public string FormatScope(string scope)
    {
        return scope.ToLower() switch
        {
            "openid" => "Basic access to your account information",
            "profile" => "Your profile information (username, display name)",
            "email" => "Your email address",
            "phone" => "Your phone number",
            "roles" => "Your role information and permissions",
            "offline_access" => "Access to your account even when you're not logged in (refresh tokens)",
            "api" => "Access to the BRU AVTOPARK API on your behalf",
            _ => $"Access to {scope}"
        };
    }

    /// <inheritdoc />
    public string GetNoun(int number, string one, string two, string five)
    {
        var mod100 = number % 100;
        var mod10 = number % 10;

        if (mod100 >= 11 && mod100 <= 19)
            return five;

        if (mod10 == 1)
            return one;

        if (mod10 >= 2 && mod10 <= 4)
            return two;

        return five;
    }
}

