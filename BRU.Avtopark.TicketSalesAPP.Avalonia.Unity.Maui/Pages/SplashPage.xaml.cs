using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class SplashPage : ContentPage
{
    public SplashPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunStartupFlow();
    }

    private async Task RunStartupFlow()
    {
        try
        {
            // Discover the API server on the local network before anything else
            StatusLabel.Text = "Поиск сервера в сети...";
            await MauiAuthService.Instance.InitializeAsync();

            var serverUrl = BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.ApiClientService.Instance.CurrentBaseUrl;
            var host = serverUrl?.Replace("http://", "").Replace("/api/", "") ?? "localhost:5000";
            StatusLabel.Text = $"Сервер: {host}";
            await Task.Delay(400);

            StatusLabel.Text = "Проверка авторизации...";
            await Task.Delay(400);

            // RestoreSessionAsync loads the token from disk AND sets ApiClientService.AuthToken
            // so HasValidTokenSync() works correctly on all subsequent pages.
            bool restored = await MauiAuthService.Instance.RestoreSessionAsync();

            if (restored)
            {
                Serilog.Log.Information("[SplashPage] Session restored — navigating to main");
                StatusLabel.Text = "Добро пожаловать!";
                await Task.Delay(400);
                await AppShell.NavigateToMainAsync();
            }
            else
            {
                Serilog.Log.Information("[SplashPage] No valid session — navigating to login");
                await Shell.Current.GoToAsync("//login");
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[SplashPage] Startup flow error");
            StatusLabel.Text = "Ошибка запуска";
            Spinner.IsRunning = false;
            await Task.Delay(1500);
            await Shell.Current.GoToAsync("//login");
        }
    }
}