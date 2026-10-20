using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TicketSalesApp.Services.Diagnostics;

/// <summary>Preserves JSON shape for troubleshooting without exporting credential values.
/// Fingerprints correlate sensitive identifiers only within the current process lifetime.</summary>
public static class SafeDiagnostics
{
    private static readonly byte[] CorrelationKey = RandomNumberGenerator.GetBytes(32);
    private static readonly HashSet<string> IdentifierNames = new(StringComparer.OrdinalIgnoreCase)
        { "sub", "subject", "identity", "email", "name", "phone_number", "username", "xuid", "userId" };
    private static readonly HashSet<string> NumericMetadata = new(StringComparer.OrdinalIgnoreCase)
        { "iat", "exp", "nbf", "expires_in", "count", "revision", "statusCode", "durationMs" };

    public static string Url(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" ? uri.Scheme + "://" + uri.Authority + uri.AbsolutePath : "[URI omitted]";

    public static string Fingerprint(string value) => Convert.ToHexString(
        HMACSHA256.HashData(CorrelationKey, Encoding.UTF8.GetBytes(value)))[..16];

    public static string JsonShape(string? json)
    {
        if (json == null) return "null";
        if (json.Length > 64 * 1024) return "[oversized JSON omitted]";
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output)) Write(writer, document.RootElement, "");
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (JsonException) { return "[non-JSON content omitted]"; }
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value, string name)
    {
        // Redact whole credential containers, including arrays/objects, not just strings.
        if (IsCredential(name)) { writer.WriteStringValue("[redacted]"); return; }
        if (IdentifierNames.Contains(name))
        { writer.WriteStringValue("fingerprint:" + Fingerprint(value.GetRawText())); return; }
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                { writer.WritePropertyName(property.Name); Write(writer, property.Value, property.Name); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Write(writer, item, name);
                writer.WriteEndArray(); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            case JsonValueKind.Number when NumericMetadata.Contains(name): value.WriteTo(writer); break;
            case JsonValueKind.True or JsonValueKind.False: value.WriteTo(writer); break;
            default: writer.WriteStringValue("[redacted]"); break;
        }
    }

    private static bool IsCredential(string name) => name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase) || name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("assertion", StringComparison.OrdinalIgnoreCase) || name.Contains("credential", StringComparison.OrdinalIgnoreCase)
        || name.Equals("code", StringComparison.OrdinalIgnoreCase) || name.StartsWith("code_", StringComparison.OrdinalIgnoreCase)
        || name.Equals("authorization", StringComparison.OrdinalIgnoreCase) || name.Equals("cookie", StringComparison.OrdinalIgnoreCase)
        || name.Equals("nonce", StringComparison.OrdinalIgnoreCase) || name.Equals("challenge", StringComparison.OrdinalIgnoreCase)
        || name.Equals("state", StringComparison.OrdinalIgnoreCase) || name.Equals("pin", StringComparison.OrdinalIgnoreCase);
}
