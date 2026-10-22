using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

internal static class ClientNetworkPolicyTests
{
    public static async Task RunAsync()
    {
        var previous = Environment.GetEnvironmentVariable("BRU_PLAINTEXT_POLICY");
        try
        {
            using var http = new HttpClient();
            foreach (var mode in Enum.GetValues<PlaintextPolicy>())
            {
                Environment.SetEnvironmentVariable("BRU_PLAINTEXT_POLICY", mode.ToString().ToLowerInvariant());
                foreach (var endpoint in new[] { "http://127.0.0.1:5000/api/", "http://192.168.1.20:5000/api/", "http://203.0.113.5/", "http://example.com/", "https://example.com/api/" })
                {
                    var uri = new Uri(endpoint);
                    var allowed = uri.Scheme == "https" || NetworkPolicy.IsPlaintextAllowed(uri, mode);
                    Check(Accepts(() => ClientEndpoints.ServerRoot(endpoint)) == allowed, "ServerRoot " + mode + endpoint);
                    Check(Accepts(() => ClientEndpoints.WebSocketEndpoint(uri, "api/realtime/stream")) == allowed, "WebSocketEndpoint " + mode + endpoint);
                    Check(Accepts(() => new QrLoginClient(http, endpoint)) == allowed, "QR " + mode + endpoint);
                    Check(Accepts(() => new ProviderLoginClient(http, endpoint)) == allowed, "Provider " + mode + endpoint);
                    await using var realtime = new RealtimeApplicationClient();
                    try { await realtime.ConnectAsync(uri, null, new CancellationToken(true)); throw new Exception("Canceled connect succeeded"); }
                    catch (ArgumentException) { Check(!allowed, "Realtime rejected allowed transport " + endpoint); }
                    catch (OperationCanceledException) { Check(allowed, "Realtime attempted forbidden transport " + endpoint); }
                }
                var plaintext = DiscoveryEndpoints.Candidates("192.168.1", false, mode).ToArray();
                Check(plaintext.Length == (mode is PlaintextPolicy.Lan or PlaintextPolicy.Any ? 150 : 0), "discovery mode " + mode);
                if (plaintext.Length > 0) Check(plaintext[0] == "http://192.168.1.100:5000/" && plaintext[^1] == "http://192.168.1.249:5000/", "plaintext ports/range");
                Check(DiscoveryEndpoints.Candidates("203.0.113", false, mode).Any() == (mode == PlaintextPolicy.Any), "public discovery policy");
                Check(DiscoveryEndpoints.Candidates("192.168.1", true, mode).First() == "https://192.168.1.100:5001/", "TLS discovery fallback");
            }
            Environment.SetEnvironmentVariable("BRU_PLAINTEXT_POLICY", "typo");
            try { _ = ClientNetworkPolicy.Mode; throw new Exception("Invalid client policy accepted"); }
            catch (InvalidOperationException) { }
        }
        finally { Environment.SetEnvironmentVariable("BRU_PLAINTEXT_POLICY", previous); }
        Console.WriteLine("PASS client four-mode ServerRoot/WebSocket/QR/provider/realtime transport and discovery candidates, plaintext-first ports, public rejection and invalid configuration");
    }

    private static bool Accepts(Func<object> action)
    {
        try { _ = action(); return true; }
        catch (Exception ex) when (ex is ArgumentException or QrLoginException or ProviderLoginException) { return false; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
