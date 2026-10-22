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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TicketSalesApp.AdminServer.Experimental.Services.Interfaces;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

internal static class QrHttpPipelineTests
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(FindRoot(), "BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:Secret"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)) }).Build();
        builder.Services.AddOptions<QrLoginOptions>().Bind(configuration.GetSection("Authentication:QrLogin"));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<QrLoginBroker>();
        builder.Services.AddSingleton(Stub.Create<IQrLoginDeviceStore>());
        builder.Services.AddSingleton(Stub.Create<IAuthWebSocketTokenValidator>());
        builder.Services.AddSingleton(Stub.Create<ISpacetimeDBService>());
        builder.Services.AddSingleton<UserAccessService>();
        builder.Services.AddSingleton(Stub.Create<IAuthOrchestrationService>());
        builder.Services.AddSingleton(Stub.Create<IFeatureFlagService>((_, _) => Task.FromResult(true)));
        builder.Services.AddControllers().AddApplicationPart(typeof(QrLoginController).Assembly);
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        await using var app = builder.Build();
        app.Use((context, next) => { context.Request.Scheme = "https"; return next(context); });
        app.UseRouting();
        app.UseCors();
        app.MapControllers();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            using var create = await http.PostAsync("/api/auth/qr/sessions", Json("{\"deviceName\":\"Desktop\",\"platform\":\"test\"}"));
            Check(create.StatusCode == HttpStatusCode.Created, "HTTP creation");
            Check(create.Headers.CacheControl?.NoStore == true, "no-store header");
            var session = JsonSerializer.Deserialize<QrCreateResponse>(await create.Content.ReadAsStringAsync(), ApiProtocol.Json)!;
            Check(Convert.FromBase64String(session.QrPng).Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG output");
            using var unknown = await http.PostAsync("/api/auth/qr/sessions", Json("{\"deviceName\":\"Desktop\",\"platform\":\"test\",\"unknown\":true}"));
            Check(unknown.StatusCode == HttpStatusCode.BadRequest && await unknown.Content.ReadAsStringAsync() == "{\"code\":\"invalid_request\"}", "unknown member error shape");
            Check(unknown.Headers.CacheControl?.NoStore == true, "invalid response no-store");
            using var missingSecret = await http.PostAsync($"/api/auth/qr/sessions/{session.SessionId}/status", Json("{}"));
            Check(missingSecret.StatusCode == HttpStatusCode.NotFound && await missingSecret.Content.ReadAsStringAsync() == "{\"code\":\"not_found\"}", "missing poll secret must be indistinguishable from unknown session");
            using var missingProof = await http.PostAsync($"/api/auth/qr/sessions/{session.SessionId}/complete", Json("{}"));
            Check(missingProof.StatusCode == HttpStatusCode.Unauthorized && await missingProof.Content.ReadAsStringAsync() == "{\"code\":\"qr_login_failed\"}", "missing proof must follow uniform failure path");
            using var malformed = await http.PostAsync("/api/auth/qr/sessions", Json("{"));
            Check(malformed.StatusCode == HttpStatusCode.BadRequest && await malformed.Content.ReadAsStringAsync() == "{\"code\":\"invalid_request\"}", "malformed JSON error shape");
            using var oversized = await http.PostAsync("/api/auth/qr/sessions", Json("{\"deviceName\":\"" + new string('x', 5000) + "\",\"platform\":\"test\"}"));
            Check(oversized.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest, "request size boundary");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/qr/sessions") { Content = Json("{\"deviceName\":\"Desktop\",\"platform\":\"test\"}") };
            request.Headers.Add("Origin", "https://evil.test");
            using var cors = await http.SendAsync(request);
            Check(!cors.Headers.Contains("Access-Control-Allow-Origin"), "DisableCors boundary");
        }
        finally { await app.StopAsync(); }
        Console.WriteLine("PASS QR HTTP pipeline: real appsettings binding, creation PNG/no-store, strict JSON, request size and CORS boundaries");
    }
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static void Check(bool value, string message) { if (!value) throw new Exception("QR pipeline: " + message); }
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "server"))) directory = directory.Parent;
        return directory?.FullName ?? throw new Exception("Repository root not found.");
    }
}
