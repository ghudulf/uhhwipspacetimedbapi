using Avalonia;
using Avalonia.Controls;
using ReactiveUI.Avalonia;
using Avalonia.WebView.Desktop;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.File;
using System;
using System.Linq;
using System.Threading;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.Debug()
                .WriteTo.File("logs/app-.log",
                    rollingInterval: RollingInterval.Day,
                    restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.File("logs/debug-.log",
                    rollingInterval: RollingInterval.Day,
                    restrictedToMinimumLevel: LogEventLevel.Verbose)
               
                .CreateLogger();

            Log.Information("Starting application...");

            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }

    }


    // Avalonia configuration, don't remove; also used by visual designer.
    // NOTE (Avalonia 12 migration): UsePlatformDetect() is valid in Avalonia 12 — no change needed.
    // NOTE (Avalonia 12 migration): UseDesktopWebView() comes from WebView.Avalonia.Desktop (pinned at 11.0.0.1).
    //   No Avalonia 12-compatible release of WebView.Avalonia.Desktop exists as of migration time.
    //   The method name UseDesktopWebView() has not been renamed; the incompatibility is at the package level.
    //   Resolution: pin-and-test — if the package fails to resolve against Avalonia 12, remove .UseDesktopWebView()
    //   and guard AvaloniaWebViewBuilder.Initialize(default) in App.axaml.cs with #if DESKTOP until a
    //   compatible release is available. See task 10.10 for the package-level tracking.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(_ => { })
            .UseDesktopWebView();

    private static void SilenceConsole()
    {
        new Thread(() =>
            {
                Console.CursorVisible = false;
                while(true)
                    Console.ReadKey(true);
            })
            { IsBackground = true }.Start();
    }
}
