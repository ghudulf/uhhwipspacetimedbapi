using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BRU_AVTOPARK.Models.Requests;
using BRU_AVTOPARK_AspireAPI.ApiService.Controllers;
using BRU_AVTOPARK_AspireAPI.ApiService.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog.Parsing;
using System.Text.Json;
using BRU.Avtopark.Contracts;

internal static class SecurityScanRegressionTests
{
    public static async Task RunAsync()
    {
        // No service dependency may be touched when a form supplies its own forged admin token.
        // The verified Bearer principal, never ReadJwtToken on form data, grants administration.
        var controller = (AuthController)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AuthController));
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() };
        var forged = "eyJhbGciOiJub25lIn0.eyJyb2xlIjoiQWRtaW5pc3RyYXRvciIsImV4cCI6NDEwMjQ0NDgwMH0.";
        foreach (var name in new[] { nameof(AuthController.RegisterClientSubmit), nameof(AuthController.UpdateClientSubmit), nameof(AuthController.DeleteClientSubmit) })
        {
            var method = typeof(AuthController).GetMethod(name)!;
            var auth = method.GetCustomAttribute<AuthorizeAttribute>();
            Check(auth?.Roles == "Administrator" && auth.AuthenticationSchemes == "Bearer" && method.GetCustomAttribute<AllowAnonymousAttribute>() == null, name + " authentication metadata");
        }
        var register = new RegisterClientFormRequest { Token = forged, ClientId = "synthetic", ClientSecret = "synthetic", DisplayName = "Synthetic" };
        var update = new UpdateClientFormRequest { Token = forged, DisplayName = "Synthetic" };
        Check(await controller.RegisterClientSubmit(register) is UnauthorizedResult, "forged register form reached service");
        Check(await controller.UpdateClientSubmit("client", update) is UnauthorizedResult, "forged update form reached service");
        Check(await controller.DeleteClientSubmit("client", forged) is UnauthorizedResult, "forged delete form reached service");
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "User")], "Bearer"));
        Check(await controller.RegisterClientSubmit(register) is ForbidResult, "nonadministrator form reached service");
        Check(await controller.UpdateClientSubmit("client", update) is ForbidResult, "nonadministrator update reached service");
        Check(await controller.DeleteClientSubmit("client", forged) is ForbidResult, "nonadministrator delete reached service");
        await HttpAndLogBoundariesAsync();
        NetworkBoundaryTests.NativeRefresh();
        await TrafficLoggingTests.RunAsync();
        Console.WriteLine("PASS security scan regressions: OAuth client form mutations require verified Bearer administrator; forged form tokens and authenticated nonadministrators rejected before services");
    }

    private static async Task HttpAndLogBoundariesAsync()
    {
        foreach (var mode in Enum.GetValues<PlaintextPolicy>())
        {
            Check(HttpSecurityPolicy.CookiePolicy(mode) == (mode == PlaintextPolicy.Never ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest), "cookie policy " + mode);
            foreach (var peer in new[] { "127.0.0.1", "192.168.1.20", "203.0.113.9" })
            {
                var context = new DefaultHttpContext();
                context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(peer);
                var called = false;
                Task Next(HttpContext _) { called = true; return Task.CompletedTask; }
                await HttpSecurityPolicy.EnforceHttps(context, Next, mode);
                var allowed = NetworkPolicy.IsPlaintextAllowed(context.Connection.RemoteIpAddress, mode);
                Check(called == allowed && (allowed || context.Response.StatusCode == 426 && context.Response.Headers.CacheControl == "no-store"), "HTTP peer policy " + mode + peer);
                context.Request.Scheme = "https"; // ForwardedHeaders sets this for trusted X-Forwarded-Proto: https.
                await HttpSecurityPolicy.EnforceHttps(context, Next, mode);
                Check(called, "forwarded HTTPS blocked");
                foreach (var path in new[] { "/health", "/alive" })
                {
                    called = false; context.Request.Scheme = "http"; context.Request.Path = path;
                    await HttpSecurityPolicy.EnforceHttps(context, Next, mode);
                    Check(called, "health exemption blocked");
                }
            }
            foreach (var origin in new[] { "http://localhost:5000", "http://192.168.1.20:5000", "http://203.0.113.9", "https://192.168.1.20" })
                Check(HttpSecurityPolicy.IsCorsOriginAllowed(origin, [], mode) == NetworkPolicy.IsPlaintextAllowed(new Uri(origin), mode), "CORS mode " + mode + origin);
            Check(HttpSecurityPolicy.IsCorsOriginAllowed("https://portal.example.invalid", ["https://portal.example.invalid"], mode), "configured CORS rejected");
            Check(!HttpSecurityPolicy.IsCorsOriginAllowed("http://user:secret@localhost", [], mode), "credential CORS accepted");
        }
        // Exercise actual trusted forwarding, rather than trusting headers in the transport policy.
        foreach (var forwardedScheme in new[] { "https", "http" })
        {
            var proxied = new DefaultHttpContext();
            proxied.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            proxied.Request.Scheme = "http";
            proxied.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
            proxied.Request.Headers["X-Forwarded-Proto"] = forwardedScheme;
            var reached = false;
            var forwarding = new Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware(
                context => HttpSecurityPolicy.EnforceHttps(context, _ => { reached = true; return Task.CompletedTask; }, PlaintextPolicy.Lan),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
                Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
                { ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto }));
            await forwarding.Invoke(proxied);
            Check(proxied.Connection.RemoteIpAddress!.Equals(System.Net.IPAddress.Parse("203.0.113.9")), "trusted proxy did not establish peer");
            Check(reached == (forwardedScheme == "https"), "post-forwarding peer/scheme policy mismatch");
        }

        IConfiguration Configuration(string? origin = null) => new ConfigurationBuilder().AddInMemoryCollection(
            origin == null ? [] : new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = origin }).Build();
        Check(HttpSecurityPolicy.CorsOrigins(Configuration(), PlaintextPolicy.Lan).Length == 0, "configured CORS should be empty");
        Check(HttpSecurityPolicy.CorsOrigins(Configuration("https://portal.example.invalid/"), PlaintextPolicy.Never).SequenceEqual(["https://portal.example.invalid"]), "explicit HTTPS CORS origin rejected");
        foreach (var origin in new[] { "*", "https://*.example.invalid", "http://portal.example.invalid", "https://name:password@example.invalid", "https://portal.example.invalid/path" })
        {
            try { HttpSecurityPolicy.CorsOrigins(Configuration(origin), PlaintextPolicy.Lan); throw new Exception("Unsafe CORS origin accepted"); }
            catch (InvalidOperationException) { }
        }
        Check(HttpSecurityPolicy.CorsOrigins(Configuration("http://localhost:5000"), PlaintextPolicy.Loopback).Length == 1, "loopback CORS rejected");
        try { HttpSecurityPolicy.Mode(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Network:PlaintextPolicy"] = "typo" }).Build()); throw new Exception("Invalid policy accepted at startup"); }
        catch (InvalidOperationException) { }

        const string injected = "private-user@example.invalid\r\nFORGED RECORD\u001b[31m";
        var log = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Warning, new InvalidOperationException(injected),
            new MessageTemplateParser().Parse("Untrusted template\r\n{Input}"), [new LogEventProperty("Input", new ScalarValue(injected))]);
        using var output = new StringWriter();
        HttpSecurityPolicy.LogFormatter().Format(log, output);
        var line = output.ToString().TrimEnd('\r', '\n');
        Check(!line.Contains('\r') && !line.Contains('\n') && !line.Contains('\u001b'), "untrusted property/template/exception forged physical log record");
        using var parsed = JsonDocument.Parse(line);
        Check(parsed.RootElement.GetProperty("Properties").GetProperty("Input").GetString() == injected, "JSON log lost structured value");

        var capture = new Capture<BRU_AVTOPARK.Services.Implementations.InputSanitizationService>();
        var sanitizer = new BRU_AVTOPARK.Services.Implementations.InputSanitizationService(capture);
        sanitizer.SanitizeEmail(injected); sanitizer.IsValidEmail(injected);
        Check(!string.Join('\n', capture.Messages).Contains("private-user", StringComparison.Ordinal), "email sanitation leaked private input");
        var delivery = new Capture<TicketSalesApp.Services.Implementations.EmailService>();
        var mail = new TicketSalesApp.Services.Implementations.EmailService(Configuration(), delivery);
        try { await mail.SendEmailAsync(injected, injected, "body"); throw new Exception("Invalid recipient unexpectedly delivered"); }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { }
        Check(!string.Join('\n', delivery.Messages).Contains("private-user", StringComparison.Ordinal), "email delivery diagnostics leaked recipient/subject");
        Console.WriteLine("PASS security HTTP/log boundaries: four-mode peer/cookie/CORS policy, health exemptions, forwarded HTTPS, escaped JSON logs and private email/subject omission");
    }

    private sealed class Capture<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Security scan regression: " + message); }
}
