using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public partial class PasskeyCredentialsViewModel(IPasskeyManagementApi api, string bearer, Func<Task<bool>> register) : ObservableObject, IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private bool needsRecheck = true;
    public ObservableCollection<PasskeyCredentialInfo> Credentials { get; } = new();
    [ObservableProperty] private string status = "Проверьте список ключей доступа.";
    [ObservableProperty] private bool isBusy;
    public bool CanRefresh => !IsBusy && !lifetime.IsCancellationRequested;
    public bool CanMutate => CanRefresh && !needsRecheck;
    public event Action? CloseRequested;
    private void Changed()
    {
        OnPropertyChanged(nameof(CanMutate)); OnPropertyChanged(nameof(CanRefresh));
        RefreshCommand.NotifyCanExecuteChanged(); RegisterCommand.NotifyCanExecuteChanged(); RemoveCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsBusyChanged(bool value) => Changed();
    private async Task Reload()
    {
        var credentials = await api.ListCredentialsAsync(bearer, lifetime.Token);
        if (lifetime.IsCancellationRequested) return;
        Credentials.Clear(); foreach (var credential in credentials) Credentials.Add(credential);
        needsRecheck = false; Status = credentials.Length == 0 ? "Ключи доступа не зарегистрированы." : $"Ключей доступа: {credentials.Length}";
    }
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        if (!CanRefresh) return;
        IsBusy = true;
        try { await Reload(); }
        catch { if (!lifetime.IsCancellationRequested) { needsRecheck = true; Status = "Не удалось проверить список. Повторите проверку позже."; } }
        finally { IsBusy = false; Changed(); }
    }
    [RelayCommand(CanExecute = nameof(CanMutate))]
    public async Task RegisterAsync()
    {
        if (!CanMutate) return;
        IsBusy = true; needsRecheck = true;
        try
        {
            if (await register() && !lifetime.IsCancellationRequested) await Reload();
            else if (!lifetime.IsCancellationRequested) Status = "Результат регистрации не подтверждён. Проверьте список перед повтором.";
        }
        catch { if (!lifetime.IsCancellationRequested) Status = "Результат неизвестен. Проверьте список перед повтором."; }
        finally { IsBusy = false; Changed(); }
    }
    [RelayCommand(CanExecute = nameof(CanMutate))]
    public async Task RemoveAsync(PasskeyCredentialInfo credential)
    {
        if (!CanMutate || !Credentials.Contains(credential)) return;
        IsBusy = true; needsRecheck = true;
        try { await api.RemoveCredentialAsync(bearer, credential.Id, lifetime.Token); if (!lifetime.IsCancellationRequested) await Reload(); }
        catch { if (!lifetime.IsCancellationRequested) Status = "Результат удаления неизвестен. Проверьте список перед повтором."; }
        finally { IsBusy = false; Changed(); }
    }
    public void SessionChanged() { lifetime.Cancel(); Credentials.Clear(); Status = "Сеанс изменён. Окно закрыто."; Changed(); CloseRequested?.Invoke(); }
    public ValueTask DisposeAsync() { lifetime.Cancel(); Credentials.Clear(); Changed(); return ValueTask.CompletedTask; }
}
