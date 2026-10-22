using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class QrDevicesWindow : Window
{
    public QrDevicesWindow()
    {
        InitializeComponent();
        var session = new QrWindowSession(this);
        Opened += async (_, _) =>
        {
            try
            {
                var api = QrClientContext.CreateApi();
                var username = await QrClientContext.AccountAsync(api.ApiBase, session.Bearer, session.Token);
                if (session.Token.IsCancellationRequested) return;
                var vm = new QrDevicesViewModel(api, DeviceKeyStore.Current, session.Bearer, username);
                DataContext = vm; session.Bind(vm);
                await vm.RefreshAsync();
            }
            catch (OperationCanceledException) { }
            catch { if (!session.Token.IsCancellationRequested) StatusText.Text = "Не удалось проверить текущую учётную запись. Проверьте подключение и повторите."; }
        };
    }
    private async void Revoke_Click(object? sender, RoutedEventArgs args)
    { if (DataContext is QrDevicesViewModel vm && sender is Button { DataContext: QrDeviceInfo device }) await vm.RevokeAsync(device); }
    private void Close_Click(object? sender, RoutedEventArgs args) => Close();
}
