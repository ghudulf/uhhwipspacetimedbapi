using System.Runtime.CompilerServices;
using System.Text.Json;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

public enum QrFlowState { Creating, Waiting, Scanned, Approved, Completed, Denied, Expired, Cancelled, Failed }
public sealed record QrFlowEvent(QrFlowState State, string? Payload = null, string? VerificationCode = null, DateTimeOffset? ExpiresAt = null, JsonElement? Login = null, string? Code = null);
public sealed class QrLoginFlow : IAsyncDisposable
{
    private readonly IQrLoginApi api;
    private readonly TimeProvider clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly CancellationTokenSource lifetime = new();
    private QrCreateResponse? session;
    private int cancelSent, started;
    private bool terminal;
    public QrLoginFlow(IQrLoginApi api, TimeProvider clock, Func<TimeSpan, CancellationToken, Task>? delay = null)
    { this.api = api; this.clock = clock; this.delay = delay ?? ((duration, ct) => Task.Delay(duration, clock, ct)); }
    public IAsyncEnumerable<QrFlowEvent> StartApprovalSessionAsync(string deviceName, string platform, string? username = null, CancellationToken cancellationToken = default) => Run(deviceName, platform, username, null, cancellationToken);
    public IAsyncEnumerable<QrFlowEvent> StartDeviceSessionAsync(string username, QrEnrollment? enrollment, string deviceName, string platform, CancellationToken cancellationToken = default) =>
        Run(deviceName, platform, username, enrollment, cancellationToken, deviceFlow: true);
    private async IAsyncEnumerable<QrFlowEvent> Run(string name, string platform, string? username, QrEnrollment? enrollment, [EnumeratorCancellation] CancellationToken cancellationToken, bool deviceFlow = false)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Flow already started.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var ct = linked.Token;
        yield return new(QrFlowState.Creating);
        string? error = null;
        if (deviceFlow && enrollment == null) error = "not_enrolled";
        else if (deviceFlow && (!QrOrigin.IsSameServer(enrollment!.Origin, api.ApiBase) || !string.Equals(username, enrollment.Username, StringComparison.OrdinalIgnoreCase))) error = "origin_mismatch";
        if (error != null) { terminal = true; yield return new(QrFlowState.Failed, Code: error); yield break; }
        QrCreateResponse? created = null;
        try { created = await api.CreateSessionAsync(name, platform, username, enrollment?.DeviceId, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { error = "cancelled"; }
        catch (QrLoginException ex) { error = ex.Code; }
        catch { error = "network_error"; }
        if (created != null)
        {
            if (!QrLoginProtocol.TryParsePayload(created.QrPayload, out var payload) || payload.SessionId != created.SessionId || payload.ApprovalSecret != created.ApprovalSecret
                || !QrLoginProtocol.TryDecode(created.PollSecret, 32, out _) || created.VerificationCode.Length != 6 || !created.VerificationCode.All(char.IsAsciiDigit)) error = "invalid_response";
            else if (!QrOrigin.IsSameServer(payload.Origin, api.ApiBase)) error = "origin_mismatch";
            else session = created; // No secret is sent (including cancellation) before payload/origin validation.
        }
        if (error != null) { terminal = true; yield return new(error == "cancelled" ? QrFlowState.Cancelled : QrFlowState.Failed, Code: error); yield break; }
        try
        {
            yield return new(QrFlowState.Waiting, session!.QrPayload, session.VerificationCode, session.ExpiresAt);
            if (deviceFlow)
            {
                try
                {
                    QrLoginProtocol.TryParsePayload(session.QrPayload, out var payload);
                    // Recheck immediately before signing/submitting credentials.
                    if (!QrOrigin.IsSameServer(payload.Origin, api.ApiBase)) throw new QrLoginException("origin_mismatch");
                    var signature = QrDeviceKey.Sign(enrollment!.Pkcs8PrivateKey, session.SessionId, session.ApprovalSecret, payload.Origin, username!, enrollment.DeviceId, session.VerificationCode);
                    await api.CompleteAsync(session.SessionId, session.ApprovalSecret, session.VerificationCode, enrollment.DeviceId, signature, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { error = "cancelled"; }
                catch (QrLoginException ex) { error = ex.Code; }
                catch { error = "network_error"; }
                if (error != null) { terminal = true; yield return new(error == "cancelled" ? QrFlowState.Cancelled : QrFlowState.Failed, Code: error); yield break; }
            }
            var interval = TimeSpan.FromMilliseconds(Math.Clamp(session.PollIntervalMs, 500, 5000));
            var next = clock.GetUtcNow();
            var consecutiveErrors = 0;
            while (!ct.IsCancellationRequested)
            {
                if (clock.GetUtcNow() >= session.ExpiresAt.AddSeconds(2)) { terminal = true; yield return new(QrFlowState.Expired); yield break; }
                var wait = next - clock.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    try { await delay(wait, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
                QrPoll? poll = null;
                try { poll = await api.StatusAsync(session.SessionId, session.PollSecret, ct).ConfigureAwait(false); consecutiveErrors = 0; }
                catch (OperationCanceledException) { break; }
                catch (QrLoginException ex) { error = ex.Code; }
                catch { error = "network_error"; }
                if (poll == null)
                {
                    if (error != "network_error" || ++consecutiveErrors >= 3) { terminal = true; yield return new(QrFlowState.Failed, Code: error); yield break; }
                    next = clock.GetUtcNow() + TimeSpan.FromMilliseconds(Math.Min(5000, interval.TotalMilliseconds * (1 << consecutiveErrors)));
                    continue;
                }
                var state = poll.State switch { "pending" => QrFlowState.Waiting, "scanned" => QrFlowState.Scanned, "approved" => QrFlowState.Approved,
                    "completed" when poll.Login is { ValueKind: JsonValueKind.Object } => QrFlowState.Completed, "denied" => QrFlowState.Denied, "expired" => QrFlowState.Expired, "cancelled" => QrFlowState.Cancelled, _ => QrFlowState.Failed };
                terminal = state is QrFlowState.Completed or QrFlowState.Denied or QrFlowState.Expired or QrFlowState.Cancelled or QrFlowState.Failed;
                yield return new(state, Login: poll.Login?.Clone(), Code: state == QrFlowState.Failed ? "invalid_response" : null);
                if (terminal) yield break;
                next += interval;
                if (next < clock.GetUtcNow()) next = clock.GetUtcNow();
            }
            yield return new(QrFlowState.Cancelled);
        }
        finally { if (!terminal) await CancelAsync().ConfigureAwait(false); }
    }
    public async Task CancelAsync()
    {
        lifetime.Cancel();
        var current = session;
        if (terminal || current == null || Interlocked.Exchange(ref cancelSent, 1) != 0) return;
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await api.CancelAsync(current.SessionId, current.PollSecret, timeout.Token).ConfigureAwait(false); }
        catch { /* Best effort; local cancellation is authoritative for this flow. */ }
    }
    public async ValueTask DisposeAsync() { await CancelAsync().ConfigureAwait(false); }
}
