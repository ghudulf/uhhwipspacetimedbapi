using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public partial class CentralViewWindowViewModel : ObservableObject
{
    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private bool _isNavPaneOpen = false;
    [ObservableProperty] private string _currentSectionTitle = "Автобусы";
    [ObservableProperty] private string _currentSectionSubtitle = "Парк транспортных средств";

    public CentralViewWindowViewModel()
    {
        _currentView = new BusManagementToolWindow();
    }

    [RelayCommand]
    private void ToggleNavPane() => IsNavPaneOpen = !IsNavPaneOpen;

    private void Navigate(object view, string title, string subtitle)
    {
        CurrentView = view;
        CurrentSectionTitle = title;
        CurrentSectionSubtitle = subtitle;
        IsNavPaneOpen = false;
    }

    [RelayCommand]
    private void ShowBusManagement() =>
        Navigate(new BusManagementToolWindow(), "Автобусы", "Парк транспортных средств");

    [RelayCommand]
    private void ShowRouteSchedules() =>
        Navigate(new RouteSchedulesManagementToolWindow(), "Расписание", "Расписание маршрутов");

    [RelayCommand]
    private void ShowTicketManagement() =>
        Navigate(new TicketManagementToolWindow(), "Билеты", "Управление билетами");

    [RelayCommand]
    private void ShowUserManagement() =>
        Navigate(new UserManagementToolWindow(), "Пользователи", "Права доступа");

    [RelayCommand]
    private void ShowSalesStatistics() =>
        Navigate(new SalesStatisticsToolWindow(), "Статистика", "Аналитика продаж");

    [RelayCommand]
    private void ShowSalesManagement() =>
        Navigate(new SalesManagementToolWindow(), "Продажи", "Кассовые операции");

    [RelayCommand]
    private void ShowRouteManagement() =>
        Navigate(new RouteManagementToolWindow(), "Маршруты", "Управление маршрутами");

    [RelayCommand]
    private void ShowEmployeeManagement() =>
        Navigate(new EmployeeManagementToolWindow(), "Сотрудники", "Кадровый учёт");

    [RelayCommand]
    private void ShowJobManagement() =>
        Navigate(new JobManagementToolWindow(), "Должности", "Штатное расписание");

    [RelayCommand]
    private void ShowMaintenanceManagement() =>
        Navigate(new MaintenanceManagementToolWindow(), "Обслуживание", "ТО и ремонт");

    [RelayCommand]
    private void ShowIncomeReport() =>
        Navigate(new IncomeReportToolWindow(), "Доходы", "Финансовые отчёты");

    [RelayCommand]
    private void ShowWebSocketDebug()
    {
        var win = new WebSocketDebugWindow();
        win.Show();
    }

    [RelayCommand]
    private void OpenAbout() => new AboutWindow().Show();

    [RelayCommand]
    private void OpenHelp() => new HelpWindow().Show();

    [RelayCommand]
    private void Refresh()
    {
        // Check if current view or its DataContext implements IRefreshable
        var refreshable = (CurrentView as IRefreshable) ??
                         ((CurrentView as Control)?.DataContext as IRefreshable);

        if (refreshable != null)
        {
            // View supports refresh interface - use it
            refreshable.Refresh();
        }
        else
        {
            // Fallback: re-create view instance to refresh content
            var title = CurrentSectionTitle;
            var subtitle = CurrentSectionSubtitle;
            var viewType = CurrentView?.GetType();

            if (viewType != null)
            {
                CurrentView = null;
                CurrentView = Activator.CreateInstance(viewType);
                CurrentSectionTitle = title;
                CurrentSectionSubtitle = subtitle;
            }
        }
    }

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}