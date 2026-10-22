using System.Net;
using System.Net.Sockets;

namespace BRU.Avtopark.Contracts;

public enum PlaintextPolicy { Never, Loopback, Lan, Any }

/// <summary>Pure, DNS-free classification. LAN means any host LAN-scoped by address or name.</summary>
public static class NetworkPolicy
{
    public static bool IsValidMode(string? value) => string.IsNullOrWhiteSpace(value) ||
        value.Trim().ToLowerInvariant() is "never" or "loopback" or "lan" or "any";

    // Missing configuration defaults to LAN; invalid configuration always fails closed.
    public static PlaintextPolicy ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "lan" => PlaintextPolicy.Lan,
        "loopback" => PlaintextPolicy.Loopback,
        "any" => PlaintextPolicy.Any,
        _ => PlaintextPolicy.Never
    };

    public static bool IsPlaintextAllowed(Uri uri, PlaintextPolicy mode) => uri.IsAbsoluteUri &&
        uri.UserInfo.Length == 0 && IsPlaintextAllowed(uri.IdnHost, mode);

    public static bool IsPlaintextAllowed(string host, PlaintextPolicy mode)
    {
        if (string.IsNullOrWhiteSpace(host) || host != host.Trim() || host.IndexOfAny(['@', '/', '\\', '#', '?', '%']) >= 0) return false;
        host = host.TrimEnd('.');
        if (IPAddress.TryParse(host, out var address)) return IsPlaintextAllowed(address, mode);
        if (Uri.CheckHostName(host) != UriHostNameType.Dns) return false;
        if (mode == PlaintextPolicy.Never) return false;
        if (mode == PlaintextPolicy.Any) return true;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (mode != PlaintextPolicy.Lan) return false;
        return !host.Contains('.') || new[] { ".local", ".lan", ".home.arpa", ".internal", ".localdomain" }
            .Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsPlaintextAllowed(IPAddress address, PlaintextPolicy mode)
    {
        if (mode == PlaintextPolicy.Never) return false;
        if (mode == PlaintextPolicy.Any) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (mode != PlaintextPolicy.Lan) return false;
        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
              bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 169 && bytes[1] == 254 ||
              bytes[0] == 100 && bytes[1] is >= 64 and <= 127
            : address.AddressFamily == AddressFamily.InterNetworkV6 &&
              ((bytes[0] & 0xfe) == 0xfc || bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80);
    }
}
