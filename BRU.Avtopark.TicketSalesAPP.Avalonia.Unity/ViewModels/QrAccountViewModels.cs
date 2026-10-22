using System;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public partial class QrApproveViewModel(IQrLoginApi api, IDeviceKeyStore store, string bearer, string username, Func<CancellationToken, Task<string>>? resolveAccount = null) : QrViewModel
{
    private readonly QrApproverFlow flow = new(api);
    private QrApprovalRequest? request;
    private string scannedText = "", code = "", details = "";
    private int attempts;
    private string countdown = "";
    private CancellationTokenSource? countdownLifetime;
    public string ScannedText { get => scannedText; set => this.RaiseAndSetIfChanged(ref scannedText, value); }
    public string VerificationCode { get => code; set => this.RaiseAndSetIfChanged(ref code, value); }
    public string Details { get => details; private set => this.RaiseAndSetIfChanged(ref details, value); }
    public int AttemptsRemaining { get => attempts; private set => this.RaiseAndSetIfChanged(ref attempts, value); }
    public bool HasRequest => request != null;
    public string Countdown { get => countdown; private set => this.RaiseAndSetIfChanged(ref countdown, value); }
    [RelayCommand]
    public async Task ScanAsync()
    {
        try { var text = await QrScanner.Current.ScanAsync(Lifetime.Token); if (text != null && !Lifetime.IsCancellationRequested) { ScannedText = text; await PeekAsync(); } }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failure(ex); }
    }
    [RelayCommand]
    public async Task PeekAsync()
    {
        if (IsBusy || Lifetime.IsCancellationRequested) return;
        IsBusy = true; request = null; this.RaisePropertyChanged(nameof(HasRequest));
        try
        {
            var result = await flow.ParseAndPeekAsync(ScannedText, api.ApiBase, Lifetime.Token);
            if (Lifetime.IsCancellationRequested) return;
            request = result;
            countdownLifetime?.Cancel();
            countdownLifetime = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token);
            _ = CountAsync(result, countdownLifetime.Token);
            AttemptsRemaining = result.Details.CodeAttemptsRemaining;
            Details = $"Устройство: {result.Details.DeviceName}\nПлатформа: {result.Details.Platform}\nАдрес: {result.Details.RequestedFrom}\nСервер: {result.Details.Origin}\nДо: {result.Details.ExpiresAt.LocalDateTime:g}" +
                (result.Details.BoundUsername == null ? "" : $"\nТолько для пользователя {result.Details.BoundUsername}");
            Status = "Сравните сервер и устройство. Введите 6 цифр с экрана входа.";
            this.RaisePropertyChanged(nameof(HasRequest));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failure(ex); }
        finally { IsBusy = false; }
    }
    private async Task CountAsync(QrApprovalRequest current, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && ReferenceEquals(request, current))
            {
                var seconds = Math.Max(0, (int)Math.Ceiling((current.Details.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds));
                Countdown = $"Осталось секунд: {seconds}";
                if (seconds == 0) { request = null; this.RaisePropertyChanged(nameof(HasRequest)); Status = "Срок действия QR-кода истёк"; break; }
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }
    }
    [RelayCommand]
    public async Task ApproveAsync()
    {
        if (IsBusy || request == null || Lifetime.IsCancellationRequested) return;
        if (VerificationCode.Length != 6 || !System.Linq.Enumerable.All(VerificationCode, char.IsAsciiDigit)) { Status = "Введите 6 цифр с экрана входа."; return; }
        IsBusy = true; QrEnrollment? enrollment = null;
        try
        {
            var bound = request.Details.BoundUsername;
            // Account lookup is deferred until AFTER ParseAndPeek validated the QR origin.
            var currentAccount = bound != null && resolveAccount != null ? await resolveAccount(Lifetime.Token) : username;
            if (bound != null && !string.Equals(bound, currentAccount, StringComparison.OrdinalIgnoreCase)) enrollment = await store.LoadAsync(api.ApiBase, bound, Lifetime.Token);
            if (Lifetime.IsCancellationRequested) return;
            if (enrollment != null) await flow.ApproveWithDeviceKeyAsync(request, enrollment, VerificationCode, Lifetime.Token);
            else await flow.ApproveWithSessionAsync(request, bearer, VerificationCode, Lifetime.Token);
            if (!Lifetime.IsCancellationRequested) { Status = "Вход подтверждён"; request = null; this.RaisePropertyChanged(nameof(HasRequest)); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (ex is QrLoginException { AttemptsRemaining: { } remaining }) AttemptsRemaining = remaining; Failure(ex); }
        finally { if (enrollment != null) CryptographicOperations.ZeroMemory(enrollment.Pkcs8PrivateKey); IsBusy = false; }
    }
    [RelayCommand]
    public async Task DenyAsync()
    {
        if (IsBusy || request == null || Lifetime.IsCancellationRequested) return;
        IsBusy = true;
        try { await flow.DenyAsync(request, Lifetime.Token); if (!Lifetime.IsCancellationRequested) { Status = "Вход отклонён"; request = null; this.RaisePropertyChanged(nameof(HasRequest)); } }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failure(ex); }
        finally { IsBusy = false; }
    }
}

