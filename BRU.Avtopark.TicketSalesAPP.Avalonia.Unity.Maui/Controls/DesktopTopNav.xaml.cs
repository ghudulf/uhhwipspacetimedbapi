namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Controls;

public partial class DesktopTopNav : ContentView
{
    public static readonly BindableProperty ActiveRouteProperty =
        BindableProperty.Create(nameof(ActiveRoute), typeof(string), typeof(DesktopTopNav),
            defaultValue: "dashboard", propertyChanged: OnActiveRouteChanged);

    public string ActiveRoute
    {
        get => (string)GetValue(ActiveRouteProperty);
        set => SetValue(ActiveRouteProperty, value);
    }

    public DesktopTopNav()
    {
        InitializeComponent();
        IsVisible = AppShell.IsDesktop;
        if (AppShell.IsDesktop)
            UpdateActiveButton(ActiveRoute);
    }

    private static void OnActiveRouteChanged(BindableObject obj, object old, object newVal)
        => ((DesktopTopNav)obj).UpdateActiveButton((string)newVal);

    private void UpdateActiveButton(string route)
    {
        var activeText   = Application.Current!.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#FF7043") : Color.FromArgb("#E64A19");
        var inactiveText = Application.Current.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#9E9E9E") : Color.FromArgb("#757575");

        SetTab(BtnDashboard, BarDashboard, route == "dashboard", activeText, inactiveText);
        SetTab(BtnRoutes,    BarRoutes,    route == "routes",    activeText, inactiveText);
        SetTab(BtnTickets,   BarTickets,   route == "tickets",   activeText, inactiveText);
        SetTab(BtnProfile,   BarProfile,   route == "profile",   activeText, inactiveText);
    }

    private static void SetTab(Button btn, BoxView bar, bool active, Color activeColor, Color inactiveColor)
    {
        btn.TextColor  = active ? activeColor : inactiveColor;
        btn.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;
        bar.IsVisible  = active;
    }

    private async void OnDashboardClicked(object? s, EventArgs e)
        => await Shell.Current.GoToAsync("//dashboard");

    private async void OnRoutesClicked(object? s, EventArgs e)
        => await Shell.Current.GoToAsync("//routes");

    private async void OnTicketsClicked(object? s, EventArgs e)
        => await Shell.Current.GoToAsync("//tickets");

    private async void OnProfileClicked(object? s, EventArgs e)
        => await Shell.Current.GoToAsync("//profile");
}
