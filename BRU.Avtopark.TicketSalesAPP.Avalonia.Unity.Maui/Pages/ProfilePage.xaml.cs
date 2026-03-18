using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class ProfilePage : ContentPage
{
    private bool _logoutInProgress;

    public ProfilePage()
    {
        try
        {
            InitializeComponent();
            Log.Debug("[ProfilePage] Initialized");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "[ProfilePage] Constructor FAILED");
            throw;
        }
    }

    protected override void OnAppearing()
    {
        try
        {
            Log.Debug("[ProfilePage] OnAppearing");
            base.OnAppearing();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ProfilePage] OnAppearing threw");
        }
    }

    protected override void OnDisappearing()
    {
        try
        {
            Log.Debug("[ProfilePage] OnDisappearing");
            base.OnDisappearing();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ProfilePage] OnDisappearing threw");
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        if (_logoutInProgress) return;
        _logoutInProgress = true;
        try
        {
            Log.Information("[ProfilePage] Logout requested");
            await MauiAuthService.Instance.LogoutAsync();

            if (Shell.Current is AppShell appShell)
            {
                appShell.BeginProgrammaticNavigation();
                await appShell.SafeGoToAsync("//login");
            }
            else
            {
                await Shell.Current.GoToAsync("//login");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ProfilePage] Logout failed");
            Console.Error.WriteLine($"[ProfilePage] Logout failed: {ex}");
        }
        finally
        {
            _logoutInProgress = false;
        }
    }
}
