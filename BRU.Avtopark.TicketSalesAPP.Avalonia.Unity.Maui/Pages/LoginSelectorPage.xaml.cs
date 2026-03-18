namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class LoginSelectorPage : ContentPage
{
    public LoginSelectorPage()
    {
        InitializeComponent();
    }

    private async void OnOAuthClicked(object sender, EventArgs e)
    {
        Console.WriteLine("[LoginSelectorPage] OAuth selected → navigating to OAuthLoginPage");
        await Shell.Current.GoToAsync("///oauth-login");
    }

    private async void OnTraditionalClicked(object sender, EventArgs e)
    {
        Console.WriteLine("[LoginSelectorPage] Traditional selected → navigating to TraditionalLoginPage");
        await Shell.Current.GoToAsync("///traditional-login");
    }
}
