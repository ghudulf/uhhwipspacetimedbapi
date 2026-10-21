using Avalonia;
using Avalonia.Controls;
using Serilog;
using Serilog.Events;
using System;
using System.IO;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BRU.Avtopark.TicketSalesApp");
            var logs = Path.Combine(userData, "logs");
            Directory.CreateDirectory(logs);
            // MSIX and Flatpak installation directories are read-only.
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(userData, "WebView2"));
            BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.ClientLogging.Configure();

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
