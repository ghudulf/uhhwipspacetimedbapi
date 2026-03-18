using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class SplashPage : ContentPage
{
    public SplashPage()
    {
        try
        {
            InitializeComponent();
            Log.Debug("[SplashPage] Initialized");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "[SplashPage] Constructor FAILED");
            throw;
        }
    }

    protected override void OnAppearing()
    {
        try
        {
            Log.Debug("[SplashPage] OnAppearing");
            base.OnAppearing();
            // Dispatch off the current call stack so the Shell navigation that brought
            // us here is fully committed before we fire the next one.
            Dispatcher.DispatchAsync(RunStartupFlow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SplashPage] OnAppearing threw");
        }
    }

    private bool _navigationStarted;
    private bool _startupFlowCompleted;

    private async Task RunStartupFlow()
    {
        if (_navigationStarted || _startupFlowCompleted) return;
        _navigationStarted = true;

        try
        {
            Log.Information("[SplashPage] RunStartupFlow: start");
            StatusLabel.Text = "Проверка авторизации...";

            // Wait for the Shell to fully settle its handler setup before
            // firing any navigation. The Shell initializes handlers for all
            // ShellContent items on first render; navigating too early causes
            // "Pending Navigations still processing" in ShellSectionHandler.
            await Task.Delay(1200);

            bool hasValidToken = await MauiAuthService.Instance.HasValidTokenAsync();
            Log.Information("[SplashPage] HasValidToken={HasToken}", hasValidToken);

            if (hasValidToken)
            {
                Log.Information("[SplashPage] Valid token found, navigating to main");
                Console.WriteLine("[SplashPage] Valid token found, navigating to main");
                StatusLabel.Text = "Добро пожаловать!";
                await Task.Delay(300);
                await NavigateProgrammaticallyAsync("//main");
            }
            else
            {
                Log.Information("[SplashPage] No valid token, navigating to login");
                Console.WriteLine("[SplashPage] No valid token, navigating to login");
                await NavigateProgrammaticallyAsync("//login");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SplashPage] Startup flow error");
            Console.Error.WriteLine($"[SplashPage] Startup flow error: {ex}");
            try
            {
                StatusLabel.Text = "Ошибка запуска";
                Spinner.IsRunning = false;
                await Task.Delay(1500);
                await NavigateProgrammaticallyAsync("//login");
            }
            catch (Exception inner)
            {
                Log.Error(inner, "[SplashPage] Recovery navigation also failed");
            }
        }
        finally
        {
            _navigationStarted = false;
            _startupFlowCompleted = true;
        }
    }

    private static async Task NavigateProgrammaticallyAsync(string route)
    {
        try
        {
            Log.Debug("[SplashPage] NavigateProgrammaticallyAsync → {Route}", route);
            if (Shell.Current is AppShell appShell)
            {
                appShell.BeginProgrammaticNavigation();
                await appShell.SafeGoToAsync(route);
            }
            else
            {
                await Shell.Current.GoToAsync(route);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SplashPage] NavigateProgrammaticallyAsync to {Route} failed", route);
            throw;
        }
    }
}
