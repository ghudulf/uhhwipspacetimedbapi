using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using Serilog;
using System;
using System.Threading.Tasks;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

/// <summary>
/// Manages the system tray icon.
/// Left-click → custom Avalonia popup (TrayPopupWindow).
/// Right-click → native OS context menu (fallback / accessibility).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private static TrayIconService? _instance;
    private static readonly object _lock = new();

    private TrayIcon? _trayIcon;
    private TrayPopupWindow? _popup;
    private Window? _ownerWindow;
    private bool _disposed;

    public static TrayIconService Instance
    {
        get
        {
            if (_instance == null)
                lock (_lock)
                    _instance ??= new TrayIconService();
            return _instance;
        }
    }

    private TrayIconService() { }

    /// <summary>
    /// Creates and shows the tray icon. Call once at app startup — before auth.
    /// The popup will show a "waiting for auth" state until <see cref="SetOwnerWindow"/> is called.
    /// </summary>
    /// <param name="ownerWindow">Optional main window; null in headless/tray-only mode or pre-auth.</param>
    public void Initialize(Window? ownerWindow = null)
    {
        if (_trayIcon != null) return;

        _ownerWindow = ownerWindow;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _trayIcon = new TrayIcon
                {
                    ToolTipText = "BRU Авторпарк — ожидание авторизации",
                    Icon = LoadAppIcon(),
                    Menu = BuildNativeMenu(),
                    IsVisible = true
                };

                _trayIcon.Clicked += OnTrayIconClicked;
                Log.Information("TrayIconService: tray icon initialized");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TrayIconService: failed to initialize tray icon");
            }
        });
    }

    /// <summary>
    /// Called after authentication succeeds to attach the main window and refresh state.
    /// </summary>
    public void SetOwnerWindow(Window? owner)
    {
        _ownerWindow = owner;
        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcon != null)
                _trayIcon.ToolTipText = "BRU Авторпарк";
        });
    }

    /// <summary>Refreshes both the popup (if open) and the native fallback menu.</summary>
    public void RefreshMenu()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _popup?.RefreshStatus();
            if (_trayIcon != null)
                _trayIcon.Menu = BuildNativeMenu();
        });
    }

    // ── Left-click: custom popup ──────────────────────────────────────────

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_popup == null || !_popup.IsVisible)
                ShowPopup();
            else
                _popup.Hide();
        });
    }

    private void ShowPopup()
    {
        // Recreate popup each time so it always reflects current state cleanly
        _popup?.Close();
        _popup = new TrayPopupWindow();
        _popup.SetOwnerWindow(_ownerWindow);
        _popup.ShowNearTray();
    }

    // ── Right-click: native OS menu (accessibility / fallback) ───────────

    private NativeMenu BuildNativeMenu()
    {
        var menu = new NativeMenu();
        bool isAuthenticated = !string.IsNullOrEmpty(ApiClientService.Instance.AuthToken);

        menu.Add(new NativeMenuItem("BRU Авторпарк") { IsEnabled = false });
        menu.Add(new NativeMenuItemSeparator());

        if (isAuthenticated)
        {
            if (_ownerWindow != null)
            {
                var label = _ownerWindow.IsVisible ? "Скрыть главное окно" : "Показать главное окно";
                AddItem(menu, label, ToggleMainWindow);
                menu.Add(new NativeMenuItemSeparator());
            }

            AddItem(menu, "Центральный вид", OpenCentralView);
            AddItem(menu, "Сотрудники", () => OpenTool<EmployeeManagementToolWindow, EmployeeManagementViewModel>("Сотрудники"));
            AddItem(menu, "Автобусы", () => OpenTool<BusManagementToolWindow, BusManagementViewModel>("Автобусы"));
            AddItem(menu, "Маршруты", () => OpenTool<RouteManagementToolWindow, RouteManagementViewModel>("Маршруты"));
            AddItem(menu, "Продажа билетов", () => OpenTool<TicketManagementToolWindow, TicketManagementViewModel>("Продажа билетов"));
            AddItem(menu, "Техобслуживание", () => OpenTool<MaintenanceManagementToolWindow, MaintenanceManagementViewModel>("Техобслуживание"));
            AddItem(menu, "Статистика продаж", () => OpenTool<SalesManagementToolWindow, SalesManagementViewModel>("Статистика продаж"));
            AddItem(menu, "Расписание", () => OpenTool<RouteSchedulesManagementToolWindow, RouteSchedulesManagementViewModel>("Расписание"));
            menu.Add(new NativeMenuItemSeparator());
            AddItem(menu, "Выйти из аккаунта и закрыть", () => _ = LogoutAndExitAsync());
        }
        else
        {
            AddItem(menu, "Войти...", OpenLoginFlow);
        }

        menu.Add(new NativeMenuItemSeparator());
        AddItem(menu, "Выход", ExitApplication);
        return menu;
    }

    private static void AddItem(NativeMenu menu, string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        menu.Add(item);
    }

    // ── Shared actions ────────────────────────────────────────────────────

    private void ToggleMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_ownerWindow == null) return;
            if (_ownerWindow.IsVisible)
                _ownerWindow.Hide();
            else
            {
                _ownerWindow.Show();
                _ownerWindow.Activate();
            }
            RefreshMenu();
        });
    }

    private static void OpenCentralView()
    {
        Dispatcher.UIThread.Post(() =>
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
                Log.Error(ex, "TrayIconService: failed to open CentralViewWindow");
            }
        });
    }

    private static void OpenTool<TControl, TViewModel>(string title)
        where TControl : UserControl, new()
        where TViewModel : new()
    {
        Dispatcher.UIThread.Post(() =>
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
                Log.Error(ex, "TrayIconService: failed to open {Control}", typeof(TControl).Name);
            }
        });
    }

    private void OpenLoginFlow()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                bool ok = await AuthenticationManager.Instance.LoginAsync();
                if (ok)
                {
                    Log.Information("TrayIconService: login successful");
                    RefreshMenu();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TrayIconService: login flow failed");
            }
        });
    }

    private async Task LogoutAndExitAsync()
    {
        try { await AuthenticationManager.Instance.LogoutAsync(); }
        catch (Exception ex) { Log.Error(ex, "TrayIconService: logout error"); }
        ExitApplication();
    }

    private static void ExitApplication()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Environment.Exit(0);
        });
    }

    // ── Icon ──────────────────────────────────────────────────────────────

    private static WindowIcon? LoadAppIcon()
    {
        try
        {
            var stream = AssetLoader.Open(new Uri("avares://BRU.Avtopark.TicketSalesAPP.Avalonia.Unity/Assets/avalonia-logo.ico"));
            return new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TrayIconService: could not load tray icon");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Dispatcher.UIThread.Post(() =>
        {
            _popup?.Close();
            _popup = null;
            if (_trayIcon == null) return;
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        });
    }
}
