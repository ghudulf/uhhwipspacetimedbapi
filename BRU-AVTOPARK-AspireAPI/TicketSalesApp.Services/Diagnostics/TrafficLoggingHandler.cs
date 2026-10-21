using System.Diagnostics;
using System.Net.Http;
using BRU.Avtopark.Contracts;
using Serilog;

namespace TicketSalesApp.Services.Diagnostics;

/// <summary>Logs capped raw HTTP previews, replaying every byte to the sender/caller.</summary>
public sealed class TrafficLoggingHandler : DelegatingHandler
{
    private readonly TrafficOptions? options;
    public event Action<string>? Logged;
    public TrafficLoggingHandler(HttpMessageHandler? inner = null, TrafficOptions? options = null) : base(inner ?? new HttpClientHandler { AllowAutoRedirect = false }) => this.options = options;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = options ?? TrafficDiagnostics.ClientOptions();
        if (settings.Level == TrafficLevel.Off) return await base.SendAsync(request, cancellationToken);
        var started = Stopwatch.GetTimestamp();
        var correlation = request.Headers.TryGetValues("Trace-Id", out var values) ? values.FirstOrDefault() : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var url = DebugPayload.Url(request.RequestUri?.ToString() ?? "", settings.Level);
        var body = await PreviewAsync(request.Content, replacement => request.Content = replacement, settings, request.RequestUri, cancellationToken);
        var headers = HeaderJson(request.Headers.Concat(request.Content?.Headers.AsEnumerable() ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()), settings);
        Log.Debug("HTTP client request {Method} {Url} TraceId={TraceId} Headers={Headers} Body={Body}", request.Method, url, correlation, headers, body);
        Logged?.Invoke($"HTTP client request {request.Method} {url} TraceId={correlation} Headers={headers} Body={body}");
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            body = await PreviewAsync(response.Content, replacement => response.Content = replacement, settings, request.RequestUri, cancellationToken, response: true);
            headers = HeaderJson(response.Headers.Concat(response.Content.Headers), settings);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Log.Debug("HTTP client response {Method} {Url} Status={StatusCode} TraceId={TraceId} ElapsedMs={ElapsedMs} Headers={Headers} Body={Body}", request.Method, url, (int)response.StatusCode, correlation, elapsed, headers, body);
            Logged?.Invoke($"HTTP client response {request.Method} {url} Status={(int)response.StatusCode} TraceId={correlation} ElapsedMs={elapsed} Headers={headers} Body={body}");
            return response;
        }
        catch (Exception ex)
        {
            Log.Debug("HTTP client failure {Method} {Url} TraceId={TraceId} ElapsedMs={ElapsedMs} Error={ErrorType}", request.Method, url, correlation, Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.GetType().Name);
            throw;
        }
    }
    private static string HeaderJson(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, TrafficOptions options) => System.Text.Json.JsonSerializer.Serialize(DebugPayload.Headers(headers, options.Level));
    private static async Task<string> PreviewAsync(HttpContent? content, Action<HttpContent> replace, TrafficOptions options, Uri? uri, CancellationToken ct, bool response = false)
    {
        if (content == null) return "";
        var type = content.Headers.ContentType?.MediaType;
        if (type == "text/event-stream" || type == "text/html" && (options.Level != TrafficLevel.Full || uri?.AbsolutePath.Contains("/auth/", StringComparison.OrdinalIgnoreCase) == true)) return "[skipped streaming/HTML]";
        if (!TrafficDiagnostics.IsText(type)) return DebugPayload.Binary(content.Headers.ContentLength ?? 0, options.Level);
        var stream = await content.ReadAsStreamAsync(ct);
        var prefix = new byte[options.MaxBodyBytes + 1];
        var count = await stream.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, ct);
        Array.Resize(ref prefix, count);
        var replay = new StreamContent(new TrafficReplayStream(prefix, stream));
        foreach (var header in content.Headers) replay.Headers.TryAddWithoutValidation(header.Key, header.Value);
        replace(replay);
        return TrafficDiagnostics.Body(prefix, content.Headers.ContentLength ?? count, type, options, uri?.AbsolutePath.Contains("/auth/", StringComparison.OrdinalIgnoreCase) == true || uri?.AbsolutePath.Contains("/token", StringComparison.OrdinalIgnoreCase) == true, response);
    }
}
