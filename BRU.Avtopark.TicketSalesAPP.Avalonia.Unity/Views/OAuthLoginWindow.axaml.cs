using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using System;
using Avalonia.Layout;
using System.Threading.Tasks;
using System.Web;
using Serilog;
using WebViewCore.Events;
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

            // Navigate to authorization URL
            Log.Debug("Navigating WebView to authorization URL");
            _webView.Url = new Uri(_authorizationUrl);
            
            if (_loadingPanel != null)
            {
                _loadingPanel.IsVisible = false;
            }

            await Task.CompletedTask;
        }

        private void OnWebViewNavigationStarting(object? sender, WebViewUrlLoadingEventArg e)
        {
            Log.Debug("WebView navigation starting to: {Url}", e.Url);

            // Check if this is a redirect to our callback URL
            if (e.Url?.ToString().StartsWith(_redirectUri, StringComparison.OrdinalIgnoreCase) == true)
            {
                Log.Information("Redirect URI detected in WebView navigation");
                e.Cancel = true; // Cancel the navigation
                ProcessRedirectUrl(e.Url.ToString());
            }
        }

        private void OnWebViewNavigationCompleted(object? sender, WebViewUrlLoadedEventArg e)
        {
            Log.Debug("WebView navigation completed");
            
            if (_loadingPanel != null)
            {
                _loadingPanel.IsVisible = false;
            }
            
            // Check URL after navigation completes
            if (_webView?.Url != null)
            {
                var currentUrl = _webView.Url.ToString();
                if (currentUrl.StartsWith(_redirectUri, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Redirect URI detected after navigation completed");
                    ProcessRedirectUrl(currentUrl);
                }
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
                Text = "Пожалуйста, завершите вход в браузере.\n\n" +
                       "После входа вы будете перенаправлены. Скопируйте полный URL из браузера и вставьте его ниже:",
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };

            var urlTextBox = new TextBox
            {
                Watermark = "Вставьте URL перенаправления сюда...",
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
                    ProcessRedirectUrl(url);
                }
                else
                {
                    Log.Warning("Submit clicked but URL is empty");
                }
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
