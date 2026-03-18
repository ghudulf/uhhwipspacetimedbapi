using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class MainContentPage : ContentPage
{
    public MainContentPage()
    {
        try
        {
            InitializeComponent();
            Log.Information("[MainContentPage] Initialized");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "[MainContentPage] Constructor FAILED");
            throw;
        }
    }

    protected override void OnAppearing()
    {
        try
        {
            Log.Debug("[MainContentPage] OnAppearing");
            base.OnAppearing();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainContentPage] OnAppearing threw");
        }
    }

    private void RefreshStatus()
    {
        try
        {
            var auth = MauiAuthService.Instance;
            bool hasToken = auth.HasValidTokenSync();

            StatusLabel.Text = hasToken
                ? $"Авторизован · токен действителен до {auth.TokenExpiresAt:HH:mm dd.MM}"
                : "Нет активного токена";

            Log.Debug("[MainContentPage] Status refreshed, hasToken={H}", hasToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainContentPage] RefreshStatus threw");
            Console.Error.WriteLine($"[MainContentPage] RefreshStatus failed: {ex}");
        }
    }

    private async void OnProfileTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            Log.Debug("[MainContentPage] Profile tapped → //main/profile_tab/profile");
            if (Shell.Current is AppShell s) await s.SafeGoToAsync("//main/profile_tab/profile");
            else await Shell.Current.GoToAsync("//main/profile_tab/profile");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainContentPage] OnProfileTapped navigation failed");
        }
    }

    private async void OnSellTicketTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            Log.Debug("[MainContentPage] SellTicket tapped → //main/tickets_tab/tickets");
            if (Shell.Current is AppShell s) await s.SafeGoToAsync("//main/tickets_tab/tickets");
            else await Shell.Current.GoToAsync("//main/tickets_tab/tickets");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainContentPage] OnSellTicketTapped navigation failed");
        }
    }

    private async void OnRoutesTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            Log.Debug("[MainContentPage] Routes tapped → //main/routes_tab/routes");
            if (Shell.Current is AppShell s) await s.SafeGoToAsync("//main/routes_tab/routes");
            else await Shell.Current.GoToAsync("//main/routes_tab/routes");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainContentPage] OnRoutesTapped navigation failed");
        }
    }

    private async void OnScheduleTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            Log.Debug("[MainContentPage] Schedule tapped → //main/routes_tab/routes");
            if (Shell.Current is AppShell s) await s.SafeGoToAsync("//main/routes_tab/routes");
            else await Shell.Current.GoToAsync("//main/routes_tab/routes");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainContentPage] OnScheduleTapped navigation failed");
        }
    }
}
