using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BRU.Avtopark.Contracts;

public enum TrafficLevel { Off, Redacted, Full }

public sealed record TrafficOptions(TrafficLevel Level = TrafficLevel.Redacted, int MaxBodyBytes = 65536,
    bool Websocket = true, bool Rows = false, int MaxRows = 20)
{
    public static TrafficLevel ParseLevel(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "redacted" => TrafficLevel.Redacted,
        "off" => TrafficLevel.Off,
        "full" => TrafficLevel.Full,
        _ => throw new ArgumentException("Traffic level must be off, redacted or full.")
    };
}

/// <summary>Pure traffic formatting. Business data is retained; only credential values are masked.</summary>
public static class DebugPayload
{
    private static readonly HashSet<string> Credentials = new(StringComparer.Ordinal)
    {
        "password", "currentpassword", "newpassword", "passwd", "secret", "clientsecret", "token", "accesstoken",
        "refreshtoken", "idtoken", "temptoken", "authorization", "cookie", "setcookie", "flowsecret", "pollsecret",
        "approvalsecret", "browsertoken", "verificationcode", "totpcode", "otp", "pin", "signature", "assertion", "privatekey", "apikey"
    };
    private static string Normalize(string name) => name.Replace("_", "").Replace("-", "").ToLowerInvariant();
    // Administrative initial credentials are never diagnostic data, even in explicit full mode.
    private static bool IsInitialCredential(string name) => Normalize(name) is "initialcredential" or "initialpassword";
    public static bool IsCredential(string name)
    {
        var normalized = Normalize(name);
        // Reducer hash arguments are still credentials, not ordinary business identifiers.
        if (normalized.EndsWith("hash", StringComparison.Ordinal)) normalized = normalized[..^4];
        return IsInitialCredential(name) || Credentials.Contains(normalized) || normalized.EndsWith("token", StringComparison.Ordinal) ||
            normalized.EndsWith("secret", StringComparison.Ordinal) || normalized.EndsWith("password", StringComparison.Ordinal);
    }
    public static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
    public static string Mask(int length) => $"***({length})";

    public static string Json(string? raw, TrafficLevel level = TrafficLevel.Redacted, int max = 65536, bool authentication = false, bool response = false)
    {
        if (level == TrafficLevel.Off) return "";
        raw ??= "null";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(raw);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 128 });
            var masked = false;
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject: writer.WriteStartObject(); break;
                        case JsonTokenType.EndObject: writer.WriteEndObject(); break;
                        case JsonTokenType.StartArray: writer.WriteStartArray(); break;
                        case JsonTokenType.EndArray: writer.WriteEndArray(); break;
                        case JsonTokenType.PropertyName:
                            var name = reader.GetString()!;
                            writer.WritePropertyName(name);
                            if (IsInitialCredential(name) || level != TrafficLevel.Full && (IsCredential(name) || authentication && (Normalize(name) is "credential" or "qrpng" or "qrpayload" || !response && Normalize(name) == "code")))
                            {
                                masked = true;
                                if (!reader.Read()) throw new JsonException();
                                var start = reader.TokenStartIndex;
                                var length = reader.TokenType == JsonTokenType.String ? reader.GetString()!.Length : -1;
                                reader.Skip();
                                writer.WriteStringValue(Mask(length >= 0 ? length : checked((int)(reader.BytesConsumed - start))));
                            }
                            break;
                        case JsonTokenType.String:
                            var value = reader.GetString()!;
                            writer.WriteStringValue(Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? Url(value, level, authentication) : value);
                            break;
                        case JsonTokenType.Number: writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true); break;
                        case JsonTokenType.True: writer.WriteBooleanValue(true); break;
                        case JsonTokenType.False: writer.WriteBooleanValue(false); break;
                        case JsonTokenType.Null: writer.WriteNullValue(); break;
                    }
                }
                if (reader.BytesConsumed == 0) throw new JsonException();
            }
            return Truncate(level == TrafficLevel.Full && !masked ? raw : Encoding.UTF8.GetString(output.ToArray()), max);
        }
        catch (JsonException) { return Truncate(Text(raw, authentication && !response, level), max); }
    }

    // Handles form data and malformed/partial JSON without silently dropping debugging data.
    private static string Text(string raw, bool authentication, TrafficLevel level) => Regex.Replace(raw,
        "(?<key>[\\w-]+)(?<separator>\\\"?\\s*[:=]\\s*)(?:\\\"(?<quoted>(?:\\\\.|[^\\\"\\\\])*)(?:\\\"|$)|(?<value>[^&\\s,}]+))",
        match => IsInitialCredential(match.Groups["key"].Value) || level != TrafficLevel.Full && (IsCredential(match.Groups["key"].Value) || authentication && Normalize(match.Groups["key"].Value) is "code" or "credential")
            ? match.Groups["key"].Value + match.Groups["separator"].Value + Mask(match.Groups["quoted"].Success ? match.Groups["quoted"].Length : match.Groups["value"].Length) : match.Value,
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Truncate(string raw, int max)
    {
        max = Math.Max(0, max);
        var bytes = Encoding.UTF8.GetBytes(raw);
        if (bytes.Length <= max) return raw;
        var count = max;
        while (count > 0 && (bytes[count] & 0xc0) == 0x80) count--;
        return Encoding.UTF8.GetString(bytes, 0, count) + $"…[truncated {bytes.Length - count} bytes]";
    }

    public static string Binary(long bytes, TrafficLevel level = TrafficLevel.Redacted) => level == TrafficLevel.Off ? "" : $"[binary {bytes} bytes]";

    public static IReadOnlyDictionary<string, string> Headers(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, TrafficLevel level = TrafficLevel.Redacted)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (level == TrafficLevel.Off) return result;
        foreach (var (name, values) in headers)
        {
            var value = string.Join(", ", values);
            if (level != TrafficLevel.Full && name.ToLowerInvariant() is "authorization" or "cookie" or "set-cookie" or "x-api-key")
            {
                var label = name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && value.Contains(' ') ? value.Split(' ')[0] :
                    name.Contains("cookie", StringComparison.OrdinalIgnoreCase) && value.Contains('=') ? string.Join(";", value.Split(';').Select(item => item.Split('=')[0].Trim())) : name;
                value = label + " sha256:" + Fingerprint(value);
            }
            result[name] = value;
        }
        return result;
    }

    public static string Url(string raw, TrafficLevel level = TrafficLevel.Redacted, bool authentication = false)
    {
        if (level == TrafficLevel.Off) return "";
        if (level == TrafficLevel.Full) return raw;
        return Regex.Replace(raw, "(?<prefix>[?&])(?<key>[^=&#]+)=(?<value>[^&#]*)",
            match => new[] { "access_token", "token", "code", "state", "id_token", "refresh_token", "client_secret" }.Contains(Uri.UnescapeDataString(match.Groups["key"].Value), StringComparer.OrdinalIgnoreCase) || authentication && Uri.UnescapeDataString(match.Groups["key"].Value) == "b"
                ? match.Groups["prefix"].Value + match.Groups["key"].Value + "=" + Mask(match.Groups["value"].Length) : match.Value,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
