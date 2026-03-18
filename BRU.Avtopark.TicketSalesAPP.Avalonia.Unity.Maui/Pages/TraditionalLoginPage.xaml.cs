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
        InitializeComponent();
        Console.WriteLine("[TraditionalLoginPage] Initialized");
    }

    /// <summary>
    /// Called by the embedded <see cref="BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Controls.TraditionalLoginControl"/>
    /// when the wizard completes. Runs on the Avalonia UI thread — marshal to MAUI main thread.
    /// </summary>
    private void OnAvaloniaAuthCompleted(object? sender, bool success)
    {
        Console.WriteLine($"[TraditionalLoginPage] AuthCompleted: success={success}");

        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (success)
            {
                Console.WriteLine("[TraditionalLoginPage] Login success → navigating to //main");
                await Shell.Current.GoToAsync("//main");
            }
            else
            {
                Console.WriteLine("[TraditionalLoginPage] Login cancelled/failed");
                ErrorLabel.Text = "Вход отменён или не удался.";
                ErrorLabel.IsVisible = true;
            }
        });
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        Console.WriteLine("[TraditionalLoginPage] Back clicked");
        await Shell.Current.GoToAsync("//login");
    }
}
