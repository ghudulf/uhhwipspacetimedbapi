namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Helpers;

/// <summary>
/// Provides utility methods for normalizing resource names used in realtime event subscriptions.
/// </summary>
public static class ResourceNormalization
{
    /// <summary>
    /// Normalizes a resource name by trimming whitespace and converting to lowercase.
    /// Returns "all" if the input is null, empty, or whitespace.
    /// </summary>
    /// <param name="resourceName">The resource name to normalize.</param>
    /// <returns>The normalized resource name, or "all" if the input is null/empty/whitespace.</returns>
    public static string Normalize(string? resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return "all";
        }

        return resourceName.Trim().ToLowerInvariant();
    }
}
