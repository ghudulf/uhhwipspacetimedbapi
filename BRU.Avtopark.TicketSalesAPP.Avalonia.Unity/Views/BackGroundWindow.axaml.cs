using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity;

public partial class BackGroundWindow : Window
{
    public BackGroundWindow()
    {
        InitializeComponent();
#if DEBUG
        DevToolsHelper.AttachOnce();
#endif
        // In headless/tray-only mode this window must never be visible
        if (App.HeadlessMode)
        {
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            WindowDecorations = WindowDecorations.None;
            Opacity = 0;
            IsVisible = false;
            return;
        }

        // Ensure fullscreen state properties are set
        this.WindowState = WindowState.FullScreen;
        this.WindowDecorations = WindowDecorations.None;

        // Explicitly set the theme variant based on the application's actual theme
        if (Application.Current != null)
            this.RequestedThemeVariant = Application.Current.ActualThemeVariant;
    }
}