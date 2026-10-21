using System;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public partial class ProviderLoginViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ProviderFlow flow;
    private readonly string provider, purpose;
    private readonly string? bearer;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? countdownLifetime;
    private int started, surfaced;
    [ObservableProperty] private string status = "Подготовка входа…";
    [ObservableProperty] private string countdown = "";
    [ObservableProperty] private bool isBusy;
    public event Action<string?>? Completed;
    public event Action? CloseRequested;
    public ProviderLoginViewModel(IProviderLoginApi api, IBrowserLauncher browser, string provider = "entra", string purpose = "login", string? bearer = null,
        TimeProvider? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.provider = provider; this.purpose = purpose; this.bearer = bearer; this.clock = clock ?? TimeProvider.System;
        flow = new ProviderFlow(api, browser, this.clock, delay: delay);
    }
    public async Task StartAsync()
    {
        if (Interlocked.Exchange(ref started, 1) != 0 || lifetime.IsCancellationRequested) return;
        IsBusy = true;
        try
        {
            await foreach (var item in flow.RunAsync(provider, purpose, bearer, cancellationToken: lifetime.Token))
            {
                if (lifetime.IsCancellationRequested) return;
                Status = item.State switch
                {
                    ProviderFlowState.Starting => "Открытие системного браузера…",
                    ProviderFlowState.Waiting => "Завершите вход в системном браузере и вернитесь в приложение.",
                    ProviderFlowState.Completed => purpose == "register" ? "Ключ доступа зарегистрирован." : purpose == "link" ? "Учётная запись Microsoft связана." : "Вход подтверждён.",
                    ProviderFlowState.Cancelled => "Вход отменён.", ProviderFlowState.Expired => "Время ожидания истекло. Закройте окно и повторите вход.",
                    _ => item.Code switch
                    {
                        "browser_unavailable" => "Не удалось открыть системный браузер. Проверьте его настройки.",
                        "not_linked" => "Сначала войдите другим способом и свяжите учётную запись Microsoft.",
                        "already_linked" => "Эта учётная запись Microsoft уже связана. Проверьте состояние связи.",
                        "unsafe_url" => "Сервер вернул небезопасный адрес. Браузер не открыт.",
                        _ => "Не удалось подтвердить результат. Проверьте состояние связи перед повтором."
                    }
                };
                if (item.State == ProviderFlowState.Waiting && item.ExpiresAt is { } expires && countdownLifetime == null)
                {
                    countdownLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    _ = CountAsync(expires, countdownLifetime.Token);
                }
                if (item.State == ProviderFlowState.Completed && Interlocked.Exchange(ref surfaced, 1) == 0) Completed?.Invoke(item.Login?.GetRawText());
            }
        }
        catch (OperationCanceledException) { }
        catch { if (!lifetime.IsCancellationRequested) Status = "Не удалось выполнить вход. Закройте окно и повторите попытку."; }
        finally { countdownLifetime?.Cancel(); IsBusy = false; }
    }
    private async Task CountAsync(DateTimeOffset expires, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Countdown = $"Осталось секунд: {Math.Max(0, (int)Math.Ceiling((expires - clock.GetUtcNow()).TotalSeconds))}";
                await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);
            }
        }
        catch (OperationCanceledException) { }
    }
    public void SessionChanged() { lifetime.Cancel(); Status = "Сеанс изменён. Окно закрыто."; CloseRequested?.Invoke(); }
    public async ValueTask DisposeAsync() { lifetime.Cancel(); countdownLifetime?.Cancel(); await flow.DisposeAsync(); }
}
