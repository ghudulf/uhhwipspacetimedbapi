using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Hubs;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SpacetimeDB;
using TicketSalesApp.AdminServer.Configuration;
using TicketSalesApp.AdminServer.Controllers;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class ReviewRegressionTests
{
    public static async Task RunAsync()
    {
        await MutationOnlySessionAsync();
        await PublishGapAsync();
        await OperationsAsync();
        await AuthorizationAsync();
        ConfigurationAndAmounts();
    }

    private static async Task MutationOnlySessionAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.UseWebSockets();
        Task<object> Handle(RealtimeCrudRequest request, CancellationToken _) => Task.FromResult<object>(new { committed = request.Command == "create" });
        app.MapGet("/mutation-only", async context =>
        {
            await WebSocketEventStreamWriter.StreamCrudSessionAsync(context, Empty(), Handle, NullLogger.Instance, context.RequestAborted);
        });
        await app.StartAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol("bru.events.v1");
            await socket.ConnectAsync(new Uri(origin.Replace("http:", "ws:") + "/mutation-only"), deadline.Token);
            await Task.Delay(100, deadline.Token); // Let the empty event pump finish before the command arrives.
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"command\":\"create\",\"requestId\":\"synthetic\"}"), WebSocketMessageType.Text, true, deadline.Token);
            var buffer = new byte[4096];
            var response = await socket.ReceiveAsync(buffer, deadline.Token);
            using var json = JsonDocument.Parse(buffer.AsMemory(0, response.Count));
            Assert(json.RootElement.GetProperty("ok").GetBoolean(), "normal event completion cancelled mutation-only session");
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", deadline.Token);
        }
        finally { await app.StopAsync(); }
        Console.WriteLine("PASS v1 mutation-only socket remains usable after normal event-sequence completion");
        static async IAsyncEnumerable<ApiDomainEvent> Empty() { await Task.CompletedTask; yield break; }
    }

    private static async Task PublishGapAsync()
    {
        var sent = new ConcurrentBag<(string Group, ApiDomainEvent Event)>();
        var clients = Stub.Create<IHubClients>((method, args) => method.Name == "Group"
            ? Stub.Create<IClientProxy>((_, values) =>
            {
                sent.Add(((string)args![0]!, (ApiDomainEvent)((object?[])values![1]!)[0]!));
                return Task.CompletedTask;
            }) : throw new Exception("Unexpected hub access"));
        var hub = Stub.Create<IHubContext<SystemEventsHub>>((method, _) => method.Name == "get_Clients" ? clients : throw new Exception("Unexpected hub dependency"));
        await using var bus = new SignalRRealtimeEventBus(hub, Options.Create(new RealtimeEventOptions { BufferCapacity = 1, PublishTimeoutMs = 10 }), NullLogger<SignalRRealtimeEventBus>.Instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var subscription = bus.SubscribeAsync("buses", deadline.Token).GetAsyncEnumerator(deadline.Token);
        var first = subscription.MoveNextAsync().AsTask();
        ApiDomainEvent Event(string name) => new(name, "buses", "POST", 201, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), null, null, null, "local", new Dictionary<string, string>());
        await bus.PublishAsync(Event("first"), deadline.Token);
        try { await bus.PublishAsync(Event("timed-out"), deadline.Token); throw new Exception("tiny channel did not apply backpressure"); }
        catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
        Assert(bus.GapCount == 1 && bus.GetRecentEvents().Any(e => e.EventName == "timed-out"), "committed event lost from history or gap not recorded");
        await bus.StartAsync(deadline.Token);
        Assert(await first && subscription.Current.EventName == "first", "queued event lost");
        Assert(await subscription.MoveNextAsync() && subscription.Current.EventName == "system.resync_required", "resource subscriber missed gap marker");
        while (!sent.Any(e => e.Group == "system-events" && e.Event.EventName == "system.resync_required")
            || !sent.Any(e => e.Group == "resource:buses" && e.Event.EventName == "system.resync_required")) await Task.Delay(1, deadline.Token);
        await bus.StopAsync(deadline.Token);
        Console.WriteLine("PASS bounded event publish preserves history and signals timed-out delivery gaps locally and through SignalR");
    }

    private static async Task OperationsAsync()
    {
        var actor = Identity.FromHexString(new string('1', 64));
        Exception? accessFailure = null;
        var salesCalls = 0;
        string? buyerName = null;
        string? buyerPhone = null;
        var database = Stub.Create<ISpacetimeDBService>((method, _) =>
        {
            if (method.Name == "ReadAsync" && method.GetGenericArguments()[0] == typeof(UserAccessSnapshot))
                return accessFailure == null ? Task.FromResult(new UserAccessSnapshot(true, false, [], ["buses.edit", "sales.create", "sales.edit", "jobs.view"]))
                    : Task.FromException<UserAccessSnapshot>(accessFailure);
            if (method.Name == "ReadAsync" && method.GetGenericArguments()[0] == typeof(JobRow)) return Task.FromException<JobRow>(new OperationException("not_found"));
            if (method.Name == "ReadAsync" && method.GetGenericArguments()[0] == typeof(Identity)) return Task.FromResult(actor);
            throw new Exception("Invalid operation reached persistence");
        });
        var sales = Stub.Create<ITicketSalesService>((method, args) =>
        {
            salesCalls++;
            buyerName = (string?)args![1];
            buyerPhone = (string?)args[2];
            return method.Name switch
            {
                "CreateSaleAsync" => Task.FromException<uint?>(new SalesServiceReachedException()),
                "UpdateSaleAsync" => Task.FromException<bool>(new SalesServiceReachedException()),
                _ => throw new Exception("Unexpected sale service call")
            };
        });
        var operations = new ApplicationOperations(database, sales, Stub.Create<IBusService>(), NullLogger<ApplicationOperations>.Instance);
        async Task Invalid(string name, object payload)
        {
            try { await operations.ExecuteAsync(name, actor, JsonSerializer.SerializeToElement(payload, ApiProtocol.Json), Guid.NewGuid().ToString("N"), default); throw new Exception("Invalid input accepted"); }
            catch (OperationException ex) when (ex.Code == "validation_failed") { }
        }
        await Invalid("buses.update", new UpdateBusCommand(1, 0));
        await Invalid("buses.update", new UpdateBusCommand(1, 0, Details: new()));
        foreach (var phone in new string?[] { null, " " })
            await Invalid("sales.create", new CreateSaleCommand(1, "Buyer", phone));
        await Invalid("sales.update", new UpdateSaleCommand(1, 0, BuyerPhone: " "));
        foreach (var name in new string?[] { null, "", " " })
        {
            await Invalid("sales.create", new CreateSaleCommand(1, name, "123"));
            if (name != null) await Invalid("sales.update", new UpdateSaleCommand(1, 0, BuyerName: name));
        }
        Assert(salesCalls == 0, "invalid buyer fields reached sale service");
        foreach (var (operation, payload) in new (string, object)[]
            { ("sales.create", new CreateSaleCommand(1, "Buyer", "")), ("sales.update", new UpdateSaleCommand(1, 0, BuyerPhone: "")) })
        {
            try { await operations.ExecuteAsync(operation, actor, JsonSerializer.SerializeToElement(payload, ApiProtocol.Json), Guid.NewGuid().ToString("N"), default); throw new Exception("sale service was not reached"); }
            catch (SalesServiceReachedException) { }
            Assert(buyerPhone == "", "empty phone was rejected or changed");
        }
        Assert(salesCalls == 2, "empty create/update phone did not pass validation");
        var tokens = Stub.Create<IAuthWebSocketTokenValidator>((_, _) => Task.FromResult<Dictionary<string, object>?>(new() { ["identity"] = actor.ToString() }));
        var controller = new ApplicationOperationsController(operations, tokens) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Headers.Authorization = "Bearer synthetic";
        accessFailure = new TimeoutException();
        Assert(await controller.Execute("jobs.get", JsonSerializer.SerializeToElement(new EntityRequest(1)), default) is ObjectResult { StatusCode: 504 }, "timeout did not map to 504");
        accessFailure = null;
        Assert(await controller.Execute("jobs.get", JsonSerializer.SerializeToElement(new EntityRequest(1)), default) is ObjectResult { StatusCode: 404 }, "not_found did not map to 404");
        var services = new ServiceCollection().AddSingleton(database).AddSingleton(operations).AddSingleton<UserAccessService>().BuildServiceProvider();
        var saleController = new TicketSalesController(database, sales, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Stub.Create<IRealtimeEventBus>(), NullLogger<TicketSalesController>.Instance)
            { ControllerContext = new() { HttpContext = new DefaultHttpContext { RequestServices = services } } };
        saleController.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("identity", actor.ToString())], "synthetic"));
        var omittedBuyer = JsonSerializer.Deserialize<CreateTicketSaleModel>("""{"ticketId":1,"saleDate":"2026-10-20T12:00:00Z"}""", ApiProtocol.Json)!;
        Assert(omittedBuyer.TicketSoldToUserPhone == null, "HTTP default test supplied a phone");
        omittedBuyer.TicketSoldToUser = null;
        try { await saleController.CreateTicketSale(omittedBuyer); throw new Exception("HTTP create did not reach sale service"); }
        catch (SalesServiceReachedException) { }
        Assert(buyerName == "ФИЗ.ПРОДАЖА" && buyerPhone == "", "legacy HTTP buyer defaults were not forwarded into the sale command");
        Console.WriteLine("PASS buyer validation distinguishes missing/blank from empty phone and preserves legacy HTTP POS defaults");
        Assert(await saleController.UpdateTicketSale(1, new UpdateTicketSaleModel { SaleDate = DateTime.UtcNow }) is BadRequestObjectResult,
            "non-null sale date was silently ignored");
        var session = new SessionController(operations) { ControllerContext = saleController.ControllerContext };
        accessFailure = new DatabaseUnavailableException("synthetic outage");
        Assert(await session.Access() is ObjectResult { StatusCode: 503 }, "session database outage did not map to 503");
        accessFailure = new TimeoutException();
        Assert(await session.Access() is ObjectResult { StatusCode: 504 }, "session timeout did not map to 504");
        accessFailure = null;
        foreach (var price in new[] { double.NaN, double.PositiveInfinity, -1, 1e12 + 1 })
        {
            try { await new TicketService(database, NullLogger<TicketService>.Instance).CreateTicketAsync(1, 1, price, "Cash", 0, actor); throw new Exception("invalid ticket price accepted"); }
            catch (ArgumentException) { }
        }
        Console.WriteLine("PASS empty fleet updates, malformed sale buyers, immutable sale date, ticket price bounds and timeout/not-found HTTP mappings reject before persistence");
    }

    private static async Task AuthorizationAsync()
    {
        var actor = Identity.FromHexString(new string('2', 64));
        var administrator = true;
        var database = Stub.Create<ISpacetimeDBService>((_, _) => Task.FromResult(new UserAccessSnapshot(true, administrator, [], [])));
        using var services = new ServiceCollection().AddSingleton(database).AddSingleton<UserAccessService>()
            .AddSingleton<IOptions<JwtSettings>>(Options.Create(new JwtSettings { Secret = new string('x', 32), ValidateIssuer = false, ValidateAudience = false })).BuildServiceProvider();
        var controller = new Probe { ControllerContext = new() { HttpContext = new DefaultHttpContext { RequestServices = services } } };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("identity", actor.ToString())], "synthetic"));
        await controller.Command("users", "create");
        try { await controller.Command("users", "unknown"); throw new Exception("admin bypass skipped command validation"); }
        catch (InvalidOperationException) { }
        await controller.Command("schedules", "next_page");
        var id = Guid.NewGuid();
        foreach (var format in new[] { "D", "N", "B", "P" })
            Assert(Probe.Operation(new("create", id.ToString(format), null, null)) == id.ToString("N"), "v1 request ID normalization failed");
        try { Probe.Operation(new("create", "not-a-guid", null, null)); throw new Exception("unstable operation ID accepted"); }
        catch (ArgumentException) { }
        controller.Request.Headers["Idempotency-Key"] = id.ToString("B");
        Assert(controller.HttpOperation() == id.ToString("N") && controller.Response.Headers["Operation-Id"] == id.ToString("N"), "HTTP effective receipt key not echoed");
        controller.Request.Headers["Idempotency-Key"] = "not-a-guid";
        try { controller.HttpOperation(); throw new Exception("invalid HTTP operation ID accepted"); }
        catch (OperationException ex) when (ex.Code == "validation_failed")
        {
            var context = new Microsoft.AspNetCore.Mvc.Filters.ExceptionContext(new ActionContext(controller.HttpContext, new(), new()), []) { Exception = ex };
            new ApplicationErrorFilterAttribute().OnException(context);
            Assert(context.ExceptionHandled && context.Result is ObjectResult { StatusCode: 400 }, "invalid HTTP operation ID was not mapped to 400");
        }
        var jwt = new JwtSecurityToken(claims: [new Claim("identity", actor.ToString())], expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 32))), SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        controller.Request.QueryString = new QueryString("?access_token=" + token);
        Assert(controller.Token() == null, "HTTP query token accepted");
        controller.HttpContext.Features.Set<IHttpWebSocketFeature>(new SocketFeature());
        Assert(controller.Token() == token && await controller.Actor() == actor, "WebSocket query actor not resolved");
        await controller.Command("buses", "create");
        administrator = false;
        try { await controller.Command("buses", "create"); throw new Exception("revoked authorization retained"); }
        catch (UnauthorizedAccessException) { }
        Console.WriteLine("PASS shared v1 admin/command guards, WebSocket-only query credentials and normalized stable operation IDs with HTTP receipt headers");
    }

    private static void ConfigurationAndAmounts()
    {
        var requiresStable = typeof(BRU_AVTOPARK_AspireAPI.ApiService.Controllers.RealtimeController)
            .GetMethod("RequiresStableOperation", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var resource in new[] { "buses", "employees", "jobs", "maintenance", "routes", "ticket-sales", "ticket_sales", "ticketsales" })
            Assert((bool)requiresStable.Invoke(null, [resource])!, "receipt-backed resource lost stable-ID requirement");
        foreach (var resource in new[] { "permissions", "roles", "route-schedules", "routeschedules", "route_schedules", "tickets", "users" })
            Assert(!(bool)requiresStable.Invoke(null, [resource])!, "legacy resource gained an unsupported stable-ID requirement");
        var properties = typeof(FeatureFlagOptions).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var category in new[] { "OAuth", "WebAuthn" })
            Assert(properties.Where(p => p.StartsWith(category, StringComparison.Ordinal) && p.EndsWith("EndpointEnabled", StringComparison.Ordinal)).ToHashSet()
                .SetEquals(EndpointAvailability.FlagsForCategory(category)), "bulk availability category incomplete");
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(root, "BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Experimental/Views/Admin/FeatureFlags.cshtml"));
        foreach (Match key in Regex.Matches(page, @"\b\w+EndpointEnabled\b")) Assert(properties.Contains(key.Value), "page references unknown availability flag");
        foreach (var category in new[] { "OAuth", "WebAuthn" }) Assert(page.Contains($"FlagsForCategory(\"{category}\")"), "page does not use complete derived category");
        var request = new BRU_AVTOPARK.Models.Requests.RegisterRequest { Username = "synthetic", Password = "synthetic-password", Role = 6 };
        Assert(Validator.TryValidateObject(request, new(request), [], true) && AuthenticationService.RegistrationRoleName(6) == "Conductor", "Conductor validation and mapping disagree");
        var registration = File.ReadAllText(Path.Combine(root, "BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Experimental/Views/Auth/Register.cshtml"));
        foreach (Match option in Regex.Matches(registration, "<option value=\"([0-6])\">([^<]+)</option>"))
        {
            var role = int.Parse(option.Groups[1].Value);
            var candidate = request with { Role = role };
            Assert(AuthenticationService.RegistrationRoleName(role) == option.Groups[2].Value
                && Validator.TryValidateObject(candidate, new(candidate), [], true), "registration option, validator and role mapping disagree");
        }
        Assert(SalePresentation.RecordedAmount(double.NaN) == null && SalePresentation.RecordedAmount(double.MaxValue) == null
            && SalePresentation.RecordedAmount(double.NegativeInfinity) == null && SalePresentation.RecordedAmount(0) == 0, "recorded amount conversion threw or fabricated amount");
        var historical = new SaleDetails(1, DateTime.UtcNow, 1, "Buyer", "123", null, new(1, 1, 12, null), null, false, 0);
        Assert(SalePresentation.MatchesTicketPrice(historical, 10, 15) && !SalePresentation.MatchesTicketPrice(historical with { RecordedAmount = 100 }, 15, 200)
            && !SalePresentation.MatchesTicketPrice(historical with { Ticket = null }, null, null), "price filtering used recorded totals or retained missing tickets");
        Console.WriteLine("PASS complete reflected OAuth/WebAuthn availability categories, Conductor role and nonthrowing recorded-amount conversion");
    }

    private static string FindRoot()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path != null && !File.Exists(Path.Combine(path.FullName, "global.json"))) path = path.Parent;
        return path?.FullName ?? throw new Exception("Repository root not found");
    }
    private sealed class SalesServiceReachedException : Exception { }

    private sealed class Probe : BaseController
    {
        public Task Command(string resource, string command) => RequireRealtimeCommandAsync(resource, command);
        public string? Token() => ResolveAccessToken();
        public Task<Identity> Actor() => RequireApplicationActorAsync();
        public static string Operation(RealtimeCrudRequest request) => ApplicationOperationId(request);
        public string HttpOperation() => ApplicationOperationId();
    }
    private sealed class SocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => throw new NotSupportedException();
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
