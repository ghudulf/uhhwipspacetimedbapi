using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BRU.Avtopark.Client;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class PasskeyCredentialsWindow : Window
{
    public PasskeyCredentialsWindow(PasskeyCredentialsViewModel? model = null)
    {
        InitializeComponent();
        var bearer = ApiClientService.Instance.AuthToken;
        var origin = ApiClientService.Instance.CurrentBaseUrl;
        async Task<bool> Register()
        {
            var browser = new ProviderLoginWindow("passkey", "register");
            await browser.ShowDialog(this);
            return browser.Succeeded;
        }
        var vm = model ?? new PasskeyCredentialsViewModel(ProviderClientContext.CreateManagementApi(), bearer ?? "", Register);
        DataContext = vm;
        void Changed(object? sender, string? token)
        { if (token != bearer || ApiClientService.Instance.CurrentBaseUrl != origin) Dispatcher.UIThread.Post(vm.SessionChanged); }
        ApiClientService.Instance.OnAuthTokenChanged += Changed;
        vm.CloseRequested += Close;
        Opened += async (_, _) => await vm.RefreshAsync();
        Closed += async (_, _) => { ApiClientService.Instance.OnAuthTokenChanged -= Changed; await vm.DisposeAsync(); };
    }
    private async void Remove_Click(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PasskeyCredentialInfo credential } && DataContext is PasskeyCredentialsViewModel vm) await vm.RemoveAsync(credential);
    }
    private void Close_Click(object? sender, RoutedEventArgs args) => Close();
}
