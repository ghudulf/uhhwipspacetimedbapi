using BRU.Avtopark.Contracts;

internal static class NetworkPolicyTests
{
    public static void Run()
    {
        string[] loopback = ["127.0.0.1", "::1", "localhost", "LOCALHOST.", "::ffff:127.0.0.1"];
        string[] lan = ["10.0.0.0", "10.255.255.255", "172.16.0.0", "172.31.255.255", "192.168.0.1", "169.254.0.0", "169.254.255.255", "100.64.0.0", "100.127.255.255", "fe80::", "febf::1", "fc00::", "fd00::", "::ffff:192.168.0.1", "server", "SERVER.local.", "server.lan", "server.home.arpa", "server.internal", "server.localdomain", "xn--bcher-kva.local"];
        string[] remote = ["172.15.255.255", "172.32.0.0", "100.63.255.255", "100.128.0.0", "169.253.255.255", "fec0::", "fbff::", "203.0.113.5", "example.com", "192.168.1.1.evil.com", "xn--bcher-kva.com"];
        foreach (var mode in Enum.GetValues<PlaintextPolicy>())
        {
            foreach (var host in loopback) Check(NetworkPolicy.IsPlaintextAllowed(host, mode) == (mode != PlaintextPolicy.Never), host, mode);
            foreach (var host in lan) Check(NetworkPolicy.IsPlaintextAllowed(host, mode) == (mode is PlaintextPolicy.Lan or PlaintextPolicy.Any), host, mode);
            foreach (var host in remote) Check(NetworkPolicy.IsPlaintextAllowed(host, mode) == (mode == PlaintextPolicy.Any), host, mode);
            foreach (var host in new[] { "10.0.0.1@evil.com", "evil.com#192.168.0.1", "server/path", " server" })
                Check(!NetworkPolicy.IsPlaintextAllowed(host, mode), host, mode);
            Check(!NetworkPolicy.IsPlaintextAllowed(new Uri("http://10.0.0.1@evil.com/"), mode), "userinfo URI", mode);
        }
        foreach (var host in new[] { "3232235777", "0300.0250.1.1", "0xc0a80101" })
            Check(NetworkPolicy.IsPlaintextAllowed(new Uri("http://" + host), PlaintextPolicy.Lan), host, PlaintextPolicy.Lan);
        Check(NetworkPolicy.ParseMode(null) == PlaintextPolicy.Lan && NetworkPolicy.ParseMode("LAN") == PlaintextPolicy.Lan, "default", PlaintextPolicy.Lan);
        Check(NetworkPolicy.ParseMode("typo") == PlaintextPolicy.Never && !NetworkPolicy.IsValidMode("typo"), "invalid fails closed", PlaintextPolicy.Never);
        Console.WriteLine("PASS NetworkPolicy four-mode matrix, address boundaries, mapped IPs, DNS-free names, canonical IP spellings and hostile inputs");
    }

    private static void Check(bool condition, string host, PlaintextPolicy mode)
    {
        if (!condition) throw new Exception($"NetworkPolicy {mode}: {host}");
    }
}
