using Avalonia;
using Avalonia.Controls;
using Serilog;
using Serilog.Events;
using System;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop;

class Program
{
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

            // --tray or --headless: start without any visible window, tray icon only
            bool headless = Array.Exists(args, a =>
                a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--headless", StringComparison.OrdinalIgnoreCase));

            if (headless)
            {
                Log.Information("Starting in headless/tray-only mode");
                BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.App.HeadlessMode = true;
            }
            else
            {
                Log.Information("Starting application...");
            }

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

    // Avalonia configuration — also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
