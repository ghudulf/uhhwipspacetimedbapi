using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.V2;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSalesApp.Services.Interfaces;

internal static class RealtimeTests
{
    public static async Task RunAsync()
    {
        var database = Stub.Create<ISpacetimeDBService>((method, _) => method.Name == "get_State" ? DatabaseConnectionState.Ready : throw new Exception("Anonymous access reached database"));
        var operations = new ApplicationOperations(database, Stub.Create<ITicketSalesService>(), Stub.Create<IBusService>(), NullLogger<ApplicationOperations>.Instance);
        var admission = new ConnectionAdmission();
        using var first = admission.TryEnter("synthetic"); using var second = admission.TryEnter("synthetic");
        using var third = admission.TryEnter("synthetic"); using var fourth = admission.TryEnter("synthetic");
        Assert(admission.TryEnter("synthetic") == null, "per-address connection bound missing");
        for (var i = 0; i < 10; i++) Assert(admission.TryAuthenticate("synthetic"), "auth budget exhausted early");
        Assert(!admission.TryAuthenticate("synthetic"), "auth budget not enforced across connections");

        var loginEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishLogin = new TaskCompletionSource<LoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = Stub.Create<IAuthOrchestrationService>((method, _) =>
        {
            if (method.Name != "LoginAsync") throw new Exception("Unexpected authentication operation");
            loginEntered.TrySetResult();
            return finishLogin.Task;
        });
        var session = new ApiSocketSession(operations, Stub.Create<IAuthWebSocketTokenValidator>(), authentication, database,
            admission, NullLogger<ApiSocketSession>.Instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var clientTcp = new TcpClient();
        var connecting = clientTcp.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token).AsTask();
        using var serverTcp = await listener.AcceptTcpClientAsync(deadline.Token);
        await connecting;
        using var client = WebSocket.CreateFromStream(clientTcp.GetStream(), false, ApiProtocol.Subprotocol, Timeout.InfiniteTimeSpan);
        using var server = WebSocket.CreateFromStream(serverTcp.GetStream(), true, ApiProtocol.Subprotocol, Timeout.InfiniteTimeSpan);
        var running = session.RunAsync(server, "protocol-test", deadline.Token);
        var welcome = await Receive(client, deadline.Token);
        Assert(welcome.Type == "welcome" && welcome.Data?.Deserialize<Capabilities>(ApiProtocol.Json)?.Operations.Any(o => o.Name == "sales.create") == true, "capabilities missing");

        var read = await Send(client, "sales.list", new PageRequest(), deadline.Token);
        var denied = await Receive(client, deadline.Token);
        Assert(denied.RequestId == read && denied.Code == "unauthenticated", "anonymous sales access not denied");
        await Send(client, "reflection.any.method", new EmptyRequest(), deadline.Token);
        Assert((await Receive(client, deadline.Token)).Code == "unsupported_operation", "unknown operation accepted");
        await Send(client, "auth.totp", new TotpCommand("123456"), deadline.Token);
        Assert((await Receive(client, deadline.Token)).Code == "challenge_expired", "unbound second factor accepted");

        var login = await Send(client, "auth.login", new LoginCommand("synthetic", "synthetic"), deadline.Token);
        await loginEntered.Task.WaitAsync(deadline.Token);
        // Queue a read and then cancel it while the serial worker is waiting for authentication.
        var queued = await Send(client, "sales.list", new PageRequest(), deadline.Token);
        var cancel = await Send(client, "cancel", new CancelCommand(queued), deadline.Token);
        Assert((await Receive(client, deadline.Token)).RequestId == cancel, "cancel was blocked behind the command worker");
        finishLogin.SetResult(LoginResult.RequiresTwoFactorAuth("server-only-challenge", true, false));
        var challenge = await Receive(client, deadline.Token);
        Assert(challenge.RequestId == login && challenge.Success && challenge.Data?.GetProperty("state").GetString() == "challenge-pending", "login challenge missing");
        Assert(!JsonSerializer.Serialize(challenge).Contains("server-only-challenge", StringComparison.Ordinal), "second factor secret leaked outside its initiating socket");
        var cancelled = await Receive(client, deadline.Token);
        Assert(cancelled.RequestId == queued && cancelled.Code == "cancelled", "queued cancellation did not prevent dispatch");
        await Send(client, "auth.logout", new EmptyRequest(), deadline.Token);
        Assert((await Receive(client, deadline.Token)).Success, "logout failed");
        await Send(client, "auth.totp", new TotpCommand("123456"), deadline.Token);
        Assert((await Receive(client, deadline.Token)).Code == "challenge_expired", "logout retained the challenge");
        await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test complete", deadline.Token);
        await running.WaitAsync(deadline.Token);
        Console.WriteLine("PASS v2 capability negotiation, anonymous allowlist, unknown operations, connection/auth budgets, bound challenges, cancellation and logout over framed sockets");
    }
    private static async Task<string> Send<T>(WebSocket socket, string operation, T payload, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new CommandEnvelope(id, operation, JsonSerializer.SerializeToElement(payload, ApiProtocol.Json)), ApiProtocol.Json), WebSocketMessageType.Text, true, ct);
        return id;
    }
    private static async Task<ResultEnvelope> Receive(WebSocket socket, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        var bytes = new byte[4096];
        WebSocketReceiveResult frame;
        do { frame = await socket.ReceiveAsync(bytes, ct); stream.Write(bytes, 0, frame.Count); } while (!frame.EndOfMessage);
        return JsonSerializer.Deserialize<ResultEnvelope>(stream.ToArray(), ApiProtocol.Json)!;
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
