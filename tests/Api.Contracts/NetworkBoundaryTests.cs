using System.Net;
using System.Reflection;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class NetworkBoundaryTests
{
    public static async Task CheckAsync(ControllerBase controller, string method, params object[] arguments)
    {
        var context = controller.HttpContext;
        var services = context.RequestServices;
        var scheme = context.Request.Scheme;
        var address = context.Connection.RemoteIpAddress;
        try
        {
            foreach (var mode in Enum.GetValues<PlaintextPolicy>())
            {
                var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    { ["Network:PlaintextPolicy"] = mode.ToString().ToLowerInvariant() }).Build();
                using var provider = new ServiceCollection().AddSingleton<IConfiguration>(config).BuildServiceProvider();
                context.RequestServices = provider;
                foreach (var peer in new[] { "127.0.0.1", "192.168.1.20", "203.0.113.5" })
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                    context.Request.Scheme = "http";
                    var value = controller.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, arguments);
                    var result = value is Task<IActionResult?> task ? await task : (IActionResult?)value;
                    if ((result == null) != NetworkPolicy.IsPlaintextAllowed(context.Connection.RemoteIpAddress, mode))
                        throw new Exception($"{controller.GetType().Name} boundary: {mode} {peer}");
                    context.Request.Scheme = "https";
                    value = controller.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, arguments);
                    result = value is Task<IActionResult?> secure ? await secure : (IActionResult?)value;
                    if (result != null) throw new Exception("HTTPS controller boundary rejected");
                }
            }
        }
        finally { context.RequestServices = services; context.Request.Scheme = scheme; context.Connection.RemoteIpAddress = address; }
        Console.WriteLine("PASS " + controller.GetType().Name + " API-call transport boundary in all four policy modes");
    }

    public static void NativeRefresh()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-BRU-Native-Login"] = "1";
        foreach (var mode in Enum.GetValues<PlaintextPolicy>())
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Network:PlaintextPolicy"] = mode.ToString().ToLowerInvariant() }).Build();
            var native = new NativeRefreshTokens(null!, new HttpContextAccessor { HttpContext = context }, config,
                new QrLoginTests.EnvironmentStub(), new RefreshRateBudget(TimeProvider.System), TimeProvider.System);
            foreach (var peer in new[] { "127.0.0.1", "192.168.1.20", "203.0.113.5" })
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                if (native.IsNative != NetworkPolicy.IsPlaintextAllowed(context.Connection.RemoteIpAddress, mode)) throw new Exception("Native refresh transport " + mode + peer);
                context.Request.Scheme = "https";
                if (!native.IsNative) throw new Exception("Native refresh HTTPS rejected");
                context.Request.Headers.Origin = "https://portal.test";
                if (native.IsNative) throw new Exception("Browser refresh origin accepted");
                context.Request.Headers.Remove("Origin"); context.Request.Scheme = "http";
            }
        }
        Console.WriteLine("PASS native refresh four-mode peer policy and unchanged browser exclusion");
    }
}
