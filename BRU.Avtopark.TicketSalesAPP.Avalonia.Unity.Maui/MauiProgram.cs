using Avalonia.Controls.Maui;
using Avalonia.Controls.Maui.Essentials;
using Avalonia.Controls.Maui.LifecycleEvents;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Handlers;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.LifecycleEvents;
using Serilog;
using Serilog.Events;
using UraniumUI;
using UraniumUI.Dialogs;
using UraniumUI.Options;
 


namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // ── Serilog: configure before anything else so all subsystems log from the start ──
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BRU.Avtopark.TicketSalesApp", "logs", "maui-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("[MAUI] Serilog initialized — log path: {Path}", logPath);
        Console.WriteLine("[MAUI] CreateMauiApp: start");

        try
        {
            var builder = MauiApp.CreateBuilder();
            Console.WriteLine("[MAUI] CreateMauiApp: builder created");

            // Signal shared Avalonia App.axaml.cs to skip the desktop startup flow
            AppContext.SetData("MAUI_HOST", true);
            Console.WriteLine("[MAUI] CreateMauiApp: MAUI_HOST flag set");

            builder
                .UseMauiApp<App>()
                // Desktop (net11.0): Avalonia is the full host — owns the entire window surface.
                // Mobile/Browser: Avalonia runs in embedding mode — AvaloniaView renders controls natively.
#if !IOS && !MACCATALYST && !ANDROID && !WINDOWS
                .UseAvaloniaApp()
#else
                .UseAvaloniaEmbedding<BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.App>()
#endif
                .UseAvaloniaEssentials()

                .UseUraniumUI()
            .UseUraniumUIMaterial()
            .UseUraniumUIBlurs(true)
                
                 .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                // Register custom Avalonia-backed MAUI handlers
                .ConfigureMauiHandlers(handlers =>
                {
                    handlers.AddHandler<OAuthLoginView, OAuthLoginViewHandler>();
                    handlers.AddHandler<TraditionalLoginView, TraditionalLoginViewHandler>();
                    Console.WriteLine("[MAUI] Registered OAuthLoginViewHandler and TraditionalLoginViewHandler");
                })
                .ConfigureLifecycleEvents(events =>
                {
                    events.AddWindows(avalonia =>
                    {
                        avalonia.OnLaunched((app, args) =>
                        {
                            // Navigation is handled by SplashPage.OnAppearing — no need to
                            // fire GoToAsync here, which would race with Shell initialization.
                            Console.WriteLine("[MAUI] OnLaunched: Shell will navigate from SplashPage");
                        });
                    });
                });

            // Register shared MAUI services (must be done on builder.Services, not in the fluent chain)
            builder.Services.AddSingleton<MauiAuthService>(_ => MauiAuthService.Instance);

            Console.WriteLine("[MAUI] CreateMauiApp: builder configured");

#if DEBUG
            builder.Logging
                .AddDebug()
                .SetMinimumLevel(LogLevel.Trace);
            Console.WriteLine("[MAUI] CreateMauiApp: debug logging enabled");
#endif

            Console.WriteLine("[MAUI] CreateMauiApp: calling builder.Build()...");
            var app = builder.Build();
            Console.WriteLine("[MAUI] CreateMauiApp: build complete");
            return app;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[MAUI] CreateMauiApp FAILED:");
            Console.Error.WriteLine(ex.ToString());
            throw;
        }
    }
}
