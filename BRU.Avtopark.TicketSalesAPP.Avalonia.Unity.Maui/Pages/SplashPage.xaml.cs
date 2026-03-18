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
            StatusLabel.Text = "Проверка авторизации...";
            await Task.Delay(800); // brief splash display

            bool hasValidToken = await MauiAuthService.Instance.HasValidTokenAsync();

            if (hasValidToken)
            {
                Console.WriteLine("[SplashPage] Valid token found, navigating to main");
                StatusLabel.Text = "Добро пожаловать!";
                await Task.Delay(400);
                await Shell.Current.GoToAsync("//main");
            }
            else
            {
                Console.WriteLine("[SplashPage] No valid token, navigating to login");
                await Shell.Current.GoToAsync("//login");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SplashPage] Startup flow error: {ex}");
            StatusLabel.Text = "Ошибка запуска";
            Spinner.IsRunning = false;
            await Task.Delay(1500);
            await Shell.Current.GoToAsync("//login");
        }
    }
}
