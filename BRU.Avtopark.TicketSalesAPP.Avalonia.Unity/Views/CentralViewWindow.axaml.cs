using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class CentralViewWindow : Window
{
    public CentralViewWindow()
    {
        InitializeComponent();
        DataContext ??= new BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels.CentralViewWindowViewModel();

#if !DESKTOP
        // On MAUI mobile/browser targets, hide the custom titlebar row at runtime.
        this.Loaded += (_, _) =>
        {
            var titleBarGrid = this.FindControl<Grid>("TitleBarGrid");
            if (titleBarGrid != null)
                titleBarGrid.IsVisible = false;

            var rootGrid = this.FindControl<Grid>("RootGrid");
            if (rootGrid?.RowDefinitions.Count > 0)
                rootGrid.RowDefinitions[0].Height = new GridLength(0);
        };
#endif
    }

    public CentralViewWindow(BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels.CentralViewWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void TitleBarDragArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
#if DESKTOP
            BeginMoveDrag(e);
#else
            { /* drag not supported on MAUI mobile/browser */ }
#endif
    }

    private void TitleBarDragArea_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
