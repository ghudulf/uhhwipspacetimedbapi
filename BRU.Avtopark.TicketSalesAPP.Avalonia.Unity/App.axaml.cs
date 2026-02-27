using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Serilog;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using AvaloniaWebView;

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
        
        // Initialize WebView.Avalonia
        AvaloniaWebViewBuilder.Initialize(default);
        Log.Information("WebView.Avalonia initialized");
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
    
        
    
       

        
    

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }


    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
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
                await ShowSplashScreenAndInitialize(desktop);
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
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
