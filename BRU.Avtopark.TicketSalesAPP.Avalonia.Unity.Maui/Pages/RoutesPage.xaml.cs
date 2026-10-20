using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class RoutesPage : ContentPage
{
    private bool _avaloniaReady;

    public RoutesPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Log.Information("[RoutesPage] OnAppearing");

        if (!_avaloniaReady)
            _ = InitialiseAvaloniaViewAsync();
    }

    private async Task InitialiseAvaloniaViewAsync()
    {
        try
        {
            // Give the Avalonia view a tick to attach and load its ViewModel
            await Task.Delay(150);

            _avaloniaReady = true;

            Dispatcher.Dispatch(() =>
            {
                LoadingOverlay.IsVisible = false;
                Log.Information("[RoutesPage] Avalonia view ready");
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[RoutesPage] Failed to initialise Avalonia view");
            ShowError($"Не удалось загрузить маршруты: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        Dispatcher.Dispatch(() =>
        {
            LoadingOverlay.IsVisible = false;
            ErrorLabel.Text = message;
            ErrorOverlay.IsVisible = true;
        });
    }

    private void OnRetryClicked(object? sender, EventArgs e)
    {
        Log.Information("[RoutesPage] Retry clicked");
        _avaloniaReady = false;
        ErrorOverlay.IsVisible = false;
        LoadingOverlay.IsVisible = true;
        _ = InitialiseAvaloniaViewAsync();
    }
}
