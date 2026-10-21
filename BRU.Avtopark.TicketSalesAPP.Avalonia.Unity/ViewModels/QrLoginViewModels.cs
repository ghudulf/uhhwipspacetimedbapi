using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using CommunityToolkit.Mvvm.Input;
using QRCoder;
using ReactiveUI;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public abstract class QrViewModel : ReactiveObject, IAsyncDisposable
{
    protected readonly CancellationTokenSource Lifetime = new();
    private string status = "Готово";
    private bool busy;
    public string Status { get => status; protected set => this.RaiseAndSetIfChanged(ref status, value); }
    public bool IsBusy { get => busy; protected set => this.RaiseAndSetIfChanged(ref busy, value); }
    public event Action? CloseRequested;
    public void SessionChanged() { Lifetime.Cancel(); Status = "Сеанс изменён. Окно закрыто."; CloseRequested?.Invoke(); }
    protected void Failure(Exception ex) => Status = ex is QrLoginException qr ? qr.Code switch
    {
        "origin_mismatch" => "Этот QR-код предназначен для другого сервера",
        "not_enrolled" => "Устройство не зарегистрировано. Войдите другим способом и включите «QR-вход на этом устройстве».",
        "verification_failed" => $"Неверный код. Осталось попыток: {qr.AttemptsRemaining ?? 0}",
        "device_exists" => "Этот ключ уже зарегистрирован.", "device_limit" => "Достигнут предел: 5 устройств. Отзовите ненужное устройство.",
        "rollback_uncertain" => "Ключ удалён локально. Проверьте список устройств на сервере и отзовите незавершённую регистрацию.",
        _ => "Не удалось выполнить QR-вход. Проверьте подключение и повторите."
    } : "Не удалось выполнить QR-вход. Проверьте подключение и повторите.";
    public virtual ValueTask DisposeAsync() { Lifetime.Cancel(); return ValueTask.CompletedTask; }
}

