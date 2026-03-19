using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

/// <summary>
/// MAUI ContentPage that hosts the Avalonia <c>TraditionalLoginControl</c> via
/// <c>AvaloniaView</c>. The control runs the full username/password → 2FA → success
/// wizard internally and raises <c>AuthCompleted</c> when done.
/// </summary>
public partial class TraditionalLoginPage : ContentPage
{
    public TraditionalLoginPage()
    {
        try
        {
            InitializeComponent();
            Log.Information("[TraditionalLoginPage] Initialized");
            Console.WriteLine("[TraditionalLoginPage] Initialized");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "[TraditionalLoginPage] Constructor FAILED");
            throw;
        }
    }

    /// <summary>
    /// Called by the embedded TraditionalLoginControl when the wizard completes.
    /// Runs on the Avalonia UI thread — marshal to MAUI dispatcher.
    /// </summary>
    private void OnAvaloniaAuthCompleted(object? sender, bool success)
    {
        try
        {
            Log.Information("[TraditionalLoginPage] AuthCompleted: success={Success}", success);
            Console.WriteLine($"[TraditionalLoginPage] AuthCompleted: success={success}");

            // MainThread.BeginInvokeOnMainThread is broken in Avalonia.Controls.Maui desktop backend.
            // Use Dispatcher.Dispatch instead.
            Dispatcher.Dispatch(async () =>
            {
                try
                {
                    if (success)
                    {
                        Log.Information("[TraditionalLoginPage] Login success → navigating to //main");
                        Console.WriteLine("[TraditionalLoginPage] Login success → navigating to //main");
                        await AppShell.NavigateToMainAsync();
                    }
                    else
                    {
                        Log.Warning("[TraditionalLoginPage] Login cancelled/failed");
                        Console.WriteLine("[TraditionalLoginPage] Login cancelled/failed");
                        ErrorLabel.Text = "Вход отменён или не удался.";
                        ErrorLabel.IsVisible = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[TraditionalLoginPage] Navigation after auth failed");
                    Console.Error.WriteLine($"[TraditionalLoginPage] Navigation after auth failed: {ex}");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[TraditionalLoginPage] OnAvaloniaAuthCompleted threw");
            Console.Error.WriteLine($"[TraditionalLoginPage] OnAvaloniaAuthCompleted threw: {ex}");
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        try
        {
            Log.Debug("[TraditionalLoginPage] Back clicked");
            Console.WriteLine("[TraditionalLoginPage] Back clicked");
            await Shell.Current.GoToAsync("//login");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[TraditionalLoginPage] OnBackClicked navigation failed");
            Console.Error.WriteLine($"[TraditionalLoginPage] OnBackClicked failed: {ex}");
        }
    }
}
