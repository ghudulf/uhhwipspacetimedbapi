using System;
using System.IO;
using System.Threading.Tasks;
using System.Web;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services
{
    public class AuthenticationManager
    {
        private readonly OAuthService _oauthService;
        private static AuthenticationManager? _instance;
        private static readonly object _lock = new object();
        private bool _isResetting = false; // Prevent retry during reset

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
            
            // Configure OAuth settings
            var clientId = "bru-avtopark-desktop-client";
            var clientSecret = "your-secure-client-secret-here-change-in-production";
            // Keep HTTPS for production security, SSL issues will be handled by fallback to browser
            var authorizationEndpoint = "https://localhost:5001/connect/authorize";
            var tokenEndpoint = "https://localhost:5001/connect/token";
            var redirectUri = "http://localhost:5000/callback";

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
            await _oauthService.LogoutAsync();
            AuthenticationStateChanged?.Invoke(this, false);
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
    }
}