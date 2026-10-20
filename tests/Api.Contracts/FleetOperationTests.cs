using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.V2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.AdminServer.Controllers;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

internal static class FleetOperationTests
{
    public static async Task RunAsync()
    {
        var allowed = true;
        MutationResult? lastReceipt = null;
        var submissions = 0;
        var actor = Identity.FromHexString(new string('1', 64));
        var database = Stub.Create<ISpacetimeDBService>((method, _) =>
        {
            if (method.Name == "get_State") return DatabaseConnectionState.Ready;
            if (method.Name == "ReadAsync" && method.GetGenericArguments()[0] == typeof(UserAccessSnapshot))
                return Task.FromResult(new UserAccessSnapshot(true, false, [], allowed ? ["buses.create", "buses.edit", "buses.delete", "buses.view"] : []));
            if (method.Name == "ReadAsync" && method.GetGenericArguments()[0] == typeof(MutationResult)) return Task.FromResult(lastReceipt);
            throw new Exception("Unexpected fleet database call " + method.Name);
        });
        var buses = Stub.Create<IBusService>((method, args) =>
        {
            var (name, actorIndex, idIndex) = method.Name switch
            {
                "CreateBusAsync" => ("buses.create", 2, 3), "UpdateBusAsync" => ("buses.update", 3, 4),
                "DeleteBusAsync" => ("buses.delete", 1, 2), "SetActiveAsync" => ("buses.set-active", 2, 3),
                _ => throw new Exception("Unexpected fleet service call")
            };
            Assert((Identity)args![actorIndex]! == actor, "transport lost effective actor");
            submissions++;
            lastReceipt = new((string)args[idIndex]!, name, 7, 2, DateTimeOffset.UnixEpoch);
            return method.Name == "CreateBusAsync" ? Task.FromResult<Bus?>(null) : Task.FromResult(true);
        });
        var tokens = Stub.Create<IAuthWebSocketTokenValidator>((_, _) => Task.FromResult<Dictionary<string, object>?>(new() { ["identity"] = actor.ToString() }));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers();
        builder.Services.AddSingleton(database); builder.Services.AddSingleton(buses); builder.Services.AddSingleton(tokens);
        builder.Services.AddSingleton(Stub.Create<ITicketSalesService>());
        builder.Services.AddSingleton(Stub.Create<IAuthOrchestrationService>());
        builder.Services.AddSingleton<ConnectionAdmission>();
        builder.Services.AddScoped<ApplicationOperations>(); builder.Services.AddScoped<ApiSocketSession>();
        await using var app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/api/realtime/stream", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync(ApiProtocol.Subprotocol);
            await context.RequestServices.GetRequiredService<ApiSocketSession>().RunAsync(socket, "fleet-test", context.RequestAborted);
        });
        app.MapPost("/api/operations/{operation}", async context =>
        {
            var controller = new ApplicationOperationsController(context.RequestServices.GetRequiredService<ApplicationOperations>(), tokens)
                { ControllerContext = new ControllerContext { HttpContext = context } };
            var payload = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body, cancellationToken: context.RequestAborted);
            var result = await controller.Execute((string)context.Request.RouteValues["operation"]!, payload, context.RequestAborted);
            await result.ExecuteResultAsync(new ActionContext(context, new(), new()));
        });
        await app.StartAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient { BaseAddress = new Uri(origin) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-fleet-token");
            await using var socket = new RealtimeApplicationClient();
            await socket.ConnectAsync(new Uri(origin + "/api/"), "synthetic-fleet-token", deadline.Token);
            async Task Check(string name, object request)
            {
                var id = Guid.NewGuid().ToString("N");
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/operations/" + name);
                message.Headers.Add("Idempotency-Key", id);
                message.Content = JsonContent.Create(request, options: ApiProtocol.Json);
                using var response = await http.SendAsync(message, deadline.Token);
                response.EnsureSuccessStatusCode();
                var fromHttp = await response.Content.ReadFromJsonAsync<MutationResult>(ApiProtocol.Json, deadline.Token);
                var fromSocket = await socket.CallAsync<object, MutationResult>(name, request, id, deadline.Token);
                Assert(fromHttp == fromSocket && fromHttp?.OperationId == id && fromHttp.Operation == name, "fleet transport receipt mismatch");
            }
            await Check("buses.create", new CreateBusCommand("Synthetic", "A", new(Capacity: 40)));
            await Check("buses.update", new UpdateBusCommand(7, 1, "Changed"));
            await Check("buses.set-active", new SetBusActiveCommand(7, false, 1));
            await Check("buses.delete", new DeleteBusCommand(7, 1));
            var before = submissions;
            foreach (var (permission, model, expectedCode, status) in new[]
                { (false, "valid", "forbidden", HttpStatusCode.Forbidden), (true, " ", "validation_failed", HttpStatusCode.BadRequest) })
            {
                allowed = permission;
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/operations/buses.create");
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
                request.Content = JsonContent.Create(new CreateBusCommand(model), options: ApiProtocol.Json);
                using var response = await http.SendAsync(request, deadline.Token);
                Assert(response.StatusCode == status, "HTTP fleet error mapping differs");
                try { await socket.CallAsync<CreateBusCommand, MutationResult>("buses.create", new(model), Guid.NewGuid().ToString("N"), deadline.Token); throw new Exception("Expected fleet denial"); }
                catch (ApplicationCommandException ex) when (ex.Code == expectedCode) { }
            }
            Assert(submissions == before, "invalid or forbidden fleet operation reached persistence");
            Console.WriteLine("PASS fleet HTTP/WebSocket mutation parity, actor/operation IDs, permission revocation and validation before persistence");
        }
        finally { await app.StopAsync(); }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