public partial class QrLoginViewModel : QrViewModel
{
    protected readonly IQrLoginApi Api;
    private readonly TimeProvider clock;
    private QrLoginFlow? flow;
    private string username = "", deviceName = Environment.MachineName, code = "", countdown = "";
    private Bitmap? image;
    private DateTimeOffset? expires;
    private CancellationTokenSource? countdownLifetime;
    private TaskCompletionSource? runFinished;
    public string Username { get => username; set => this.RaiseAndSetIfChanged(ref username, value); }
    public string DeviceName { get => deviceName; set => this.RaiseAndSetIfChanged(ref deviceName, value); }
    public string VerificationCode { get => code; private set => this.RaiseAndSetIfChanged(ref code, value); }
    public string Countdown { get => countdown; private set => this.RaiseAndSetIfChanged(ref countdown, value); }
    public Bitmap? QrImage { get => image; private set { var previous = image; this.RaiseAndSetIfChanged(ref image, value); previous?.Dispose(); } }
    public event Action<string>? Completed;
    public QrLoginViewModel(IQrLoginApi api, TimeProvider? clock = null) { Api = api; this.clock = clock ?? TimeProvider.System; }
    protected virtual Task<QrEnrollment?> EnrollmentAsync() => Task.FromResult<QrEnrollment?>(null);
    protected virtual bool DeviceFlow => false;
    [RelayCommand]
    public async Task StartAsync()
    {
        if (IsBusy || Lifetime.IsCancellationRequested) return;
        IsBusy = true;
        var finished = runFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        QrEnrollment? enrollment = null;
        try
        {
            if (flow != null) await flow.DisposeAsync();
            QrImage = null; VerificationCode = "";
            enrollment = await EnrollmentAsync();
            if (Lifetime.IsCancellationRequested) return;
            if (DeviceFlow && enrollment == null) throw new QrLoginException("not_enrolled");
            flow = new QrLoginFlow(Api, clock);
            var events = DeviceFlow ? flow.StartDeviceSessionAsync(Username.Trim(), enrollment, DeviceName, Environment.OSVersion.Platform.ToString(), Lifetime.Token)
                : flow.StartApprovalSessionAsync(DeviceName, Environment.OSVersion.Platform.ToString(), string.IsNullOrWhiteSpace(Username) ? null : Username.Trim(), Lifetime.Token);
            await foreach (var state in events)
            {
                if (Lifetime.IsCancellationRequested) break;
                if (state.Payload != null)
                {
                    if (!QrLoginProtocol.TryParsePayload(state.Payload, out var parsed) || !QrOrigin.IsSameServer(parsed.Origin, Api.ApiBase)) throw new QrLoginException("origin_mismatch");
                    using var generator = new QRCodeGenerator();
                    using var data = generator.CreateQrCode(state.Payload, QRCodeGenerator.ECCLevel.Q);
                    using var png = new PngByteQRCode(data);
                    using var stream = new MemoryStream(png.GetGraphic(6));
                    QrImage = new Bitmap(stream); // Render locally; server-provided qrPng is deliberately ignored.
                    VerificationCode = state.VerificationCode ?? "";
                    expires = state.ExpiresAt;
                    StartCountdown();
                }
                Status = state.State switch
                {
                    QrFlowState.Creating => "Создание QR-кода…", QrFlowState.Waiting => "Ожидание подтверждения на другом устройстве", QrFlowState.Scanned => "QR-код открыт. Введите код на подтверждающем устройстве.",
                    QrFlowState.Approved => "Вход подтверждён…", QrFlowState.Completed => "Вход подтверждён", QrFlowState.Denied => "Вход отклонён", QrFlowState.Expired => "Срок действия QR-кода истёк", QrFlowState.Cancelled => "Вход отменён", _ => "Не удалось выполнить QR-вход"
                };
                if (state.State == QrFlowState.Failed) Failure(new QrLoginException(state.Code ?? "invalid_response"));
                if (state.State == QrFlowState.Completed && state.Login is { } login) Completed?.Invoke(login.GetRawText());
            }
        }
        catch (OperationCanceledException) { Status = "Вход отменён"; }
        catch (Exception ex) { Failure(ex); }
        finally
        {
            if (enrollment != null) CryptographicOperations.ZeroMemory(enrollment.Pkcs8PrivateKey);
            countdownLifetime?.Cancel(); IsBusy = false;
            finished.TrySetResult();
        }
    }
    [RelayCommand]
    public async Task NewAsync()
    {
        if (IsBusy)
        {
            var previous = runFinished;
            await CancelAsync();
            if (previous != null) await previous.Task;
        }
        await StartAsync();
    }
    private void StartCountdown()
    {
        countdownLifetime?.Cancel();
        countdownLifetime = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token);
        _ = CountAsync(countdownLifetime.Token);
    }
    private async Task CountAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Countdown = $"Осталось секунд: {Math.Max(0, (int)Math.Ceiling(((expires ?? clock.GetUtcNow()) - clock.GetUtcNow()).TotalSeconds))}";
                await Task.Delay(TimeSpan.FromSeconds(1), clock, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }
    [RelayCommand]
    public async Task CancelAsync() { if (flow != null) await flow.CancelAsync(); Status = "Вход отменён"; }
    public override async ValueTask DisposeAsync() { await base.DisposeAsync(); countdownLifetime?.Cancel(); if (flow != null) await flow.DisposeAsync(); QrImage = null; }
}

public sealed class QrDeviceLoginViewModel(IQrLoginApi api, IDeviceKeyStore store, TimeProvider? clock = null) : QrLoginViewModel(api, clock)
{
    protected override bool DeviceFlow => true;
    protected override Task<QrEnrollment?> EnrollmentAsync() => string.IsNullOrWhiteSpace(Username) ? Task.FromResult<QrEnrollment?>(null) : store.LoadAsync(Api.ApiBase, Username, Lifetime.Token);
}
