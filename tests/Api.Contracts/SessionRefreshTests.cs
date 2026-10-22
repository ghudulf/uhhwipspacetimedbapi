using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.V2;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

internal static class SessionRefreshTests
{
    public static async Task RunAsync()
    {
        var journal = new DatabaseChangeJournal();
        var actor = new string('1', 64); var acceptedToken = "old-access"; var refreshCalls = 0;
        var database = Stub.Create<ISpacetimeDBService>((method, _) => method.Name switch
        {
            "get_State" => DatabaseConnectionState.Ready,
            "get_Changes" => journal,
            "ReadAsync" => Task.FromResult(new UserAccessSnapshot(true, false, [], [])),
            _ => throw new Exception("Unexpected refresh database call")
        });
        var validator = Stub.Create<IAuthWebSocketTokenValidator>((_, args) => Task.FromResult<Dictionary<string, object>?>(
            (string)args![0]! == acceptedToken ? new() { ["identity"] = actor } : null));
        var refreshAccepted = true;
        var auth = Stub.Create<IAuthOrchestrationService>((method, args) =>
        {
            Check(method.Name == "RefreshTokenAsync" && (string)args![0]! == "refresh-proof", "refresh proof routing");
            refreshCalls++;
            return Task.FromResult(refreshAccepted ? RefreshTokenResult.Successful("new-access", "rotated-refresh", DateTime.UtcNow.AddHours(1)) : RefreshTokenResult.Failed("private-reason"));
        });
        var operations = new ApplicationOperations(database, Stub.Create<ITicketSalesService>(), Stub.Create<IBusService>(), NullLogger<ApplicationOperations>.Instance);
        var admission = new ConnectionAdmission();
        var session = new ApiSocketSession(operations, validator, auth, database, admission, NullLogger<ApiSocketSession>.Instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var clientTcp = new TcpClient();
        var connecting = clientTcp.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token).AsTask();
        using var serverTcp = await listener.AcceptTcpClientAsync(deadline.Token); await connecting;
        using var client = WebSocket.CreateFromStream(clientTcp.GetStream(), false, ApiProtocol.Subprotocol, Timeout.InfiniteTimeSpan);
        using var server = WebSocket.CreateFromStream(serverTcp.GetStream(), true, ApiProtocol.Subprotocol, Timeout.InfiniteTimeSpan);
        var running = session.RunAsync(server, "refresh-test", deadline.Token);
        var welcome = await Receive(client, deadline.Token);
        Check(welcome.Data!.Value.Deserialize<Capabilities>(ApiProtocol.Json)!.Operations.Any(operation => operation.Name == "auth.refresh"), "capability missing");
        await Send(client, "auth.attach", new AttachRequest("old-access"), deadline.Token);
        var attached = await Receive(client, deadline.Token); Check(attached.Success, "initial attach");
        var subscription = Guid.NewGuid().ToString("N");
        await Send(client, "subscriptions.open", new SubscriptionRequest(subscription, "session.access", JsonSerializer.SerializeToElement(new EmptyRequest())), deadline.Token);
        Check((await Receive(client, deadline.Token)).Success, "subscription open");
        acceptedToken = "new-access";
        await Send(client, "auth.refresh", new RefreshCommand("refresh-proof"), deadline.Token);
        var invalidation = await Receive(client, deadline.Token);
        Check(invalidation.Type == "invalidation" && invalidation.Data!.Value.Deserialize<SubscriptionInvalidation>(ApiProtocol.Json) is { Reason: "session_changed" } changed && changed.SubscriptionId == subscription, "refresh did not invalidate lease");
        var refreshed = await Receive(client, deadline.Token);
        var reply = refreshed.Data!.Value.Deserialize<AuthenticationReply>(ApiProtocol.Json)!;
        Check(refreshed.Success && refreshed.Generation > attached.Generation && reply is { State: "authenticated", Token: "new-access", RefreshToken: "rotated-refresh", ExpiresAt: not null }, "token replacement/generation/reply");
        await Send(client, "session.access", new EmptyRequest(), deadline.Token);
        Check((await Receive(client, deadline.Token)).Success, "new token not installed");
        refreshAccepted = false;
        await Send(client, "auth.refresh", new RefreshCommand("refresh-proof"), deadline.Token);
        var failed = await Receive(client, deadline.Token);
        Check(!failed.Success && failed.Code == "session_expired" && !JsonSerializer.Serialize(failed).Contains("private-reason", StringComparison.Ordinal), "failure not generic");
        await Send(client, "session.access", new EmptyRequest(), deadline.Token);
        Check((await Receive(client, deadline.Token)).Code == "unauthenticated", "failed refresh retained credentials");
        refreshAccepted = true; acceptedToken = "different-token";
        await Send(client, "auth.refresh", new RefreshCommand("refresh-proof"), deadline.Token);
        Check((await Receive(client, deadline.Token)).Code == "invalid_credentials", "new token bypassed validator");
        var beforeRate = refreshCalls;
        for (var i = 0; i < 10; i++) admission.TryAuthenticate("refresh-test");
        await Send(client, "auth.refresh", new RefreshCommand("refresh-proof"), deadline.Token);
        Check((await Receive(client, deadline.Token)).Code == "rate_limited" && refreshCalls == beforeRate, "auth budget bypassed");
        await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", deadline.Token); await running.WaitAsync(deadline.Token);
        Console.WriteLine("PASS v2 auth.refresh framed sockets: refresh proof routing, new-token validation/replacement, generation bump, session_changed lease invalidation, rotated reply, generic fail-closed errors and shared auth budget");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Session refresh: " + message); }
    private static Task Send<T>(WebSocket socket, string operation, T payload, CancellationToken ct) => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(
        new CommandEnvelope(Guid.NewGuid().ToString("N"), operation, JsonSerializer.SerializeToElement(payload, ApiProtocol.Json)), ApiProtocol.Json), WebSocketMessageType.Text, true, ct);
    private static async Task<ResultEnvelope> Receive(WebSocket socket, CancellationToken ct)
    {
        using var stream = new MemoryStream(); var bytes = new byte[4096]; WebSocketReceiveResult frame;
        do { frame = await socket.ReceiveAsync(bytes, ct); stream.Write(bytes, 0, frame.Count); } while (!frame.EndOfMessage);
        return JsonSerializer.Deserialize<ResultEnvelope>(stream.ToArray(), ApiProtocol.Json)!;
    }
}
