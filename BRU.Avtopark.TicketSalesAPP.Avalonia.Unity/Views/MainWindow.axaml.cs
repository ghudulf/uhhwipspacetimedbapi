using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using Material.Icons;
using Reactive.Bindings;
 
using System;
using System.Collections.Specialized;
using System.Linq;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    private Button? _minimizeButton;
    private Button? _maximizeButton;
    private Path? _maximizeIcon;
    private Button? _closeButton;
    private Grid? _titleBarDragArea;
    
    // Main utility buttons
    private Button? _runEmployeeManagementButton;
    private Button? _runBusManagementButton;
    private Button? _runRouteManagementButton;
    private Button? _runTicketSalesButton;
    private Button? _runMaintenanceButton;
    private Button? _runReportsButton;
    private Button? _openCentralViewButton;
    private Button? _systemSettingsButton;
    private Button? _createBackupButton;
    private Button? _testTokenButton;
    private Button? _webSocketDebugButton;
    
    // Command buttons
    private Button? _okButton;
    private Button? _exitButton;
    private Button? _logoutAndExitButton;
    private Button? _helpButton;

    // WebSocket debug window singleton
    private WebSocketDebugWindow? _webSocketDebugWindow;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindow()
    {
        WindowDecorations = WindowDecorations.None;
        _viewModel = new MainWindowViewModel();
        
        DataContext = _viewModel;

        InitializeComponent();
        
        // Setup title bar after components are initialized
        SetupTitleBar();
        
        SubscribeToWindowState();
    }

     
    private void SetupTitleBar()
    {
        _minimizeButton = this.FindControl<Button>("MinimizeButton");
        _maximizeButton = this.FindControl<Button>("MaximizeButton");
        _maximizeIcon = this.FindControl<Path>("MaximizeIcon");
        _closeButton = this.FindControl<Button>("CloseButton");
        _titleBarDragArea = this.FindControl<Grid>("TitleBarDragArea");

        if (_minimizeButton != null)
            _minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;

        if (_maximizeButton != null)
            _maximizeButton.Click += (_, _) => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

        if (_closeButton != null)
            _closeButton.Click += (_, _) => Close();

        if (_titleBarDragArea != null)
        {
            _titleBarDragArea.PointerPressed += TitleBarDragArea_PointerPressed;
            _titleBarDragArea.DoubleTapped += TitleBarDragArea_DoubleTapped;
        }
    }

    private void TitleBarDragArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void TitleBarDragArea_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    private void SubscribeToWindowState()
    {
        this.GetObservable(WindowStateProperty).Subscribe(s =>
        {
            if (s != WindowState.FullScreen)
            {
                if (_maximizeIcon != null)
                    _maximizeIcon.Data = Geometry.Parse("M0 0 H8 V8 H0 Z M0 1 H8 M1 0 V8");

                if (_maximizeButton != null)
                    _maximizeButton.SetValue(ToolTip.TipProperty, "Развернуть");

                Padding = new Thickness(0, 0, 0, 0);
            }
            if (s == WindowState.FullScreen)
            {
                if (_maximizeIcon != null)
                    _maximizeIcon.Data = Geometry.Parse("M0 2 H6 V8 H0 Z M2 0 H8 V6 H2 Z M2 2 H6 V6 H2 Z");

                if (_maximizeButton != null)
                    _maximizeButton.SetValue(ToolTip.TipProperty, "Восстановить");

                Padding = new Thickness(0,0,0,0);
            }
        });
    }

    private void DragOver(object sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.Text))
        {
            e.DragEffects = DragDropEffects.Move;
        }
    }

    private void Drop(object sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.Text))
        {
            var text = e.DataTransfer.TryGetText();
            // Drag-and-drop text handling placeholder
            _ = text;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

    }  

    private async void AboutButton_Click(object? sender, RoutedEventArgs e)
    {
       
    }

    private async void HelpButton_Click(object? sender, RoutedEventArgs e)
    {
        // Show help dialog or documentation
        // Example: new HelpWindow().ShowDialog(this);
    }

    

    // Event handlers for utility buttons
    private void RunEmployeeManagement_Click(object? sender, RoutedEventArgs e)
    {
        // Open Employee Management window/wizard
        // Example: new EmployeeManagementWindow().Show();
    }
    
    private void RunBusManagement_Click(object? sender, RoutedEventArgs e)
    {
        // Open Bus Management window/wizard
        // Example: new BusManagementWindow().Show();
    }
    
    private void RunRouteManagement_Click(object? sender, RoutedEventArgs e)
    {
        // Open Route Management window/wizard
        // Example: new RouteManagementWindow().Show();
    }
    
    private void RunTicketSales_Click(object? sender, RoutedEventArgs e)
    {
        // Open Ticket Sales (POS) window
        // Example: new TicketSalesWindow().Show();
    }
    
    private void RunMaintenance_Click(object? sender, RoutedEventArgs e)
    {
        // Open Maintenance window/wizard
        // Example: new MaintenanceWindow().Show();
    }
    
    private void RunReports_Click(object? sender, RoutedEventArgs e)
    {
        // Open Reports window
        // Example: new ReportsWindow().Show();
    }
    
    private void OpenCentralView_Click(object? sender, RoutedEventArgs e)
    {
        // Create and show the Central View window
        var centralViewWindow = new CentralViewWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            DataContext = new BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels.CentralViewWindowViewModel()
        };
        
        centralViewWindow.Show();
        
        // Hide the current window (launcher)
        this.Hide();
        
        // When the central view is closed, show this window again
        centralViewWindow.Closed += (_, _) => this.Show();
    }
    
    private void SystemSettings_Click(object? sender, RoutedEventArgs e)
    {
        // Open System Settings window/wizard
        // Example: new SystemSettingsWindow().Show();
    }
    
    private void CreateBackup_Click(object? sender, RoutedEventArgs e)
    {
        // Open Backup window/wizard
        // Example: new BackupWindow().Show();
    }

    private async void TestToken_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Serilog.Log.Information("Testing token via debug endpoint");
            
            var apiClient = ApiClientService.Instance;
            var httpClient = apiClient.CreateClient();
            httpClient.BaseAddress = new Uri("http://localhost:5000/");
            var response = await httpClient.GetAsync("debug/tokentest");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Serilog.Log.Information("Token test response: {Response}", content);
                
                // Show dialog with results
                var dialog = new Window
                {
                    Title = "Результат Теста Токена",
                    Width = 600,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new ScrollViewer
                    {
                        Content = new TextBox
                        {
                            Text = content,
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(10)
                        }
                    }
                };
                
                await dialog.ShowDialog(this);
            }
            else
            {
                Serilog.Log.Warning("Token test failed with status: {Status}", response.StatusCode);
                
                var dialog = new Window
                {
                    Title = "Ошибка Теста Токена",
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Children =
                        {
                            new TextBlock 
                            { 
                                Text = $"Ошибка: {response.StatusCode}",
                                FontSize = 14,
                                Margin = new Thickness(0, 0, 0, 10)
                            },
                            new TextBlock 
                            { 
                                Text = await response.Content.ReadAsStringAsync(),
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    }
                };
                
                await dialog.ShowDialog(this);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error testing token");
            
            var dialog = new Window
            {
                Title = "Ошибка",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock 
                        { 
                            Text = "Ошибка при тестировании токена:",
                            FontSize = 14,
                            Margin = new Thickness(0, 0, 0, 10)
                        },
                        new TextBlock 
                        { 
                            Text = ex.Message,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
            
            await dialog.ShowDialog(this);
        }
    }

    // Event handlers for command buttons
    private void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        // Default action - could launch the selected utility
    }
    
    private void ExitButton_Click(object? sender, RoutedEventArgs e)
    {
        // Just close without logout - user stays logged in for next session
        Close();
        
        // Get the current application instance
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Shutdown the entire application
            desktop.Shutdown();
        }
        else
        {
            // Fallback method if the above doesn't work
            Environment.Exit(0);
        }
    }
    
    private async void LogoutAndExitButton_Click(object? sender, RoutedEventArgs e)
    {
        // Logout and clear all tokens before exiting
        try
        {
            Serilog.Log.Information("User clicked Logout and Exit");
            await AuthenticationManager.Instance.LogoutAsync();
            Serilog.Log.Information("User logged out successfully");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error during logout");
        }
        
        // Close this window
        Close();
        
        // Get the current application instance
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Shutdown the entire application
            desktop.Shutdown();
        }
        else
        {
            // Fallback method if the above doesn't work
            Environment.Exit(0);
        }
    }

    private void OpenWebSocketDebug_Click(object? sender, RoutedEventArgs e)
    {
        // Reuse existing window if it's still open (including minimized state)
        if (_webSocketDebugWindow == null)
        {
            _webSocketDebugWindow = new WebSocketDebugWindow();
            _webSocketDebugWindow.Closed += (s, args) => _webSocketDebugWindow = null;
        }

        _webSocketDebugWindow.Show();
        _webSocketDebugWindow.Activate();
    }
}