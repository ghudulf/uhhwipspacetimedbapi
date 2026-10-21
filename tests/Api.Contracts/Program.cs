using System.Reflection;
using BRU_AVTOPARK.Models.Requests;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Controllers;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketSalesApp.AdminServer.Configuration;
using TicketSalesApp.AdminServer.Controllers;
using TicketSalesApp.Services.Interfaces;

if (args.Contains("--write-operation-ledger")) { OperationCoverageTests.WriteLedger(); return; }
OperationCoverageTests.Run();
await RoutingDiagnosticsTests.RunAsync();
SecretConfigurationTests.Run();
await CredentialExportTests.RunAsync();
await EndpointAvailabilityTests.RunAsync();

var loginAvailable = true;
var runtimeFlags = Stub.Create<TicketSalesApp.AdminServer.Experimental.Services.Interfaces.IFeatureFlagService>((method, args) =>
    method.Name == "GetFlagAsync" && (string)args![0]! == nameof(FeatureFlagOptions.LoginEndpointEnabled)
        ? Task.FromResult(loginAvailable) : throw new Exception("Unexpected endpoint flag"));
var loginResult = LoginResult.RequiresTwoFactorAuth("synthetic-challenge", true, false);
var orchestration = Stub.Create<IAuthOrchestrationService>((method, args) => method.Name == "LoginAsync"
    ? Task.FromResult(loginResult) : throw new InvalidOperationException("Unexpected orchestration call"));
using var cache = new MemoryCache(new MemoryCacheOptions());
var controller = new AuthController(orchestration, Stub.Create<IHtmlRenderingService>(), Stub.Create<IRequestDetector>(),
    Options.Create(new FeatureFlagOptions { EnableLoginRefactoring = true }), NullLogger<AuthController>.Instance,
    cache, Stub.Create<IOidcHelperService>(), Stub.Create<IOpenIdConnectService>(), Stub.Create<ISpacetimeDBService>(),
    Stub.Create<IRealtimeEventBus>(), Stub.Create<IAuthWebSocketService>(), runtimeFlags)
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
var request = new LoginRequest { Username = "synthetic-user", Password = "synthetic-password" };
var required = await controller.Login(request);
Assert(required is OkObjectResult { Value: ApiResponse<TwoFactorResponse> { Success: true, Data.RequiresTwoFactor: true } },
    "required second factor was mapped to failed login");
loginResult = LoginResult.Failed("Invalid credentials");
Assert(await controller.Login(request) is UnauthorizedObjectResult, "invalid primary authentication did not return 401");
loginAvailable = false;
Assert(await controller.Login(request) is ObjectResult { StatusCode: 503 }, "runtime override did not disable endpoint");
loginAvailable = true;
Assert(await controller.Login(request) is UnauthorizedObjectResult, "runtime override did not re-enable endpoint");
Console.WriteLine("PASS HTTP login challenge/error mapping and runtime endpoint disable/re-enable");

foreach (var enabled in new[] { false, true })
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:Windows:Enabled"] = enabled.ToString(), ["Authentication:Windows:Realm"] = "synthetic-realm" }).Build();
    var windows = new WindowsAuthenticationController(configuration, Stub.Create<IAuthOrchestrationService>(),
        NullLogger<WindowsAuthenticationController>.Instance, Stub.Create<ISpacetimeDBService>())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    Assert(enabled ? await windows.Login() is BadRequestObjectResult : await windows.Login() is NotFoundResult,
        "Windows provider did not enforce disabled/HTTPS boundary");
    if (enabled)
    {
        windows.Request.Scheme = "https";
        windows.Request.Headers["X-BRU-Native-Login"] = "1";
        windows.Request.Headers.Origin = "https://untrusted.invalid";
        Assert(await windows.Login() is BadRequestObjectResult, "browser Origin passed native-only login boundary");
        Assert(await windows.LinkStatus(new WindowsAuthenticationController.LinkRequest("synthetic-token"), default) is BadRequestObjectResult,
            "browser Origin passed native-only link-status boundary");
        Assert(await windows.Link(new WindowsAuthenticationController.LinkRequest("synthetic-token"), default) is BadRequestObjectResult,
            "browser Origin passed native-only account-link boundary");
    }
}
Console.WriteLine("PASS Windows provider disabled, HTTPS and browser-origin boundaries (no platform negotiation simulated)");

await RoutingTests.RunAsync();
SubscriptionTests.Run();
await RealtimeTests.RunAsync();
ReportTests.Run();
await SharedClientTests.RunAsync();
await FleetOperationTests.RunAsync();
await AdminOperationTests.RunAsync();
await ReviewRegressionTests.RunAsync();
await QrLoginTests.RunAsync();
await ProviderFlowBrokerTests.RunAsync();
await EntraAuthenticationTests.RunAsync();
await SessionRefreshTests.RunAsync();
await ProviderClientTests.RunAsync();

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

public class Stub : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?>? Implementation { get; set; }
    public static T Create<T>(Func<MethodInfo, object?[]?, object?>? implementation = null) where T : class
    {
        var proxy = Create<T, Stub>();
        ((Stub)(object)proxy).Implementation = implementation;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        Implementation?.Invoke(targetMethod!, args) ?? throw new InvalidOperationException("Unexpected dependency invocation: " + targetMethod?.Name);
}
