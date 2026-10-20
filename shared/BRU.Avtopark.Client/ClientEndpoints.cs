namespace BRU.Avtopark.Client;

public static class ClientEndpoints
{
    // Existing OpenIddict registration. Changing this requires a coordinated client registration migration.
    public const string DefaultOAuthCallbackUri = "http://localhost:5000/callback";
    public static bool IsOAuthCallback(string candidate, string configured)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var actual) || !Uri.TryCreate(configured, UriKind.Absolute, out var expected)) return false;
        return actual.Scheme is "http" or "https" && actual.Scheme == expected.Scheme
            && actual.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase) && actual.Port == expected.Port
            && actual.AbsolutePath.Equals(expected.AbsolutePath, StringComparison.Ordinal)
            && actual.UserInfo.Length == 0 && actual.Fragment.Length == 0;
    }

    // The trusted origin is explicitly configured, never learned from discovery. Preserve the selected deployment prefix.
    public static Uri WindowsAuthenticationEndpoint(string apiBase, Uri trustedOrigin, bool linking)
    {
        if (trustedOrigin.Scheme != "https" || trustedOrigin.AbsolutePath != "/" || trustedOrigin.UserInfo.Length != 0
            || trustedOrigin.Query.Length != 0 || trustedOrigin.Fragment.Length != 0)
            throw new ArgumentException("Expected a trusted HTTPS origin.", nameof(trustedOrigin));
        var root = ServerRoot(apiBase);
        if (root.Scheme != "https" || !root.Host.Equals(trustedOrigin.Host, StringComparison.OrdinalIgnoreCase)
            || root.Port != trustedOrigin.Port)
            throw new ArgumentException("Selected API does not match the configured trusted Windows origin.", nameof(apiBase));
        return new Uri(root, linking ? "api/auth/windows/link" : "api/auth/windows");
    }

    public static Uri WebSocketEndpoint(Uri apiBase, string endpoint)
    {
        if (apiBase.Scheme != "https" && !(apiBase.Scheme == "http" && apiBase.IsLoopback))
            throw new ArgumentException("Configure an HTTPS server for realtime access. Plain HTTP is limited to loopback development.");
        var relative = endpoint.TrimStart('/');
        if (relative.Contains('\\') || relative.Contains('?') || relative.Contains('#') || !Uri.TryCreate(relative, UriKind.Relative, out _))
            throw new ArgumentException("Expected a relative endpoint path without query or fragment.", nameof(endpoint));
        var http = new Uri(ServerRoot(apiBase.AbsoluteUri), relative);
        return new UriBuilder(http) { Scheme = http.Scheme == "https" ? "wss" : "ws" }.Uri;
    }

    public static Uri ServerRoot(string apiBase)
    {
        var uri = new Uri(apiBase, UriKind.Absolute);
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Expected an HTTP(S) API base without credentials, query or fragment.", nameof(apiBase));
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return new UriBuilder(uri) { Path = path + "/" }.Uri;
    }
}
