using System.Security.Cryptography;
using System.Text.Json;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using Serilog;
using Serilog.Core;
using Serilog.Events;

internal static class QrPresentationTests
{
    public static async Task RunAsync()
    {
        var sink = new Sink(); var previous = Log.Logger;
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger(); Log.Logger = logger;
        try
        {
            await DisplayAndTerminalStatesAsync();
            await DeviceAndApproverAsync();
            await ViewModelsAndRollbackAsync();
            await ExternalLoginParityAsync();
            await SessionWindowCancellationAsync();
            foreach (var secret in new[] { "654321", "synthetic-bearer", "synthetic-temp", "private-user", FakeApi.Approval, FakeApi.PollSecret, "bruqr://", "signature-sentinel" })
                Check(!string.Join('\n', sink.Messages).Contains(secret, StringComparison.Ordinal), "sensitive logging");
        }
        finally { Log.Logger = previous; }
        Console.WriteLine("PASS QR Serilog sink: no payload/secret/code/signature/token/username logged during display, approval, self-scan and enrollment rollback");
    }
    private static async Task DisplayAndTerminalStatesAsync()
    {
        var clock = new Clock(); var api = new FakeApi(clock);
        var login = JsonSerializer.SerializeToElement(new { requiresTwoFactor = true, tempToken = "synthetic-temp" });
        api.Polls.Enqueue(new("scanned")); api.Polls.Enqueue(new("approved")); api.Polls.Enqueue(new("completed", login));
        await using (var flow = new QrLoginFlow(api, clock, clock.Delay))
        {
            var events = await Collect(flow.StartApprovalSessionAsync("Desktop", "test"));
            Check(events.Select(e => e.State).SequenceEqual(new[] { QrFlowState.Creating, QrFlowState.Waiting, QrFlowState.Scanned, QrFlowState.Approved, QrFlowState.Completed }), "display state machine");
            Check(events.Count(e => e.Login != null) == 1 && events[^1].Login!.Value.GetRawText() == login.GetRawText() && api.StatusCalls == 3, "2FA/exactly-once terminal");
        }
        Check(api.CancelCalls == 0, "completed flow cancelled on dispose");
        foreach (var state in new[] { "denied", "expired", "cancelled", "consumed" })
        {
            var fake = new FakeApi(clock); fake.Polls.Enqueue(new(state));
            await using var flow = new QrLoginFlow(fake, clock, clock.Delay);
            var events = await Collect(flow.StartApprovalSessionAsync("Desktop", "test"));
            Check(events[^1].State == (state == "consumed" ? QrFlowState.Failed : Enum.Parse<QrFlowState>(state, true)) && fake.StatusCalls == 1, "terminal polling stop");
        }
        var failures = new FakeApi(clock) { PollFailures = 3 };
        await using (var flow = new QrLoginFlow(failures, clock, clock.Delay))
        { var events = await Collect(flow.StartApprovalSessionAsync("Desktop", "test")); Check(events[^1].Code == "network_error" && failures.StatusCalls == 3, "bounded transient retry"); }
        var recovered = new FakeApi(clock) { PollFailures = 2 }; recovered.Polls.Enqueue(new("completed", login));
        await using (var flow = new QrLoginFlow(recovered, clock, clock.Delay))
        { var events = await Collect(flow.StartApprovalSessionAsync("Desktop", "test")); Check(events[^1].State == QrFlowState.Completed, "transient recovery"); }
        var expired = new FakeApi(clock) { SessionSeconds = -3 };
        await using (var flow = new QrLoginFlow(expired, clock, clock.Delay))
        { var events = await Collect(flow.StartApprovalSessionAsync("Desktop", "test")); Check(events[^1].State == QrFlowState.Expired && expired.StatusCalls == 0, "local expiry"); }
        var cancelled = new FakeApi(clock);
        var cancelFlow = new QrLoginFlow(cancelled, clock, clock.Delay);
        await using (var iterator = cancelFlow.StartApprovalSessionAsync("Desktop", "test").GetAsyncEnumerator())
        {
            await iterator.MoveNextAsync(); await iterator.MoveNextAsync();
            await cancelFlow.DisposeAsync(); await cancelFlow.CancelAsync();
            while (await iterator.MoveNextAsync()) { }
        }
        Check(cancelled.StatusCalls == 0 && cancelled.CancelCalls == 1, "dispose/cancel exactly once before polling");
        Console.WriteLine("PASS QR FlowDisplayTerminals: fake-time steady polling, one completion/2FA passthrough, all terminals, expiry, disposal and bounded transient retry");
    }
    private static async Task DeviceAndApproverAsync()
    {
        var clock = new Clock(); var generated = QrDeviceKey.Generate();
        var enrollment = new QrEnrollment(FakeApi.Origin, "private-user", generated.DeviceId, generated.Pkcs8PrivateKey);
        try
        {
            var api = new FakeApi(clock);
            api.Complete = (request, signature) =>
            {
                using var verifier = ECDsa.Create(); verifier.ImportSubjectPublicKeyInfo(generated.Spki, out _);
                Check(QrLoginProtocol.TryDecode(signature, 64, out var bytes) && verifier.VerifyData(QrLoginProtocol.SigningMessage(FakeApi.Id, FakeApi.Approval, FakeApi.Origin, "private-user", generated.DeviceId, "654321"), bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), "self-scan canonical signature/code");
            };
            api.Polls.Enqueue(new("completed", JsonSerializer.SerializeToElement(new { token = "synthetic-temp" })));
            await using (var flow = new QrLoginFlow(api, clock, clock.Delay))
            { var events = await Collect(flow.StartDeviceSessionAsync("private-user", enrollment, "Phone", "test")); Check(events[^1].State == QrFlowState.Completed && api.CompleteCalls == 1, "self-scan completion"); }
            var noEnrollment = new FakeApi(clock);
            await using (var flow = new QrLoginFlow(noEnrollment, clock, clock.Delay))
            { var events = await Collect(flow.StartDeviceSessionAsync("private-user", null, "Phone", "test")); Check(events[^1].Code == "not_enrolled" && noEnrollment.Calls == 0, "unenrolled network leak"); }
            var mismatch = new FakeApi(clock) { ResponseOrigin = "https://evil.test" };
            await using (var flow = new QrLoginFlow(mismatch, clock, clock.Delay))
            { var events = await Collect(flow.StartDeviceSessionAsync("private-user", enrollment, "Phone", "test")); Check(events[^1].Code == "origin_mismatch" && mismatch.CompleteCalls == 0 && mismatch.StatusCalls == 0 && mismatch.CancelCalls == 0, "self-scan origin guard before any secret submission"); }
            var approverApi = new FakeApi(clock); var approver = new QrApproverFlow(approverApi);
            try { await approver.ParseAndPeekAsync(QrLoginProtocol.BuildPayload(FakeApi.Id, FakeApi.Approval, "https://evil.test"), FakeApi.Origin); throw new Exception("origin accepted"); }
            catch (QrLoginException ex) { Check(ex.Code == "origin_mismatch" && approverApi.Calls == 0, "approver origin zero network"); }
            var request = await approver.ParseAndPeekAsync(QrLoginProtocol.BuildPayload(FakeApi.Id, FakeApi.Approval, FakeApi.Origin), FakeApi.Origin);
            await approver.ApproveWithSessionAsync(request, "synthetic-bearer", "654321");
            await approver.ApproveWithDeviceKeyAsync(request, enrollment, "654321");
            try { await approver.ApproveWithDeviceKeyAsync(request, enrollment with { Username = "other" }, "654321"); throw new Exception("wrong account accepted"); }
            catch (QrLoginException ex) { Check(ex.Code == "wrong_account", "bound account guard"); }
        }
        finally { CryptographicOperations.ZeroMemory(generated.Pkcs8PrivateKey); }
        Console.WriteLine("PASS QR DeviceAndApprover: canonical self-scan proof/code, missing enrollment zero network, origin-before-secret guards and bound username");
    }
    private static async Task ViewModelsAndRollbackAsync()
    {
        var clock = new Clock(); var api = new FakeApi(clock); api.Polls.Enqueue(new("completed", JsonSerializer.SerializeToElement(new { requiresTwoFactor = true, tempToken = "synthetic-temp" })));
        await using (var vm = new QrLoginViewModel(api, clock))
        {
            var completed = 0; var sawLocalImage = false;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.QrImage) && vm.QrImage != null) sawLocalImage = true; };
            vm.Completed += _ => completed++;
            await vm.StartAsync();
            Check(sawLocalImage && completed == 1 && !vm.IsBusy, "display view model/local PNG ignores invalid server PNG");
        }
        var store = new Store(); var approveApi = new FakeApi(clock) { ApprovalFailure = true };
        await using (var vm = new QrApproveViewModel(approveApi, store, "synthetic-bearer", "private-user"))
        {
            vm.ScannedText = QrLoginProtocol.BuildPayload(FakeApi.Id, FakeApi.Approval, FakeApi.Origin);
            await vm.PeekAsync(); Check(vm.HasRequest && vm.AttemptsRemaining == 3, "approval confirmation metadata");
            vm.VerificationCode = "654321"; await vm.ApproveAsync(); Check(vm.AttemptsRemaining == 2 && vm.Status.Contains("2"), "attempt display");
            approveApi.ApprovalFailure = false; await vm.ApproveAsync(); Check(!vm.HasRequest, "approval terminal view model");
            vm.SessionChanged(); var calls = approveApi.Calls; await vm.PeekAsync(); Check(approveApi.Calls == calls, "session-change cancellation");
        }
        var key = QrDeviceKey.Generate();
        try
        {
            store.Enrollment = new(FakeApi.Origin, "private-user", key.DeviceId, key.Pkcs8PrivateKey);
            var keyApi = new FakeApi(clock);
            await using var vm = new QrApproveViewModel(keyApi, store, "synthetic-bearer", "other-user");
            vm.ScannedText = QrLoginProtocol.BuildPayload(FakeApi.Id, FakeApi.Approval, FakeApi.Origin); vm.VerificationCode = "654321";
            await vm.PeekAsync(); await vm.ApproveAsync();
            Check(keyApi.CompleteCalls == 1 && keyApi.ApproveCalls == 0, "bound-other-account routes to enrolled device proof");
        }
        finally { CryptographicOperations.ZeroMemory(key.Pkcs8PrivateKey); store.Enrollment = null; }
        var rollbackApi = new FakeApi(clock); store.SaveFailure = true;
        await using (var devices = new QrDevicesViewModel(rollbackApi, store, "synthetic-bearer", "private-user"))
        { await devices.EnrollAsync(); Check(rollbackApi.RevokeCalls == 1 && store.DeleteCalls == 1 && store.Enrollment == null, "save failure enrollment rollback"); }
        store.SaveFailure = false; rollbackApi.EnrollFailure = true;
        await using (var devices = new QrDevicesViewModel(rollbackApi, store, "synthetic-bearer", "private-user"))
        { await devices.EnrollAsync(); Check(rollbackApi.RevokeCalls == 2 && store.DeleteCalls == 2 && store.Enrollment == null, "uncertain enroll rollback"); }
        rollbackApi.EnrollFailure = false;
        await using (var devices = new QrDevicesViewModel(rollbackApi, store, "synthetic-bearer", "private-user"))
        {
            await devices.EnrollAsync(); Check(store.Enrollment != null, "successful enrollment persisted");
            var local = store.Enrollment!; var row = new QrDeviceInfo(local.DeviceId, "Phone", "test", 0, null);
            devices.Devices.Add(row); await devices.RevokeAsync(row); Check(store.Enrollment == null, "local revocation retained key");
            devices.SessionChanged(); var calls = rollbackApi.Calls; await devices.RefreshAsync(); Check(calls == rollbackApi.Calls, "devices session-change cancellation");
        }
        var missingApi = new FakeApi(clock);
        await using (var deviceVm = new QrDeviceLoginViewModel(missingApi, store) { Username = "private-user" })
        { await deviceVm.StartAsync(); Check(missingApi.Calls == 0 && deviceVm.Status.Contains("не зарегистрировано"), "unenrolled view model zero network"); }
        Console.WriteLine("PASS QR ViewModels: local image/status/2FA completion, approval routing/attempts, session cancellation, enrollment rollback and local revocation");
    }
    private static async Task ExternalLoginParityAsync()
    {
        var previousToken = ApiClientService.Instance.AuthToken;
        try
        {
            foreach (var description in new[] { "Вход с учётной записью домена", "Вход по QR" })
            {
                var vm = new AuthViewModel();
                await vm.ApplyExternalLoginAsync("{\"token\":\"synthetic-bearer\"}", description: description);
                Check(vm.IsAuthenticated && vm.CurrentStep == 4 && vm.UserInfo == description && vm.StatusMessage == "Права доступа будут проверены на сервере." && vm.NextButtonText == "Завершить" && vm.ProgressValue == 100, "Windows/QR direct-login state parity");
                var factor = new AuthViewModel();
                await factor.ApplyExternalLoginAsync("{\"requiresTwoFactor\":true,\"twoFactorType\":\"totp\",\"tempToken\":\"synthetic-temp\"}");
                Check(factor.RequiresTwoFactor && factor.TwoFactorType == "totp" && factor.CurrentStep == 3 && !factor.IsAuthenticated && !factor.IsLoading, "external TOTP state passthrough");
                Check((string?)typeof(AuthViewModel).GetField("_tempToken", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(factor) == "synthetic-temp", "temp-token preservation");
            }
            var cancelled = new AuthViewModel();
            try { await cancelled.ApplyExternalLoginAsync("{\"token\":\"must-not-apply\"}", new CancellationToken(true)); throw new Exception("cancelled external login accepted"); }
            catch (OperationCanceledException) { Check(!cancelled.IsAuthenticated && ApiClientService.Instance.AuthToken != "must-not-apply", "cancelled result applied"); }
        }
        finally { ApiClientService.Instance.AuthToken = previousToken; }
        Console.WriteLine("PASS external-login shared handler preserves Windows/QR direct result, full wizard fields, TOTP/temp token and cancellation behaviour");
    }
    private static async Task SessionWindowCancellationAsync()
    {
        var token = ApiClientService.Instance.AuthToken;
        var window = new BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.QrApproveWindow();
        try
        {
            window.Show();
            ApiClientService.Instance.AuthToken = "synthetic-session-change";
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            Check(!window.IsVisible && window.DataContext is QrApproveViewModel { Status: "Сеанс изменён. Окно закрыто." }, "window retained stale authenticated session");
        }
        finally { window.Close(); ApiClientService.Instance.AuthToken = token; }
        Console.WriteLine("PASS QR authenticated window cancels/closes through the actual OnAuthTokenChanged event");
    }
    private static async Task<List<QrFlowEvent>> Collect(IAsyncEnumerable<QrFlowEvent> events) { var list = new List<QrFlowEvent>(); await foreach (var e in events) list.Add(e); return list; }
    private static void Check(bool value, string message) { if (!value) throw new Exception("QR presentation: " + message); }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public Task Delay(TimeSpan duration, CancellationToken ct) { ct.ThrowIfCancellationRequested(); now += duration; return Task.CompletedTask; }
    }
    private sealed class FakeApi(Clock clock) : IQrLoginApi
    {
        public const string Origin = "https://qr.example.test/base";
        public static readonly string Id = QrLoginProtocol.Base64Url(new byte[16]);
        public static readonly string Approval = QrLoginProtocol.Base64Url(Enumerable.Repeat((byte)2, 32).ToArray());
        public static readonly string PollSecret = QrLoginProtocol.Base64Url(Enumerable.Repeat((byte)3, 32).ToArray());
        public string ApiBase => Origin + "/api/";
        public string ResponseOrigin = Origin;
        public int SessionSeconds = 120, Calls, StatusCalls, CompleteCalls, CancelCalls, ApproveCalls, RevokeCalls, PollFailures;
        public bool ApprovalFailure, EnrollFailure;
        public Queue<QrPoll> Polls = new();
        public Action<string, string>? Complete;
        public Task<QrCreateResponse> CreateSessionAsync(string deviceName, string platform, string? username = null, string? deviceId = null, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new QrCreateResponse(Id, PollSecret, Approval, QrLoginProtocol.BuildPayload(Id, Approval, ResponseOrigin), "invalid-server-png", "654321", clock.GetUtcNow().AddSeconds(SessionSeconds), 500)); }
        public Task<QrPoll> StatusAsync(string id, string pollSecret, CancellationToken cancellationToken = default)
        { Calls++; StatusCalls++; if (PollFailures-- > 0) throw new QrLoginException("network_error"); return Task.FromResult(Polls.TryDequeue(out var poll) ? poll : new QrPoll("pending")); }
        public Task CancelAsync(string id, string pollSecret, CancellationToken cancellationToken = default) { Calls++; CancelCalls++; return Task.CompletedTask; }
        public Task<QrPeekResponse> PeekAsync(string id, string approvalSecret, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new QrPeekResponse("Desktop", "test", clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(2), "192.0.2.x", Origin, "private-user", 3)); }
        public Task ApproveAsync(string id, string approvalSecret, string code, string bearer, CancellationToken cancellationToken = default)
        { Calls++; ApproveCalls++; if (ApprovalFailure) throw new QrLoginException("verification_failed", 400, 2); return Task.CompletedTask; }
        public Task DenyAsync(string id, string approvalSecret, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
        public Task CompleteAsync(string id, string approvalSecret, string code, string deviceId, string signature, CancellationToken cancellationToken = default)
        { Calls++; CompleteCalls++; Complete?.Invoke(code, signature); return Task.CompletedTask; }
        public Task<QrDeviceResponse> EnrollDeviceAsync(string bearer, string name, string platform, string spkiBase64, CancellationToken cancellationToken = default)
        { Calls++; if (EnrollFailure) throw new QrLoginException("network_error"); return Task.FromResult(new QrDeviceResponse(QrLoginProtocol.DeviceIdFor(Convert.FromBase64String(spkiBase64)))); }
        public Task<QrDeviceInfo[]> ListDevicesAsync(string bearer, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(Array.Empty<QrDeviceInfo>()); }
        public Task RevokeDeviceAsync(string bearer, string deviceId, CancellationToken cancellationToken = default) { Calls++; RevokeCalls++; return Task.CompletedTask; }
    }
    private sealed class Store : IDeviceKeyStore
    {
        public QrEnrollment? Enrollment; public bool SaveFailure; public int DeleteCalls;
        public Task SaveAsync(QrEnrollment enrollment, CancellationToken cancellationToken = default)
        { if (SaveFailure) throw new IOException("signature-sentinel"); Enrollment = enrollment with { Pkcs8PrivateKey = (byte[])enrollment.Pkcs8PrivateKey.Clone() }; return Task.CompletedTask; }
        public Task<QrEnrollment?> LoadAsync(string origin, string username, CancellationToken cancellationToken = default) => Task.FromResult(Enrollment == null ? null : Enrollment with { Pkcs8PrivateKey = (byte[])Enrollment.Pkcs8PrivateKey.Clone() });
        public Task DeleteAsync(string origin, string username, CancellationToken cancellationToken = default)
        { DeleteCalls++; if (Enrollment != null) CryptographicOperations.ZeroMemory(Enrollment.Pkcs8PrivateKey); Enrollment = null; return Task.CompletedTask; }
    }
    private sealed class Sink : ILogEventSink { public List<string> Messages = new(); public void Emit(LogEvent logEvent) => Messages.Add(logEvent.RenderMessage()); }
}
