using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Web;
using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Implementation of input sanitization service providing defense-in-depth protection
/// against injection attacks, XSS, and other input-based vulnerabilities.
/// </summary>
public partial class InputSanitizationService : IInputSanitizationService
{
    private readonly ILogger<InputSanitizationService> _logger;

    // Compiled regex patterns for performance
    [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled)]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[\+]?[0-9\s\-\(\)]+$", RegexOptions.Compiled)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled)]
    private static partial Regex ClientIdPattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_\-]+$", RegexOptions.Compiled)]
    private static partial Regex TokenPattern();

    // Dangerous patterns that might indicate injection attempts
    [GeneratedRegex(@"(<script|javascript:|onerror=|onload=|eval\(|exec\(|<iframe|<object|<embed)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex XssPattern();

    [GeneratedRegex(@"(union\s+select|insert\s+into|delete\s+from|drop\s+table|update\s+set|--|;|\/\*|\*\/|xp_|sp_)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SqlInjectionPattern();

    [GeneratedRegex(@"(\$\{|\#\{|<%|%>|\{\{|\}\})", RegexOptions.Compiled)]
    private static partial Regex TemplateInjectionPattern();

    [GeneratedRegex(@"(\.\.\/|\.\.\\|%2e%2e|%252e)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PathTraversalPattern();

    public InputSanitizationService(ILogger<InputSanitizationService> logger)
    {
        _logger = logger;
    }

    public string SanitizeUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return string.Empty;
        }

        // Remove any characters that aren't alphanumeric, underscore, hyphen, or dot
        var sanitized = Regex.Replace(username.Trim(), @"[^a-zA-Z0-9_\-\.]", "");

        if (sanitized != username)
        {
            _logger.LogWarning("Username sanitized: original length {Original}, sanitized length {Sanitized}", 
                username.Length, sanitized.Length);
        }

        return sanitized;
    }

    public bool IsValidUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        if (username.Length < 2 || username.Length > 100)
        {
            return false;
        }

        if (!UsernamePattern().IsMatch(username))
        {
            _logger.LogWarning("Invalid username pattern detected: {Username}", username);
            return false;
        }

        if (ContainsSuspiciousPatterns(username))
        {
            _logger.LogWarning("Suspicious patterns detected in username: {Username}", username);
            return false;
        }

        return true;
    }

    public string SanitizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        // Remove whitespace and convert to lowercase
        var sanitized = email.Trim().ToLowerInvariant();

        // Remove any dangerous characters while preserving valid email format
        sanitized = Regex.Replace(sanitized, @"[^a-z0-9._%+\-@]", "");

        if (sanitized != email.Trim().ToLowerInvariant())
        {
            _logger.LogWarning("Email sanitized: original {Original}, sanitized {Sanitized}", 
                email, sanitized);
        }

        return sanitized;
    }

    public bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        if (email.Length > 254) // RFC 5321
        {
            return false;
        }

        if (!EmailPattern().IsMatch(email))
        {
            _logger.LogWarning("Invalid email pattern detected: {Email}", email);
            return false;
        }

        if (ContainsSuspiciousPatterns(email))
        {
            _logger.LogWarning("Suspicious patterns detected in email: {Email}", email);
            return false;
        }

        return true;
    }

    public string SanitizePhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return string.Empty;
        }

        // Keep only digits, +, spaces, hyphens, and parentheses
        var sanitized = Regex.Replace(phoneNumber.Trim(), @"[^0-9\+\s\-\(\)]", "");

        if (sanitized != phoneNumber.Trim())
        {
            _logger.LogWarning("Phone number sanitized: original {Original}, sanitized {Sanitized}", 
                phoneNumber, sanitized);
        }

        return sanitized;
    }

    public bool IsValidPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return false;
        }

        if (phoneNumber.Length > 20)
        {
            return false;
        }

        if (!PhonePattern().IsMatch(phoneNumber))
        {
            _logger.LogWarning("Invalid phone number pattern detected: {PhoneNumber}", phoneNumber);
            return false;
        }

        return true;
    }

    public string SanitizeClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return string.Empty;
        }

        // Remove any characters that aren't alphanumeric, underscore, hyphen, or dot
        var sanitized = Regex.Replace(clientId.Trim(), @"[^a-zA-Z0-9_\-\.]", "");

        if (sanitized != clientId.Trim())
        {
            _logger.LogWarning("Client ID sanitized: original {Original}, sanitized {Sanitized}", 
                clientId, sanitized);
        }

        return sanitized;
    }

    public bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (url.Length > 2048)
        {
            return false;
        }

        // Check for valid URI format
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _logger.LogWarning("Invalid URL format: {Url}", url);
            return false;
        }

        // Only allow http and https schemes
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning("Invalid URL scheme: {Scheme} in {Url}", uri.Scheme, url);
            return false;
        }

        // Check for suspicious patterns
        if (ContainsSuspiciousPatterns(url))
        {
            _logger.LogWarning("Suspicious patterns detected in URL: {Url}", url);
            return false;
        }

        // Check for path traversal attempts
        if (PathTraversalPattern().IsMatch(url))
        {
            _logger.LogWarning("Path traversal attempt detected in URL: {Url}", url);
            return false;
        }

        return true;
    }

    public string SanitizeDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        // Remove HTML tags
        var sanitized = Regex.Replace(displayName, @"<[^>]*>", "");

        // Remove script-related content
        sanitized = Regex.Replace(sanitized, @"(javascript:|onerror=|onload=)", "", RegexOptions.IgnoreCase);

        // Trim and limit length
        sanitized = sanitized.Trim();
        if (sanitized.Length > 200)
        {
            sanitized = sanitized.Substring(0, 200);
        }

        if (sanitized != displayName.Trim())
        {
            _logger.LogWarning("Display name sanitized: original length {Original}, sanitized length {Sanitized}", 
                displayName.Length, sanitized.Length);
        }

        return sanitized;
    }

    public bool ContainsSuspiciousPatterns(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        // Check for XSS patterns
        if (XssPattern().IsMatch(input))
        {
            _logger.LogWarning("XSS pattern detected in input");
            return true;
        }

        // Check for SQL injection patterns
        if (SqlInjectionPattern().IsMatch(input))
        {
            _logger.LogWarning("SQL injection pattern detected in input");
            return true;
        }

        // Check for template injection patterns
        if (TemplateInjectionPattern().IsMatch(input))
        {
            _logger.LogWarning("Template injection pattern detected in input");
            return true;
        }

        // Check for path traversal patterns
        if (PathTraversalPattern().IsMatch(input))
        {
            _logger.LogWarning("Path traversal pattern detected in input");
            return true;
        }

        // Check for null bytes
        if (input.Contains('\0'))
        {
            _logger.LogWarning("Null byte detected in input");
            return true;
        }

        return false;
    }

    public bool TrySanitize(string input, int maxLength, string allowedCharacters, [NotNullWhen(true)] out string? sanitized)
    {
        sanitized = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (input.Length > maxLength)
        {
            _logger.LogWarning("Input exceeds maximum length: {Length} > {MaxLength}", input.Length, maxLength);
            return false;
        }

        try
        {
            var pattern = new Regex(allowedCharacters, RegexOptions.Compiled);
            if (!pattern.IsMatch(input))
            {
                _logger.LogWarning("Input contains disallowed characters");
                return false;
            }

            if (ContainsSuspiciousPatterns(input))
            {
                _logger.LogWarning("Input contains suspicious patterns");
                return false;
            }

            sanitized = input.Trim();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during input sanitization");
            return false;
        }
    }

    public string HtmlEncode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return HttpUtility.HtmlEncode(input);
    }

    public bool IsValidToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (token.Length > 512)
        {
            return false;
        }

        if (!TokenPattern().IsMatch(token))
        {
            _logger.LogWarning("Invalid token pattern detected");
            return false;
        }

        return true;
    }
}
