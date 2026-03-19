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

    // Route helper — desktop uses flat routes, mobile uses nested ones
    private static string Route(string desktop, string mobile)
        => AppShell.IsDesktop ? $"//{desktop}" : $"//{mobile}";

    private async void OnProfileTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(Route("profile", "dashboard_m"));

    private async void OnSellTicketTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(Route("tickets", "tickets_m"));

    private async void OnRoutesTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(Route("routes", "routes_m"));

    private async void OnScheduleTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(Route("routes", "routes_m"));
}
