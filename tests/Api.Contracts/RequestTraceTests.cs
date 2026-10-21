using BRU_AVTOPARK_AspireAPI.ApiService.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
        Console.WriteLine("PASS request trace: existing HttpContext identifier in log scope and error response header; status and body shape preserved");
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
