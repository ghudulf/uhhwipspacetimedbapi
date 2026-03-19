using Avalonia.Controls.Maui;
using Avalonia.Controls.Maui.Essentials;
using Avalonia.Controls.Maui.LifecycleEvents;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Handlers;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Avalonia.Controls.Maui.Compatibility;

using Microsoft.Maui.LifecycleEvents;
using Serilog;
using Serilog.Events;

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
                .UseAvaloniaCompatibility()
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
                        avalonia.OnLaunched(async (app, args) =>
                        {
                            Console.WriteLine("[MAUI] OnLaunched: checking token for initial navigation");
                            try
                            {
                                bool hasValidToken = await MauiAuthService.Instance.HasValidTokenAsync();

                                Console.WriteLine($"[MAUI] OnLaunched: hasValidToken={hasValidToken}, navigating to //splash");
                                await Shell.Current.GoToAsync("//splash");
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[MAUI] OnLaunched error: {ex}");
                                try { await Shell.Current.GoToAsync("//splash"); }
                                catch { /* ignore secondary failure */ }
                            }
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