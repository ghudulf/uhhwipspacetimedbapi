using System.Diagnostics;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui;

public partial class AppShell : Shell
{
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
        Console.WriteLine($"[AppShell] OnNavigating: {args.Current?.Location} -> {args.Target?.Location}");
        Debug.WriteLine($"[AppShell] OnNavigating: {args.Current?.Location} -> {args.Target?.Location}");
        base.OnNavigating(args);
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        Console.WriteLine($"[AppShell] OnNavigated: {args.Current?.Location}");
        Debug.WriteLine($"[AppShell] OnNavigated: {args.Current?.Location}");
        base.OnNavigated(args);
    }
}
