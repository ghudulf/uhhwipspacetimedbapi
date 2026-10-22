using System.Collections;
using System.Reflection;
using BRU_AVTOPARK_AspireAPI.ApiService.Authentication;
using Microsoft.Extensions.Options;
using SpacetimeDB;

internal static class ProviderFlowBrokerTests
{
    public static async Task RunAsync()
    {
        var clock = new Clock();
        var broker = new ProviderFlowBroker(clock, Options.Create(new ProviderFlowOptions()));
        ProviderFlowStart Create(string address = "192.0.2.1") => broker.Create("entra", "login", null, address, ProviderFlowBroker.RandomSecret(), "nonce", "verifier", "https://api.test/callback");
        var flow = broker.Create("entra", "link", Identity.FromHexString(new string('1', 64)), "192.0.2.1", "state", "nonce", "verifier", "https://api.test/callback");
        Check(flow.FlowId.Length == 22 && flow.FlowSecret.Length == 43, "entropy/encoding");
        Reject(() => broker.Complete(flow.FlowId, "entra", null, out _), "not_found");
        Reject(() => broker.Complete(flow.FlowId, "entra", "wrong", out _), "not_found");
        Reject(() => broker.Complete(flow.FlowId, "passkey", flow.FlowSecret, out _), "not_found");
        Check(broker.ConsumeState("unknown") == null && broker.ConsumeState("state")?.Nonce == "nonce" && broker.ConsumeState("state") == null, "single-use state");
        Check(broker.Authenticate(flow.FlowId, "entra", new("issuer", "subject")), "authentication");
        var completions = 0;
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            if (broker.Complete(flow.FlowId, "entra", flow.FlowSecret, out var proof) == "completed")
            {
                Check(proof is { Purpose: "link", Actor: not null, ExternalIdentity.Subject: "subject" }, "bound proof");
                Interlocked.Increment(ref completions);
            }
        })));
        Check(completions == 1, "50 parallel exactly-once redemptions");
        var expired = Create(); clock.Advance(300);
        Check(broker.Complete(expired.FlowId, "entra", expired.FlowSecret, out _) == "expired", "expiry");
        Check(!broker.Authenticate(expired.FlowId, "entra", new("issuer", "subject")), "expired authenticated");
        var cancel = Create(); broker.Cancel(cancel.FlowId, "entra", cancel.FlowSecret); broker.Cancel(cancel.FlowId, "entra", cancel.FlowSecret);
        Check(broker.Complete(cancel.FlowId, "entra", cancel.FlowSecret, out _) == "cancelled", "cancel");
        var failed = Create(); broker.Fail(failed.FlowId);
        Check(broker.Complete(failed.FlowId, "entra", failed.FlowSecret, out _) == "failed", "failed");
        var poll = Create();
        for (var i = 0; i < 90; i++) broker.Complete(poll.FlowId, "entra", poll.FlowSecret, out _);
        Reject(() => broker.Complete(poll.FlowId, "entra", poll.FlowSecret, out _), "rate_limited");
        for (var i = 0; i < 5; i++) Create("address");
        Reject(() => Create("address"), "address_limit");
        for (var i = 0; i < 10; i++) { var f = Create("rate"); broker.Cancel(f.FlowId, "entra", f.FlowSecret); }
        Reject(() => Create("rate"), "rate_limited");
        var raw = (IDictionary)typeof(ProviderFlowBroker).GetField("flows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(broker)!;
        foreach (var item in raw.Values)
            foreach (var field in item!.GetType().GetFields())
                Check(field.GetValue(item) is not string value || value != flow.FlowSecret && value != "state", "raw secret/state retained");
        clock.Advance(1000); Create("after-sweep");
        Check(raw.Count == 1, "sweep did not remove tombstones");
        var capacity = new ProviderFlowBroker(clock, Options.Create(new ProviderFlowOptions()));
        for (var i = 0; i < 2000; i++) capacity.Create("passkey", "login", null, "ip" + i);
        Reject(() => capacity.Create("passkey", "login", null, "overflow"), "capacity");
        Check(new ProviderFlowOptions { FlowSeconds = 1 }.FlowSeconds == 60 && new ProviderFlowOptions { FlowSeconds = 1000 }.FlowSeconds == 600, "TTL clamp");
        Console.WriteLine("PASS ProviderFlowBroker: 50 parallel redeems, bound actor/provider, hashed secrets/state, single-use state, expiry/cancel/failure, capacity/IP/create/poll limits and sweeping");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Provider broker: " + message); }
    private static void Reject(Action action, string code)
    {
        try { action(); } catch (ProviderFlowException ex) { Check(ex.Code == code, "wrong code"); return; }
        throw new Exception("Provider broker: expected " + code);
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
}
