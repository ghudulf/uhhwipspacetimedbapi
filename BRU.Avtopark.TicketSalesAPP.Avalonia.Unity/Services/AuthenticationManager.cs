using System;
using System.IO;
using System.Threading.Tasks;
using System.Web;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Generic;
using Avalonia.Controls.ApplicationLifetimes;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services
{
    public class AuthenticationManager
    {
        private readonly OAuthService _oauthService;
        private readonly string _serverRoot;
        private static AuthenticationManager? _instance;
        private static readonly object _lock = new object();
        private bool _isResetting = false; // Prevent retry during reset

        /// <summary>
        /// Path to the persistent logout-pending marker file.
        /// Survives app restarts so the WebView session is cleared even after a crash/restart.
        /// </summary>
        private static readonly string _logoutFlagPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BRU.Avtopark.TicketSalesApp",
            "logout_pending");

        /// <summary>
        /// Set to true by LogoutAsync() so the next OAuthLoginWindow knows to clear
        /// WebView session cookies. Persisted to disk so it survives app restarts.
        /// Consumed (deleted) once the window reads it.
        /// </summary>
        public static bool ClearWebViewSessionOnNextLogin
        {
            get => File.Exists(_logoutFlagPath);
            private set
            {
                if (value)
                {
                    // Ensure directory exists before writing
                    Directory.CreateDirectory(Path.GetDirectoryName(_logoutFlagPath)!);
                    File.WriteAllText(_logoutFlagPath, "1");
                }
                else
                {
                    if (File.Exists(_logoutFlagPath))
                        File.Delete(_logoutFlagPath);
                }
            }
        }

        /// <summary>
        /// Called by OAuthLoginWindow after reading the flag to reset it, so subsequent
        /// logins (e.g. token refresh flows) don't unnecessarily clear the session.
        /// </summary>
        /// <returns>True if the flag was set and has been consumed, false otherwise.</returns>
        public static bool ConsumeClearWebViewSessionFlag()
        {
            var wasSet = ClearWebViewSessionOnNextLogin;
            ClearWebViewSessionOnNextLogin = false;
            return wasSet;
        }

        public static AuthenticationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AuthenticationManager();
                        }
                    }
                }
                return _instance;
            }
        }

        public event EventHandler<bool>? AuthenticationStateChanged;

        private AuthenticationManager()
        {
            var tokenStorage = new TokenStorageService();

            var clientId = "bru-avtopark-desktop-client";
            var clientSecret = "your-secure-client-secret-here-change-in-production";

            // Derive server root from the discovered base URL (set during splash screen)
            var baseUrl = ApiClientService.Instance.CurrentBaseUrl ?? "http://localhost:5000/api/";
            var serverRoot = baseUrl.EndsWith("api/", StringComparison.OrdinalIgnoreCase)
                ? baseUrl[..^4].TrimEnd('/')
                : baseUrl.TrimEnd('/');

            var authorizationEndpoint = $"{serverRoot}/connect/authorize";
            var tokenEndpoint         = $"{serverRoot}/connect/token";
            // Redirect URI must always be localhost — it's a server-side callback endpoint
            // registered in OpenIddict. The LAN IP is only used for authorize/token endpoints.
            var redirectUri           = "http://localhost:5000/callback";

            _serverRoot = serverRoot;

            _oauthService = new OAuthService(
                clientId,
                clientSecret,
                authorizationEndpoint,
                tokenEndpoint,
                redirectUri,
                tokenStorage
            );
        }

        public async Task<bool> LoginAsync()
        {
            // Prevent login attempts during reset
            if (_isResetting)
            {
                Log.Warning("Login attempt blocked - authentication reset in progress");
                return false;
            }
            
            try
            {
                Log.Information("Starting OAuth login process with custom WebView window");
                
                // Check if already authenticated
                if (await _oauthService.IsAuthenticatedAsync())
                {
                    Log.Information("User is already authenticated");
                    return true;
                }

                Log.Debug("User not authenticated, starting OAuth flow");
                
                // CRITICAL: Clear any stale OAuth state files before starting new flow
                // This prevents callback loop issues from previous failed attempts
                try
                {
                    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var appFolder = Path.Combine(appDataPath, "BRU.Avtopark.TicketSalesApp");
                    
                    if (Directory.Exists(appFolder))
                    {
                        var tempFiles = Directory.GetFiles(appFolder, "oauth_*");
                        foreach (var file in tempFiles)
                        {
                            try
                            {
                                File.Delete(file);
                                Log.Debug("Cleared stale OAuth state file: {File}", file);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Could not delete OAuth state file: {File}", file);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error clearing stale OAuth state files: {Message}", ex.Message);
                }

                // Define requested scopes
                var scopes = new[]
                {
                    "openid",
                    "profile",
                    "email",
                    "offline_access",
                    "api"
                };

                Log.Debug("Generating authorization URL with scopes: {Scopes}", string.Join(", ", scopes));

                // Generate authorization URL with PKCE
                var authUrl = _oauthService.GenerateAuthorizationUrl(scopes, out var state, out var codeVerifier);
                
                Log.Information("Authorization URL generated");
                Log.Debug("Authorization URL: {AuthUrl}", authUrl);
                Log.Debug("State: {State}, CodeVerifier length: {Length}", state, codeVerifier.Length);

                // Get the main window
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var mainWindow = lifetime?.MainWindow;

                if (mainWindow == null)
                {
                    Log.Warning("No main window available for OAuth dialog");
                    // Try to find any open window
                    if (lifetime != null && lifetime.Windows.Count > 0)
                    {
                        mainWindow = lifetime.Windows[0];
                        Log.Debug("Using first available window as parent: {WindowType}", mainWindow.GetType().Name);
                    }
                    else
                    {
                        Log.Error("No windows available for OAuth authentication");
                        return false;
                    }
                }

                Log.Information("Opening OAuth login window");

                // Create and show OAuth login window
                var oauthWindow = new OAuthLoginWindow(
                    authUrl,
                    "http://localhost:5000/callback",
                    state,
                    codeVerifier
                );

                OAuthResult? result;
                
                try
                {
                    Log.Debug("Showing OAuth login window as dialog");
                    result = await oauthWindow.ShowDialog<OAuthResult>(mainWindow);
                    Log.Information("OAuth login window closed with result");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "OAuth login window threw exception: {Message}", ex.Message);
                    await ResetAuthenticationStateAsync();
                    return false;
                }

                if (result == null || !result.Success)
                {
                    Log.Warning("OAuth authentication cancelled or failed: {Error}", result?.Error ?? "null result");
                    
                    // If user cancelled, don't reset state - they might retry
                    if (result?.Error != "user_cancelled")
                    {
                        Log.Information("Non-cancellation error detected, resetting authentication state");
                        await ResetAuthenticationStateAsync();
                    }
                    
                    return false;
                }

                Log.Information("Authorization code received successfully, exchanging for tokens");
                Log.Debug("Authorization code length: {Length}", result.Code?.Length ?? 0);

                // Exchange authorization code for tokens
                try
                {
                    var tokens = await _oauthService.ExchangeCodeForTokenAsync(result.Code!, result.CodeVerifier!);

                    if (tokens != null && !string.IsNullOrEmpty(tokens.AccessToken))
                    {
                        Log.Information("Token exchange successful, access token received");
                        Log.Debug("Access token length: {Length}, expires in: {ExpiresIn} seconds", 
                            tokens.AccessToken.Length, tokens.ExpiresIn);
                        
                        // Log token metadata
                        Log.Information("=== OAuth Token Metadata ===");
                        Log.Information("Token Type: {TokenType}", tokens.TokenType);
                        Log.Information("Expires In: {ExpiresIn} seconds", tokens.ExpiresIn);
                        Log.Information("Expires At: {ExpiresAt}", tokens.ExpiresAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                        Log.Information("Has Refresh Token: {HasRefreshToken}", !string.IsNullOrEmpty(tokens.RefreshToken));
                        Log.Information("Has ID Token: {HasIdToken}", !string.IsNullOrEmpty(tokens.IdToken));
                        Log.Information("Scopes: {Scopes}", tokens.Scope ?? "none");
                        Log.Information("=== End OAuth Token Metadata ===");
                        
                        // CRITICAL: Set the access token in ApiClientService so API calls work
                        Log.Information("Setting access token in ApiClientService");
                        ApiClientService.Instance.AuthToken = tokens.AccessToken;
                        Log.Information("Access token set in ApiClientService for authenticated API calls");
                        Log.Debug("Verifying token was set - AuthToken is null: {IsNull}, length: {Length}", 
                            ApiClientService.Instance.AuthToken == null, 
                            ApiClientService.Instance.AuthToken?.Length ?? 0);
                        
                        // ENHANCEMENT: Fetch and log token claims from server
                        await FetchAndLogTokenClaimsAsync(tokens.AccessToken);
                        
                        AuthenticationStateChanged?.Invoke(this, true);
                        return true;
                    }

                    Log.Warning("Token exchange returned null or empty access token");
                    await ResetAuthenticationStateAsync();
                    return false;
                }
                catch (OAuthAuthorizationException authEx)
                {
                    Log.Error(authEx, "Authorization error during token exchange: {Message}", authEx.Message);
                    Log.Information("Authorization failed, tokens have been cleared. Resetting authentication state.");
                    await ResetAuthenticationStateAsync();
                    
                    // Notify that authentication state changed (logged out)
                    AuthenticationStateChanged?.Invoke(this, false);
                    
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unexpected error during token exchange: {Message}", ex.Message);
                    await ResetAuthenticationStateAsync();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "OAuth login error: {Message}", ex.Message);
                await ResetAuthenticationStateAsync();
                return false;
            }
        }

        public async Task<string?> GetAccessTokenAsync()
        {
            return await _oauthService.GetValidAccessTokenAsync();
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            return await _oauthService.IsAuthenticatedAsync();
        }

        public async Task LogoutAsync()
        {
            Log.Information("=== LOGOUT: Starting logout process ===");
            
            // Signal that the next OAuth window must clear WebView session cookies
            ClearWebViewSessionOnNextLogin = true;
            Log.Information("LOGOUT: WebView session clear flag set");
            
            // Clear OAuth tokens from storage
            await _oauthService.LogoutAsync();
            Log.Information("LOGOUT: Cleared OAuth tokens from storage");
            
            // Clear token from ApiClientService
            ApiClientService.Instance.AuthToken = null;
            Log.Information("LOGOUT: Cleared token from ApiClientService");
            
            // Clear any cached user data
            ApiClientService.Instance.IsAdmin = null;
            ApiClientService.Instance.UserRole = null;
            Log.Information("LOGOUT: Cleared cached user data");
            
            // Clean up any state files that may have been written to disk
            await CleanupStateFilesAsync();
            
            // Notify listeners
            AuthenticationStateChanged?.Invoke(this, false);
            Log.Information("LOGOUT: Notified authentication state changed");
            
            Log.Information("=== LOGOUT: Logout complete ===");
        }

        public async Task<bool> RefreshAuthenticationAsync()
        {
            try
            {
                var token = await _oauthService.GetValidAccessTokenAsync();
                return !string.IsNullOrEmpty(token);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Removes any state files the app may have written to disk — both in the
        /// %LocalAppData% app folder and next to the executable (bin folder), where
        /// AvaloniaWebView / WebView2 can drop its user-data directory at runtime.
        /// Called on every explicit logout so a fresh restart starts clean.
        /// </summary>
        private static async Task CleanupStateFilesAsync()
        {
            await Task.Run(() =>
            {
                // 1. %LocalAppData%\BRU.Avtopark.TicketSalesApp — oauth_* temp files
                try
                {
                    var appDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "BRU.Avtopark.TicketSalesApp");

                    if (Directory.Exists(appDataFolder))
                    {
                        foreach (var file in Directory.GetFiles(appDataFolder, "oauth_*"))
                        {
                            TryDelete(file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "CleanupStateFiles: error sweeping LocalAppData folder");
                }

                // 2. Bin / working directory — WebView2 user-data folder
                //    AvaloniaWebView (WebView2 backend) creates "<exename>.WebView2\EBWebView"
                //    next to the executable. Delete the whole "<exename>.WebView2" tree.
                var baseDir = AppContext.BaseDirectory;
                try
                {
                    // Match any directory ending in ".WebView2" next to the exe
                    foreach (var dir in Directory.GetDirectories(baseDir, "*.WebView2"))
                    {
                        try
                        {
                            Directory.Delete(dir, recursive: true);
                            Log.Information("CleanupStateFiles: deleted WebView2 data dir {Dir}", dir);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "CleanupStateFiles: could not delete {Dir}", dir);
                        }
                    }
                    // Also sweep legacy names just in case
                    foreach (var dirName in new[] { "EBWebView", "WebView2", ".webview" })
                    {
                        var path = Path.Combine(baseDir, dirName);
                        if (Directory.Exists(path))
                        {
                            try { Directory.Delete(path, recursive: true); Log.Information("CleanupStateFiles: deleted {Dir}", path); }
                            catch (Exception ex) { Log.Warning(ex, "CleanupStateFiles: could not delete {Dir}", path); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "CleanupStateFiles: error sweeping bin directory");
                }
            });
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) { Log.Warning(ex, "CleanupStateFiles: could not delete {File}", path); }
        }

        /// <summary>
        /// Resets all authentication state and clears stored tokens.
        /// This is the failsafe of failsafes - called when OAuth flow completely fails.
        /// </summary>
        private async Task ResetAuthenticationStateAsync()
        {
            if (_isResetting)
            {
                Log.Warning("Reset already in progress, skipping duplicate reset");
                return;
            }
            
            _isResetting = true;
            Log.Warning("=== RESETTING AUTHENTICATION STATE (FAILSAFE) ===");
            
            try
            {
                // Clear all stored tokens
                await _oauthService.LogoutAsync();
                Log.Information("Cleared stored OAuth tokens");
                
                // Clear any cached application data that might interfere
                try
                {
                    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var appFolder = Path.Combine(appDataPath, "BRU.Avtopark.TicketSalesApp");
                    
                    // Clear any temporary OAuth state files
                    if (Directory.Exists(appFolder))
                    {
                        var tempFiles = Directory.GetFiles(appFolder, "oauth_*");
                        foreach (var file in tempFiles)
                        {
                            try
                            {
                                File.Delete(file);
                                Log.Debug("Deleted temporary OAuth file: {File}", file);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Could not delete temporary file: {File}", file);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error clearing temporary OAuth files: {Message}", ex.Message);
                }
                
                // Notify listeners that authentication state changed to logged out
                AuthenticationStateChanged?.Invoke(this, false);
                Log.Information("Notified authentication state changed to logged out");
                
                // Wait a moment before allowing new login attempts
                await Task.Delay(1000);
                
                Log.Information("Authentication state reset complete - user can retry login");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during authentication state reset: {Message}", ex.Message);
                // Even if reset fails, still notify state change
                AuthenticationStateChanged?.Invoke(this, false);
            }
            finally
            {
                _isResetting = false;
            }
        }

        /// <summary>
        /// Public method to manually reset authentication state.
        /// Can be called from UI when user wants to start fresh.
        /// </summary>
        public async Task ResetAuthenticationAsync()
        {
            await ResetAuthenticationStateAsync();
        }

        /// <summary>
        /// ENHANCEMENT: Fetches and logs token claims from the server's tokeninfo endpoint
        /// This is useful for encrypted/opaque tokens where client-side parsing isn't possible
        /// </summary>
        private async Task FetchAndLogTokenClaimsAsync(string accessToken)
        {
            try
            {
                Log.Information("Fetching token claims from server tokeninfo endpoint");
                
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                
                var response = await httpClient.GetAsync($"{_serverRoot}/connect/tokeninfo");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var tokenInfo = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);
                    
                    Log.Information("=== OAuth Access Token Claims (from server) ===");
                    
                    if (tokenInfo.TryGetProperty("claims", out var claims))
                    {
                        foreach (var claim in claims.EnumerateObject())
                        {
                            if (claim.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                var values = new List<string>();
                                foreach (var item in claim.Value.EnumerateArray())
                                {
                                    // Guard against non-string JsonElement kinds (numbers, booleans, objects, arrays)
                                    // to prevent InvalidOperationException from GetString() on non-string elements.
                                    string itemValue = item.ValueKind == System.Text.Json.JsonValueKind.String || item.ValueKind == System.Text.Json.JsonValueKind.Null
                                        ? (item.GetString() ?? string.Empty)
                                        : item.GetRawText();
                                    values.Add(itemValue);
                                }
                                Log.Information("  {ClaimType}: [{Values}]", claim.Name, string.Join(", ", values));
                            }
                            else if (claim.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                Log.Information("  {ClaimType}: {Value}", claim.Name, claim.Value.GetString());
                            }
                            else
                            {
                                Log.Information("  {ClaimType}: {Value}", claim.Name, claim.Value.GetRawText());
                            }
                        }
                    }
                    
                    if (tokenInfo.TryGetProperty("authenticated", out var authenticated))
                        Log.Information("  Authenticated: {Value}", authenticated.GetBoolean());
                    
                    if (tokenInfo.TryGetProperty("authentication_type", out var authType))
                        Log.Information("  Authentication Type: {Value}", authType.GetString());
                    
                    Log.Information("=== End OAuth Access Token Claims ===");
                }
                else
                {
                    Log.Warning("Failed to fetch token claims from server. Status: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching token claims from server: {Message}", ex.Message);
            }
        }
    }
}