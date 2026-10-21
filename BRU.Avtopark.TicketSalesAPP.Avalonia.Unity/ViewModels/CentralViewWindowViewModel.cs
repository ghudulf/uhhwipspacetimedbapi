using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using System;
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
    [ObservableProperty] private bool _isNavPaneOpen = true;
    [ObservableProperty] private string _currentSectionTitle = "Автобусы";
    [ObservableProperty] private string _currentSectionSubtitle = "Парк транспортных средств";
    [ObservableProperty] private string _userInitials = string.Empty;
    [ObservableProperty] private bool _isConnected = false;
    [ObservableProperty] private string _connectionStatus = "Проверка доступа…";

    partial void OnIsConnectedChanged(bool value)
    {
        ConnectionStatus = value ? "Подключено к серверу" : "Отключено";
    }

    public CentralViewWindowViewModel()
    {
        CurrentSectionTitle = "Проверка доступа";
        CurrentSectionSubtitle = "Ожидание ответа сервера";
    }

    public bool CanLinkWindowsAccount => IsConnected && _access?.IsActive == true && WindowsAccountClient.TrustedOrigin != null;
    public bool CanUseQr => IsConnected && _access?.IsActive == true;
    private bool entraEnabled;
    public bool CanLinkEntraAccount => IsConnected && _access?.IsActive == true && entraEnabled;
    [RelayCommand(CanExecute = nameof(CanLinkEntraAccount))]
    private void LinkEntraAccount() => OpenQrWindow(() => new EntraAccountLinkWindow());

    [RelayCommand(CanExecute = nameof(CanUseQr))]
    private void ApproveQrLogin() => OpenQrWindow(() => new QrApproveWindow());

    [RelayCommand(CanExecute = nameof(CanUseQr))]
    private void ManageQrDevices() => OpenQrWindow(() => new QrDevicesWindow());

    private static void OpenQrWindow(Func<global::Avalonia.Controls.Window> create)
    {
        try
        {
            var window = create();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }) window.Show(owner);
            else window.Show();
        }
        catch { Serilog.Log.Warning("QR window unavailable"); }
    }

    [RelayCommand(CanExecute = nameof(CanLinkWindowsAccount))]
    private void LinkWindowsAccount()
    {
        var window = new WindowsAccountLinkWindow();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } owner)
            window.Show(owner);
        else window.Show();
    }

    private readonly SemaphoreSlim _accessChanged = new(0, 1);
    private SessionAccess? _access;
    private string? _activePermission;
    private bool _running;
    private long _sessionGeneration;

    private bool CanAccess(string permission) => IsConnected && _access?.IsActive == true
        && _access.Permissions.Contains(permission, StringComparer.Ordinal);

    public bool CanShowBusManagement => CanAccess("buses.view");
    public bool CanShowRouteManagement => CanAccess("routes.view");
    public bool CanShowRouteSchedules => CanAccess("schedules.view");
    public bool CanShowMaintenanceManagement => CanAccess("maintenance.view");
    public bool CanShowTicketManagement => CanAccess("tickets.view");
    public bool CanShowSalesManagement => CanAccess("sales.view");
    public bool CanShowSalesStatistics => CanAccess("sales.view");
    public bool CanShowIncomeReport => CanAccess("sales.view");
    public bool CanShowEmployeeManagement => CanAccess("employees.view");
    public bool CanShowJobManagement => CanAccess("jobs.view");
    public bool CanShowUserManagement => CanAccess("users.view");
    public bool CanShowWebSocketDebug => IsConnected && _access?.IsAdministrator == true;

    private void TokenChanged(object? sender, string? token)
    {
        Interlocked.Increment(ref _sessionGeneration);
        Dispatcher.UIThread.Post(() => ApplyAccess(null, false));
        if (_accessChanged.CurrentCount == 0) _accessChanged.Release();
    }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_running) return;
        _running = true;
        var api = ApiClientService.Instance;
        api.OnAuthTokenChanged += TokenChanged;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var generation = Interlocked.Read(ref _sessionGeneration);
                var token = api.AuthToken;
                var origin = api.CurrentBaseUrl;
                try
                {
                    var access = string.IsNullOrEmpty(token) || origin == null ? null
                        : await SessionAccessClient.GetAsync(origin, token, cancellationToken);
                    BRU.Avtopark.Client.ProviderCapabilities? providers = null;
                    if (access?.IsActive == true)
                    {
                        try { providers = await ProviderClientContext.CreateApi().CapabilitiesAsync(cancellationToken); }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                        catch { } // Older/offline capability endpoints must not disable existing account navigation.
                    }
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (generation == Interlocked.Read(ref _sessionGeneration) && token == api.AuthToken && origin == api.CurrentBaseUrl)
                        {
                            entraEnabled = providers?.Entra == true;
                            ApplyAccess(access, access?.IsActive == true);
                        }
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Serilog.Log.Warning("Access refresh failed ({ErrorType}); navigation disabled", ex.GetType().Name);
                    await Dispatcher.UIThread.InvokeAsync(() => ApplyAccess(null, false));
                }
                await _accessChanged.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { api.OnAuthTokenChanged -= TokenChanged; _running = false; }
    }

    private void ApplyAccess(SessionAccess? access, bool connected)
    {
        _access = access;
        IsConnected = connected;
        OnPropertyChanged(nameof(CanLinkEntraAccount));
        LinkEntraAccountCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanLinkWindowsAccount));
        LinkWindowsAccountCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUseQr));
        ApproveQrLoginCommand.NotifyCanExecuteChanged();
        ManageQrDevicesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowBusManagement));
        ShowBusManagementCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowRouteManagement));
        ShowRouteManagementCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowRouteSchedules));
        ShowRouteSchedulesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowMaintenanceManagement));
        ShowMaintenanceManagementCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowTicketManagement));
        ShowTicketManagementCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowSalesManagement));
        ShowSalesManagementCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowSalesStatistics));
        ShowSalesStatisticsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowIncomeReport));
        ShowIncomeReportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowEmployeeManagement));
        ShowEmployeeManagementCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowJobManagement));
        ShowJobManagementCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowUserManagement));
        ShowUserManagementCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowWebSocketDebug));
        ShowWebSocketDebugCommand.NotifyCanExecuteChanged();
        if (_activePermission != null && !CanAccess(_activePermission))
        {
            if ((CurrentView as global::Avalonia.Controls.Control)?.DataContext is IDisposable disposable) disposable.Dispose();
            CurrentView = null;
            _activePermission = null;
        }
        if (CurrentView == null)
        {
            CurrentSectionTitle = connected ? "Выберите раздел" : "Нет соединения или доступа";
            CurrentSectionSubtitle = connected ? "Доступные разделы показаны в меню" : "Ожидание проверки прав на сервере";
        }
    }

    [RelayCommand]
    private void ToggleNavPane() => IsNavPaneOpen = !IsNavPaneOpen;

    private void Navigate(object view, string title, string subtitle)
    {
        _activePermission = view.GetType().Name switch
        {
            nameof(BusManagementToolWindow) => "buses.view",
            nameof(RouteManagementToolWindow) => "routes.view",
            nameof(RouteSchedulesManagementToolWindow) => "schedules.view",
            nameof(MaintenanceManagementToolWindow) => "maintenance.view",
            nameof(TicketManagementToolWindow) => "tickets.view",
            nameof(SalesManagementToolWindow) => "sales.view",
            nameof(SalesStatisticsToolWindow) => "sales.view",
            nameof(IncomeReportToolWindow) => "sales.view",
            nameof(EmployeeManagementToolWindow) => "employees.view",
            nameof(JobManagementToolWindow) => "jobs.view",
            nameof(UserManagementToolWindow) => "users.view",
            _ => null
        };
        CurrentView = view;
        CurrentSectionTitle = title;
        CurrentSectionSubtitle = subtitle;
    }

    [RelayCommand(CanExecute = nameof(CanShowBusManagement))]
    private void ShowBusManagement() =>
        Navigate(new BusManagementToolWindow(), "Автобусы", "Парк транспортных средств");

    [RelayCommand(CanExecute = nameof(CanShowRouteSchedules))]
    private void ShowRouteSchedules() =>
        Navigate(new RouteSchedulesManagementToolWindow(), "Расписание", "Расписание маршрутов");

    [RelayCommand(CanExecute = nameof(CanShowTicketManagement))]
    private void ShowTicketManagement() =>
        Navigate(new TicketManagementToolWindow(), "Билеты", "Управление билетами");

    [RelayCommand(CanExecute = nameof(CanShowUserManagement))]
    private void ShowUserManagement() =>
        Navigate(new UserManagementToolWindow(), "Пользователи", "Права доступа");

    [RelayCommand(CanExecute = nameof(CanShowSalesStatistics))]
    private void ShowSalesStatistics() =>
        Navigate(new SalesStatisticsToolWindow(), "Статистика", "Аналитика продаж");

    [RelayCommand(CanExecute = nameof(CanShowSalesManagement))]
    private void ShowSalesManagement() =>
        Navigate(new SalesManagementToolWindow(), "Продажи", "Кассовые операции");

    [RelayCommand(CanExecute = nameof(CanShowRouteManagement))]
    private void ShowRouteManagement() =>
        Navigate(new RouteManagementToolWindow(), "Маршруты", "Управление маршрутами");

    [RelayCommand(CanExecute = nameof(CanShowEmployeeManagement))]
    private void ShowEmployeeManagement() =>
        Navigate(new EmployeeManagementToolWindow(), "Сотрудники", "Кадровый учёт");

    [RelayCommand(CanExecute = nameof(CanShowJobManagement))]
    private void ShowJobManagement() =>
        Navigate(new JobManagementToolWindow(), "Должности", "Штатное расписание");

    [RelayCommand(CanExecute = nameof(CanShowMaintenanceManagement))]
    private void ShowMaintenanceManagement() =>
        Navigate(new MaintenanceManagementToolWindow(), "Обслуживание", "ТО и ремонт");

    [RelayCommand(CanExecute = nameof(CanShowIncomeReport))]
    private void ShowIncomeReport() =>
        Navigate(new IncomeReportToolWindow(), "Доходы", "Финансовые отчёты");

    [RelayCommand(CanExecute = nameof(CanShowWebSocketDebug))]
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
        // Re-create view instance to refresh content
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

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
