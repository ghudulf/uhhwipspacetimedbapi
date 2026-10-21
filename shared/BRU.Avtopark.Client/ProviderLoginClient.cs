using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

public sealed class ProviderLoginException(string code, int status = 0) : Exception(Safe(code))
{
    private static string Safe(string code) => code.Length is > 0 and <= 64 && code.All(c => c is >= 'a' and <= 'z' or '_') ? code : "invalid_response";
    public string Code { get; } = Safe(code);
    public int Status { get; } = status;
}
public sealed record ProviderCapabilities(bool Entra, bool Passkeys, bool Windows, bool Qr);
public sealed record ProviderStart(string FlowId, string FlowSecret, DateTimeOffset ExpiresAt, int PollIntervalMs, string? AuthorizationUrl = null, string? BrowserUrl = null);
public sealed record ProviderPoll(string State, JsonElement? Login = null, bool? Linked = null, bool? Registered = null, string? Code = null);
public sealed record ProviderLinkStatus(string State);
public sealed record PasskeyCredentialInfo(string Id, DateTime CreatedAt);
public interface IPasskeyManagementApi
{
    Task<PasskeyCredentialInfo[]> ListCredentialsAsync(string bearer, CancellationToken cancellationToken = default);
    Task RemoveCredentialAsync(string bearer, string credentialId, CancellationToken cancellationToken = default);
}
public interface IProviderLoginApi
{
    string ApiBase { get; }
    Task<ProviderCapabilities> CapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<ProviderStart> StartAsync(string provider, string purpose, string? bearer = null, string? username = null, CancellationToken cancellationToken = default);
    Task<ProviderPoll> CompleteAsync(string provider, string id, string secret, CancellationToken cancellationToken = default);
    Task CancelAsync(string provider, string id, string secret, CancellationToken cancellationToken = default);
    Task<ProviderLinkStatus> EntraLinkStatusAsync(string bearer, CancellationToken cancellationToken = default);
    Task UnlinkEntraAsync(string bearer, CancellationToken cancellationToken = default);
}

/// <summary>Injected clients must disable automatic redirects. Never owns the supplied HttpClient.</summary>
public sealed class ProviderLoginClient : IProviderLoginApi, IPasskeyManagementApi
{
    private readonly HttpClient http;
    private readonly Uri root;
    public string ApiBase => root.AbsoluteUri;
    public ProviderLoginClient(HttpClient http, string apiBase)
    {
        this.http = http;
        try
        {
            root = ClientEndpoints.ServerRoot(apiBase);
            if (root.Scheme != "https" && !(root.Scheme == "http" && root.IsLoopback)) throw new ArgumentException();
        }
        catch { throw new ProviderLoginException("invalid_server"); }
    }
    private static string Provider(string provider) => provider is "entra" or "passkey" ? provider : throw new ProviderLoginException("invalid_request");
    private static string Flow(string provider, string id, string action) => QrLoginProtocol.TryDecode(id, 16, out _) ? Provider(provider) + "/flows/" + id + "/" + action : throw new ProviderLoginException("invalid_request");
    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? bearer, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var uri = new Uri(root, "api/auth/" + path);
            using var request = new HttpRequestMessage(method, uri);
            if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (body != null)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(body, ApiProtocol.Json);
                if (bytes.Length > 4096) throw new ProviderLoginException("invalid_request");
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 || response.RequestMessage?.RequestUri is { } final && final != uri) throw new ProviderLoginException("redirect_rejected");
            if (response.Content.Headers.ContentLength > 65536) throw new ProviderLoginException("invalid_response");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var content = new MemoryStream();
            var buffer = new byte[4096]; int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (content.Length + read > 65536) throw new ProviderLoginException("invalid_response");
                content.Write(buffer, 0, read);
            }
            if (!response.IsSuccessStatusCode)
            {
                var code = "invalid_response";
                try { using var json = JsonDocument.Parse(content.ToArray()); code = json.RootElement.GetProperty("code").GetString() ?? code; } catch { }
                throw new ProviderLoginException(code, (int)response.StatusCode);
            }
            if (typeof(T) == typeof(bool)) return (T)(object)true;
            return JsonSerializer.Deserialize<T>(content.ToArray(), ApiProtocol.Json) ?? throw new ProviderLoginException("invalid_response");
        }
        catch (ProviderLoginException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException) { throw new ProviderLoginException("network_error"); }
        catch { throw new ProviderLoginException("invalid_response"); }
    }
    public Task<ProviderCapabilities> CapabilitiesAsync(CancellationToken cancellationToken = default) => SendAsync<ProviderCapabilities>(HttpMethod.Get, "providers", null, null, cancellationToken);
    public Task<ProviderStart> StartAsync(string provider, string purpose, string? bearer = null, string? username = null, CancellationToken cancellationToken = default) =>
        SendAsync<ProviderStart>(HttpMethod.Post, Provider(provider) + "/start", provider == "entra" ? new { purpose } : (object)new { purpose, username }, bearer, cancellationToken);
    public Task<ProviderPoll> CompleteAsync(string provider, string id, string secret, CancellationToken cancellationToken = default) => SendAsync<ProviderPoll>(HttpMethod.Post, Flow(provider, id, "complete"), new { flowSecret = secret }, null, cancellationToken);
    public Task CancelAsync(string provider, string id, string secret, CancellationToken cancellationToken = default) => SendAsync<bool>(HttpMethod.Post, Flow(provider, id, "cancel"), new { flowSecret = secret }, null, cancellationToken);
    public Task<ProviderLinkStatus> EntraLinkStatusAsync(string bearer, CancellationToken cancellationToken = default) => SendAsync<ProviderLinkStatus>(HttpMethod.Post, "entra/link/status", new { }, bearer, cancellationToken);
    public Task UnlinkEntraAsync(string bearer, CancellationToken cancellationToken = default) => SendAsync<bool>(HttpMethod.Post, "entra/unlink", new { }, bearer, cancellationToken);
    public async Task<PasskeyCredentialInfo[]> ListCredentialsAsync(string bearer, CancellationToken cancellationToken = default)
    {
        // Legacy management envelope contains message/success/data; retain that server contract.
        var envelope = await SendAsync<JsonElement>(HttpMethod.Get, "webauthn/credentials", null, bearer, cancellationToken);
        try
        {
            if (!envelope.GetProperty("success").GetBoolean()) throw new ProviderLoginException("management_failed");
            return envelope.GetProperty("data").GetProperty("credentials").Deserialize<PasskeyCredentialInfo[]>(ApiProtocol.Json) ?? [];
        }
        catch (ProviderLoginException) { throw; }
        catch { throw new ProviderLoginException("invalid_response"); }
    }
    public Task RemoveCredentialAsync(string bearer, string credentialId, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = Convert.FromBase64String(credentialId);
            if (bytes.Length is < 1 or > 1024) throw new FormatException();
            return SendAsync<bool>(HttpMethod.Delete, "webauthn/credentials/" + QrLoginProtocol.Base64Url(bytes), null, bearer, cancellationToken);
        }
        catch { throw new ProviderLoginException("invalid_request"); }
    }
}
