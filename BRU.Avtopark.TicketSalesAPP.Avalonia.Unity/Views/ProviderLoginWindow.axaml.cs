using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class ProviderLoginWindow : Window
{
    public string? LoginJson { get; private set; }
    public bool Succeeded { get; private set; }
    public ProviderLoginWindow(string provider = "entra", string purpose = "login", ProviderLoginViewModel? model = null)
    {
        InitializeComponent();
        var token = ApiClientService.Instance.AuthToken;
        var origin = ApiClientService.Instance.CurrentBaseUrl;
        var vm = model ?? new ProviderLoginViewModel(ProviderClientContext.CreateApi(), ProviderClientContext.Browser, provider, purpose, purpose == "login" ? null : token);
        DataContext = vm;
        void Changed(object? sender, string? current)
        {
            if (current != token || ApiClientService.Instance.CurrentBaseUrl != origin) Dispatcher.UIThread.Post(vm.SessionChanged);
        }
        ApiClientService.Instance.OnAuthTokenChanged += Changed;
        vm.CloseRequested += Close;
        vm.Completed += json => { LoginJson = json; Succeeded = true; Close(); };
        Opened += async (_, _) => await vm.StartAsync();
        Closed += async (_, _) => { ApiClientService.Instance.OnAuthTokenChanged -= Changed; await vm.DisposeAsync(); };
    }
    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close();
}
