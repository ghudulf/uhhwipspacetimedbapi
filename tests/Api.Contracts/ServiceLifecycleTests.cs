using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Hubs;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Options;
using BRU_AVTOPARK_AspireAPI.ApiService.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class ServiceLifecycleTests
{
    public static async Task RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startup = new OAuthClientRegistrationService(async ct =>
        { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }, NullLogger<OAuthClientRegistrationService>.Instance);
        await startup.StartAsync(default); await entered.Task.WaitAsync(deadline.Token);
        await startup.StopAsync(deadline.Token);
        Check(startup.ExecuteTask?.IsCompleted == true, "startup task did not stop with host cancellation");
        using var failed = new OAuthClientRegistrationService(_ => throw new InvalidOperationException("synthetic failure"), NullLogger<OAuthClientRegistrationService>.Instance);
        await failed.StartAsync(default);
        await failed.ExecuteTask!.WaitAsync(deadline.Token);
        Check(failed.ExecuteTask.IsCompletedSuccessfully, "startup failure left an unobserved fault");

        using var aborted = new CancellationTokenSource(); aborted.Cancel();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestAborted = aborted.Token };
        var requestDatabase = new SpacetimeDBService(new ConfigurationBuilder().Build(), NullLogger<SpacetimeDBService>.Instance,
            new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = context });
        try { await requestDatabase.ReadAsync(_ => true); throw new Exception("Legacy read ignored request abort"); }
        catch (OperationCanceledException) { }
        try { await requestDatabase.ExecuteReducerAsync(new Reducer.EnsureDefaultPermissions(), _ => true); throw new Exception("Legacy write ignored request abort"); }
        catch (OperationCanceledException) { }

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var managerCalls = 0;
        var manager = Stub.Create<IOpenIddictApplicationManager>((method, args) =>
        {
            if (method.Name != "FindByClientIdAsync" || (CancellationToken)args![1]! != cancelled.Token)
                throw new Exception("Startup manager call lost host cancellation");
            managerCalls++;
            return new ValueTask<object?>(Task.FromCanceled<object?>(cancelled.Token));
        });
        using var managers = new ServiceCollection().AddSingleton(manager).BuildServiceProvider();
        var oidc = new OpenIdConnectService(Stub.Create<ISpacetimeDBService>(), managers, NullLogger<OpenIdConnectService>.Instance);
        Check(!(await oidc.GetApplicationByClientIdAsync("synthetic", cancelled.Token)).success &&
            !(await oidc.RegisterClientApplicationAsync("synthetic", "", "test", ["https://client.invalid/callback"], [], ["openid"], false, cancelled.Token)).success &&
            !(await oidc.UpdateClientApplicationAsync("synthetic", null, null, null, null, null, null, cancelled.Token)).success && managerCalls == 3,
            "Startup service cancellation did not reach all application manager paths");

        var pending = new TaskCompletionSource<List<RouteSchedule>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var db = Stub.Create<ISpacetimeDBService>((method, args) => method.Name == "ReadAsync"
            ? pending.Task.WaitAsync((CancellationToken)args![1]!) : throw new Exception("Unexpected schedule database operation"));
        var services = new ServiceCollection();
        services.AddSingleton(_ => new RouteScheduleService(db, NullLogger<RouteScheduleService>.Instance, new ConfigurationBuilder().Build()));
        var provider = services.BuildServiceProvider();
        var schedule = provider.GetRequiredService<RouteScheduleService>();
        var read = schedule.GetAllSchedulesAsync();
        provider.Dispose();
        try { await read.WaitAsync(deadline.Token); throw new Exception("DI disposal did not cancel owned schedule read"); }
        catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
        schedule.Dispose(); // Repeated provider/user disposal is safe.

        var hub = Stub.Create<IHubContext<SystemEventsHub>>();
        var busServices = new ServiceCollection();
        busServices.AddSingleton(_ => new SignalRRealtimeEventBus(hub, Options.Create(new RealtimeEventOptions()), NullLogger<SignalRRealtimeEventBus>.Instance));
        await using var busProvider = busServices.BuildServiceProvider();
        var bus = busProvider.GetRequiredService<SignalRRealtimeEventBus>();
        await bus.StartAsync(default);
        await using var subscription = bus.SubscribeAsync("buses", deadline.Token).GetAsyncEnumerator(deadline.Token);
        var next = subscription.MoveNextAsync().AsTask();
        await bus.StopAsync(deadline.Token);
        Check(!await next.WaitAsync(deadline.Token), "event subscription did not close at shutdown");
        bus.Dispose(); await bus.DisposeAsync(); await bus.StopAsync(deadline.Token);
        Console.WriteLine("PASS service lifecycle: observed/cancellable startup shutdown and failure, DI disposal cancels schedule operations, event bus stops subscriptions and repeated sync/async/DI disposal is safe within two seconds");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
