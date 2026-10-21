using System.Net;
using System.Text;
using System.Text.Json;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

internal static class ProviderClientTests
{
    public static async Task RunAsync()
    {
        var id = QrLoginProtocol.Base64Url(new byte[16]); var secret = QrLoginProtocol.Base64Url(new byte[32]);
        var login = JsonSerializer.SerializeToElement(new { success = false, requiresTwoFactor = true, tempToken = "temp" });
        var clock = new Clock();
        ProviderStart Start(string? url = null, bool passkey = false) => new(id, secret, clock.GetUtcNow().AddMinutes(5), 1,
            passkey ? null : url ?? "https://login.microsoftonline.com/tenant/authorize?state=private", passkey ? url ?? "https://api.test/base/api/auth/passkey/browser?f=" + id + "&b=" + secret : null);
        var api = new Api(Start()) { Polls = new([new("pending"), new("completed", login)]) };
        var launcher = new Browser(); var waits = new List<TimeSpan>();
        Task Delay(TimeSpan duration, CancellationToken _) { waits.Add(duration); clock.Advance(duration); return Task.CompletedTask; }
        await using (var flow = new ProviderFlow(api, launcher, clock, delay: Delay))
        {
            var events = await Collect(flow.RunAsync("entra"));
            Check(launcher.Calls == 1 && events.Count(e => e.State == ProviderFlowState.Completed) == 1 && events.Last().Login!.Value.GetProperty("requiresTwoFactor").GetBoolean() && waits.All(w => w.TotalMilliseconds == 500), "cadence/exactly-once/2FA passthrough");
        }
        foreach (var url in new[] { "http://login.microsoftonline.com/auth", "https://login.microsoftonline.com.evil.test/auth", "https://user@login.microsoftonline.com/auth", "https://evil.test/auth", "file:///tmp/test", "https://login.microsoftonline.com/auth#fragment" })
        {
            api = new(Start(url)); launcher = new();
            await using var flow = new ProviderFlow(api, launcher, clock, delay: Delay);
            Check((await Collect(flow.RunAsync("entra"))).Last() is { State: ProviderFlowState.Failed, Code: "unsafe_url" } && launcher.Calls == 0 && api.Completes == 0 && api.Cancels == 0, "unsafe Entra URL launched/contacted");
        }
        var goodPasskey = Start(passkey: true).BrowserUrl!;
        foreach (var url in new[] { goodPasskey.Replace("api.test", "evil.test"), goodPasskey + "&extra=1", goodPasskey + "&f=" + id, goodPasskey.Replace("/base/", "/other/"), goodPasskey.Replace("?f=" + id, "?f=wrong"), goodPasskey.Replace("https://", "https://user@") })
        {
            api = new(Start(url, true)); launcher = new();
            await using var flow = new ProviderFlow(api, launcher, clock, delay: Delay);
            Check((await Collect(flow.RunAsync("passkey"))).Last().Code == "unsafe_url" && launcher.Calls == 0, "unsafe passkey URL launched");
        }
        api = new(Start(passkey: true)) { Polls = new([new("completed", Registered: true)]) }; launcher = new();
        await using (var flow = new ProviderFlow(api, launcher, clock, delay: Delay))
            Check((await Collect(flow.RunAsync("passkey", "register", "bearer"))).Last() is { State: ProviderFlowState.Completed, Registered: true } && launcher.Calls == 1, "registration result");
        api = new(Start()) { Polls = new([new("completed", Linked: true)]) };
        await using (var flow = new ProviderFlow(api, new Browser(), clock, delay: Delay))
            Check((await Collect(flow.RunAsync("entra", "link", "bearer"))).Last().Linked, "link result");
        api = new(Start()) { NetworkFailures = 3 };
        await using (var flow = new ProviderFlow(api, new Browser(), clock, delay: Delay))
            Check((await Collect(flow.RunAsync("entra"))).Last().Code == "network_error" && api.Completes == 3, "transient retry bound");
        api = new(Start()) { NetworkFailures = 1, Polls = new([new("completed", login)]) };
        await using (var flow = new ProviderFlow(api, new Browser(), clock, delay: Delay))
            Check((await Collect(flow.RunAsync("entra"))).Last().State == ProviderFlowState.Completed, "transient recovery");
        api = new(Start());
        await using (var flow = new ProviderFlow(api, new Browser(), clock, delay: (_, _) => { clock.Advance(TimeSpan.FromMinutes(6)); return Task.CompletedTask; }))
            Check((await Collect(flow.RunAsync("entra"))).Last().State == ProviderFlowState.Expired && api.Completes == 0, "local expiry");
        api = new(Start());
        await using (var flow = new ProviderFlow(api, new Browser(), clock, delay: Delay))
        {
            await using var iterator = flow.RunAsync("entra").GetAsyncEnumerator();
            Check(await iterator.MoveNextAsync() && await iterator.MoveNextAsync(), "waiting flow");
            await flow.CancelAsync(); await flow.CancelAsync();
            Check(await iterator.MoveNextAsync() && iterator.Current.State == ProviderFlowState.Cancelled && api.Cancels == 1, "cancel once");
        }
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(async request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("/start", StringComparison.Ordinal))
            {
                Check(request.Headers.Authorization?.Parameter == "application-bearer" && await request.Content!.ReadAsStringAsync() == "{\"purpose\":\"link\"}", "start bearer/strict body");
                return Response(JsonSerializer.Serialize(Start(), ApiProtocol.Json));
            }
            Check(request.Headers.Authorization == null, "bearer leaked to anonymous redemption");
            return Response("{\"state\":\"pending\"}");
        }));
        var client = new ProviderLoginClient(http, "https://api.test/base");
        await client.StartAsync("entra", "link", "application-bearer");
        await client.CompleteAsync("entra", id, secret);
        Check(requests.SequenceEqual(["/base/api/auth/entra/start", "/base/api/auth/entra/flows/" + id + "/complete"]), "deployment prefix lost");
        foreach (var response in new[] { new HttpResponseMessage(HttpStatusCode.Redirect), Response(new string('x', 65537)), Response("not-json"), new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"code\":\"private secret URL\"}") } })
        {
            using var badHttp = new HttpClient(new Handler(_ => Task.FromResult(response)));
            try { await new ProviderLoginClient(badHttp, "https://api.test").CapabilitiesAsync(); throw new Exception("accepted invalid response"); }
            catch (ProviderLoginException ex) { Check(!ex.Message.Contains("secret", StringComparison.Ordinal), "unsafe error text"); }
        }
        Console.WriteLine("PASS Provider client/flow fakes: URL safety launches nothing, same-origin exact passkey query, 2FA/link/register passthrough, cadence clamp, retries/expiry/cancel-once, size/redirect/error caps and bearer/deployment-prefix boundaries");
    }
    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static async Task<List<ProviderFlowEvent>> Collect(IAsyncEnumerable<ProviderFlowEvent> stream) { var list = new List<ProviderFlowEvent>(); await foreach (var item in stream) list.Add(item); return list; }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Provider client: " + message); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }
    private sealed class Browser : IBrowserLauncher
    { public int Calls; public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; } }
    private sealed class Clock : TimeProvider
    { private DateTimeOffset now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => now; public void Advance(TimeSpan duration) => now += duration; }
    private sealed class Api(ProviderStart start) : IProviderLoginApi
    {
        public string ApiBase => "https://api.test/base";
        public Queue<ProviderPoll> Polls = new([new("pending")]); public int Completes, Cancels, NetworkFailures;
        public Task<ProviderCapabilities> CapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ProviderCapabilities(true, false, false, true));
        public Task<ProviderStart> StartAsync(string provider, string purpose, string? bearer = null, string? username = null, CancellationToken cancellationToken = default) => Task.FromResult(start);
        public Task<ProviderPoll> CompleteAsync(string provider, string id, string secret, CancellationToken cancellationToken = default)
        { Completes++; if (NetworkFailures-- > 0) throw new ProviderLoginException("network_error"); return Task.FromResult(Polls.Count > 1 ? Polls.Dequeue() : Polls.Peek()); }
        public Task CancelAsync(string provider, string id, string secret, CancellationToken cancellationToken = default) { Cancels++; return Task.CompletedTask; }
        public Task<ProviderLinkStatus> EntraLinkStatusAsync(string bearer, CancellationToken cancellationToken = default) => Task.FromResult(new ProviderLinkStatus("unlinked"));
        public Task UnlinkEntraAsync(string bearer, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
