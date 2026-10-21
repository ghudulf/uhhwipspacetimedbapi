using BRU_AVTOPARK_AspireAPI.ApiService.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

internal static class RequestTraceTests
{
    public static async Task RunAsync()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "existing-request-trace" };
        var capture = new Capture();
        context.Response.Body = new MemoryStream();
        var middleware = new RequestTraceMiddleware(async http =>
        {
            if (capture.Trace != http.TraceIdentifier) throw new Exception("Request log scope does not carry the existing trace");
            http.Response.StatusCode = 409;
            await http.Response.WriteAsJsonAsync(new { error = "existing-error-shape" });
        }, capture);
        await middleware.InvokeAsync(context);
        if (context.Response.Headers["Trace-Id"] != context.TraceIdentifier || context.Response.StatusCode != 409)
            throw new Exception("Error response lost its existing trace identifier or status");
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        if (!string.Equals(await reader.ReadToEndAsync(), "{\"error\":\"existing-error-shape\"}", StringComparison.Ordinal))
            throw new Exception("Trace middleware changed the response body contract");
        await ExceptionPipelineAsync();
        Console.WriteLine("PASS request trace: existing HttpContext identifier in log scope and error response header; status and body shape preserved");
    }

    private static async Task ExceptionPipelineAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        await using var app = builder.Build();
        app.Use((context, next) => { context.TraceIdentifier = "existing-pipeline-trace"; return next(); });
        app.UseMiddleware<RequestTraceMiddleware>(); app.UseExceptionHandler();
        app.MapGet("/failure", (HttpContext _) => Task.FromException(new InvalidOperationException("synthetic request failure")));
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient(); http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            using var response = await http.GetAsync(address + "/failure");
            using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if ((int)response.StatusCode != 500 || response.Headers.GetValues("Trace-Id").Single() != "existing-pipeline-trace" ||
                document.RootElement.GetProperty("traceId").GetString() != "existing-pipeline-trace")
                throw new Exception("Exception-handler response clearing lost request correlation");
        }
        finally { await app.StopAsync(); }
    }
    private sealed class Capture : ILogger<RequestTraceMiddleware>
    {
        public string? Trace;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        { Trace = ((IEnumerable<KeyValuePair<string, object>>)state).Single(p => p.Key == "TraceId").Value.ToString(); return null; }
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
