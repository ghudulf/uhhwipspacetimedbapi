using System.Net;
using System.Text;
using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

internal static class EntraHttpPipelineTests
{
    public static async Task RunAsync()
    {
        using var idp = new EntraAuthenticationTests.FakeIdp();
        var options = EntraAuthenticationTests.OptionsValue();
        var logs = new Capture();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); builder.Logging.AddProvider(logs);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IOptions<EntraOptions>>(Options.Create(options));
        builder.Services.AddSingleton<IOptions<ProviderFlowOptions>>(Options.Create(new ProviderFlowOptions()));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ProviderFlowBroker>();
        builder.Services.AddSingleton<IHttpClientFactory>(new EntraAuthenticationTests.Clients(idp));
        builder.Services.AddSingleton<EntraAuthenticationService>();
        builder.Services.AddSingleton(Stub.Create<IAuthWebSocketTokenValidator>());
        builder.Services.AddSingleton(Stub.Create<ISpacetimeDBService>());
        builder.Services.AddSingleton<UserAccessService>();
        builder.Services.AddSingleton(Stub.Create<IAuthOrchestrationService>());
        builder.Services.AddControllers().AddApplicationPart(typeof(EntraAuthenticationController).Assembly);
        builder.Services.AddCors(value => value.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        await using var app = builder.Build();
        app.Use((context, next) => { context.Request.Scheme = "https"; return next(context); });
        app.UseRouting(); app.UseCors(); app.MapControllers();
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            using var start = await http.PostAsync("/api/auth/entra/start", Json("{\"purpose\":\"login\"}"));
            Check(start.StatusCode == HttpStatusCode.Created && start.Headers.CacheControl?.NoStore == true, "start status/headers");
            var flow = JsonSerializer.Deserialize<EntraStartResponse>(await start.Content.ReadAsStringAsync(), ApiProtocol.Json)!;
            var query = QueryHelpers.ParseQuery(new Uri(flow.AuthorizationUrl).Query);
            idp.Nonce = query["nonce"]!; idp.Challenge = query["code_challenge"]!;
            using var callback = await http.GetAsync("/api/auth/entra/callback?code=synthetic-private-code&state=" + query["state"]);
            Check((await callback.Content.ReadAsStringAsync()).Contains("Вход выполнен", StringComparison.Ordinal) && callback.Headers.CacheControl?.NoStore == true &&
                callback.Headers.GetValues("Content-Security-Policy").Single().Contains("default-src 'none'", StringComparison.Ordinal) && callback.Headers.GetValues("Referrer-Policy").Single() == "no-referrer", "browser callback");
            foreach (var body in new[] { "{", "{\"purpose\":\"login\",\"actingUser\":\"attacker\"}" })
            {
                using var bad = await http.PostAsync("/api/auth/entra/start", Json(body));
                Check(bad.StatusCode == HttpStatusCode.BadRequest && await bad.Content.ReadAsStringAsync() == "{\"code\":\"invalid_request\"}" && bad.Headers.CacheControl?.NoStore == true, "strict JSON");
            }
            using var oversized = await http.PostAsync("/api/auth/entra/start", Json("{\"purpose\":\"" + new string('x', 5000) + "\"}"));
            Check(oversized.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, "4KiB body cap");
            using var missing = await http.PostAsync("/api/auth/entra/flows/unknown/complete", Json("{}"));
            Check(missing.StatusCode == HttpStatusCode.NotFound && await missing.Content.ReadAsStringAsync() == "{\"code\":\"not_found\"}", "missing secret response");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/entra/start") { Content = Json("{\"purpose\":\"login\"}") };
            request.Headers.Add("Origin", "https://evil.test");
            using var cors = await http.SendAsync(request);
            Check(!cors.Headers.Contains("Access-Control-Allow-Origin"), "CORS disabled");
            options.Enabled = false;
            foreach (var route in new[] { "start", "flows/id/complete", "flows/id/cancel", "link/status", "unlink" })
            {
                using var disabled = await http.PostAsync("/api/auth/entra/" + route, Json("{"));
                Check(disabled.StatusCode == HttpStatusCode.NotFound, "disabled malformed request bypass: " + route);
            }
            using var disabledCallback = await http.GetAsync("/api/auth/entra/callback?error=private-idp-error");
            Check(disabledCallback.StatusCode == HttpStatusCode.NotFound, "disabled callback");
            var text = string.Join('\n', logs.Messages);
            foreach (var secret in new[] { flow.FlowSecret, query["state"].ToString(), query["nonce"].ToString(), "synthetic-private-code", "private-idp-error", EntraAuthenticationTests.Subject, "synthetic@example.test" })
                Check(!text.Contains(secret, StringComparison.Ordinal), "secret in capture sink");
        }
        finally { await app.StopAsync(); }
        Console.WriteLine("PASS Entra HTTP pipeline: strict JSON/4KiB/CORS/no-store/CSP, disabled routes including malformed requests and log capture excludes codes/state/nonce/secrets/subjects/email");
    }
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
    private static void Check(bool value, string message) { if (!value) throw new Exception("Entra pipeline: " + message); }
    private sealed class Capture : ILoggerProvider
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Messages = new();
        public ILogger CreateLogger(string categoryName) => new Sink(this);
        public void Dispose() { }
        private sealed class Sink(Capture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => owner.Messages.Enqueue(formatter(state, exception));
        }
    }
}
