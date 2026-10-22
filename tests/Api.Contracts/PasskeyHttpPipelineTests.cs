using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using TicketSalesApp.AdminServer.Experimental.Services.Interfaces;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

internal static class PasskeyHttpPipelineTests
{
    public static async Task RunAsync()
    {
        var options = new PasskeyOptions { Enabled = true, PublicOrigin = PasskeyTests.Origin };
        var store = new PasskeyTests.Store();
        var logs = new Capture();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Debug); builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IOptions<PasskeyOptions>>(Options.Create(options));
        builder.Services.AddSingleton<IOptions<EntraOptions>>(Options.Create(new EntraOptions()));
        builder.Services.AddSingleton<IOptions<ProviderFlowOptions>>(Options.Create(new ProviderFlowOptions()));
        builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddSingleton<ProviderFlowBroker>();
        builder.Services.AddSingleton<IWebAuthnService>(PasskeyTests.Service(store));
        builder.Services.AddSingleton(store.Database); builder.Services.AddSingleton<UserAccessService>();
        builder.Services.AddSingleton(Stub.Create<IAuthWebSocketTokenValidator>());
        builder.Services.AddSingleton(Stub.Create<IAuthOrchestrationService>());
        builder.Services.AddSingleton(Stub.Create<IFeatureFlagService>((_, _) => Task.FromResult(false)));
        builder.Services.AddControllers().AddApplicationPart(typeof(PasskeyAuthenticationController).Assembly);
        builder.Services.AddCors(value => value.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        await using var app = builder.Build();
        app.Use((context, next) => { context.Request.Scheme = "https"; return next(context); }); app.UseRouting(); app.UseCors(); app.MapControllers();
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            using var capability = await http.GetAsync("/api/auth/providers");
            PasskeyTests.Check((await capability.Content.ReadAsStringAsync()).Contains("\"passkeys\":true", StringComparison.Ordinal) && capability.Headers.CacheControl?.NoStore == true, "enabled capability");
            using var start = await http.PostAsync("/api/auth/passkey/start", Json("{\"purpose\":\"login\"}"));
            PasskeyTests.Check(start.StatusCode == HttpStatusCode.Created && start.Headers.CacheControl?.NoStore == true, "HTTP start");
            var flow = JsonSerializer.Deserialize<PasskeyStartResponse>(await start.Content.ReadAsStringAsync(), ApiProtocol.Json)!;
            var query = QueryHelpers.ParseQuery(new Uri(flow.BrowserUrl).Query);
            using var page = await http.GetAsync("/api/auth/passkey/browser?f=" + flow.FlowId + "&b=" + query["b"]);
            var html = await page.Content.ReadAsStringAsync();
            var nonce = Regex.Match(html, "<script nonce=\"([A-Za-z0-9_-]+)\">").Groups[1].Value;
            PasskeyTests.Check(page.IsSuccessStatusCode && nonce.Length == 43 && page.Headers.GetValues("Content-Security-Policy").Single().Contains("script-src 'nonce-" + nonce + "'", StringComparison.Ordinal) &&
                page.Headers.GetValues("Content-Security-Policy").Single().Contains("frame-ancestors 'none'", StringComparison.Ordinal) && page.Headers.CacheControl?.NoStore == true && page.Headers.GetValues("Referrer-Policy").Single() == "no-referrer", "nonce/CSP/headers");
            PasskeyTests.Check(!Regex.IsMatch(html, "\\son[a-z]+=", RegexOptions.IgnoreCase) && html.Contains("history.replaceState", StringComparison.Ordinal) && !html.Contains(query["b"].ToString(), StringComparison.Ordinal) && !html.Contains(flow.FlowSecret, StringComparison.Ordinal), "script/page secret safety");
            using var unknown = await http.PostAsync("/api/auth/passkey/start", Json("{\"purpose\":\"login\",\"actingUser\":\"attacker\"}"));
            PasskeyTests.Check(unknown.StatusCode == HttpStatusCode.BadRequest && await unknown.Content.ReadAsStringAsync() == "{\"code\":\"invalid_request\"}", "strict request shape");
            using var corsRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/passkey/start") { Content = Json("{\"purpose\":\"login\"}") }; corsRequest.Headers.Add("Origin", "https://evil.test");
            using var cors = await http.SendAsync(corsRequest); PasskeyTests.Check(!cors.Headers.Contains("Access-Control-Allow-Origin"), "CORS disabled");
            options.Enabled = false;
            foreach (var route in new[] { "start", "flows/id/options", "flows/id/assert", "flows/id/complete", "flows/id/cancel" })
            {
                using var disabled = await http.PostAsync("/api/auth/passkey/" + route, Json("{"));
                PasskeyTests.Check(disabled.StatusCode == HttpStatusCode.NotFound, "disabled malformed route: " + route);
            }
            using var disabledPage = await http.GetAsync("/api/auth/passkey/browser?f=id&b=secret");
            PasskeyTests.Check(disabledPage.StatusCode == HttpStatusCode.NotFound, "disabled browser");
            foreach (var secret in new[] { flow.FlowSecret, query["b"].ToString(), Convert.ToBase64String(store.Handle), PasskeyTests.Actor.ToString() })
                PasskeyTests.Check(!string.Join('\n', logs.Messages).Contains(secret, StringComparison.Ordinal), "browser secret in Debug-level HTTP capture sink");
        }
        finally { await app.StopAsync(); }
        Console.WriteLine("PASS passkey real HTTP pipeline: enabled capability, no-store, CSP script nonce and frame/origin headers, no inline handlers/embedded secrets, history scrub, strict JSON, CORS and disabled malformed/browser routes");
    }
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
    private sealed class Capture : ILoggerProvider
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Messages = new();
        public ILogger CreateLogger(string categoryName) => new Sink(this);
        public void Dispose() { }
        private sealed class Sink(Capture capture) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => capture.Messages.Enqueue(formatter(state, exception));
        }
    }
}
