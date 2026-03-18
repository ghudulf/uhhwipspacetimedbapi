using System.Diagnostics;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui;

public partial class App : Application
{
    public App()
    {
        Console.WriteLine("[App] Constructor: start");
        Debug.WriteLine("[App] Constructor: start");
        try
        {
            InitializeComponent();
            Console.WriteLine("[App] Constructor: InitializeComponent complete");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[App] Constructor FAILED: {ex}");
            Debug.WriteLine($"[App] Constructor FAILED: {ex}");
            throw;
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Console.WriteLine("[App] CreateWindow: called");
        Debug.WriteLine("[App] CreateWindow: called");
        try
        {
            var shell = new AppShell();
            Console.WriteLine("[App] CreateWindow: AppShell created");
            var window = new Window(shell);
            Console.WriteLine("[App] CreateWindow: Window created");
            return window;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[App] CreateWindow FAILED: {ex}");
            Debug.WriteLine($"[App] CreateWindow FAILED: {ex}");
            throw;
        }
    }

    protected override void OnStart()
    {
        Console.WriteLine("[App] OnStart");
        Debug.WriteLine("[App] OnStart");
        base.OnStart();
    }

    protected override void OnSleep()
    {
        Console.WriteLine("[App] OnSleep");
        Debug.WriteLine("[App] OnSleep");
        base.OnSleep();
    }

    protected override void OnResume()
    {
        Console.WriteLine("[App] OnResume");
        Debug.WriteLine("[App] OnResume");
        base.OnResume();
    }
}
