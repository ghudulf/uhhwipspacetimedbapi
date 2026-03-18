using Avalonia.Controls.Maui;
using Avalonia.Controls.Maui.Essentials;
using Microsoft.Extensions.Logging;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
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
                .UseAvaloniaApp()         // full hosting: Avalonia replaces MAUI rendering
                .UseAvaloniaEssentials()  // Avalonia-backed Essentials APIs
                .ConfigureFonts(fonts =>
                {
                    // Only register fonts that actually exist in Resources/Fonts/
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

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
