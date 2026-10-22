using System.Reflection;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Controllers;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketSalesApp.AdminServer.Configuration;
using TicketSalesApp.Services.Interfaces;

internal static class RoutingTests
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        await using var app = builder.Build();
        var actions = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().Where(a => a.AttributeRouteInfo?.Template == "api/Auth/connect/scopes").ToArray();
        if (actions.Length != 1 || actions[0].ActionName != nameof(AuthController.OAuthScopes))
            throw new Exception("OAuth scope route remains ambiguous or missing.");
        if (actions[0].MethodInfo.GetCustomAttribute<AuthorizeAttribute>() == null)
            throw new Exception("Scope route lost its authorization boundary.");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        foreach (var browser in new[] { false, true })
        {
            var controller = new AuthController(
                Stub.Create<IAuthOrchestrationService>((method, _) => method.Name == "GetOAuthScopesAsync"
                    ? Task.FromResult(OAuthScopesResult.Successful(new List<string> { "openid" })) : throw new Exception("Unexpected operation")),
                Stub.Create<IHtmlRenderingService>((method, _) => method.Name == "RenderOidcScopesList" ? Task.FromResult("<html>scopes</html>") : throw new Exception("Unexpected renderer")),
                Stub.Create<IRequestDetector>((method, _) => method.Name == "IsBrowserRequest" ? browser : throw new Exception("Unexpected detection")),
                Options.Create(new FeatureFlagOptions { EnableOAuthScopesRefactoring = true, EnableOAuthScopesPageRefactoring = true }),
                NullLogger<AuthController>.Instance, cache, Stub.Create<IOidcHelperService>(), Stub.Create<IOpenIdConnectService>(),
                Stub.Create<ISpacetimeDBService>(), Stub.Create<IRealtimeEventBus>(), Stub.Create<IAuthWebSocketService>())
                { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            var response = await controller.OAuthScopes();
            if (browser ? response is not ContentResult { ContentType: "text/html" }
                : response is not OkObjectResult { Value: ApiResponse<List<OAuthScopeDto>> { Success: true } })
                throw new Exception("Scope route did not preserve selected HTML/JSON representation.");
        }
        Console.WriteLine("PASS MVC scope route has a single authorized action and preserves HTML/JSON representations");
    }
}
