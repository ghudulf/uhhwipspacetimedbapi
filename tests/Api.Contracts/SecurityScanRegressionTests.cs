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
        Console.WriteLine("PASS security scan regressions: OAuth client form mutations require verified Bearer administrator; forged form tokens and authenticated nonadministrators rejected before services");
    }

    private static async Task HttpAndLogBoundariesAsync()
    {
        Check(HttpSecurityPolicy.CookiePolicy(false) == CookieSecurePolicy.Always, "production cookie can be sent without TLS");
        var context = new DefaultHttpContext();
        var called = 0;
        Task Next(HttpContext _) { called++; return Task.CompletedTask; }
        await HttpSecurityPolicy.EnforceHttps(context, Next, false);
        Check(called == 0 && context.Response.StatusCode == 426 && context.Response.Headers.CacheControl == "no-store", "production HTTP reached endpoint");
        context.Request.Scheme = "https";
        await HttpSecurityPolicy.EnforceHttps(context, Next, false);
        Check(called == 1, "HTTPS endpoint was blocked");

        IConfiguration Configuration(string? origin = null) => new ConfigurationBuilder().AddInMemoryCollection(
            origin == null ? [] : new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = origin }).Build();
        Check(HttpSecurityPolicy.CorsOrigins(Configuration(), false).Length == 0, "unconfigured CORS allows arbitrary sites");
        Check(HttpSecurityPolicy.CorsOrigins(Configuration("https://portal.example.invalid/"), false).SequenceEqual(["https://portal.example.invalid"]), "explicit HTTPS CORS origin rejected");
        foreach (var origin in new[] { "*", "https://*.example.invalid", "http://portal.example.invalid", "https://name:password@example.invalid", "https://portal.example.invalid/path" })
        {
            try { HttpSecurityPolicy.CorsOrigins(Configuration(origin), false); throw new Exception("Unsafe CORS origin accepted"); }
            catch (InvalidOperationException) { }
        }
        Check(HttpSecurityPolicy.CorsOrigins(Configuration("http://localhost:5000"), true).Length == 1, "loopback Development CORS rejected");

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
        Console.WriteLine("PASS security HTTP/log boundaries: production HTTPS/Secure cookies, explicit CORS allowlist, single-record escaped JSON logs and private email/subject omission");
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
