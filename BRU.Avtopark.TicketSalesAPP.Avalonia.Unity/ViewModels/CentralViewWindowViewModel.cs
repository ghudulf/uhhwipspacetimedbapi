using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public partial class CentralViewWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private bool _isNavPaneOpen = false;

    public CentralViewWindowViewModel()
    {
        _currentView = new BusManagementToolWindow();
    }

    [RelayCommand]
    private void ToggleNavPane() => IsNavPaneOpen = !IsNavPaneOpen;

    [RelayCommand]
    private void ShowBusManagement()
    {
        CurrentView = new BusManagementToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowRouteSchedules()
    {
        CurrentView = new RouteSchedulesManagementToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowTicketManagement()
    {
        CurrentView = new TicketManagementToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowUserManagement()
    {
        CurrentView = new UserManagementToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowSalesStatistics()
    {
        CurrentView = new SalesStatisticsToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowSalesManagement()
    {
        CurrentView = new SalesManagementToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowRouteManagement()
    {
        CurrentView = new RouteManagementToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowEmployeeManagement()
    {
        CurrentView = new EmployeeManagementToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowJobManagement()
    {
        CurrentView = new JobManagementToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowMaintenanceManagement()
    {
        CurrentView = new MaintenanceManagementToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowIncomeReport()
    {
        CurrentView = new IncomeReportToolWindow();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowWebSocketDebug()
    {
        var win = new WebSocketDebugWindow();
        win.Show();
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void OpenAbout()
    {
        var win = new AboutWindow();
        win.Show();
    }

    [RelayCommand]
    private void OpenHelp()
    {
        var win = new HelpWindow();
        win.Show();
    }

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
