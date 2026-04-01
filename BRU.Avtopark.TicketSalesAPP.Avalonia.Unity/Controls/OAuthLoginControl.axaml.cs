using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Web;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Controls;

/// <summary>
/// Reusable Avalonia UserControl that hosts the full OAuth PKCE WebView flow.
/// Extracted from OAuthLoginWindow so it can be embedded in both standalone
/// Avalonia Windows and MAUI pages via AvaloniaView.
///
/// Raises <see cref="AuthCompleted"/> when the flow finishes (success or failure).
/// </summary>
public partial class OAuthLoginControl : UserControl
{
    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Raised on the UI thread when the OAuth flow completes.</summary>
    public event EventHandler<OAuthResult>? AuthCompleted;

    // ── Styled properties (so MAUI handler can bind) ─────────────────────

    public static readonly StyledProperty<string> AuthorizationUrlProperty =
        AvaloniaProperty.Register<OAuthLoginControl, string>(nameof(AuthorizationUrl), string.Empty);

    public static readonly StyledProperty<string> RedirectUriProperty =
        AvaloniaProperty.Register<OAuthLoginControl, string>(nameof(RedirectUri), string.Empty);

    public static readonly StyledProperty<string> ExpectedStateProperty =
        AvaloniaProperty.Register<OAuthLoginControl, string>(nameof(ExpectedState), string.Empty);

    public static readonly StyledProperty<string> CodeVerifierProperty =
        AvaloniaProperty.Register<OAuthLoginControl, string>(nameof(CodeVerifier), string.Empty);

    public string AuthorizationUrl
    {
        get => GetValue(AuthorizationUrlProperty);
        set => SetValue(AuthorizationUrlProperty, value);
    }
    public string RedirectUri
    {
        get => GetValue(RedirectUriProperty);
        set => SetValue(RedirectUriProperty, value);
    }
    public string ExpectedState
    {
        get => GetValue(ExpectedStateProperty);
        set => SetValue(ExpectedStateProperty, value);
    }
    public string CodeVerifier
    {
        get => GetValue(CodeVerifierProperty);
        set => SetValue(CodeVerifierProperty, value);
    }

    // ── Private state ────────────────────────────────────────────────────

    private Panel? _webViewContainer;
    private Panel? _loadingPanel;
    private NativeWebView? _webView;
    private System.Threading.CancellationTokenSource? _timeoutCts;
    private int _callbackAttempts;
    private DateTime _lastCallbackTime = DateTime.MinValue;
    private const int MaxCallbackAttempts = 2;

    // Loop detection: count how many times we land back on /connect/authorize
    // after the initial load (which is expected once).
    private int _authorizePageHits;
    private bool _loginAttempted;
    private const int MaxAuthorizeHits = 3;

    // ── Constructor ──────────────────────────────────────────────────────

