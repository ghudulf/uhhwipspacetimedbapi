using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

public sealed class QrLoginException(string code, int status = 0, int? attemptsRemaining = null) : Exception(Safe(code))
{
    private static string Safe(string value) => value.Length is > 0 and <= 64 && value.All(c => c is >= 'a' and <= 'z' or '_') ? value : "invalid_response";
    public string Code { get; } = Safe(code);
    public int Status { get; } = status;
    public int? AttemptsRemaining { get; } = attemptsRemaining;
}
public sealed record QrPoll(string State, JsonElement? Login = null);
public interface IQrLoginApi
{
    string ApiBase { get; }
    Task<QrCreateResponse> CreateSessionAsync(string deviceName, string platform, string? username = null, string? deviceId = null, CancellationToken cancellationToken = default);
    Task<QrPoll> StatusAsync(string id, string pollSecret, CancellationToken cancellationToken = default);
    Task CancelAsync(string id, string pollSecret, CancellationToken cancellationToken = default);
    Task<QrPeekResponse> PeekAsync(string id, string approvalSecret, CancellationToken cancellationToken = default);
    Task ApproveAsync(string id, string approvalSecret, string code, string bearer, CancellationToken cancellationToken = default);
    Task DenyAsync(string id, string approvalSecret, CancellationToken cancellationToken = default);
    Task CompleteAsync(string id, string approvalSecret, string code, string deviceId, string signature, CancellationToken cancellationToken = default);
    Task<QrDeviceResponse> EnrollDeviceAsync(string bearer, string name, string platform, string spkiBase64, CancellationToken cancellationToken = default);
    Task<QrDeviceInfo[]> ListDevicesAsync(string bearer, CancellationToken cancellationToken = default);
    Task RevokeDeviceAsync(string bearer, string deviceId, CancellationToken cancellationToken = default);
}

/// <summary>The supplied HttpClient MUST use a handler with AllowAutoRedirect=false. This class never owns that client.</summary>
public sealed class QrLoginClient : IQrLoginApi
{
    public const int MaxResponseBytes = 65536;
    private readonly HttpClient http;
    private readonly Uri root;
    public string ApiBase { get; }
    public QrLoginClient(HttpClient http, string apiBase)
    {
        this.http = http;
        try
        {
            root = ClientEndpoints.ServerRoot(apiBase);
            if (root.Scheme != "https" && !(root.Scheme == "http" && root.IsLoopback)) throw new ArgumentException();
            ApiBase = root.AbsoluteUri;
        }
        catch { throw new QrLoginException("invalid_server"); }
    }
    private static string Session(string id, string action)
    {
        if (!QrLoginProtocol.TryDecode(id, 16, out _)) throw new QrLoginException("invalid_request");
        return "sessions/" + id + "/" + action;
    }
    private async Task<T> Send<T>(HttpMethod method, string path, object? body, string? bearer, CancellationToken cancellationToken, HttpStatusCode? expectedStatus = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var uri = new Uri(root, "api/auth/qr/" + path);
            using var request = new HttpRequestMessage(method, uri);
            if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (body != null)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(body, ApiProtocol.Json);
                if (bytes.Length > 4096) throw new QrLoginException("invalid_request");
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 || response.RequestMessage?.RequestUri is { } final && final != uri)
                throw new QrLoginException("redirect_rejected", (int)response.StatusCode);
            if (response.IsSuccessStatusCode && expectedStatus != null && response.StatusCode != expectedStatus) throw new QrLoginException("invalid_response");
            if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new QrLoginException("invalid_response");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var content = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (content.Length + count > MaxResponseBytes) throw new QrLoginException("invalid_response");
                content.Write(buffer, 0, count);
            }
            var data = content.ToArray();
            if (!response.IsSuccessStatusCode)
            {
                var code = "invalid_response"; int? attempts = null;
                try
                {
                    using var json = JsonDocument.Parse(data);
                    if (json.RootElement.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String) code = value.GetString() ?? code;
                    if (json.RootElement.TryGetProperty("attemptsRemaining", out var remaining) && remaining.TryGetInt32(out var number)) attempts = Math.Clamp(number, 0, 3);
                }
                catch (JsonException) { }
                throw new QrLoginException(code, (int)response.StatusCode, attempts);
            }
            if (typeof(T) == typeof(bool)) return (T)(object)true;
            return JsonSerializer.Deserialize<T>(data, ApiProtocol.Json) ?? throw new QrLoginException("invalid_response");
        }
        catch (QrLoginException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException) { throw new QrLoginException("network_error"); }
        catch { throw new QrLoginException("invalid_response"); }
    }
    public Task<QrCreateResponse> CreateSessionAsync(string deviceName, string platform, string? username = null, string? deviceId = null, CancellationToken cancellationToken = default) => Send<QrCreateResponse>(HttpMethod.Post, "sessions", new QrCreateRequest(deviceName, platform, username, deviceId), null, cancellationToken);
    public Task<QrPoll> StatusAsync(string id, string pollSecret, CancellationToken cancellationToken = default) => Send<QrPoll>(HttpMethod.Post, Session(id, "status"), new QrPollRequest(pollSecret), null, cancellationToken);
    public Task CancelAsync(string id, string pollSecret, CancellationToken cancellationToken = default) => Send<bool>(HttpMethod.Post, Session(id, "cancel"), new QrPollRequest(pollSecret), null, cancellationToken);
    public Task<QrPeekResponse> PeekAsync(string id, string approvalSecret, CancellationToken cancellationToken = default) => Send<QrPeekResponse>(HttpMethod.Post, Session(id, "peek"), new QrSecretRequest(approvalSecret), null, cancellationToken);
    public Task ApproveAsync(string id, string approvalSecret, string code, string bearer, CancellationToken cancellationToken = default) => Send<bool>(HttpMethod.Post, Session(id, "approve"), new QrApproveRequest(approvalSecret, code), bearer, cancellationToken);
    public Task DenyAsync(string id, string approvalSecret, CancellationToken cancellationToken = default) => Send<bool>(HttpMethod.Post, Session(id, "deny"), new QrSecretRequest(approvalSecret), null, cancellationToken);
    public Task CompleteAsync(string id, string approvalSecret, string code, string deviceId, string signature, CancellationToken cancellationToken = default) => Send<bool>(HttpMethod.Post, Session(id, "complete"), new QrCompleteRequest(approvalSecret, code, deviceId, signature), null, cancellationToken);
    public Task<QrDeviceResponse> EnrollDeviceAsync(string bearer, string name, string platform, string spkiBase64, CancellationToken cancellationToken = default) => Send<QrDeviceResponse>(HttpMethod.Post, "devices", new QrEnrollRequest(name, platform, spkiBase64), bearer, cancellationToken, HttpStatusCode.Created);
    public Task<QrDeviceInfo[]> ListDevicesAsync(string bearer, CancellationToken cancellationToken = default) => Send<QrDeviceInfo[]>(HttpMethod.Get, "devices", null, bearer, cancellationToken);
    public Task RevokeDeviceAsync(string bearer, string deviceId, CancellationToken cancellationToken = default) => Send<bool>(HttpMethod.Delete, "devices/" + Uri.EscapeDataString(deviceId), null, bearer, cancellationToken);
}