public partial class QrDevicesViewModel(IQrLoginApi api, IDeviceKeyStore store, string bearer, string username) : QrViewModel
{
    private string deviceName = Environment.MachineName;
    public string DeviceName { get => deviceName; set => this.RaiseAndSetIfChanged(ref deviceName, value); }
    public ObservableCollection<QrDeviceInfo> Devices { get; } = new();
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy || Lifetime.IsCancellationRequested) return;
        IsBusy = true;
        try { var devices = await api.ListDevicesAsync(bearer, Lifetime.Token); if (!Lifetime.IsCancellationRequested) { Devices.Clear(); foreach (var item in devices) Devices.Add(item); Status = "Список устройств обновлён"; } }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failure(ex); }
        finally { IsBusy = false; }
    }
    [RelayCommand]
    public async Task EnrollAsync()
    {
        if (IsBusy || Lifetime.IsCancellationRequested) return;
        IsBusy = true;
        var generated = QrDeviceKey.Generate();
        var saved = false;
        try
        {
            // Do not replace a working local enrollment implicitly.
            var existing = await store.LoadAsync(api.ApiBase, username, Lifetime.Token);
            if (existing != null) { CryptographicOperations.ZeroMemory(existing.Pkcs8PrivateKey); throw new QrLoginException("device_exists"); }
            var response = await api.EnrollDeviceAsync(bearer, DeviceName, Environment.OSVersion.Platform.ToString(), QrDeviceKey.ToSpkiBase64(generated.Spki), Lifetime.Token);
            if (response.DeviceId != generated.DeviceId) throw new QrLoginException("invalid_response");
            Lifetime.Token.ThrowIfCancellationRequested();
            await store.SaveAsync(new(QrOrigin.Normalize(api.ApiBase), username.ToLowerInvariant(), generated.DeviceId, generated.Pkcs8PrivateKey), Lifetime.Token);
            Lifetime.Token.ThrowIfCancellationRequested();
            saved = true; Status = "QR-вход на этом устройстве включён";
        }
        catch (Exception ex)
        {
            // An enroll response can be lost after commit. The deterministic ID lets rollback revoke the generated key anyway.
            if (ex is not QrLoginException { Code: "device_exists" })
            {
                var uncertain = false;
                try { await store.DeleteAsync(api.ApiBase, username); } catch { uncertain = true; }
                try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await api.RevokeDeviceAsync(bearer, generated.DeviceId, timeout.Token); }
                catch (QrLoginException qr) when (qr.Status == 404) { }
                catch { uncertain = true; }
                Failure(uncertain ? new QrLoginException("rollback_uncertain") : ex);
            }
            else Failure(ex);
        }
        finally { CryptographicOperations.ZeroMemory(generated.Pkcs8PrivateKey); IsBusy = false; }
        if (saved && !Lifetime.IsCancellationRequested) await RefreshAsync();
    }
    [RelayCommand]
    public async Task RevokeAsync(QrDeviceInfo device)
    {
        if (IsBusy || Lifetime.IsCancellationRequested) return;
        IsBusy = true; QrEnrollment? local = null;
        try
        {
            local = await store.LoadAsync(api.ApiBase, username, Lifetime.Token);
            await api.RevokeDeviceAsync(bearer, device.DeviceId, Lifetime.Token);
            if (local?.DeviceId == device.DeviceId) await store.DeleteAsync(api.ApiBase, username);
            if (!Lifetime.IsCancellationRequested) { Devices.Remove(device); Status = "Устройство отозвано"; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failure(ex); }
        finally { if (local != null) CryptographicOperations.ZeroMemory(local.Pkcs8PrivateKey); IsBusy = false; }
    }
}
