using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Serilog.Formatting.Json;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Security;

public static class HttpSecurityPolicy
{
    // JSON format escapes attacker-controlled text, template text and exception newlines;
    // a log event cannot manufacture a second physical record in any configured API sink.
    public static JsonFormatter LogFormatter() => new(renderMessage: true);

    public static CookieSecurePolicy CookiePolicy(bool development) =>
        development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

    public static string[] CorsOrigins(IConfiguration configuration, bool development)
    {
        var values = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return values.Select(value =>
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                uri.AbsolutePath != "/" || uri.Host.Contains('*') ||
                !(uri.Scheme == "https" || development && uri.Scheme == "http" && uri.IsLoopback))
                throw new InvalidOperationException("Cors:AllowedOrigins must contain explicit HTTPS origins (HTTP loopback only in Development).");
            return uri.GetLeftPart(UriPartial.Authority);
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Orchestrator probes and genuinely local, unproxied clients (a loopback peer that carries no forwarding headers)
    // cannot be a remote plaintext client, so they stay reachable; every other plaintext request is refused.
    private static bool IsLocalProbe(HttpContext context)
    {
        var path = context.Request.Path;
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) || path.Equals("/alive", StringComparison.OrdinalIgnoreCase)) return true;
        return context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address)
            && !context.Request.Headers.ContainsKey("X-Forwarded-For") && !context.Request.Headers.ContainsKey("X-Forwarded-Proto")
            && !context.Request.Headers.ContainsKey("Forwarded");
    }

    public static async Task EnforceHttps(HttpContext context, RequestDelegate next, bool development)
    {
        if (!development && !context.Request.IsHttps && !IsLocalProbe(context))
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            context.Response.Headers.CacheControl = "no-store";
            return;
        }
        await next(context);
    }
}
