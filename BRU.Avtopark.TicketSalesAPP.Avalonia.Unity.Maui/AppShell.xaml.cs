using System.Diagnostics;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui;

public partial class AppShell : Shell
{
    // Routes that belong to the authenticated TabBar section
    private static readonly HashSet<string> _authenticatedRoutes =
        new(StringComparer.OrdinalIgnoreCase) { "main", "dashboard", "routes", "tickets", "profile" };

    public AppShell()
    {
        Console.WriteLine("[AppShell] Constructor: start");
        Debug.WriteLine("[AppShell] Constructor: start");
        try
        {
            InitializeComponent();
            Console.WriteLine("[AppShell] Constructor: InitializeComponent complete");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AppShell] Constructor FAILED: {ex}");
            Debug.WriteLine($"[AppShell] Constructor FAILED: {ex}");
            throw;
        }
    }
    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        Log.Debug("[AppShell] OnNavigating: {From} -> {To}",
            args.Current?.Location, args.Target?.Location);
        base.OnNavigating(args);
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        var location = args.Current?.Location?.ToString() ?? string.Empty;
        Log.Debug("[AppShell] OnNavigated: {Location}", location);

        // Show the tab bar only when inside the authenticated section
        bool isAuthenticated = _authenticatedRoutes.Any(r =>
            location.Contains(r, StringComparison.OrdinalIgnoreCase));

        Shell.SetTabBarIsVisible(this, isAuthenticated);

        base.OnNavigated(args);
    }
}