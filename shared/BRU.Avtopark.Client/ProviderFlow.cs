using System.Runtime.CompilerServices;
using System.Text.Json;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

public interface IBrowserLauncher
{
    Task OpenAsync(Uri url, CancellationToken cancellationToken = default);
}
public enum ProviderFlowState { Starting, Waiting, Completed, Failed, Expired, Cancelled }
public sealed record ProviderFlowEvent(ProviderFlowState State, DateTimeOffset? ExpiresAt = null, JsonElement? Login = null, bool Linked = false, bool Registered = false, string? Code = null);

/// <summary>One browser ceremony. Browser URLs never escape into status, exceptions or diagnostics.</summary>
public sealed class ProviderFlow(IProviderLoginApi api, IBrowserLauncher browser, TimeProvider clock,
    IEnumerable<string>? allowedAuthorityHosts = null, Func<TimeSpan, CancellationToken, Task>? delay = null) : IAsyncDisposable
{
    private readonly HashSet<string> hosts = (allowedAuthorityHosts ?? ["login.microsoftonline.com"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? ((duration, ct) => Task.Delay(duration, clock, ct));
    private readonly CancellationTokenSource lifetime = new();
    private ProviderStart? flow;
    private string? providerName;
    private int started, cancelSent;
    private bool terminal;
    private bool SafeUrl(string provider, ProviderStart response, out Uri? uri)
    {
        uri = null;
        var text = provider == "entra" ? response.AuthorizationUrl : response.BrowserUrl;
        if (text is not { Length: <= 8192 } || !Uri.TryCreate(text, UriKind.Absolute, out uri) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        if (provider == "entra") return uri.Scheme == "https" && hosts.Contains(uri.IdnHost);
        try
        {
            var root = ClientEndpoints.ServerRoot(api.ApiBase);
            var expected = new Uri(root, "api/auth/passkey/browser");
            if (uri.GetLeftPart(UriPartial.Path) != expected.GetLeftPart(UriPartial.Path)) return false;
            var query = uri.Query.TrimStart('?').Split('&');
            if (query.Length != 2) return false;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in query)
            {
                var pair = part.Split('=');
                if (pair.Length != 2 || !values.TryAdd(pair[0], Uri.UnescapeDataString(pair[1]))) return false;
            }
            return values.Count == 2 && values.TryGetValue("f", out var id) && id == response.FlowId && values.TryGetValue("b", out var secret) && QrLoginProtocol.TryDecode(secret, 32, out _);
        }
        catch { return false; }
    }
    public async IAsyncEnumerable<ProviderFlowEvent> RunAsync(string provider, string purpose = "login", string? bearer = null, string? username = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Flow already started.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var ct = linked.Token;
        yield return new(ProviderFlowState.Starting);
        string? error = null;
        ProviderStart? response = null;
        try { response = await api.StartAsync(provider, purpose, bearer, username, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { error = "cancelled"; }
        catch (ProviderLoginException ex) { error = ex.Code; }
        catch { error = "network_error"; }
        Uri? uri = null;
        if (response != null)
        {
            if (provider is not ("entra" or "passkey") || !QrLoginProtocol.TryDecode(response.FlowId, 16, out _) || !QrLoginProtocol.TryDecode(response.FlowSecret, 32, out _) ||
                response.ExpiresAt <= clock.GetUtcNow() || response.ExpiresAt > clock.GetUtcNow().AddMinutes(10)) error = "invalid_response";
            else if (!SafeUrl(provider, response, out uri)) error = "unsafe_url";
            else { flow = response; providerName = provider; }
        }
        if (error != null || flow == null) { terminal = true; yield return new(error == "cancelled" ? ProviderFlowState.Cancelled : ProviderFlowState.Failed, Code: error ?? "invalid_response"); yield break; }
        try
        {
            try { await browser.OpenAsync(uri!, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { error = "cancelled"; }
            catch { error = "browser_unavailable"; }
            if (error != null)
            {
                await CancelAsync().ConfigureAwait(false);
                terminal = true; yield return new(error == "cancelled" ? ProviderFlowState.Cancelled : ProviderFlowState.Failed, Code: error); yield break;
            }
            yield return new(ProviderFlowState.Waiting, flow.ExpiresAt);
            var interval = TimeSpan.FromMilliseconds(Math.Clamp(flow.PollIntervalMs, 500, 5000));
            var failures = 0;
            while (!ct.IsCancellationRequested)
            {
                try { await wait(interval, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                if (clock.GetUtcNow() >= flow.ExpiresAt) { terminal = true; yield return new(ProviderFlowState.Expired); yield break; }
                ProviderPoll? poll = null;
                try { poll = await api.CompleteAsync(provider, flow.FlowId, flow.FlowSecret, ct).ConfigureAwait(false); failures = 0; }
                catch (OperationCanceledException) { break; }
                catch (ProviderLoginException ex) { error = ex.Code; }
                catch { error = "network_error"; }
                if (ct.IsCancellationRequested) break; // A cancelled/account-changed window never surfaces a raced completion.
                if (poll == null)
                {
                    if (error == "network_error" && ++failures < 3) continue;
                    terminal = true; yield return new(ProviderFlowState.Failed, Code: error); yield break;
                }
                if (poll.State == "pending") continue;
                var completed = poll.State == "completed" && (purpose == "login" ? poll.Login is { ValueKind: JsonValueKind.Object } : purpose == "link" ? poll.Linked == true : poll.Registered == true);
                var state = completed ? ProviderFlowState.Completed : poll.State switch { "expired" => ProviderFlowState.Expired, "cancelled" => ProviderFlowState.Cancelled, _ => ProviderFlowState.Failed };
                terminal = true;
                yield return new(state, flow.ExpiresAt, completed ? poll.Login?.Clone() : null, completed && poll.Linked == true, completed && poll.Registered == true,
                    state == ProviderFlowState.Failed ? poll.Code ?? "provider_failed" : null);
                yield break;
            }
            yield return new(ProviderFlowState.Cancelled);
        }
        finally { if (!terminal) await CancelAsync().ConfigureAwait(false); }
    }
    public async Task CancelAsync()
    {
        lifetime.Cancel();
        if (terminal || flow == null || Interlocked.Exchange(ref cancelSent, 1) != 0) return;
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await api.CancelAsync(providerName!, flow.FlowId, flow.FlowSecret, timeout.Token).ConfigureAwait(false); } catch { }
    }
    public async ValueTask DisposeAsync() => await CancelAsync().ConfigureAwait(false);
}
