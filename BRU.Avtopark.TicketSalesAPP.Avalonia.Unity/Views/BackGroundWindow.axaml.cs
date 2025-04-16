using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity;

public partial class BackGroundWindow : Window
{
    public BackGroundWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        // Ensure fullscreen state properties are set
        this.WindowState = WindowState.FullScreen;
        this.SystemDecorations = SystemDecorations.None;

        // Explicitly set the theme variant based on the application's actual theme
        // This helps if the window is shown before the theme is fully propagated.
        if (Application.Current != null)
        {
            this.RequestedThemeVariant = Application.Current.ActualThemeVariant;
        }
    }
}