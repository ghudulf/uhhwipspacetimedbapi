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

        // Convert HTTPS localhost:5001 → HTTP localhost:5000 for dev self-signed cert compat
        var navUrl = AuthorizationUrl;
        if (navUrl.StartsWith("https://localhost:5001", StringComparison.OrdinalIgnoreCase))
            navUrl = navUrl.Replace("https://localhost:5001", "http://localhost:5000");
        else if (navUrl.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase))
            navUrl = navUrl.Replace("https://localhost", "http://localhost");

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
        await Task.CompletedTask;
    }

    // ── Navigation events ────────────────────────────────────────────────

    private void OnNavigationStarting(object? sender, WebViewNavigationStartingEventArgs e)
    {
        var url = e.Request?.ToString() ?? string.Empty;
        Log.Debug("[OAuthLoginControl] NavigationStarting: {Url}", url);

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
        Log.Debug("[OAuthLoginControl] NavigationCompleted — success={S}, url={U}", e.IsSuccess, currentUrl);

        if (currentUrl.StartsWith(RedirectUri, StringComparison.OrdinalIgnoreCase))
        {
            ProcessCallback(currentUrl);
            return;
        }

        if (!e.IsSuccess)
        {
            Log.Warning("[OAuthLoginControl] Navigation failed");
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
