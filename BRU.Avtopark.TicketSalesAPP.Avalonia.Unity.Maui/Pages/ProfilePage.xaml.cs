using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class ProfilePage : ContentPage
{
    public ProfilePage() => InitializeComponent();

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        Log.Information("[ProfilePage] Logout requested");
        await MauiAuthService.Instance.LogoutAsync();
        await Shell.Current.GoToAsync("//login");
    }
}
