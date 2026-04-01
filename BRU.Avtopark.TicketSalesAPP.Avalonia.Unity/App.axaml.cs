using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using Serilog;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity;

public partial class App : Application
{
    private Window? _mainWindow;

    /// <summary>
    /// When true the app starts without any visible window — only the tray icon.
    /// Set by Program.cs when --tray or --headless is passed on the command line.
    /// </summary>
    public static bool HeadlessMode { get; set; } = false;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();
#if DESKTOP
        // Uncomment when Avalonia.Controls.WebView is wired up:
        // AvaloniaWebViewBuilder.Initialize(default);
#endif
    }

    // ── Normal startup (splash → login → main window) ─────────────────────

    private async Task ShowSplashScreenAndInitialize(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var splashScreen = new SplashScreen();
            splashScreen.Show();

            await Task.Delay(1000);

            Log.Debug("Checking server availability");
            if (!await splashScreen.CheckServerAvailability())
            {
                Log.Error("Server not available");
                await Task.Delay(5000);
                desktop.Shutdown();
                return;
            }

            // Connection confirmed — register tray icon now, while splash winds down
            TrayIconService.Instance.Initialize(ownerWindow: null);

            await Task.Delay(3000);
            splashScreen.Close();
            Log.Debug("Splash screen closed");

            var backgroundWindow = new BackGroundWindow();
            backgroundWindow.Show();
            await Task.Delay(500);

            bool isAuthenticated = await TryRestoreOrLoginAsync(backgroundWindow, desktop);

            if (isAuthenticated)
            {
                _mainWindow = new MainWindow();
                _mainWindow.DataContext = new MainWindowViewModel();
                desktop.MainWindow = _mainWindow;
                _mainWindow.Show();

                // Auth done — update tray with main window reference and refresh state
                TrayIconService.Instance.SetOwnerWindow(_mainWindow);
                TrayIconService.Instance.RefreshMenu();

                // Keep app alive when main window is closed (tray-only after close)
                _mainWindow.Closing += (_, e) =>
                {
                    e.Cancel = true;
                    _mainWindow.Hide();
                    TrayIconService.Instance.RefreshMenu();
                };
            }
            else
            {
                Log.Information("Authentication failed or cancelled. Shutting down...");
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during application initialization");
            desktop.Shutdown();
        }
    }

    // ── Headless / tray-only startup ──────────────────────────────────────

    private static async Task StartHeadlessAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            Log.Information("Starting in headless/tray-only mode");

            // Invisible anchor window — keeps the lifetime alive, owns any future dialogs
            var anchor = new Window
            {
                Width = 0,
                Height = 0,
                ShowInTaskbar = false,
                IsVisible = false,
                WindowState = WindowState.Minimized,
                WindowDecorations = WindowDecorations.None,
                Opacity = 0
            };
            anchor.Show();
            desktop.MainWindow = anchor;

            // Run splash screen — handles API discovery and server availability check
            var splashScreen = new SplashScreen();
            splashScreen.Show();
            await Task.Delay(1000);

            Log.Debug("Headless: checking server availability");
            if (!await splashScreen.CheckServerAvailability())
            {
                Log.Error("Headless: server not available");
                await Task.Delay(5000);
                desktop.Shutdown();
                return;
            }

            await Task.Delay(3000);
            splashScreen.Close();
            Log.Debug("Headless: splash screen closed");

            // Silently restore a cached token if one exists — no UI shown
            var tokenStorage = new TokenStorageService();
            var existingTokens = await tokenStorage.GetTokensAsync();

            if (existingTokens != null && !string.IsNullOrEmpty(existingTokens.AccessToken)
                && existingTokens.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            {
                Log.Information("Headless: valid cached token restored");
                ApiClientService.Instance.AuthToken = existingTokens.AccessToken;
            }
            else
            {
                if (existingTokens != null) await tokenStorage.ClearTokensAsync();
                Log.Information("Headless: no cached token — waiting for user to log in via tray");
            }

            // Show tray icon — user triggers login themselves via the popup
            TrayIconService.Instance.Initialize(ownerWindow: null);
            TrayIconService.Instance.RefreshMenu();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during headless startup");
            desktop.Shutdown();
        }
    }

    // ── Shared auth helpers ───────────────────────────────────────────────

    /// <summary>
    /// Shows the login method selector and runs the chosen auth flow.
    /// Works with or without an owner window — headless-safe.
    /// </summary>
    public static async Task<bool> RunLoginSelectorAsync(Window? owner = null)
    {
        // If a logout-pending flag is set, clean up WebView data before showing any login UI
        if (AuthenticationManager.ClearWebViewSessionOnNextLogin)
        {
            Log.Information("Logout flag detected — cleaning up WebView session data before login");
            await AuthenticationManager.CleanupWebViewDataAsync();
        }

        var selector = new LoginMethodSelectorWindow();
        LoginMethod? method;

        if (owner != null)
        {
            method = await selector.ShowDialog<LoginMethod?>(owner);
        }
        else
        {
            // Headless: no owner — show standalone and wait for Close
            var tcs = new TaskCompletionSource<LoginMethod?>();
            selector.Closed += (_, _) => tcs.TrySetResult(selector.SelectedMethod);
            selector.Show();
            method = await tcs.Task;
        }

        if (!method.HasValue)
        {
            Log.Information("Login selector: cancelled");
            return false;
        }

        if (method.Value == LoginMethod.OAuth)
        {
            Log.Information("Login selector: OAuth chosen");
            try
            {
                bool ok = await AuthenticationManager.Instance.LoginAsync();
                if (!ok) Log.Warning("OAuth authentication failed");
                return ok;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "OAuth authentication error");
                return false;
            }
        }
        else
        {
            Log.Information("Login selector: Traditional chosen");
            var authWindow = new AuthWindow { DataContext = new AuthViewModel() };
            var tcs = new TaskCompletionSource<bool>();
            authWindow.Closed += (_, _) =>
                tcs.TrySetResult(authWindow.DataContext is AuthViewModel vm && vm.IsAuthenticated);
            authWindow.Show();
            return await tcs.Task;
        }
    }

    /// <summary>
    /// Attempts to restore a cached/refreshed session silently.
    /// If no valid session exists, shows the login method selector.
    /// Restores all old failsafes: token refresh, WebView cleanup, IsAuthenticated short-circuit.
    /// </summary>
    private static async Task<bool> TryRestoreOrLoginAsync(
        Window ownerWindow,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var tokenStorage = new TokenStorageService();
        var authManager  = AuthenticationManager.Instance;

        // ── 1. Check if already fully authenticated (token valid in memory) ──
        if (await authManager.IsAuthenticatedAsync())
        {
            Log.Information("Session already authenticated — skipping login selector");
            // Ensure ApiClientService has the token
            var liveToken = await authManager.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(liveToken))
                ApiClientService.Instance.AuthToken = liveToken;
            return true;
        }

        // ── 2. Try to restore cached token ────────────────────────────────────
        var existingTokens = await tokenStorage.GetTokensAsync();

        if (existingTokens != null && !string.IsNullOrEmpty(existingTokens.AccessToken))
        {
            if (existingTokens.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            {
                // Token still valid — load it and short-circuit
                Log.Information("Valid cached token found — restoring session");
                ApiClientService.Instance.AuthToken = existingTokens.AccessToken;
                return true;
            }
            else
            {
                // ── 3. Token expired — attempt silent refresh ─────────────────
                Log.Warning("Cached token expired — attempting silent refresh");
                bool refreshed = await authManager.RefreshAuthenticationAsync();
                if (refreshed)
                {
                    Log.Information("Token refreshed successfully — session restored");
                    var refreshedToken = await authManager.GetAccessTokenAsync();
                    if (!string.IsNullOrEmpty(refreshedToken))
                        ApiClientService.Instance.AuthToken = refreshedToken;
                    return true;
                }

                Log.Warning("Token refresh failed — clearing stored tokens, requiring new login");
                await tokenStorage.ClearTokensAsync();
            }
        }
        else
        {
            Log.Information("No cached token found — proceeding to login selector");
        }

        // ── 4. No valid session — show login selector ─────────────────────────
        bool ok = await RunLoginSelectorAsync(ownerWindow);
        return ok;
    }

    // ── Framework initialization ──────────────────────────────────────────

    public override void OnFrameworkInitializationCompleted()
    {
        if (!HostEnvironment.IsMauiHost && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                if (HeadlessMode)
                {
                    // Tray-only: no splash, no main window
                    _ = StartHeadlessAsync(desktop);
                }
                else
                {
#if DEBUG
                    Log.Debug("Showing under construction window for debug build");
                    var underConstructionWindow = new UnderConstructionWindow();
                    underConstructionWindow.Closed += async (_, _) =>
                    {
                        Log.Debug("Under construction window closed, showing splash screen");
                        await ShowSplashScreenAndInitialize(desktop);
                    };
                    underConstructionWindow.Show();
#else
                    _ = ShowSplashScreenAndInitialize(desktop);
#endif
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during application initialization");
                desktop.Shutdown();
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView { DataContext = new MainViewModel() };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            activityLifetime.MainViewFactory = () => new MainView { DataContext = new MainViewModel() };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
