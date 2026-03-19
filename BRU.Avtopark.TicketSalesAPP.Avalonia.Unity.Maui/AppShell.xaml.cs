using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui;

public partial class AppShell : Shell
{
    private static readonly HashSet<string> _authenticatedRoutes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "dashboard", "routes", "tickets", "profile",
            "dashboard_m", "routes_m", "tickets_m", "profile_m",
            "main_mobile"
        };

    private string _firstTabRoute = "//dashboard";

    /// <summary>True when running on a desktop idiom — pages use a custom top nav bar.</summary>
    public static bool IsDesktop { get; private set; }

    public AppShell()
    {
        InitializeComponent();

        IsDesktop = DeviceInfo.Idiom == DeviceIdiom.Desktop
                 || DeviceInfo.Idiom == DeviceIdiom.TV
                 || DeviceInfo.Idiom == DeviceIdiom.Unknown; // net11.0 generic desktop returns Unknown

        Log.Debug("[AppShell] Idiom={Idiom} IsDesktop={D}", DeviceInfo.Idiom, IsDesktop);

        if (IsDesktop)
        {
            // Show flat desktop ShellContent items — no Tab wrapper, no tab bar
            DesktopDashboard.IsVisible = false; // revealed on NavigateToMainAsync
            DesktopRoutes.IsVisible    = false;
            DesktopTickets.IsVisible   = false;
            DesktopProfile.IsVisible   = false;
            MobileTabs.IsVisible       = false;
            _firstTabRoute             = "//dashboard";
        }
        else
        {
            MobileTabs.IsVisible = false; // revealed on NavigateToMainAsync
            _firstTabRoute       = "//dashboard_m";
        }

        // Ensure tab bar is never shown on desktop
        Shell.SetTabBarIsVisible(this, !IsDesktop);
    }

    public static async Task NavigateToMainAsync()
    {
        if (Shell.Current is not AppShell shell) return;

        if (IsDesktop)
        {
            // Reveal all desktop pages so their routes are navigable
            shell.DesktopDashboard.IsVisible = true;
            shell.DesktopRoutes.IsVisible    = true;
            shell.DesktopTickets.IsVisible   = true;
            shell.DesktopProfile.IsVisible   = true;
            Shell.SetTabBarIsVisible(Shell.Current, false);
        }
        else
        {
            shell.MobileTabs.IsVisible = true;
        }

        await Shell.Current.GoToAsync(shell._firstTabRoute);
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        var location = args.Current?.Location?.ToString() ?? string.Empty;
        Log.Debug("[AppShell] OnNavigated: {Location}", location);

        bool isAuthenticated = _authenticatedRoutes.Any(r =>
            location.Contains(r, StringComparison.OrdinalIgnoreCase));

        if (!isAuthenticated && !IsDesktop)
            MobileTabs.IsVisible = false;

        // Desktop: always hide Shell tab bar
        Shell.SetTabBarIsVisible(this, isAuthenticated && !IsDesktop);

        base.OnNavigated(args);
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        Log.Debug("[AppShell] OnNavigating: {From} → {To}",
            args.Current?.Location, args.Target?.Location);
        base.OnNavigating(args);
    }
}
