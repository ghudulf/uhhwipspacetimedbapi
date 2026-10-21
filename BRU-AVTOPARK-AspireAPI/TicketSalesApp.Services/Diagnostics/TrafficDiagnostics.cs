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
    public static string Url(string raw) => DebugPayload.Url(raw, Options.Level);
    public static string Serialize(object? value) => Json(JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true }));
    public static string Body(byte[] preview, long length, string? contentType, TrafficOptions options)
    {
        if (options.Level == TrafficLevel.Off) return "";
        if (!IsText(contentType)) return DebugPayload.Binary(length, options.Level);
        var raw = Encoding.UTF8.GetString(preview);
        var result = DebugPayload.Json(raw, options.Level, options.MaxBodyBytes);
        return length > preview.Length && !result.Contains("…[truncated ", StringComparison.Ordinal)
            ? result + $"…[truncated {length - preview.Length} bytes]" : result;
    }
    public static bool IsText(string? contentType) => contentType != null &&
        (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
         contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase));
}
