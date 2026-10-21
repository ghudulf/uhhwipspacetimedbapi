using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

/// <summary>Client configuration boundary; classification itself remains pure in Contracts.</summary>
public static class ClientNetworkPolicy
{
    public static PlaintextPolicy Mode
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("BRU_PLAINTEXT_POLICY");
            if (!NetworkPolicy.IsValidMode(value)) throw new InvalidOperationException("BRU_PLAINTEXT_POLICY must be never, loopback, lan or any.");
            return NetworkPolicy.ParseMode(value);
        }
    }

    public static bool IsHttpAllowed(Uri uri)
    {
        var mode = Mode; // Validate configuration even when this endpoint already uses TLS.
        return uri.IsAbsoluteUri && uri.UserInfo.Length == 0 &&
            (uri.Scheme == "https" || uri.Scheme == "http" && NetworkPolicy.IsPlaintextAllowed(uri, mode));
    }
}
