using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews;
using Serilog;
using System;
using System.Threading.Tasks;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

/// <summary>
/// Custom tray popup — a borderless Avalonia window that appears near the
/// system tray and auto-dismisses on focus loss.
/// </summary>
public partial class TrayPopupWindow : Window
{
    private Window? _ownerWindow;

    // Named controls (resolved after InitializeComponent)
    private Ellipse? _statusDot;
    private TextBlock? _statusLabel;
    private TextBlock? _roleLabel;
    private TextBlock? _serverLabel;
    private Button? _closePopupButton;
    private Button? _showMainWindowBtn;
    private TextBlock? _showMainWindowLabel;
    private Button? _logoutBtn;
    private Button? _exitBtn;
    private Button? _centralViewBtn;
    private Button? _employeeBtn;
    private Button? _busBtn;
    private Button? _routeBtn;
    private Button? _ticketBtn;
    private Button? _maintenanceBtn;
    private Button? _salesBtn;
    private Button? _schedulesBtn;
    private Border? _authOverlay;
    private Button? _loginFromOverlayBtn;
    private StackPanel? _modulePanel;

    public TrayPopupWindow()
    {
        AvaloniaXamlLoader.Load(this);
        ResolveControls();
        WireEvents();
        RefreshStatus();
    }

    private void ResolveControls()
    {
        _statusDot         = this.FindControl<Ellipse>("StatusDot");
        _statusLabel       = this.FindControl<TextBlock>("StatusLabel");
        _roleLabel         = this.FindControl<TextBlock>("RoleLabel");
        _serverLabel       = this.FindControl<TextBlock>("ServerLabel");
        _closePopupButton  = this.FindControl<Button>("ClosePopupButton");
        _showMainWindowBtn = this.FindControl<Button>("ShowMainWindowBtn");
        _showMainWindowLabel = this.FindControl<TextBlock>("ShowMainWindowLabel");
        _logoutBtn         = this.FindControl<Button>("LogoutBtn");
        _exitBtn           = this.FindControl<Button>("ExitBtn");
        _centralViewBtn    = this.FindControl<Button>("CentralViewBtn");
        _employeeBtn       = this.FindControl<Button>("EmployeeBtn");
        _busBtn            = this.FindControl<Button>("BusBtn");
        _routeBtn          = this.FindControl<Button>("RouteBtn");
        _ticketBtn         = this.FindControl<Button>("TicketBtn");
        _maintenanceBtn    = this.FindControl<Button>("MaintenanceBtn");
        _salesBtn          = this.FindControl<Button>("SalesBtn");
        _schedulesBtn      = this.FindControl<Button>("SchedulesBtn");
        _authOverlay       = this.FindControl<Border>("AuthOverlay");
        _loginFromOverlayBtn = this.FindControl<Button>("LoginFromOverlayBtn");
        _modulePanel       = this.FindControl<StackPanel>("ModulePanel");
    }

    private void WireEvents()
    {
        if (_closePopupButton != null)
            _closePopupButton.Click += (_, _) => Hide();

        if (_centralViewBtn != null)
            _centralViewBtn.Click += (_, _) => { Hide(); OpenCentralView(); };

        if (_employeeBtn != null)
            _employeeBtn.Click += (_, _) => { Hide(); OpenTool<EmployeeManagementToolWindow, EmployeeManagementViewModel>("Сотрудники"); };

        if (_busBtn != null)
            _busBtn.Click += (_, _) => { Hide(); OpenTool<BusManagementToolWindow, BusManagementViewModel>("Автобусы"); };

        if (_routeBtn != null)
            _routeBtn.Click += (_, _) => { Hide(); OpenTool<RouteManagementToolWindow, RouteManagementViewModel>("Маршруты"); };

        if (_ticketBtn != null)
            _ticketBtn.Click += (_, _) => { Hide(); OpenTool<TicketManagementToolWindow, TicketManagementViewModel>("Продажа билетов"); };

        if (_maintenanceBtn != null)
            _maintenanceBtn.Click += (_, _) => { Hide(); OpenTool<MaintenanceManagementToolWindow, MaintenanceManagementViewModel>("Техобслуживание"); };

        if (_salesBtn != null)
            _salesBtn.Click += (_, _) => { Hide(); OpenTool<SalesManagementToolWindow, SalesManagementViewModel>("Статистика продаж"); };

        if (_schedulesBtn != null)
            _schedulesBtn.Click += (_, _) => { Hide(); OpenTool<RouteSchedulesManagementToolWindow, RouteSchedulesManagementViewModel>("Расписание"); };

        if (_showMainWindowBtn != null)
            _showMainWindowBtn.Click += (_, _) => { Hide(); ToggleMainWindow(); };

        if (_logoutBtn != null)
            _logoutBtn.Click += async (_, _) => { Hide(); await LogoutAndExitAsync(); };

        if (_exitBtn != null)
            _exitBtn.Click += (_, _) => { Hide(); ExitApp(); };

        if (_loginFromOverlayBtn != null)
            _loginFromOverlayBtn.Click += async (_, _) => await TriggerLoginAsync();

        // Auto-dismiss on focus loss
        this.Deactivated += (_, _) => Hide();
    }

