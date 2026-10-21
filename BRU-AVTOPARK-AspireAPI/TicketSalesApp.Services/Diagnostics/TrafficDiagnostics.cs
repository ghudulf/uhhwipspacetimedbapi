using System.Text;
using System.Text.Json;
using BRU.Avtopark.Contracts;
using Microsoft.Extensions.Configuration;

namespace TicketSalesApp.Services.Diagnostics;

public static class TrafficDiagnostics
{
    private static TrafficOptions? configured;
    public static TrafficOptions Options => configured ?? ClientOptions();
    public static TrafficOptions ClientOptions() => new(TrafficOptions.ParseLevel(Environment.GetEnvironmentVariable("BRU_DEBUG_TRAFFIC")),
        Positive(Environment.GetEnvironmentVariable("BRU_DEBUG_TRAFFIC_MAX_BYTES"), 65536));
    public static TrafficOptions ServerOptions(IConfiguration config) => new(TrafficOptions.ParseLevel(config["Diagnostics:Traffic:Level"]),
        Positive(config["Diagnostics:Traffic:MaxBodyBytes"], 65536), config.GetValue("Diagnostics:Traffic:Websocket", true),
        config.GetValue("Diagnostics:Traffic:Rows", false), Positive(config["Diagnostics:Traffic:MaxRows"], 20));
    private static int Positive(string? value, int fallback) => string.IsNullOrWhiteSpace(value) ? fallback :
        int.TryParse(value, out var count) && count is > 0 and <= 16777216 ? count : throw new ArgumentException("Traffic limits must be positive integers no greater than 16777216.");
    public static void Configure(TrafficOptions options) => configured = options;
    public static bool Enabled => Options.Level != TrafficLevel.Off;
    public static string Json(string? raw) => DebugPayload.Json(raw, Options.Level, Options.MaxBodyBytes);
    public static string AuthJson(string? raw) => DebugPayload.Json(raw, Options.Level, Options.MaxBodyBytes, authentication: true);
    public static string Url(string raw) => DebugPayload.Url(raw, Options.Level);
    public static string Serialize(object? value, TrafficOptions? options = null)
    {
        options ??= Options;
        try { return DebugPayload.Json(JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true }), options.Level, options.MaxBodyBytes); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException) { return "[unserializable " + value?.GetType().Name + "]"; }
    }
    public static void Frame(Microsoft.Extensions.Logging.ILogger logger, string direction, string raw)
    {
        if (!Enabled || !Options.Websocket) return;
        Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(logger, "WebSocket {Direction} Frame={Frame}", direction,
            DebugPayload.Json(raw, Options.Level, Options.MaxBodyBytes, raw.Contains("totp", StringComparison.OrdinalIgnoreCase)));
    }
    public static string ReducerArguments(object args, string operation, TrafficOptions options)
    {
        var values = args.GetType().GetFields().ToDictionary(field => field.Name, field => field.GetValue(args));
        if (options.Level == TrafficLevel.Redacted && operation.Contains("token", StringComparison.OrdinalIgnoreCase))
            foreach (var key in values.Keys.ToArray())
                if (key.EndsWith("Hash", StringComparison.OrdinalIgnoreCase)) values[key] = DebugPayload.Mask(values[key]?.ToString()?.Length ?? 0);
        return Serialize(values, options);
    }
    public static string Snapshot(object? value, TrafficOptions options)
    {
        var items = value as System.Collections.IEnumerable;
        if (value is string) items = null;
        items ??= value?.GetType().GetProperty("Items")?.GetValue(value) as System.Collections.IEnumerable;
        if (items == null) return "type=" + (value?.GetType().Name ?? "null") + (options.Rows || options.Level == TrafficLevel.Full ? " value=" + Serialize(value, options) : "");
        var count = items is System.Collections.ICollection collection ? collection.Count : items.Cast<object?>().Count();
        return "rows=" + count + (options.Rows || options.Level == TrafficLevel.Full ? " preview=" + Serialize(items.Cast<object?>().Take(options.MaxRows).ToArray(), options) : "");
    }
    public static string Body(byte[] preview, long length, string? contentType, TrafficOptions options, bool authentication = false)
    {
        if (options.Level == TrafficLevel.Off) return "";
        if (!IsText(contentType)) return DebugPayload.Binary(length, options.Level);
        var raw = Encoding.UTF8.GetString(preview);
        var result = DebugPayload.Json(raw, options.Level, options.MaxBodyBytes, authentication);
        return length > preview.Length && !result.Contains("…[truncated ", StringComparison.Ordinal)
            ? result + $"…[truncated {length - preview.Length} bytes]" : result;
    }
    public static bool IsText(string? contentType) => contentType != null &&
        (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
         contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase));
}
