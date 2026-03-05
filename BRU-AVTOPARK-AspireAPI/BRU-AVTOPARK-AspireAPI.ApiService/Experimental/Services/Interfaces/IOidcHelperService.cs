namespace BRU_AVTOPARK.Services.Interfaces;

/// <summary>
/// Helper methods for working with OpenIddict application objects.
/// Replaces reflection-based helper methods from the original AuthController.
/// </summary>
public interface IOidcHelperService
{
    /// <summary>Extract the ClientId from an OpenIddict application object.</summary>
    Task<string> GetClientIdAsync(object application);

    /// <summary>Extract the DisplayName from an OpenIddict application object.</summary>
    Task<string> GetDisplayNameAsync(object application);

    /// <summary>Extract the RedirectUris from an OpenIddict application object.</summary>
    Task<List<string>> GetRedirectUrisAsync(object application);

    /// <summary>Extract the PostLogoutRedirectUris from an OpenIddict application object.</summary>
    Task<List<string>> GetPostLogoutRedirectUrisAsync(object application);

    /// <summary>Extract the Permissions from an OpenIddict application object.</summary>
    Task<List<string>> GetPermissionsAsync(object application);

    /// <summary>Extract the ConsentType from an OpenIddict application object.</summary>
    Task<string> GetConsentTypeAsync(object application);

    /// <summary>Split textarea input (newline-separated) into a string array.</summary>
    string[] SplitTextareaInput(string input);

    /// <summary>Get an icon SVG for a given OAuth scope.</summary>
    string GetScopeIcon(string scope);

    /// <summary>Format a scope name for display.</summary>
    string FormatScope(string scope);

    /// <summary>Get the correct form of a Russian noun based on number (for pluralization).</summary>
    string GetNoun(int number, string one, string two, string five);
}