    /// <summary>
    /// Refreshes the status indicators from live service state.
    /// Shows the auth overlay when not authenticated, hides it when authenticated.
    /// </summary>
    public void RefreshStatus()
    {
        bool authenticated = !string.IsNullOrEmpty(ApiClientService.Instance.AuthToken);

        if (_statusDot != null)
            _statusDot.Fill = new SolidColorBrush(authenticated ? Color.Parse("#4CAF50") : Color.Parse("#F44336"));

        if (_statusLabel != null)
            _statusLabel.Text = authenticated ? "Подключено" : "Ожидание авторизации...";

        if (_roleLabel != null)
            _roleLabel.Text = ApiClientService.Instance.RoleName ?? "—";

        if (_serverLabel != null)
        {
            var url = ApiClientService.Instance.CurrentBaseUrl ?? "localhost:5000";
            try { _serverLabel.Text = new Uri(url).Authority; }
            catch { _serverLabel.Text = url; }
        }

        // Show auth overlay and dim modules when not authenticated
        if (_authOverlay != null)
            _authOverlay.IsVisible = !authenticated;

        if (_modulePanel != null)
            _modulePanel.Opacity = authenticated ? 1.0 : 0.25;

        // Show logout only when authenticated
        if (_logoutBtn != null)
            _logoutBtn.IsVisible = authenticated;
    }

    /// <summary>
    /// Configures the "Show/Hide main window" button visibility and label.
    /// </summary>
    public void SetOwnerWindow(Window? owner)
    {
        _ownerWindow = owner;
        if (_showMainWindowBtn != null)
            _showMainWindowBtn.IsVisible = owner != null;
        UpdateShowHideLabel();
    }

    private void UpdateShowHideLabel()
    {
        if (_showMainWindowLabel == null || _ownerWindow == null) return;
        _showMainWindowLabel.Text = _ownerWindow.IsVisible
            ? "Скрыть главное окно"
            : "Показать главное окно";
    }

    /// <summary>
    /// Positions the popup above the taskbar (bottom-right corner) and shows it.
    /// </summary>
    public void ShowNearTray()
    {
        RefreshStatus();
        UpdateShowHideLabel();

        // Measure the window so we know its height
        Measure(new Size(300, double.PositiveInfinity));

        var screen = Screens.Primary;
        if (screen == null) { Show(); return; }

        var workArea = screen.WorkingArea;
        var scaling  = screen.Scaling;

        // Convert device pixels → logical pixels
        double workRight  = workArea.Right  / scaling;
        double workBottom = workArea.Bottom / scaling;

        double popupW = 300;
        double popupH = DesiredSize.Height > 0 ? DesiredSize.Height : 480;

        Position = new PixelPoint(
            (int)((workRight  - popupW) * scaling),
            (int)((workBottom - popupH) * scaling));

        Show();
        Activate();
    }

    // ── Actions ───────────────────────────────────────────────────────────

    private async Task TriggerLoginAsync()
    {
        try
        {
            if (_loginFromOverlayBtn != null) _loginFromOverlayBtn.IsEnabled = false;
            if (_statusLabel != null) _statusLabel.Text = "Выбор метода входа...";

            // Delegate to the shared login selector — no owner needed (headless-safe)
            bool ok = await App.RunLoginSelectorAsync(owner: null);

            if (ok)
            {
                Log.Information("TrayPopup: login successful");
                RefreshStatus();
                TrayIconService.Instance.RefreshMenu();
            }
            else
            {
                if (_statusLabel != null) _statusLabel.Text = "Ожидание авторизации...";
                if (_loginFromOverlayBtn != null) _loginFromOverlayBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "TrayPopup: login failed");
            if (_statusLabel != null) _statusLabel.Text = "Ошибка входа";
            if (_loginFromOverlayBtn != null) _loginFromOverlayBtn.IsEnabled = true;
        }
    }

    private void ToggleMainWindow()
    {
        if (_ownerWindow == null) return;
        if (_ownerWindow.IsVisible)
            _ownerWindow.Hide();
        else
        {
            _ownerWindow.Show();
            _ownerWindow.Activate();
        }
    }

    private static void OpenCentralView()
    {
        try
        {
            var win = new CentralViewWindow(new CentralViewWindowViewModel())
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            win.Show();
            win.Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "TrayPopup: failed to open CentralViewWindow");
        }
    }

    private static void OpenTool<TControl, TViewModel>(string title)
        where TControl : UserControl, new()
        where TViewModel : new()
    {
        try
        {
            var host = new Window
            {
                Title = title,
                Content = new TControl { DataContext = new TViewModel() },
                Width = 900,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            host.Show();
            host.Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "TrayPopup: failed to open {Control}", typeof(TControl).Name);
        }
    }

    private static async Task LogoutAndExitAsync()
    {
        try { await AuthenticationManager.Instance.LogoutAsync(); }
        catch (Exception ex) { Log.Error(ex, "TrayPopup: logout error"); }
        ExitApp();
    }

    private static void ExitApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }
}
