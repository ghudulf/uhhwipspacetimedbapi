using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity;
using Serilog;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity;

public partial class App : Application
{
    private Window? _mainWindow;
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();
#if DESKTOP
        // AvaloniaWebViewBuilder is only available on the desktop entry point.
        // The DESKTOP constant is defined in the Desktop .csproj and the MAUI
        // .csproj for the net10.0 (generic desktop) TFM only — not for
        // net10.0-android or net10.0-ios — so this call is safely excluded on
        // mobile/browser targets.
        // Uncomment when Avalonia.Controls.WebView is wired up:
        // AvaloniaWebViewBuilder.Initialize(default);
        // Log.Information("WebView.Avalonia initialized");
#endif
    }


    private async Task ShowSplashScreenAndInitialize(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var splashScreen = new SplashScreen();
            splashScreen.Show();

            // Initial delay to show splash screen
            await Task.Delay(1000);

            // Check server availability
            Log.Debug("Checking server availability");
            if (!await splashScreen.CheckServerAvailability())
            {
                Log.Error("Server not available");
                // Wait for 5 seconds to show the error message
                await Task.Delay(5000);
                desktop.Shutdown();
                return;
            }

            // Additional initialization delay
            await Task.Delay(3000);

            // Close the splash screen
            splashScreen.Close();
            Log.Debug("Splash screen closed");

            // Show background window
            var backgroundWindow = new BackGroundWindow();
            backgroundWindow.Show();
            
            // Short delay to ensure background window is displayed
            await Task.Delay(500);

            // CRITICAL: Try to load existing OAuth token from storage
            // This allows users to stay logged in across app restarts
            Log.Information("Attempting to load existing OAuth token from storage");
            var tokenStorage = new Services.TokenStorageService();
            var existingTokens = await tokenStorage.GetTokensAsync();
            
            if (existingTokens != null && !string.IsNullOrEmpty(existingTokens.AccessToken))
            {
                // Check if token is still valid (with 5 minute buffer)
                if (existingTokens.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
                {
                    Log.Information("Valid OAuth token found in storage, setting in ApiClientService");
                    Services.ApiClientService.Instance.AuthToken = existingTokens.AccessToken;
                    Log.Information("OAuth token loaded from storage and set in ApiClientService");
                }
                else
                {
                    Log.Warning("Stored OAuth token has expired, clearing and requiring new login");
                    await tokenStorage.ClearTokensAsync();
                }
            }
            else
            {
                Log.Information("No valid OAuth token found in storage");
            }

            // Show login method selector
            var loginMethodSelector = new LoginMethodSelectorWindow();
            var selectedMethod = await loginMethodSelector.ShowDialog<LoginMethod?>(backgroundWindow);

            if (!selectedMethod.HasValue)
            {
                Log.Information("No login method selected. Shutting down...");
                desktop.Shutdown();
                return;
            }

            bool isAuthenticated = false;

            if (selectedMethod.Value == LoginMethod.OAuth)
            {
                // OAuth login flow
                Log.Information("User selected OAuth login");
                try
                {
                    var authManager = Services.AuthenticationManager.Instance;
                    isAuthenticated = await authManager.LoginAsync();
                    
                    if (isAuthenticated)
                    {
                        Log.Information("OAuth authentication successful");
                    }
                    else
                    {
                        Log.Warning("OAuth authentication failed");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error during OAuth authentication");
                    isAuthenticated = false;
                }
            }
            else
            {
                // Traditional login flow
                Log.Information("User selected traditional login");
                var authWindow = new AuthWindow
                {
                    DataContext = new AuthViewModel()
                };

                // Handle authentication result
                var authCompleted = new TaskCompletionSource<bool>();
                authWindow.Closed += (s, e) =>
                {
                    if (authWindow.DataContext is AuthViewModel vm && vm.IsAuthenticated)
                    {
                        authCompleted.TrySetResult(true);
                    }
                    else
                    {
                        authCompleted.TrySetResult(false);
                    }
                };

                authWindow.Show();
                isAuthenticated = await authCompleted.Task;
            }

            if (isAuthenticated)
            {
                // Create main window
                _mainWindow = new MainWindow();
                var mainViewModel = new MainWindowViewModel();
                _mainWindow.DataContext = mainViewModel;

                desktop.MainWindow = _mainWindow;
                _mainWindow.Show();
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
    
        
    
       

        
    

    public override void OnFrameworkInitializationCompleted()
    {
        var isMauiHost = AppContext.GetData("MAUI_HOST") as bool? == true;

        if (!isMauiHost && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
          try
            {
#if DEBUG
                Log.Debug("Showing under construction window for debug build");
                var underConstructionWindow = new UnderConstructionWindow();
                
                // Set up handler for when under construction window closes
                underConstructionWindow.Closed += async (s, e) =>
                {
                    Log.Debug("Under construction window closed, showing splash screen");
                    await ShowSplashScreenAndInitialize(desktop);
                };
                
                underConstructionWindow.Show();
#else
                Log.Debug("Creating splash screen");
                // Create and show the splash screen directly in release mode
                // Fire-and-forget: OnFrameworkInitializationCompleted cannot be async
                _ = ShowSplashScreenAndInitialize(desktop);
#endif

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during application initialization");
                desktop.Shutdown();
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            // MAUI single-view targets: Browser/WASM, iOS.
            // The full desktop startup flow (splash, multi-window, OAuth) does NOT run here.
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            // Android via MAUI.
            // The full desktop startup flow does NOT run here.
            activityLifetime.MainViewFactory = () => new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
