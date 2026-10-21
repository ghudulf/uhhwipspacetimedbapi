using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Serilog.Formatting.Json;
using BRU.Avtopark.Contracts;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Security;

public static class HttpSecurityPolicy
{
    // JSON format escapes attacker-controlled text, template text and exception newlines;
    // a log event cannot manufacture a second physical record in any configured API sink.
    public static JsonFormatter LogFormatter() => new(renderMessage: true);

    public static PlaintextPolicy Mode(IConfiguration configuration)
    {
        var value = configuration["Network:PlaintextPolicy"];
        if (!NetworkPolicy.IsValidMode(value)) throw new InvalidOperationException("Network:PlaintextPolicy must be never, loopback, lan or any.");
        return NetworkPolicy.ParseMode(value);
    }

    public static bool IsTransportAllowed(HttpContext context, IConfiguration? configuration = null) =>
        context.Request.IsHttps || context.Connection.RemoteIpAddress is { } address && NetworkPolicy.IsPlaintextAllowed(address,
            configuration != null ? Mode(configuration) : context.RequestServices?.GetService<IConfiguration>() is { } configured ? Mode(configured) : PlaintextPolicy.Lan);

    public static CookieSecurePolicy CookiePolicy(PlaintextPolicy mode) =>
        mode == PlaintextPolicy.Never ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

    public static string[] CorsOrigins(IConfiguration configuration, PlaintextPolicy mode)
    {
        var values = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return values.Select(value =>
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                uri.AbsolutePath != "/" || uri.Host.Contains('*') ||
                !(uri.Scheme == "https" || uri.Scheme == "http" && NetworkPolicy.IsPlaintextAllowed(uri, mode)))
                throw new InvalidOperationException("Cors:AllowedOrigins must contain explicit HTTP(S) origins permitted by Network:PlaintextPolicy.");
            return uri.GetLeftPart(UriPartial.Authority);
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsCorsOriginAllowed(string origin, string[] configured, PlaintextPolicy mode)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" || uri.Host.Contains('*')) return false;
        return configured.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase) || NetworkPolicy.IsPlaintextAllowed(uri, mode);
    }

    // Called after trusted forwarded-header processing: peer address and Request.IsHttps are authoritative here.
    public static async Task EnforceHttps(HttpContext context, RequestDelegate next, PlaintextPolicy mode)
    {
        var probe = context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase) || context.Request.Path.Equals("/alive", StringComparison.OrdinalIgnoreCase);
        if (!context.Request.IsHttps && !probe && !(context.Connection.RemoteIpAddress is { } address && NetworkPolicy.IsPlaintextAllowed(address, mode)))
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            context.Response.Headers.CacheControl = "no-store";
            return;
        }
        await next(context);
    }
}
