using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class MainContentPage : ContentPage
{
    public MainContentPage()
    {
        InitializeComponent();
        Log.Information("[MainContentPage] Initialized");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var auth = MauiAuthService.Instance;
        bool hasToken = auth.HasValidTokenSync();

        StatusLabel.Text = hasToken
            ? $"Авторизован · токен действителен до {auth.TokenExpiresAt:HH:mm dd.MM}"
            : "Нет активного токена";

        Log.Debug("[MainContentPage] Status refreshed, hasToken={H}", hasToken);
    }

    private async void OnProfileTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("//main/profile_tab/profile");

    private async void OnSellTicketTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("//main/tickets_tab/tickets");

    private async void OnRoutesTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("//main/routes_tab/routes");

    private async void OnScheduleTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("//main/routes_tab/routes");
}