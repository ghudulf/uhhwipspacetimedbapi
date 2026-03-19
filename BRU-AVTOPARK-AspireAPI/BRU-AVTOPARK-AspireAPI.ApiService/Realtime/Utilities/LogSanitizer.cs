namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Utilities;

/// <summary>
/// Provides utility methods for sanitizing strings before logging to prevent log injection attacks.
/// </summary>
internal static class LogSanitizer
{
    /// <summary>
    /// Sanitizes a string for safe logging by removing control characters to prevent log injection.
    /// </summary>
    /// <param name="value">The string to sanitize.</param>
    /// <returns>A sanitized string safe for logging. Returns <see cref="string.Empty"/> when the input is null or empty.</returns>
    public static string SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return new string(value.Where(c => !char.IsControl(c)).ToArray());
    }

    /// <summary>
    /// Sanitizes and truncates a log field to prevent log injection and excessive log growth.
    /// </summary>
    /// <param name="value">The value to sanitize.</param>
    /// <param name="maxLength">Maximum length to truncate to.</param>
    /// <returns>A sanitized and truncated string safe for logging.</returns>
    public static string SanitizeLogField(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Remove control characters to prevent log injection
        var sanitized = new string(value
            .Where(c => !char.IsControl(c))
            .ToArray());

        // Guard against negative length in AsSpan and Substring
        var clampedLength = Math.Max(maxLength, 0);
        
        // If value is already within bounds, return it unchanged
        if (sanitized.Length <= clampedLength)
        {
            return sanitized;
        }
        
        if (clampedLength <= 3)
        {
            return "...".Substring(0, Math.Min(clampedLength, 3));
        }

        // Truncate if needed - ensure output never exceeds maxLength
        return string.Concat(sanitized.AsSpan(0, clampedLength - 3), "...");
    }
}