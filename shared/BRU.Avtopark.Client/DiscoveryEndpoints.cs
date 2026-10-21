using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

public static class DiscoveryEndpoints
{
    // Call each pass separately: TLS discovery starts only after the plaintext pass finds nothing.
    public static IEnumerable<string> Candidates(string subnet, bool tls, PlaintextPolicy mode)
    {
        for (var i = 100; i <= 249; i++)
        {
            var uri = new Uri($"{(tls ? "https" : "http")}://{subnet}.{i}:{(tls ? 5001 : 5000)}/");
            if (tls || NetworkPolicy.IsPlaintextAllowed(uri, mode)) yield return uri.AbsoluteUri;
        }
    }
}
