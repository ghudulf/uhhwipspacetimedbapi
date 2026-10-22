using System.Net;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.V2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

internal static class SharedClientTests
{
    public static async Task RunAsync()
    {
        var changes = new DatabaseChangeJournal(2);
        var active = true;
        var valid = true;
        var slowRead = false;
        var publishDuringRead = false;
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdRead = new TaskCompletionSource<UserAccessSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        HttpContext? activeConnection = null;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(Stub.Create<ISpacetimeDBService>((method, args) =>
        {
            if (method.Name == "get_Changes") return changes;
            if (method.Name == "get_State") return DatabaseConnectionState.Ready;
            if (method.Name == "ReadAsync" && method.GetGenericArguments()[0] == typeof(UserAccessSnapshot))
            {
                if (publishDuringRead) { publishDuringRead = false; changes.Publish("access"); }
                if (slowRead)
                {
                    readEntered.TrySetResult();
                    return holdRead.Task.WaitAsync((CancellationToken)args![1]!);
                }
                return Task.FromResult(new UserAccessSnapshot(active, true, ["Administrator"], ["sales.view", "sales.create"]));
            }
            throw new Exception("Unexpected database call: " + method.Name);
        }));
        builder.Services.AddSingleton(Stub.Create<ITicketSalesService>((method, _) =>
        {
            if (method.Name != "CreateSaleAsync") throw new Exception("Unexpected mutation");
            activeConnection!.Abort();
            return Task.FromException<uint?>(new CommitOutcomeUnknownException("synthetic interrupted mutation"));
        }));
        builder.Services.AddSingleton(Stub.Create<IBusService>());
        builder.Services.AddSingleton(Stub.Create<IAuthOrchestrationService>());
        builder.Services.AddSingleton(Stub.Create<IAuthWebSocketTokenValidator>((_, _) => Task.FromResult(valid
            ? new Dictionary<string, object> { ["identity"] = new string('1', 64) } : null)));
        builder.Services.AddSingleton<ConnectionAdmission>();
        builder.Services.AddScoped<ApplicationOperations>();
        builder.Services.AddScoped<ApiSocketSession>();
        await using var app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/api/realtime/stream", async context =>
        {
            activeConnection = context;
            using var socket = await context.WebSockets.AcceptWebSocketAsync(ApiProtocol.Subprotocol);
            await context.RequestServices.GetRequiredService<ApiSocketSession>().RunAsync(socket, "shared-client-test", context.RequestAborted);
        });
        await app.StartAsync();
        try
        {
            var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var client = new RealtimeApplicationClient();
            var frames = new System.Collections.Concurrent.ConcurrentQueue<(string Direction, string Frame)>();
            client.FrameLogged += (direction, frame) => frames.Enqueue((direction, frame));
            await client.ConnectAsync(new Uri(origin + "/api/"), "synthetic-client-token-proof", deadline.Token);
            Assert(frames.Any(frame => frame.Direction == "SENDING") && frames.Any(frame => frame.Direction == "RECEIVED RAW") && !frames.Any(frame => frame.Frame.Contains("synthetic-client-token-proof", StringComparison.Ordinal)), "shared client frame callbacks or token masking");
            var counted = client.CountedRequestsSent;
            await client.CallAsync<EmptyRequest, System.Text.Json.JsonElement>("ping", new(), null, deadline.Token);
            await client.CallAsync<EmptyRequest, Capabilities>("capabilities", new(), null, deadline.Token);
            Assert(client.CountedRequestsSent == counted, "heartbeat/capabilities consumed counted request budget");
            try
            {
                await client.CallAsync<SubscriptionRequest, SubscriptionSnapshot>("subscriptions.open", new(Guid.NewGuid().ToString("N"), "sales.create", System.Text.Json.JsonSerializer.SerializeToElement(new EmptyRequest())), null, deadline.Token);
                throw new Exception("mutation accepted as subscription");
            }
            catch (ApplicationCommandException ex) when (ex.Code == "unsupported_subscription") { }
            publishDuringRead = true;
            await using (var race = client.WatchAsync<EmptyRequest, AccessSnapshot>("session.access", new(), deadline.Token).GetAsyncEnumerator())
            {
                Assert(await race.MoveNextAsync(), "snapshot race produced no update");
                if (race.Current.IsSynchronized) Assert(await race.MoveNextAsync(), "change during snapshot was lost");
                Assert(!race.Current.IsSynchronized, "change between watermark and snapshot was ignored");
                Assert(await race.MoveNextAsync() && race.Current.IsSynchronized, "racing snapshot did not recover");
            }
            await using (var subscription = client.WatchAsync<EmptyRequest, AccessSnapshot>("session.access", new(), deadline.Token).GetAsyncEnumerator())
            {
                Assert(await subscription.MoveNextAsync() && subscription.Current.IsSynchronized, "initial subscription snapshot missing");
                changes.Publish("access");
                Assert(await subscription.MoveNextAsync() && !subscription.Current.IsSynchronized && subscription.Current.Reason == "changed", "committed change did not invalidate snapshot");
                Assert(await subscription.MoveNextAsync() && subscription.Current.IsSynchronized, "subscription did not acquire replacement snapshot");
                changes.Publish("buses"); changes.Publish("sales"); changes.Publish("access");
                Assert(await subscription.MoveNextAsync() && subscription.Current.Reason == "resync_required", "overflow was silently ignored");
                Assert(await subscription.MoveNextAsync() && subscription.Current.IsSynchronized, "overflow recovery did not refresh");
                changes.Reset();
                Assert(await subscription.MoveNextAsync() && subscription.Current.Reason == "resync_required", "database epoch change ignored");
                Assert(await subscription.MoveNextAsync() && subscription.Current.IsSynchronized, "reconnect recovery missing");
                active = false;
                Assert(await subscription.MoveNextAsync() && subscription.Current.Reason == "forbidden", "idle subscription retained revoked access");
                try { await subscription.MoveNextAsync(); throw new Exception("revoked subscription continued"); }
                catch (ApplicationCommandException ex) when (ex.Code == "forbidden") { }
                active = true;
            }
            Console.WriteLine("PASS production shared subscription snapshot, committed invalidation, bounded-history recovery, reconnect epoch and idle access revocation");
            var access = await client.CallAsync<EmptyRequest, AccessSnapshot>("session.access", new(), null, deadline.Token);
            Assert(access is { IsActive: true, IsAdministrator: true }, "shared client failed authenticated operation");
            var simultaneous = Enumerable.Range(0, 4).Select(_ => client.CallAsync<EmptyRequest, AccessSnapshot>("session.access", new(), null, deadline.Token));
            Assert((await Task.WhenAll(simultaneous)).All(a => a?.IsActive == true), "multiplexed request correlation failed");
            try { await client.CallAsync<EmptyRequest, AccessSnapshot>("not.registered", new(), null, deadline.Token); throw new Exception("unsupported operation dispatched"); }
            catch (OperationNotDispatchedException) { }
            slowRead = true;
            using var acquisitionGate = new SemaphoreSlim(1, 1);
            async Task<AccessSnapshot?> CoordinatorStyleReadAsync(CancellationToken ct)
            {
                await acquisitionGate.WaitAsync(ct);
                RealtimeApplicationClient acquired;
                try { acquired = client; }
                finally { acquisitionGate.Release(); }
                return await acquired.CallAsync<EmptyRequest, AccessSnapshot>("session.access", new(), null, ct);
            }
            using (var cancel = new CancellationTokenSource())
            {
                var beforeReads = client.CountedRequestsSent;
                var reading = CoordinatorStyleReadAsync(cancel.Token);
                await readEntered.Task.WaitAsync(deadline.Token);
                Assert(!client.TryBeginRotation() && client.IsConnected, "rotation interrupted an in-flight operation");
                var secondRead = CoordinatorStyleReadAsync(cancel.Token);
                while (client.CountedRequestsSent < beforeReads + 2) await Task.Delay(1, deadline.Token);
                Assert(!reading.IsCompleted && client.OutstandingRequests >= 2, "slow read serialized a second coordinator-style call before dispatch");
                var occupied = new[] { secondRead }.Concat(Enumerable.Range(0, ApiProtocol.MaxOutstanding - 2)
                    .Select(_ => client.CallAsync<EmptyRequest, AccessSnapshot>("session.access", new(), null, cancel.Token))).ToArray();
                var close = (Task)typeof(RealtimeApplicationClient).GetMethod("CloseSubscriptionAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(client, [Guid.NewGuid().ToString("N")])!;
                await Task.Delay(25, deadline.Token);
                Assert(client.IsConnected, "pre-dispatch subscription-close slot exhaustion aborted shared requests");
                cancel.Cancel();
                try { await reading; throw new Exception("Cancelled client read completed"); } catch (OperationCanceledException) { }
                foreach (var pending in occupied)
                    try { await pending; } catch (OperationCanceledException) { } catch (ApplicationCommandException ex) when (ex.Code == "cancelled") { }
                await close.WaitAsync(deadline.Token);
            }
            slowRead = false;
            Assert(client.IsConnected, "read cancellation destroyed the shared transport");
            Assert((await client.CallAsync<EmptyRequest, AccessSnapshot>("session.access", new(), null, deadline.Token))?.IsActive == true,
                "read after cancellation failed");
            active = false;
            await ExpectCode(client, "forbidden", deadline.Token);
            active = true;
            valid = false;
            await ExpectCode(client, "session_expired", deadline.Token);
            await ExpectCode(client, "unauthenticated", deadline.Token);
            var acquiredOldClient = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resumeOldCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<MutationResult?> CallRacingRotationAsync()
            {
                var acquired = client;
                acquiredOldClient.SetResult();
                await resumeOldCall.Task;
                return await acquired.CallAsync<CreateSaleCommand, MutationResult>("sales.create", new(1, "synthetic", ""), Guid.NewGuid().ToString("N"), deadline.Token);
            }
            var racingRotation = CallRacingRotationAsync();
            await acquiredOldClient.Task.WaitAsync(deadline.Token);
            Assert(client.TryBeginRotation() && !client.IsConnected, "quiescent connection could not rotate between operations");
            var beforeRotationCall = client.CountedRequestsSent;
            resumeOldCall.SetResult();
            try { await racingRotation; throw new Exception("old-client mutation dispatched during rotation"); }
            catch (OperationNotDispatchedException) { }
            await client.DisposeAsync();
            try { await client.CallAsync<CreateSaleCommand, MutationResult>("sales.create", new(1, "synthetic", ""), Guid.NewGuid().ToString("N"), deadline.Token); throw new Exception("disposed-client mutation dispatched"); }
            catch (OperationNotDispatchedException) { }
            Assert(client.CountedRequestsSent == beforeRotationCall, "rotation-racing mutation consumed a dispatched request");
            Console.WriteLine("PASS coordinator-style calls dispatch concurrently; rotation/disposal races remain provably pre-dispatch");
            valid = true;
            await using (var disposing = new RealtimeApplicationClient())
            {
                await disposing.ConnectAsync(new Uri(origin + "/api/"), "synthetic-client-token-proof", deadline.Token);
                var slots = (SemaphoreSlim)typeof(RealtimeApplicationClient).GetField("_slots", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(disposing)!;
                slots.Dispose();
                var closeDuringDisposal = (Task)typeof(RealtimeApplicationClient).GetMethod("CloseSubscriptionAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(disposing, [Guid.NewGuid().ToString("N")])!;
                await closeDuringDisposal.WaitAsync(deadline.Token);
                Assert(closeDuringDisposal.IsCompletedSuccessfully && disposing.IsConnected, "undispatched disposed-slot close faulted or aborted the shared socket");
            }
            Console.WriteLine("PASS background subscription close observes disposal exceptions without aborting an undispatched lease release");
            await using var interrupted = new RealtimeApplicationClient();
            await interrupted.ConnectAsync(new Uri(origin + "/api/"), "synthetic-client-token-proof", deadline.Token);
            try
            {
                await interrupted.CallAsync<CreateSaleCommand, MutationResult>("sales.create", new(1, "synthetic", "123"), Guid.NewGuid().ToString("N"), deadline.Token);
                throw new Exception("Disconnected mutation reported success");
            }
            catch (ApplicationCommandException ex) when (ex.Code == "commit_outcome_unknown") { }
            Console.WriteLine("PASS shared client/server handshake, correlation, non-dispatch fallback, live revocation/expiry, read cancellation without disconnect and uncertain mutation after disconnect");
        }
        finally { await app.StopAsync(); }
    }
    private static async Task ExpectCode(RealtimeApplicationClient client, string code, CancellationToken ct)
    {
        try { await client.CallAsync<EmptyRequest, AccessSnapshot>("session.access", new(), null, ct); throw new Exception("Expected " + code); }
        catch (ApplicationCommandException ex) when (ex.Code == code) { }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
