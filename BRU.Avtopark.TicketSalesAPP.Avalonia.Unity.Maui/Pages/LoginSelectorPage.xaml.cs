using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class LoginSelectorPage : ContentPage
{
    public LoginSelectorPage()
    {
        try
        {
            InitializeComponent();
            Log.Debug("[LoginSelectorPage] Initialized");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "[LoginSelectorPage] Constructor FAILED");
            throw;
        }
    }

    protected override void OnAppearing()
    {
        try
        {
            Log.Information("[LoginSelectorPage] OnAppearing — page is now visible");
            base.OnAppearing();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LoginSelectorPage] OnAppearing threw");
        }
    }

    protected override void OnDisappearing()
    {
        try
        {
            Log.Information("[LoginSelectorPage] OnDisappearing");
            base.OnDisappearing();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LoginSelectorPage] OnDisappearing threw");
        }
    }

    private async void OnOAuthClicked(object sender, EventArgs e)
    {
        try
        {
            Log.Information("[LoginSelectorPage] OAuth selected → navigating to OAuthLoginPage");
            Console.WriteLine("[LoginSelectorPage] OAuth selected → navigating to OAuthLoginPage");
            await Shell.Current.GoToAsync("///oauth-login");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LoginSelectorPage] OnOAuthClicked navigation failed");
            Console.Error.WriteLine($"[LoginSelectorPage] OnOAuthClicked failed: {ex}");
        }
    }

    private async void OnTraditionalClicked(object sender, EventArgs e)
    {
        try
        {
            Log.Information("[LoginSelectorPage] Traditional selected → navigating to TraditionalLoginPage");
            Console.WriteLine("[LoginSelectorPage] Traditional selected → navigating to TraditionalLoginPage");
            await Shell.Current.GoToAsync("///traditional-login");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LoginSelectorPage] OnTraditionalClicked navigation failed");
            Console.Error.WriteLine($"[LoginSelectorPage] OnTraditionalClicked failed: {ex}");
        }
    }
}
