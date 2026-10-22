using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BRU.Avtopark.Client;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class EntraAccountLinkWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly string? bearer;
    private readonly IProviderLoginApi? api;
    public EntraAccountLinkWindow(IProviderLoginApi? api = null)
    {
        InitializeComponent();
        bearer = ApiClientService.Instance.AuthToken;
        try { this.api = api ?? ProviderClientContext.CreateApi(); } catch { StatusText.Text = "Сервер недоступен."; StatusButton.IsEnabled = false; }
        ApiClientService.Instance.OnAuthTokenChanged += Changed;
        Closed += (_, _) => { ApiClientService.Instance.OnAuthTokenChanged -= Changed; lifetime.Cancel(); };
    }
    private void Changed(object? sender, string? token)
    {
        if (token != bearer) { lifetime.Cancel(); Dispatcher.UIThread.Post(Close); }
    }
    private void DisableMutations() { LinkButton.IsEnabled = false; UnlinkButton.IsEnabled = false; }
    private async void Status_Click(object? sender, RoutedEventArgs args)
    {
        DisableMutations(); StatusButton.IsEnabled = false;
        try
        {
            var status = await api!.EntraLinkStatusAsync(bearer ?? "", lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            StatusText.Text = status.State == "linked" ? "Учётная запись Microsoft связана с текущим аккаунтом приложения." : "Учётная запись Microsoft не связана.";
            LinkButton.IsEnabled = status.State == "unlinked";
            UnlinkButton.IsEnabled = status.State == "linked";
        }
        catch { if (!lifetime.IsCancellationRequested) StatusText.Text = "Не удалось проверить связь. Повторите проверку позже."; }
        finally { if (!lifetime.IsCancellationRequested) StatusButton.IsEnabled = true; }
    }
    private async void Link_Click(object? sender, RoutedEventArgs args)
    {
        DisableMutations(); StatusButton.IsEnabled = false;
        try
        {
            var window = new ProviderLoginWindow("entra", "link");
            await window.ShowDialog(this);
            if (!lifetime.IsCancellationRequested) StatusText.Text = window.Succeeded ? "Связь подтверждена сервером." : "Результат не подтверждён. Проверьте состояние перед повтором.";
        }
        catch { if (!lifetime.IsCancellationRequested) StatusText.Text = "Результат неизвестен. Проверьте состояние перед повтором."; }
        finally { if (!lifetime.IsCancellationRequested) StatusButton.IsEnabled = true; }
    }
    private async void Unlink_Click(object? sender, RoutedEventArgs args)
    {
        DisableMutations(); StatusButton.IsEnabled = false;
        try
        {
            await api!.UnlinkEntraAsync(bearer ?? "", lifetime.Token);
            if (!lifetime.IsCancellationRequested) StatusText.Text = "Связь удалена. Для входа потребуется другой способ.";
        }
        catch { if (!lifetime.IsCancellationRequested) StatusText.Text = "Результат неизвестен. Проверьте состояние перед повтором."; }
        finally { if (!lifetime.IsCancellationRequested) StatusButton.IsEnabled = true; }
    }
    private void Close_Click(object? sender, RoutedEventArgs args) => Close();
}
