using System.Net;
using System.Text;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog.Parsing;
using TicketSalesApp.Services.Diagnostics;

internal static class TrafficLoggingTests
{
    public static async Task RunAsync()
    {
        foreach (var level in new[] { "off", "redacted", "full" })
        {
            var logs = new Capture();
            var config = Config(level);
            var context = new DefaultHttpContext { TraceIdentifier = "traffic-trace" };
            var raw = """{"name":"Alice","access_token":"sentinel"}""";
            context.Request.Method = "POST"; context.Request.Path = "/data"; context.Request.QueryString = new("?code=sentinel&search=Alice");
            context.Request.ContentType = "application/json"; context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(raw));
            context.Request.Headers.Authorization = "Bearer sentinel";
            using var response = new MemoryStream(); context.Response.Body = response;
            var middleware = new TrafficLoggingMiddleware(async ctx =>
            {
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                Check(await reader.ReadToEndAsync() == raw, "request body consumed");
                ctx.Response.ContentType = "application/json"; ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync(raw);
            }, logs.Logger<TrafficLoggingMiddleware>(), config);
            await middleware.InvokeAsync(context);
            Check(Encoding.UTF8.GetString(response.ToArray()) == raw, "response bytes changed");
            var text = string.Join('\n', logs.Messages);
            Check(level == "off" ? text.Length == 0 : text.Contains("Alice") && text.Contains("traffic-trace") && text.Contains("HTTP failed"), "level / trace / warning contract");
            Check(level == "full" ? text.Contains("sentinel") : !text.Contains("sentinel"), "credential policy");
        }
        foreach (var type in new[] { "text/event-stream", "text/html", "application/octet-stream" })
        {
            var logs = new Capture(); var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
            await new TrafficLoggingMiddleware(async ctx => { ctx.Response.ContentType = type; await ctx.Response.WriteAsync("sentinel"); }, logs.Logger<TrafficLoggingMiddleware>(), Config("redacted")).InvokeAsync(context);
            Check(!string.Join('\n', logs.Messages).Contains("sentinel"), "stream/HTML/binary body captured");
        }
        foreach (var path in new[] { "/health", "/alive", "/metrics" })
        {
            var logs = new Capture(); var context = new DefaultHttpContext(); context.Request.Path = path;
            await new TrafficLoggingMiddleware(_ => Task.CompletedTask, logs.Logger<TrafficLoggingMiddleware>(), Config("full")).InvokeAsync(context);
            Check(logs.Messages.Count == 0, "health noise");
        }
        using (var output = new MemoryStream())
        using (var tee = new TrafficCaptureStream(output, 32))
        {
            await tee.WriteAsync(new byte[1024 * 1024]);
            Check(tee.BufferedBytes == 32 && tee.TotalBytes == 1024 * 1024 && output.Length == 1024 * 1024, "unbounded response capture");
            Check(TrafficDiagnostics.Body(Encoding.UTF8.GetBytes(new string('x', 32)), 100, "text/plain", new(MaxBodyBytes: 32)).Contains("…[truncated 68 bytes]"), "cap marker");
        }
        Check(TrafficDiagnostics.ServerOptions(Config("full")).Level == TrafficLevel.Full, "server config binding");
        var formatter = new EscapedTextFormatter(true);
        using (var output = new StringWriter())
        {
            var evt = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Debug, new Exception("failure\nnext"), new MessageTemplateParser().Parse("Body {Body}"),
                [new LogEventProperty("Body", new ScalarValue("Alice\r\nFORGED")), new("TraceId", new ScalarValue("trace")), new("SourceContext", new ScalarValue("Source"))]);
            formatter.Format(evt, output); formatter.Format(evt, output);
            var text = output.ToString();
            Check(text.Split(EscapedTextFormatter.UnsafeWarning).Length == 2 && text.Contains("[Debug] Source TraceId=trace") && text.Contains("\\n") && text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 3, "human formatter / one warning / forged record");
        }
        await HostedAsync();
        await ClientAsync();
        await OperationsAndReducersAsync();
        using (var output = new StringWriter())
        {
            var jsonFormatter = new TrafficJsonFormatter(true);
            var evt = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Debug, null, new MessageTemplateParser().Parse("Business {Name}"), [new("Name", new ScalarValue("Alice\r\nLine"))]);
            jsonFormatter.Format(evt, output); jsonFormatter.Format(evt, output);
            var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Check(lines.Length == 3, "JSONL unsafe warning count");
            foreach (var line in lines) { using var document = System.Text.Json.JsonDocument.Parse(line); Check(document.RootElement.TryGetProperty("MessageTemplate", out _), "invalid JSONL record"); }
        }
        Console.WriteLine("PASS traffic middleware: bodies/trace/headers/credentials, off/full/redacted, health/stream/HTML skips, byte-identical delivery, capped tee, config, readable escaped formatter and real hosted HTTP capture");
    }
    private static IConfiguration Config(string level) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Diagnostics:Traffic:Level"] = level, ["Diagnostics:Traffic:MaxBodyBytes"] = "256" }).Build();
    private static async Task ClientAsync()
    {
        var raw = """{"name":"Alice","access_token":"sentinel","nullable":null}""";
        using var handler = new TrafficLoggingHandler(new Reply(raw), new(MaxBodyBytes: 64));
        var lines = new List<string>(); handler.Logged += lines.Add;
        using var http = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.test/data?code=sentinel") { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new("Bearer", "sentinel");
        using var result = await http.SendAsync(request);
        Check(await result.Content.ReadAsStringAsync() == raw && await request.Content.ReadAsStringAsync() == raw, "client preview consumed content");
        var log = string.Join('\n', lines);
        Check(log.Contains("Alice") && log.Contains("nullable") && log.Contains("sha256:") && !log.Contains("sentinel"), "client raw JSON / headers / masking");
        using var largeHandler = new TrafficLoggingHandler(new Reply(new string('x', 10000)), new(MaxBodyBytes: 32));
        lines.Clear(); largeHandler.Logged += lines.Add;
        using var largeClient = new HttpClient(largeHandler);
        var large = await largeClient.GetStringAsync("https://api.test/data");
        Check(large.Length == 10000 && lines.Last().Contains("…[truncated 9968 bytes]"), "client cap / replay");
    }
    private sealed class Reply(string raw) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(raw, Encoding.UTF8, "application/json") });
    }
    private static async Task OperationsAndReducersAsync()
    {
        var logs = new Capture();
        var database = Stub.Create<TicketSalesApp.Services.Interfaces.ISpacetimeDBService>((_, _) =>
            Task.FromResult(new TicketSalesApp.Services.Authorization.UserAccessSnapshot(true, true, ["Administrator"], [])));
        var operations = new BRU_AVTOPARK_AspireAPI.ApiService.Application.ApplicationOperations(database,
            Stub.Create<TicketSalesApp.Services.Interfaces.ITicketSalesService>(), Stub.Create<TicketSalesApp.Services.Interfaces.IBusService>(),
            logs.Logger<BRU_AVTOPARK_AspireAPI.ApiService.Application.ApplicationOperations>());
        await operations.ExecuteAsync("session.access", SpacetimeDB.Identity.FromHexString(new string('1', 64)), System.Text.Json.JsonSerializer.SerializeToElement(new EmptyRequest()), null, default);
        Check(logs.Messages.Any(line => line.Contains("Result=") && line.Contains("Administrator") && line.Contains("Actor=")), "operation result JSON missing");
        var service = new TicketSalesApp.Services.Implementations.SpacetimeDBService(Config("redacted"), logs.Logger<TicketSalesApp.Services.Implementations.SpacetimeDBService>());
        try { await service.ExecuteReducerAsync(new SpacetimeDB.Types.Reducer.RotateRefreshToken("sentinel", "next-sentinel"), _ => true); }
        catch (TicketSalesApp.Services.Interfaces.DatabaseUnavailableException) { }
        var reducer = logs.Messages.Single(line => line.Contains("Reducer call"));
        Check(reducer.Contains("Arguments=") && reducer.Contains("***") && !reducer.Contains("sentinel"), "reducer hash argument leak");
    }
    private static async Task HostedAsync()
    {
        var logs = new Capture(); var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); builder.Logging.AddProvider(logs); builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostics:Traffic:Level"] = "redacted" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build(); app.UseMiddleware<RequestTraceMiddleware>(); app.UseMiddleware<TrafficLoggingMiddleware>();
        app.MapGet("/data", () => Results.Json(new { employee = "Alice", access_token = "sentinel" }));
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            using var result = await http.GetAsync("/data");
            Check((await result.Content.ReadAsStringAsync()).Contains("sentinel"), "hosted response changed");
            var lines = logs.Messages.Where(line => line.Contains("HTTP response")).ToArray();
            Check(lines.Length == 1 && lines[0].Contains("Alice") && !lines[0].Contains("sentinel") && lines[0].Contains(result.Headers.GetValues("Trace-Id").Single()), "hosted traffic missing / trace mismatch");
        }
        finally { await app.StopAsync(); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("TrafficLogging: " + message); }
    internal sealed class Capture : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Sink(this);
        public ILogger<T> Logger<T>() => new Typed<T>(this);
        public void Dispose() { }
        private sealed class Sink(Capture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter) { lock (owner.Messages) owner.Messages.Add(formatter(state, ex)); }
        }
        private sealed class Typed<T>(Capture owner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter) => owner.Messages.Add(formatter(state, ex));
        }
    }
}
