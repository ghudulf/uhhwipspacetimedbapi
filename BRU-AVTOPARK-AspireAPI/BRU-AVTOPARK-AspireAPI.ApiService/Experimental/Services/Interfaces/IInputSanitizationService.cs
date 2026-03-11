using System.Diagnostics.CodeAnalysis;

namespace BRU_AVTOPARK.Services.Interfaces;

/// <summary>
/// Service for sanitizing and validating user inputs to prevent injection attacks.
/// Provides defense-in-depth protection beyond model validation.
/// </summary>
public interface IInputSanitizationService
{
    /// <summary>
    /// Sanitizes a username by removing potentially dangerous characters.
    /// Allows: alphanumeric, underscore, hyphen, dot
    /// </summary>
    string SanitizeUsername(string username);

    /// <summary>
    /// Validates that a username contains only safe characters.
    /// Returns true if valid, false otherwise.
    /// </summary>
    bool IsValidUsername(string username);

    /// <summary>
    /// Sanitizes an email address by removing dangerous characters while preserving valid email format.
    /// </summary>
    string SanitizeEmail(string email);

    /// <summary>
    /// Validates that an email address is safe and properly formatted.
    /// </summary>
    bool IsValidEmail(string email);

    /// <summary>
    /// Sanitizes a phone number by removing all non-numeric characters except + and spaces.
    /// </summary>
    string SanitizePhoneNumber(string phoneNumber);

    /// <summary>
    /// Validates that a phone number contains only safe characters.
    /// </summary>
    bool IsValidPhoneNumber(string phoneNumber);

    /// <summary>
    /// Sanitizes a client ID for OAuth by removing dangerous characters.
    /// </summary>
    string SanitizeClientId(string clientId);

    /// <summary>
    /// Validates a URL to ensure it's properly formatted and doesn't contain injection attempts.
    /// </summary>
    bool IsValidUrl(string url);

    /// <summary>
    /// Sanitizes a display name by removing HTML/script tags and dangerous characters.
    /// </summary>
    string SanitizeDisplayName(string displayName);

    /// <summary>
    /// Detects potential injection patterns in input strings.
    /// Returns true if suspicious patterns are found.
    /// </summary>
    bool ContainsSuspiciousPatterns(string input);

    /// <summary>
    /// Validates and sanitizes a string input with custom rules.
    /// </summary>
    /// <param name="input">The input string to sanitize</param>
    /// <param name="maxLength">Maximum allowed length</param>
    /// <param name="allowedCharacters">Regex pattern of allowed characters</param>
    /// <param name="sanitized">The sanitized output</param>
    /// <returns>True if input is valid after sanitization</returns>
    bool TrySanitize(string input, int maxLength, string allowedCharacters, [NotNullWhen(true)] out string? sanitized);

    /// <summary>
    /// Encodes HTML special characters to prevent XSS attacks.
    /// </summary>
    string HtmlEncode(string input);

    /// <summary>
    /// Validates that a token string contains only safe characters (alphanumeric, hyphens, underscores).
    /// </summary>
    bool IsValidToken(string token);
}
