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
    }

    private void TitleBarDragArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
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
