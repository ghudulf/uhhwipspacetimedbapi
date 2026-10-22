using BRU_AVTOPARK_AspireAPI.ApiService.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketSalesApp.AdminServer.Configuration;

internal static class RoutingDiagnosticsTests
{
    public static async Task RunAsync()
    {
        var count = 0;
        var failure = new InvalidOperationException("synthetic downstream failure after commit");
        var middleware = new FeatureFlagRoutingMiddleware(_ => { count++; throw failure; },
            NullLogger<FeatureFlagRoutingMiddleware>.Instance, Options.Create(new FeatureFlagOptions()));
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/auth/login"; context.Request.Method = "POST";
        try { await middleware.InvokeAsync(context); throw new Exception("Downstream exception was swallowed"); }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure)) { }
        if (count != 1) throw new Exception("Routing diagnostics retried a downstream mutation");
        count = 0;
        middleware = new FeatureFlagRoutingMiddleware(_ => { count++; return Task.CompletedTask; },
            NullLogger<FeatureFlagRoutingMiddleware>.Instance, new BrokenOptions());
        await middleware.InvokeAsync(context);
        if (count != 1) throw new Exception("Diagnostic option failure prevented/duplicated request execution");
        Console.WriteLine("PASS routing diagnostics preserves downstream exception and executes request once despite diagnostic failure");
    }
    private sealed class BrokenOptions : IOptions<FeatureFlagOptions>
    {
        public FeatureFlagOptions Value => throw new InvalidOperationException("synthetic configuration failure");
    }
}
