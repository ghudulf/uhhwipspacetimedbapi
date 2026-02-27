using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using System;
using System.Linq;
using Avalonia.Layout;
using System.Threading.Tasks;
using System.Web;
using Serilog;
using WebViewCore.Events;
using Avalonia.Threading;
using AvaloniaWebView;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
    public partial class OAuthLoginWindow : Window
    {
        private readonly string _authorizationUrl;
        private readonly string _redirectUri;
        private readonly string _expectedState;
        private readonly string _codeVerifier;
        private TaskCompletionSource<OAuthResult>? _completionSource;
        private Panel? _webViewContainer;
        private Panel? _loadingPanel;
        private WebView? _webView;
        private bool _useWebView = true;
        private System.Threading.CancellationTokenSource? _navigationTimeoutCts;
        private int _retryAttempts = 0;
        private const int MaxRetryAttempts = 2; // Allow 2 retries before complete reset
        private int _callbackNavigationAttempts = 0;
        private const int MaxCallbackAttempts = 2; // If we see callback URL more than twice, it's a loop
        private DateTime _lastCallbackAttempt = DateTime.MinValue;

        public OAuthLoginWindow()
        {
            InitializeComponent();
            _authorizationUrl = string.Empty;
            _redirectUri = string.Empty;
            _expectedState = string.Empty;
            _codeVerifier = string.Empty;
        }

        public OAuthLoginWindow(string authorizationUrl, string redirectUri, string expectedState, string codeVerifier)
        {
            InitializeComponent();
            _authorizationUrl = authorizationUrl;
            _redirectUri = redirectUri;
            _expectedState = expectedState;
            _codeVerifier = codeVerifier;
            _completionSource = new TaskCompletionSource<OAuthResult>();

            Log.Information("OAuthLoginWindow created");
            Log.Debug("Authorization URL: {Url}", authorizationUrl);
            Log.Debug("Redirect URI: {Uri}", redirectUri);
            Log.Debug("Expected state: {State}", expectedState);

#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _webViewContainer = this.FindControl<Panel>("WebViewContainer");
            _loadingPanel = this.FindControl<Panel>("LoadingPanel");
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            
            if (!string.IsNullOrEmpty(_authorizationUrl))
            {
                _ = LoadAuthorizationPageAsync();
            }
        }

        private async Task LoadAuthorizationPageAsync()
        {
            try
            {
                Log.Information("Loading OAuth authorization page");
                
                // Try to use WebView first, fallback to browser if it fails
                if (_useWebView)
                {
                    try
                    {
                        await LoadWithWebViewAsync();
                    }
                    catch (Exception webViewEx)
                    {
                        Log.Warning(webViewEx, "WebView failed to load, falling back to browser method");
                        _useWebView = false;
                        await LoadWithBrowserFallbackAsync();
                    }
                }
                else
                {
                    await LoadWithBrowserFallbackAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading authorization page: {Message}", ex.Message);
                _completionSource?.TrySetException(ex);
                Close();
            }
        }

        private async Task LoadWithWebViewAsync()
        {
            Log.Information("Attempting to load with embedded WebView");
            
            if (_loadingPanel != null)
            {
                _loadingPanel.IsVisible = true;
            }

            // Create WebView programmatically
            _webView = new WebView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Subscribe to navigation events
            _webView.NavigationStarting += OnWebViewNavigationStarting;
            _webView.NavigationCompleted += OnWebViewNavigationCompleted;

            // Add WebView to container
            if (_webViewContainer != null)
            {
                _webViewContainer.Children.Clear();
                _webViewContainer.Children.Add(_webView);
                Log.Debug("WebView added to container");
            }

            // For local development with self-signed certificates, we need to use HTTP instead of HTTPS
            // or configure the WebView to accept self-signed certificates
            var navigationUrl = _authorizationUrl;
            
            // Check if this is localhost HTTPS and convert to HTTP for WebView compatibility
            if (_authorizationUrl.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase))
            {
                navigationUrl = _authorizationUrl.Replace("https://localhost", "http://localhost");
                Log.Warning("Converting HTTPS localhost to HTTP for WebView compatibility: {Url}", navigationUrl);
                Log.Warning("Note: This is only for local development. Production should use HTTPS.");
            }
            
            // Navigate to authorization URL
            Log.Debug("Navigating WebView to authorization URL");
            _webView.Url = new Uri(navigationUrl);
            
            // Start navigation timeout (30 seconds)
            _navigationTimeoutCts = new System.Threading.CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(30000, _navigationTimeoutCts.Token);
                    
                    // If we reach here, navigation timed out
                    if (!_navigationTimeoutCts.Token.IsCancellationRequested)
                    {
                        Log.Warning("WebView navigation timeout after 30 seconds");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            HandleNavigationError("Navigation timeout");
                        });
                    }
                }
                catch (TaskCanceledException)
                {
                    // Navigation completed successfully, timeout was cancelled
                    Log.Debug("Navigation timeout cancelled (navigation completed)");
                }
            });
            
            await Task.CompletedTask;
        }
                
        

        private void OnWebViewNavigationStarting(object? sender, WebViewUrlLoadingEventArg e)
        {
            Log.Debug("WebView navigation starting to: {Url}", e.Url);

            var url = e.Url?.ToString() ?? "";
            
            // Check if this is a redirect to our callback URL
            if (url.StartsWith(_redirectUri, StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("Redirect URI detected in WebView navigation");
                
                // Detect callback loop - if we're hitting callback repeatedly without going through auth flow
                var timeSinceLastCallback = DateTime.UtcNow - _lastCallbackAttempt;
                _lastCallbackAttempt = DateTime.UtcNow;
                
                // If we see callback attempts within 5 seconds of each other, it's likely a loop
                if (timeSinceLastCallback.TotalSeconds < 5)
                {
                    _callbackNavigationAttempts++;
                    Log.Warning("Callback navigation attempt {Attempt} within {Seconds} seconds", 
                        _callbackNavigationAttempts, timeSinceLastCallback.TotalSeconds);
                    
                    if (_callbackNavigationAttempts > MaxCallbackAttempts)
                    {
                        Log.Error("CALLBACK LOOP DETECTED - Client keeps trying to navigate to callback URL!");
                        Log.Error("This indicates stored invalid authorization data. Triggering complete reset.");
                        e.Cancel = true;
                        
                        // Show error and trigger complete reset
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ShowCallbackLoopError();
                        });
                        return;
                    }
                }
                else
                {
                    // Reset counter if enough time has passed
                    _callbackNavigationAttempts = 1;
                }
                
                e.Cancel = true; // Cancel the navigation
                
                // Check if the URL contains an error
                if (url.Contains("error=", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Error("Error detected in callback URL: {Url}", url);
                    // Extract error from URL
                    var uri = new Uri(url);
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    var error = query["error"];
                    var errorDescription = query["error_description"];
                    
                    Log.Error("OAuth error: {Error} - {Description}", error, errorDescription);
                    
                    // Show error to user and trigger reset
                    HandleAuthorizationError(error, errorDescription);
                }
                else
                {
                    // Success case - process the authorization code
                    ProcessRedirectUrl(url);
                }
            }
        }

        private void ShowCallbackLoopError()
        {
            Log.Error("Showing callback loop error dialog");
            
            if (_webViewContainer == null)
            {
                Log.Warning("WebViewContainer not found, cannot show error UI");
                TriggerCompleteAuthenticationReset();
                return;
            }
            
            // Create error UI
            var errorPanel = new StackPanel
            {
                Spacing = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(40)
            };
            
            var titleText = new TextBlock
            {
                Text = "Обнаружен цикл авторизации",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Colors.Red),
                Margin = new Thickness(0, 0, 0, 10)
            };
            
            var messageText = new TextBlock
            {
                Text = "Клиент постоянно пытается перейти на страницу callback, что указывает на сохраненные неверные данные авторизации.\n\n" +
                       "Все данные авторизации будут очищены.\n\n" +
                       "Нажмите OK для сброса и повторной попытки.",
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500,
                Margin = new Thickness(0, 0, 0, 20)
            };
            
            var okButton = new Button
            {
                Content = "OK - Сбросить и закрыть",
                Width = 200,
                Height = 36,
                Padding = new Thickness(20, 8),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            okButton.Click += (s, e) =>
            {
                Log.Information("User acknowledged callback loop error");
                TriggerCompleteAuthenticationReset();
            };
            
            errorPanel.Children.Add(titleText);
            errorPanel.Children.Add(messageText);
            errorPanel.Children.Add(okButton);
            
            _webViewContainer.Children.Clear();
            _webViewContainer.Children.Add(errorPanel);
            
            Log.Debug("Callback loop error dialog displayed");
        }

        private void HandleAuthorizationError(string? error, string? errorDescription)
        {
            Log.Error("Handling authorization error: {Error} - {Description}", error, errorDescription);
            
            _retryAttempts++;
            
            if (_retryAttempts > MaxRetryAttempts)
            {
                Log.Error("Maximum retry attempts exceeded after authorization error");
                ShowAuthorizationErrorDialog(
                    "Превышено количество попыток",
                    $"Ошибка авторизации: {errorDescription ?? error ?? "Неизвестная ошибка"}\n\n" +
                    "Превышено максимальное количество попыток.\n\n" +
                    "Выберите действие:");
                return;
            }
            
            // Show error dialog with options
            ShowAuthorizationErrorDialog(
                "Ошибка авторизации",
                $"Ошибка: {errorDescription ?? error ?? "Неизвестная ошибка"}\n\n" +
                $"Попытка {_retryAttempts} из {MaxRetryAttempts}\n\n" +
                "Выберите действие:");
        }

        private void ShowAuthorizationErrorDialog(string title, string message)
        {
            Log.Debug("Showing authorization error dialog: {Title}", title);
            
            if (_webViewContainer == null)
            {
                Log.Warning("WebViewContainer not found, cannot show error UI");
                return;
            }
            
            // Create error UI
            var errorPanel = new StackPanel
            {
                Spacing = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(40)
            };
            
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Colors.Red),
                Margin = new Thickness(0, 0, 0, 10)
            };
            
            var messageText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500,
                Margin = new Thickness(0, 0, 0, 20)
            };
            
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            // Retry button (only if not exceeded max attempts)
            if (_retryAttempts <= MaxRetryAttempts)
            {
                var retryButton = new Button
                {
                    Content = "Повторить попытку",
                    Width = 200,
                    Height = 36,
                    Padding = new Thickness(20, 8),
                    BorderThickness = new Thickness(1)
                };
                
                retryButton.Click += async (s, e) =>
                {
                    Log.Information("User clicked retry button after authorization error");
                    await ResetAndRetryWebViewAsync();
                };
                
                buttonPanel.Children.Add(retryButton);
            }
            
            // Use browser button
            var browserButton = new Button
            {
                Content = "Использовать браузер",
                Width = 200,
                Height = 36,
                Padding = new Thickness(20, 8),
                BorderThickness = new Thickness(1)
            };
            
            browserButton.Click += async (s, e) =>
            {
                Log.Information("User chose to use browser after authorization error");
                _useWebView = false;
                await LoadWithBrowserFallbackAsync();
            };
            
            // Open DevTools button (for debugging)
            var devToolsButton = new Button
            {
                Content = "Открыть DevTools (отладка)",
                Width = 200,
                Height = 36,
                Padding = new Thickness(20, 8),
                BorderThickness = new Thickness(1)
            };
            
            devToolsButton.Click += (s, e) =>
            {
                Log.Information("User clicked DevTools button");
                if (_webView != null)
                {
                    try
                    {
                        var opened = _webView.OpenDevToolsWindow();
                        Log.Information("DevTools window opened: {Success}", opened);
                        if (!opened)
                        {
                            Log.Warning("Failed to open DevTools window");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error opening DevTools: {Message}", ex.Message);
                    }
                }
                else
                {
                    Log.Warning("WebView is null, cannot open DevTools");
                }
            };
            
            // Reset and cancel button
            var resetButton = new Button
            {
                Content = "Сбросить и отменить",
                Width = 200,
                Height = 36,
                Padding = new Thickness(20, 8),
                BorderThickness = new Thickness(1)
            };
            
            resetButton.Click += (s, e) =>
            {
                Log.Information("User chose to reset and cancel after authorization error");
                TriggerCompleteAuthenticationReset();
            };
            
            buttonPanel.Children.Add(browserButton);
            buttonPanel.Children.Add(devToolsButton);
            buttonPanel.Children.Add(resetButton);
            
            errorPanel.Children.Add(titleText);
            errorPanel.Children.Add(messageText);
            errorPanel.Children.Add(buttonPanel);
            
            _webViewContainer.Children.Clear();
            _webViewContainer.Children.Add(errorPanel);
            
            Log.Debug("Authorization error dialog displayed");
        }

        private void OnWebViewNavigationCompleted(object? sender, WebViewUrlLoadedEventArg e)
        {
            Log.Debug("WebView navigation completed, Success: {Success}", e.IsSuccess);
            
            // Cancel navigation timeout since navigation completed
            _navigationTimeoutCts?.Cancel();
            _navigationTimeoutCts?.Dispose();
            _navigationTimeoutCts = null;
            
            if (_loadingPanel != null)
            {
                _loadingPanel.IsVisible = false;
            }
            
            // Check if navigation failed (HTTP error like 500, 404, etc.)
            if (!e.IsSuccess)
            {
                Log.Error("WebView navigation failed");
                HandleNavigationError("Navigation failed with error status");
                return;
            }
            
            // Check URL after navigation completes
            if (_webView?.Url != null)
            {
                var currentUrl = _webView.Url.ToString();
                
                Log.Debug("Current URL after navigation: {Url}", currentUrl);
                
                // Check for error indicators in the URL or page
                if (currentUrl.Contains("/error", StringComparison.OrdinalIgnoreCase) ||
                    currentUrl.Contains("error=", StringComparison.OrdinalIgnoreCase) ||
                    currentUrl.Contains("500", StringComparison.OrdinalIgnoreCase) ||
                    currentUrl.Contains("400", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning("Error detected in navigation URL: {Url}", currentUrl);
                    HandleNavigationError(currentUrl);
                    return;
                }
                
                if (currentUrl.StartsWith(_redirectUri, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Redirect URI detected after navigation completed");
                    ProcessRedirectUrl(currentUrl);
                }
            }
        }

        private void HandleNavigationError(string failedUrl)
        {
            Log.Error("Handling navigation error for URL: {Url}", failedUrl);
            
            // Check if this is an SSL/certificate error (common with localhost HTTPS)
            if (failedUrl.Contains("https://localhost", StringComparison.OrdinalIgnoreCase) ||
                failedUrl.Contains("Navigation failed with error status"))
            {
                Log.Error("SSL/Certificate error detected - WebView cannot load HTTPS localhost");
                
                // Show error dialog with options
                ShowNavigationErrorDialog(
                    "Ошибка SSL сертификата",
                    "WebView не может загрузить страницу из-за проблем с SSL сертификатом.\n\n" +
                    "Это нормально для локальной разработки с самоподписанными сертификатами.\n\n" +
                    "Выберите действие:");
                return;
            }
            
            // Check if this is an authorization error
            if (failedUrl.Contains("authorize", StringComparison.OrdinalIgnoreCase) ||
                failedUrl.Contains("oauth", StringComparison.OrdinalIgnoreCase) ||
                failedUrl.Contains("connect", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("Authorization endpoint error detected");
                
                _retryAttempts++;
                
                if (_retryAttempts > MaxRetryAttempts)
                {
                    Log.Error("Maximum retry attempts exceeded");
                    ShowNavigationErrorDialog(
                        "Превышено количество попыток",
                        "Не удалось подключиться к серверу авторизации после нескольких попыток.\n\n" +
                        "Выберите действие:");
                    return;
                }
                
                // Show error dialog with retry option
                ShowNavigationErrorDialog(
                    "Ошибка авторизации",
                    $"Произошла ошибка при подключении к серверу авторизации.\n\n" +
                    $"Попытка {_retryAttempts} из {MaxRetryAttempts}\n\n" +
                    "Выберите действие:");
            }
            else
            {
                // Generic navigation error
                Log.Error("Generic navigation error");
                ShowNavigationErrorDialog(
                    "Ошибка загрузки",
                    "Не удалось загрузить страницу авторизации.\n\n" +
                    "Выберите действие:");
            }
        }

        private void ShowNavigationErrorDialog(string title, string message)
        {
            Log.Debug("Showing navigation error dialog: {Title}", title);
            
            if (_webViewContainer == null)
            {
                Log.Warning("WebViewContainer not found, cannot show error UI");
                return;
            }
            
            // Create error UI
            var errorPanel = new StackPanel
            {
                Spacing = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(40)
            };
            
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Colors.Red),
                Margin = new Thickness(0, 0, 0, 10)
            };
            
            var messageText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500,
                Margin = new Thickness(0, 0, 0, 20)
            };
            
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            // Retry button (only if not exceeded max attempts)
            if (_retryAttempts <= MaxRetryAttempts)
            {
                var retryButton = new Button
                {
                    Content = "Повторить попытку",
                    Width = 200,
                    Height = 36,
                    Padding = new Thickness(20, 8),
                    BorderThickness = new Thickness(1)
                };
                
                retryButton.Click += async (s, e) =>
                {
                    Log.Information("User clicked retry button");
                    await ResetAndRetryWebViewAsync();
                };
                
                buttonPanel.Children.Add(retryButton);
            }
            
            // Use browser button
            var browserButton = new Button
            {
                Content = "Использовать браузер",
                Width = 200,
                Height = 36,
                Padding = new Thickness(20, 8),
                BorderThickness = new Thickness(1)
            };
            
            browserButton.Click += async (s, e) =>
            {
                Log.Information("User chose to use browser");
                _useWebView = false;
                await LoadWithBrowserFallbackAsync();
            };
            
            // Open DevTools button (for debugging)
            var devToolsButton = new Button
            {
                Content = "Открыть DevTools (отладка)",
                Width = 200,
                Height = 36,
                Padding = new Thickness(20, 8),
                BorderThickness = new Thickness(1)
            };
            
            devToolsButton.Click += (s, e) =>
            {
                Log.Information("User clicked DevTools button");
                if (_webView != null)
                {
                    try
                    {
                        var opened = _webView.OpenDevToolsWindow();
                        Log.Information("DevTools window opened: {Success}", opened);
                        if (!opened)
                        {
                            Log.Warning("Failed to open DevTools window");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error opening DevTools: {Message}", ex.Message);
                    }
                }
                else
                {
                    Log.Warning("WebView is null, cannot open DevTools");
                }
            };
            
            // Reset and cancel button
            var resetButton = new Button
            {
                Content = "Сбросить и отменить",
                Width = 200,
                Height = 36,
                Padding = new Thickness(20, 8),
                BorderThickness = new Thickness(1)
            };
            
            resetButton.Click += (s, e) =>
            {
                Log.Information("User chose to reset and cancel");
                TriggerCompleteAuthenticationReset();
            };
            
            buttonPanel.Children.Add(browserButton);
            buttonPanel.Children.Add(devToolsButton);
            buttonPanel.Children.Add(resetButton);
            
            errorPanel.Children.Add(titleText);
            errorPanel.Children.Add(messageText);
            errorPanel.Children.Add(buttonPanel);
            
            _webViewContainer.Children.Clear();
            _webViewContainer.Children.Add(errorPanel);
            
            Log.Debug("Navigation error dialog displayed");
        }

        private async Task ResetAndRetryWebViewAsync()
        {
            Log.Information("Resetting WebView and retrying authorization");
            
            _retryAttempts++;
            Log.Information("Retry attempt {Attempt} of {MaxAttempts}", _retryAttempts, MaxRetryAttempts);
            
            // Check if we've exceeded max retry attempts
            if (_retryAttempts > MaxRetryAttempts)
            {
                Log.Error("Maximum retry attempts ({MaxAttempts}) exceeded, triggering complete authentication reset", MaxRetryAttempts);
                TriggerCompleteAuthenticationReset();
                return;
            }
            
            try
            {
                // Cancel any pending navigation timeout
                _navigationTimeoutCts?.Cancel();
                _navigationTimeoutCts?.Dispose();
                _navigationTimeoutCts = null;
                
                // Cleanup existing WebView
                if (_webView != null)
                {
                    try
                    {
                        _webView.NavigationStarting -= OnWebViewNavigationStarting;
                        _webView.NavigationCompleted -= OnWebViewNavigationCompleted;
                        
                        // Clear WebView cookies and storage
                        await ClearWebViewDataAsync(_webView);
                        
                        if (_webViewContainer != null)
                        {
                            _webViewContainer.Children.Remove(_webView);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error cleaning up WebView during reset");
                    }
                    _webView = null;
                }
                
                // Wait a moment before retrying
                await Task.Delay(1000);
                
                // Retry loading with WebView
                await LoadWithWebViewAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error resetting WebView, falling back to browser");
                _useWebView = false;
                await LoadWithBrowserFallbackAsync();
            }
        }

        /// <summary>
        /// Clears WebView cookies, cache, and local storage.
        /// This ensures a clean state for retry attempts.
        /// </summary>
        private async Task ClearWebViewDataAsync(WebView webView)
        {
            try
            {
                Log.Information("Clearing WebView cookies, cache, and local storage");
                
                // Try to clear cookies via JavaScript if WebView supports it
                try
                {
                    // Execute JavaScript to clear cookies and local storage
                    var clearScript = @"
                        (function() {
                            try {
                                // Clear cookies
                                document.cookie.split(';').forEach(function(c) { 
                                    document.cookie = c.replace(/^ +/, '').replace(/=.*/, '=;expires=' + new Date().toUTCString() + ';path=/'); 
                                });
                                
                                // Clear local storage
                                if (typeof(Storage) !== 'undefined') {
                                    localStorage.clear();
                                    sessionStorage.clear();
                                }
                                
                                return 'cleared';
                            } catch(e) {
                                return 'error: ' + e.message;
                            }
                        })();
                    ";
                    
                    // Note: ExecuteScriptAsync might not be available in all WebView implementations
                    // This is a best-effort attempt
                    if (webView.GetType().GetMethod("ExecuteScriptAsync") != null)
                    {
                        var result = await (dynamic)webView.GetType()
                            .GetMethod("ExecuteScriptAsync")!
                            .Invoke(webView, new object[] { clearScript })!;
                        Log.Debug("WebView data clear script result: {Result}", result);
                    }
                    else
                    {
                        Log.Debug("ExecuteScriptAsync not available, skipping JavaScript-based clearing");
                    }
                }
                catch (Exception jsEx)
                {
                    Log.Warning(jsEx, "Could not clear WebView data via JavaScript: {Message}", jsEx.Message);
                }
                
                // Try to clear via WebView API if available
                try
                {
                    var clearCookiesMethod = webView.GetType().GetMethod("ClearCookies");
                    if (clearCookiesMethod != null)
                    {
                        clearCookiesMethod.Invoke(webView, null);
                        Log.Debug("Cleared WebView cookies via API");
                    }
                    
                    var clearCacheMethod = webView.GetType().GetMethod("ClearCache");
                    if (clearCacheMethod != null)
                    {
                        clearCacheMethod.Invoke(webView, null);
                        Log.Debug("Cleared WebView cache via API");
                    }
                }
                catch (Exception apiEx)
                {
                    Log.Warning(apiEx, "Could not clear WebView data via API: {Message}", apiEx.Message);
                }
                
                Log.Information("WebView data clearing completed (best effort)");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error clearing WebView data: {Message}", ex.Message);
                // Don't throw - this is best effort
            }
        }

        private async Task LoadWithBrowserFallbackAsync()
        {
            Log.Information("Using browser fallback method");
            
            // Open the authorization URL in the default browser
            Log.Debug("Opening authorization URL in default browser");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _authorizationUrl,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            Log.Information("Browser opened with authorization URL");

            // Show instructions to the user
            if (_loadingPanel != null)
            {
                _loadingPanel.IsVisible = false;
            }

            Log.Debug("Creating instruction UI");

            // Add instruction text with classic styling
            var instructionText = new TextBlock
            {
                Text = "Авторизация открыта в браузере.\n\n" +
                       "После входа браузер попытается перейти на адрес callback, который не будет загружаться (это нормально).\n\n" +
                       "Скопируйте ПОЛНЫЙ URL из адресной строки браузера (начинается с http://localhost:5000/callback) и вставьте его ниже:",
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };

            var urlTextBox = new TextBox
            {
                Watermark = "http://localhost:5000/callback?code=...",
                Width = 600,
                Margin = new Thickness(20, 10, 20, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(8),
                BorderThickness = new Thickness(1)
            };

            var submitButton = new Button
            {
                Content = "Отправить",
                Width = 150,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(20, 6),
                BorderThickness = new Thickness(1)
            };

            submitButton.Click += (s, e) =>
            {
                var url = urlTextBox.Text;
                Log.Debug("Submit button clicked, URL: {Url}", url);
                if (!string.IsNullOrEmpty(url))
                {
                    // Check if URL contains error
                    if (url.Contains("error=", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warning("Error detected in pasted URL: {Url}", url);
                        try
                        {
                            var uri = new Uri(url);
                            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                            var error = query["error"];
                            var errorDescription = query["error_description"];
                            HandleAuthorizationError(error, errorDescription);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to parse error from URL");
                            ShowErrorMessage("Ошибка в URL. Проверьте правильность скопированного адреса.");
                        }
                    }
                    else
                    {
                        ProcessRedirectUrl(url);
                    }
                }
                else
                {
                    Log.Warning("Submit clicked but URL is empty");
                    ShowErrorMessage("Пожалуйста, вставьте URL из браузера.");
                }
            };

            var cancelButton = new Button
            {
                Content = "Отмена",
                Width = 150,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(20, 6),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 10, 0, 0)
            };

            cancelButton.Click += (s, e) =>
            {
                Log.Information("User clicked cancel in browser fallback");
                var result = new OAuthResult
                {
                    Success = false,
                    Error = "user_cancelled",
                    ErrorDescription = "User cancelled the authentication"
                };
                _completionSource?.TrySetResult(result);
                Close(result);
            };

            var stackPanel = new StackPanel
            {
                Spacing = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            stackPanel.Children.Add(instructionText);
            stackPanel.Children.Add(urlTextBox);
            stackPanel.Children.Add(submitButton);
            stackPanel.Children.Add(cancelButton);

            if (_webViewContainer != null)
            {
                _webViewContainer.Children.Clear();
                _webViewContainer.Children.Add(stackPanel);
                Log.Debug("Instruction UI added to container");
            }
            else
            {
                Log.Warning("WebViewContainer not found");
            }

            await Task.CompletedTask;
        }

        private void ShowErrorMessage(string message)
        {
            if (_webViewContainer == null) return;

            var errorText = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Colors.Red),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(20, 10, 20, 0)
            };

            // Find the stack panel and add error message
            var stackPanel = _webViewContainer.Children.OfType<StackPanel>().FirstOrDefault();
            if (stackPanel != null)
            {
                // Remove any existing error messages
                var existingErrors = stackPanel.Children.OfType<TextBlock>()
                    .Where(tb => tb.Foreground is SolidColorBrush brush && brush.Color == Colors.Red)
                    .ToList();
                foreach (var error in existingErrors)
                {
                    stackPanel.Children.Remove(error);
                }

                // Add new error message after the instruction text
                stackPanel.Children.Insert(1, errorText);
            }
        }

        private void ProcessRedirectUrl(string url)
        {
            try
            {
                Log.Information("Processing redirect URL");
                Log.Debug("Redirect URL: {Url}", url);
                
                var uri = new Uri(url);
                var query = HttpUtility.ParseQueryString(uri.Query);

                var code = query["code"];
                var state = query["state"];
                var error = query["error"];
                var errorDescription = query["error_description"];

                Log.Debug("Parsed query parameters - code: {HasCode}, state: {State}, error: {Error}", 
                    !string.IsNullOrEmpty(code), state, error);

                OAuthResult result;

                if (!string.IsNullOrEmpty(error))
                {
                    Log.Error("OAuth error received: {Error} - {Description}", error, errorDescription);
                    result = new OAuthResult
                    {
                        Success = false,
                        Error = error,
                        ErrorDescription = errorDescription
                    };
                }
                else if (state != _expectedState)
                {
                    Log.Error("State mismatch - expected: {Expected}, received: {Received}", _expectedState, state);
                    result = new OAuthResult
                    {
                        Success = false,
                        Error = "invalid_state",
                        ErrorDescription = "State parameter mismatch"
                    };
                }
                else if (!string.IsNullOrEmpty(code))
                {
                    Log.Information("Authorization code received successfully");
                    result = new OAuthResult
                    {
                        Success = true,
                        Code = code,
                        CodeVerifier = _codeVerifier
                    };
                }
                else
                {
                    Log.Error("No authorization code in response");
                    result = new OAuthResult
                    {
                        Success = false,
                        Error = "invalid_response",
                        ErrorDescription = "No authorization code received"
                    };
                }

                Log.Debug("Setting result and closing window - Success: {Success}", result.Success);
                _completionSource?.TrySetResult(result);
                Close(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing redirect URL: {Message}", ex.Message);
                var errorResult = new OAuthResult
                {
                    Success = false,
                    Error = "exception",
                    ErrorDescription = ex.Message
                };
                _completionSource?.TrySetException(ex);
                Close(errorResult);
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // Cancel any pending navigation timeout
            _navigationTimeoutCts?.Cancel();
            _navigationTimeoutCts?.Dispose();
            _navigationTimeoutCts = null;
            
            // Cleanup WebView
            if (_webView != null)
            {
                try
                {
                    _webView.NavigationStarting -= OnWebViewNavigationStarting;
                    _webView.NavigationCompleted -= OnWebViewNavigationCompleted;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error cleaning up WebView");
                }
                _webView = null;
            }
            
            base.OnClosing(e);
        }

        private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            var result = new OAuthResult
            {
                Success = false,
                Error = "user_cancelled",
                ErrorDescription = "User cancelled the authentication"
            };
            _completionSource?.TrySetResult(result);
            Close(result);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Log.Information("User clicked cancel button");
            var result = new OAuthResult
            {
                Success = false,
                Error = "user_cancelled",
                ErrorDescription = "User cancelled the authentication"
            };
            _completionSource?.TrySetResult(result);
            Close(result);
        }

        public Task<OAuthResult> GetResultAsync()
        {
            return _completionSource?.Task ?? Task.FromResult(new OAuthResult
            {
                Success = false,
                Error = "not_initialized",
                ErrorDescription = "Window not properly initialized"
            });
        }

        /// <summary>
        /// Called when all authentication methods have failed.
        /// This triggers the complete reset of authentication state.
        /// </summary>
        private void TriggerCompleteAuthenticationReset()
        {
            Log.Error("=== ALL AUTHENTICATION METHODS FAILED - TRIGGERING COMPLETE RESET ===");
            
            var result = new OAuthResult
            {
                Success = false,
                Error = "authentication_failed",
                ErrorDescription = "All authentication methods failed. Authentication state will be reset."
            };
            
            _completionSource?.TrySetResult(result);
            Close(result);
        }
    }

    public class OAuthResult
    {
        public bool Success { get; set; }
        public string? Code { get; set; }
        public string? CodeVerifier { get; set; }
        public string? Error { get; set; }
        public string? ErrorDescription { get; set; }
    }
}
