using Avalonia;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
<<<<<<< HEAD
using Serilog;
using Serilog.Events;
=======
using Avalonia.WebView.Desktop;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.File;
>>>>>>> maintofix
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
<<<<<<< HEAD
=======
                .WriteTo.File("logs/debug-.log",
                    rollingInterval: RollingInterval.Day,
                    restrictedToMinimumLevel: LogEventLevel.Verbose)
               
>>>>>>> maintofix
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
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
<<<<<<< HEAD
            .UseReactiveUI();
=======
            .UseReactiveUI()
            .UseDesktopWebView();
>>>>>>> maintofix

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
