using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace BRU.Avtopark.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrCreateRequest(string DeviceName, string Platform, string? Username = null, string? DeviceId = null);
public sealed record QrCreateResponse(string SessionId, string PollSecret, string ApprovalSecret, string QrPayload, string QrPng, string VerificationCode, DateTimeOffset ExpiresAt, int PollIntervalMs);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrPollRequest(string? PollSecret = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrSecretRequest(string? ApprovalSecret = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrApproveRequest(string? ApprovalSecret = null, string? VerificationCode = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrCompleteRequest(string? ApprovalSecret = null, string? VerificationCode = null, string? DeviceId = null, string? Signature = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrEnrollRequest(string DeviceName, string Platform, string PublicKey);
public sealed record QrDeviceResponse(string DeviceId);
public sealed record QrDeviceInfo(string DeviceId, string DeviceName, string Platform, ulong CreatedAt, ulong? LastUsedAt);
public sealed record QrStatusResponse(string State, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Login = null);
public sealed record QrPeekResponse(string DeviceName, string Platform, DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt, string RequestedFrom, string Origin, string? BoundUsername, int CodeAttemptsRemaining);
public sealed record QrPayload(string SessionId, string ApprovalSecret, string Origin);

public static class QrLoginProtocol
{
    public const string Version = "bru-qr/1";
    public const string Scheme = "bruqr";
    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static bool TryDecode(string? value, int bytes, out byte[] decoded)
    {
        decoded = [];
        if (value == null || value.Length != (bytes * 8 + 5) / 6 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return false;
        try { decoded = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4)); }
        catch (FormatException) { return false; }
        return decoded.Length == bytes && Base64Url(decoded) == value;
    }
    public static bool ValidOrigin(string? origin) => origin is { Length: > 0 and <= 512 } &&
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && !origin.Any(char.IsControl) && !origin.Contains('\\');
    public static string BuildPayload(string sessionId, string approvalSecret, string origin)
    {
        if (!TryDecode(sessionId, 16, out _) || !TryDecode(approvalSecret, 32, out _) || !ValidOrigin(origin)) throw new ArgumentException("Invalid QR payload.");
        return $"bruqr://v1/session?s={sessionId}&k={approvalSecret}&o={Uri.EscapeDataString(origin)}";
    }
    public static bool TryParsePayload(string? text, out QrPayload payload)
    {
        payload = null!;
        if (text == null || text.Length > 2048 || !text.StartsWith("bruqr://v1/session?", StringComparison.Ordinal) ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Scheme || uri.Host != "v1" || uri.AbsolutePath != "/session" || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 || uri.Port != -1) return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query[1..].Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || pair[0] is not ("s" or "k" or "o") || !values.TryAdd(pair[0], Uri.UnescapeDataString(pair[1]))) return false;
        }
        if (values.Count != 3 || !TryDecode(values.GetValueOrDefault("s"), 16, out _) || !TryDecode(values.GetValueOrDefault("k"), 32, out _) || !ValidOrigin(values.GetValueOrDefault("o"))) return false;
        payload = new(values["s"], values["k"], values["o"]);
        return true;
    }
    public static byte[] SigningMessage(string sessionId, string approvalSecret, string origin, string username, string deviceId, string verificationCode) =>
        Encoding.UTF8.GetBytes(string.Join('\n', Version, sessionId, approvalSecret, origin, username.ToLowerInvariant(), deviceId, verificationCode));
    public static string DeviceIdFor(byte[] spki) => Base64Url(SHA256.HashData(spki)[..16]);
    public static string MaskIp(string address)
    {
        if (!IPAddress.TryParse(address, out var ip)) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var bytes = ip.GetAddressBytes();
        return bytes.Length == 4 ? $"{bytes[0]}.{bytes[1]}.{bytes[2]}.x" :
            string.Join(':', Enumerable.Range(0, 3).Select(i => ((bytes[i * 2] << 8) | bytes[i * 2 + 1]).ToString("x", CultureInfo.InvariantCulture))) + ":x";
    }
}
