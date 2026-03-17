using Avalonia.Controls.Maui;
using Avalonia.Controls.Maui.Essentials;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity;
using Microsoft.Extensions.Logging;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // Set MAUI_HOST flag for App.axaml.cs to detect MAUI-hosted mode
        AppContext.SetData("MAUI_HOST", true);

        builder
            .UseMauiApp<App>()                    // MAUI App.cs defined in this project
            .UseAvaloniaApp()                     // Full hosting: Avalonia replaces MAUI rendering
                                                  // For Browser/WASM use: .UseAvaloniaApp(useSingleViewLifetime: true)
            .UseAvaloniaEssentials()              // Avalonia implementations of Essentials APIs
            .ConfigureFonts(fonts =>
            {
                // Fonts placed in Resources/Fonts/ with MauiFont build action
                // are converted to Avalonia embedded resources at build time.
                fonts.AddFont("Inter-Regular.ttf", "InterRegular");
                fonts.AddFont("Inter-Medium.ttf", "InterMedium");
                fonts.AddFont("Inter-Bold.ttf", "InterBold");
            });

        // Custom MAUI handler registrations go AFTER UseAvaloniaApp so they
        // take precedence over the Avalonia defaults.
        // builder.ConfigureMauiHandlers(handlers => { ... });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}