    public OAuthLoginControl()
    {
        InitializeComponent();
        _webViewContainer = this.FindControl<Panel>("WebViewContainer");
        _loadingPanel = this.FindControl<Panel>("LoadingPanel");
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ── Lifecycle ────────────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!string.IsNullOrEmpty(AuthorizationUrl))
            _ = StartFlowAsync();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AuthorizationUrlProperty && VisualRoot != null)
            _ = StartFlowAsync();
    }

    // ── Flow entry point ─────────────────────────────────────────────────

    public async Task StartFlowAsync()
    {
        if (string.IsNullOrEmpty(AuthorizationUrl)) return;

        Log.Information("[OAuthLoginControl] Starting OAuth flow");
        Log.Debug("[OAuthLoginControl] AuthorizationUrl: {Url}", AuthorizationUrl);
        Log.Information("[OAuthLoginControl] RedirectUri (intercept target): {RedirectUri}", RedirectUri);

        // Reset loop counters on each new flow start
        _authorizePageHits = 0;
        _loginAttempted = false;
        _callbackAttempts = 0;
        _lastCallbackTime = DateTime.MinValue;

        // Validate URL is not already a callback
        if (AuthorizationUrl.Contains("/callback", StringComparison.OrdinalIgnoreCase) ||
            AuthorizationUrl.StartsWith(RedirectUri, StringComparison.OrdinalIgnoreCase))
        {
            Log.Error("[OAuthLoginControl] AuthorizationUrl is a callback URL — aborting");
            ShowError("Обнаружен цикл авторизации. Данные авторизации будут очищены.");
            RaiseCompleted(new OAuthResult { Success = false, Error = "callback_loop" });
            return;
        }

        bool clearSession = AuthenticationManager.ConsumeClearWebViewSessionFlag();
        await LoadWebViewAsync(clearSession);
    }

    // ── WebView loading ──────────────────────────────────────────────────

    private async Task LoadWebViewAsync(bool clearSession)
    {
        if (_loadingPanel != null) _loadingPanel.IsVisible = true;

        _webView = new NativeWebView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _webView.NavigationStarted += OnNavigationStarting;
        _webView.NavigationCompleted += OnNavigationCompleted;

        if (_webViewContainer != null)
        {
            _webViewContainer.Children.Clear();
            _webViewContainer.Children.Add(_webView);
        }

        if (clearSession)
        {
            Log.Information("[OAuthLoginControl] Clearing WebView session (post-logout)");
            await ClearWebViewDataAsync(_webView);
        }

        // If the auth URL uses HTTPS but the server was discovered on HTTP, convert for WebView compat
        var navUrl = AuthorizationUrl;
        if (navUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var discoveredBase = Services.ApiClientService.Instance.CurrentBaseUrl ?? string.Empty;
            if (discoveredBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                // Extract host:port from discovered URL and substitute into auth URL
                var discoveredUri = new Uri(discoveredBase);
                var authUri = new Uri(navUrl);
                navUrl = navUrl.Replace($"https://{authUri.Host}:{authUri.Port}", $"http://{discoveredUri.Host}:{discoveredUri.Port}");
                navUrl = navUrl.Replace($"https://{authUri.Host}", $"http://{discoveredUri.Host}:{discoveredUri.Port}");
                Log.Information("[OAuthLoginControl] Converted auth URL to HTTP for WebView: {Url}", navUrl);
            }
        }

        Log.Information("[OAuthLoginControl] Navigating to: {Url}", navUrl);
        _webView.Source = new Uri(navUrl);

        // 30-second navigation timeout
        _timeoutCts = new System.Threading.CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(30_000, _timeoutCts.Token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                    ShowError("Превышено время ожидания загрузки страницы авторизации."));
            }
            catch (TaskCanceledException) { }
        });

        await Task.CompletedTask;
    }

    private static async Task ClearWebViewDataAsync(NativeWebView webView)
    {
        // NativeWebView does not expose a direct ClearData API in this version;
        // session clearing is handled by deleting the WebView2 user-data directory
        // (done by AuthenticationManager.CleanupStateFilesAsync on logout).
        await AuthenticationManager.CleanupWebViewDataAsync();
        await Task.CompletedTask;
    }

    // ── Navigation events ────────────────────────────────────────────────

    private void OnNavigationStarting(object? sender, WebViewNavigationStartingEventArgs e)
    {
        var url = e.Request?.ToString() ?? string.Empty;
        Log.Debug("[OAuthLoginControl] NavigationStarting: {Url}", url);

        // Track when the user submits the login form (POST to /connect/authorize/callback)
        if (url.Contains("/connect/authorize/callback", StringComparison.OrdinalIgnoreCase))
        {
            _loginAttempted = true;
            Log.Information("[OAuthLoginControl] Login form submitted — watching for cookie redirect");
        }

        if (!url.StartsWith(RedirectUri, StringComparison.OrdinalIgnoreCase)) return;

        // Callback loop detection
        var elapsed = DateTime.UtcNow - _lastCallbackTime;
        _lastCallbackTime = DateTime.UtcNow;
        if (elapsed.TotalSeconds < 5) _callbackAttempts++;
        else _callbackAttempts = 1;

        if (_callbackAttempts > MaxCallbackAttempts)
        {
            Log.Error("[OAuthLoginControl] Callback loop detected");
            e.Cancel = true;
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                ShowError("Обнаружен цикл авторизации. Нажмите Отмена и попробуйте снова.");
                RaiseCompleted(new OAuthResult { Success = false, Error = "callback_loop" });
            });
            return;
        }

        e.Cancel = true;

        if (url.Contains("error=", StringComparison.OrdinalIgnoreCase))
        {
            var query = HttpUtility.ParseQueryString(new Uri(url).Query);
            var error = query["error"];
            var desc = query["error_description"];
            Log.Error("[OAuthLoginControl] OAuth error in callback: {Error} — {Desc}", error, desc);
            Dispatcher.UIThread.InvokeAsync(() =>
                ShowError($"Ошибка авторизации: {desc ?? error}"));
            RaiseCompleted(new OAuthResult { Success = false, Error = error ?? "oauth_error" });
        }
        else
        {
            ProcessCallback(url);
        }
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _timeoutCts?.Cancel();
        _timeoutCts?.Dispose();
        _timeoutCts = null;

        if (_loadingPanel != null) _loadingPanel.IsVisible = false;

        var currentUrl = _webView?.Source?.ToString() ?? string.Empty;
        Log.Information("[OAuthLoginControl] NavigationCompleted — success={S}, url={U}", e.IsSuccess, currentUrl);
        Log.Debug("[OAuthLoginControl] Watching for redirect to: {RedirectUri}", RedirectUri);

        if (currentUrl.StartsWith(RedirectUri, StringComparison.OrdinalIgnoreCase))
        {
            ProcessCallback(currentUrl);
            return;
        }

        // Detect loop: landing back on /connect/authorize after a login attempt
        if (currentUrl.Contains("/connect/authorize", StringComparison.OrdinalIgnoreCase) && e.IsSuccess)
        {
            _authorizePageHits++;
            Log.Warning("[OAuthLoginControl] Landed on /connect/authorize (hit #{N}, loginAttempted={L}) — " +
                        "if this happens after login, the auth cookie was not sent. " +
                        "Server: check CookieSecurePolicy (should be SameAsRequest for HTTP LAN access). " +
                        "Current URL: {Url}", _authorizePageHits, _loginAttempted, currentUrl);

            if (_loginAttempted && _authorizePageHits >= MaxAuthorizeHits)
            {
                Log.Error("[OAuthLoginControl] Login loop confirmed after {N} redirects back to /connect/authorize. " +
                          "Possible causes: (1) Server CookieSecurePolicy=Always blocks HTTP cookies, " +
                          "(2) SameSite=Strict blocks cross-origin cookie, " +
                          "(3) WebView2 cookie isolation. " +
                          "Aborting flow.", _authorizePageHits);
                Dispatcher.UIThread.InvokeAsync(() =>
                    ShowLoopDiagnostic(currentUrl));
                return;
            }
        }

        if (!e.IsSuccess)
        {
            Log.Warning("[OAuthLoginControl] Navigation failed — url={U}", currentUrl);
            Dispatcher.UIThread.InvokeAsync(() =>
                ShowError("Не удалось загрузить страницу авторизации. Проверьте подключение к серверу."));
        }
    }

    // ── Callback processing ──────────────────────────────────────────────

    private void ProcessCallback(string url)
    {
        Log.Information("[OAuthLoginControl] Processing callback: {Url}", url);
        try
        {
            var query = HttpUtility.ParseQueryString(new Uri(url).Query);
            var code = query["code"];
            var state = query["state"];
            var error = query["error"];

            if (!string.IsNullOrEmpty(error))
            {
                RaiseCompleted(new OAuthResult { Success = false, Error = error });
                return;
            }

            if (string.IsNullOrEmpty(code))
            {
                Log.Error("[OAuthLoginControl] No code in callback");
                RaiseCompleted(new OAuthResult { Success = false, Error = "no_code" });
                return;
            }

            if (state != ExpectedState)
            {
                Log.Error("[OAuthLoginControl] State mismatch — expected {E}, got {G}", ExpectedState, state);
                RaiseCompleted(new OAuthResult { Success = false, Error = "state_mismatch" });
                return;
            }

            Log.Information("[OAuthLoginControl] Authorization code received");
            RaiseCompleted(new OAuthResult
            {
                Success = true,
                Code = code,
                CodeVerifier = CodeVerifier
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OAuthLoginControl] Error processing callback");
            RaiseCompleted(new OAuthResult { Success = false, Error = ex.Message });
        }
    }

    // ── UI helpers ───────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        if (_webViewContainer == null) return;
        _webViewContainer.Children.Clear();
        _webViewContainer.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Red,
            FontSize = 14,
            Margin = new Thickness(40)
        });
    }

    private void ShowLoopDiagnostic(string lastUrl)
    {
        if (_webViewContainer == null) return;
        _webViewContainer.Children.Clear();

        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(30),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Обнаружен цикл авторизации",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.Red,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"Сервер перенаправляет обратно на страницу входа {_authorizePageHits} раз(а).\n" +
                   "Возможные причины:\n" +
                   "• Сервер не принимает cookie по HTTP (CookieSecurePolicy)\n" +
                   "• WebView2 блокирует cookie для этого домена\n" +
                   "• Сессия не создаётся на сервере\n\n" +
                   $"Последний URL: {lastUrl}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Brushes.DarkRed
        });

        var retryButton = new Button
        {
            Content = "Повторить (очистить сессию)",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        retryButton.Click += async (_, _) =>
        {
            Log.Information("[OAuthLoginControl] User requested retry with session clear");
            _authorizePageHits = 0;
            _loginAttempted = false;
            _callbackAttempts = 0;
            await LoadWebViewAsync(clearSession: true);
        };

        var cancelButton = new Button
        {
            Content = "Отмена",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        cancelButton.Click += (_, _) =>
        {
            Log.Information("[OAuthLoginControl] User cancelled after loop detection");
            RaiseCompleted(new OAuthResult { Success = false, Error = "login_loop" });
        };

        panel.Children.Add(retryButton);
        panel.Children.Add(cancelButton);
        _webViewContainer.Children.Add(panel);
    }

    private void RaiseCompleted(OAuthResult result)
    {
        Dispatcher.UIThread.InvokeAsync(() => AuthCompleted?.Invoke(this, result));
    }

    // ── Button handlers ──────────────────────────────────────────────────

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Log.Information("[OAuthLoginControl] User cancelled");
        _timeoutCts?.Cancel();
        RaiseCompleted(new OAuthResult { Success = false, Error = "user_cancelled" });
    }
}
