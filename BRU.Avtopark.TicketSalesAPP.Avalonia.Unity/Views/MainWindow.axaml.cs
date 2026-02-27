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
using Material.Icons;
using Reactive.Bindings;
using ReDocking;
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
    
    // Command buttons
    private Button? _okButton;
    private Button? _exitButton;
    private Button? _helpButton;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindow()
    {
        ExtendClientAreaChromeHints =
            ExtendClientAreaChromeHints.SystemChrome | ExtendClientAreaChromeHints.OSXThickTitleBar;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 22;
        _viewModel = new MainWindowViewModel();
        _viewModel.FloatingWindows.CollectionChanged += FloatingWindowsOnCollectionChanged;
        DataContext = _viewModel;

        InitializeComponent();
        
        // Setup title bar after components are initialized
        SetupTitleBar();
        SetupUtilityButtons();
        SetupCommandButtons();
        SubscribeToWindowState();
    }

    private void FloatingWindowsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            foreach (ToolWindowViewModel item in e.NewItems!)
            {
                _ = new ToolWindow(item, this);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            foreach (ToolWindowViewModel item in e.OldItems!)
            {
                this.OwnedWindows.FirstOrDefault(x => x.DataContext == item)?.Close();
            }
        }
    }

    private void OnSideBarButtonDrop(object? sender, SideBarButtonMoveEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        var oldItems = GetItemsSource(viewModel, e.SourceLocation);
        var oldSelectedItem = GetSelectedItem(viewModel, e.SourceLocation);
        var newItems = GetItemsSource(viewModel, e.DestinationLocation);

        if (e.Item is not ToolWindowViewModel item)
        {
            return;
        }

        if (oldSelectedItem.Value == item)
        {
            oldSelectedItem.Value = null;
        }

        if (oldItems == newItems)
        {
            var sourceIndex = oldItems.IndexOf(item);
            var destinationIndex = e.DestinationIndex;
            if (sourceIndex < destinationIndex)
            {
                destinationIndex--;
            }
            try
            {
                oldItems.Move(sourceIndex, destinationIndex);
                item.IsSelected.Value = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        else
        {
            oldItems.Remove(item);
            var newItem = new ToolWindowViewModel(item.Name.Value, item.Icon.Value, item.Content.Value);
            newItems.Insert(e.DestinationIndex, newItem);
            newItem.IsSelected.Value = true;
        }

        e.Handled = true;
    }

    internal static ReactiveCollection<ToolWindowViewModel> GetItemsSource(MainWindowViewModel viewModel,
        DockAreaLocation location)
    {
        return (location.ButtonLocation, location.LeftRight) switch
        {
            (SideBarButtonLocation.UpperTop, SideBarLocation.Left) => viewModel.LeftUpperTopTools,
            (SideBarButtonLocation.UpperBottom, SideBarLocation.Left) => viewModel.LeftUpperBottomTools,
            (SideBarButtonLocation.LowerTop, SideBarLocation.Left) => viewModel.LeftLowerTopTools,
            (SideBarButtonLocation.LowerBottom, SideBarLocation.Left) => viewModel.LeftLowerBottomTools,
            (SideBarButtonLocation.UpperTop, SideBarLocation.Right) => viewModel.RightUpperTopTools,
            (SideBarButtonLocation.UpperBottom, SideBarLocation.Right) => viewModel.RightUpperBottomTools,
            (SideBarButtonLocation.LowerTop, SideBarLocation.Right) => viewModel.RightLowerTopTools,
            (SideBarButtonLocation.LowerBottom, SideBarLocation.Right) => viewModel.RightLowerBottomTools,
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null)
        };
    }

    private static ReactiveProperty<ToolWindowViewModel?> GetSelectedItem(MainWindowViewModel viewModel,
        DockAreaLocation location)
    {
        return (location.ButtonLocation, location.LeftRight) switch
        {
            (SideBarButtonLocation.UpperTop, SideBarLocation.Left) => viewModel.SelectedLeftUpperTopTool,
            (SideBarButtonLocation.UpperBottom, SideBarLocation.Left) => viewModel.SelectedLeftUpperBottomTool,
            (SideBarButtonLocation.LowerTop, SideBarLocation.Left) => viewModel.SelectedLeftLowerTopTool,
            (SideBarButtonLocation.LowerBottom, SideBarLocation.Left) => viewModel.SelectedLeftLowerBottomTool,
            (SideBarButtonLocation.UpperTop, SideBarLocation.Right) => viewModel.SelectedRightUpperTopTool,
            (SideBarButtonLocation.UpperBottom, SideBarLocation.Right) => viewModel.SelectedRightUpperBottomTool,
            (SideBarButtonLocation.LowerTop, SideBarLocation.Right) => viewModel.SelectedRightLowerTopTool,
            (SideBarButtonLocation.LowerBottom, SideBarLocation.Right) => viewModel.SelectedRightLowerBottomTool,
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null)
        };
    }

    private void OnSideBarButtonDisplayModeChanged(object? sender, SideBarButtonDisplayModeChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        if (e.Item is not ToolWindowViewModel item || item.DisplayMode.Value == e.DisplayMode) return;
        item.IsSelected.Value = false;
        item.DisplayMode.Value = e.DisplayMode;
        item.IsSelected.Value = true;
        if (e.DisplayMode == DockableDisplayMode.Floating)
        {
            viewModel.FloatingWindows.Add(item);
        }
        else
        {
            viewModel.FloatingWindows.Remove(item);
        }

        e.Handled = true;
    }

    private void OnButtonFlyoutRequested(object? sender, SideBarButtonFlyoutRequestedEventArgs e)
    {
        
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
        if (e.Data.Contains(DataFormats.Text))
        {
            e.DragEffects = DragDropEffects.Move;
        }
    }

    private void Drop(object sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Text))
        {
            var window = e.Data.Get(DataFormats.Text) as Window;
            if (window != null)
            {
                var vm = DataContext as BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels.MainWindowViewModel;
                if (vm != null)
                {
                    // Extract content and create tab
                    var content = window.Content as Control;
                    window.Content = null;
                    window.Hide();

                    
                }
            }
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var aboutButton = this.FindControl<Button>("AboutButton");
        var helpButton = this.FindControl<Button>("HelpButton");

        if (aboutButton != null)
            aboutButton.Click += AboutButton_Click;

        if (helpButton != null)
            helpButton.Click += HelpButton_Click;
    }

    private async void AboutButton_Click(object? sender, RoutedEventArgs e)
    {
       
    }

    private async void HelpButton_Click(object? sender, RoutedEventArgs e)
    {
        // Show help dialog or documentation
        // Example: new HelpWindow().ShowDialog(this);
    }

    private void SetupUtilityButtons()
    {
        // Connect utility buttons from XAML
        _runEmployeeManagementButton = this.FindControl<Button>("RunEmployeeManagementButton");
        _runBusManagementButton = this.FindControl<Button>("RunBusManagementButton");
        _runRouteManagementButton = this.FindControl<Button>("RunRouteManagementButton");
        _runTicketSalesButton = this.FindControl<Button>("RunTicketSalesButton");
        _runMaintenanceButton = this.FindControl<Button>("RunMaintenanceButton");
        _runReportsButton = this.FindControl<Button>("RunReportsButton");
        _openCentralViewButton = this.FindControl<Button>("OpenCentralViewButton");
        _systemSettingsButton = this.FindControl<Button>("SystemSettingsButton");
        _createBackupButton = this.FindControl<Button>("CreateBackupButton");
        
        // Attach event handlers to utility buttons
        if (_runEmployeeManagementButton != null)
            _runEmployeeManagementButton.Click += RunEmployeeManagement_Click;
            
        if (_runBusManagementButton != null)
            _runBusManagementButton.Click += RunBusManagement_Click;
            
        if (_runRouteManagementButton != null)
            _runRouteManagementButton.Click += RunRouteManagement_Click;
            
        if (_runTicketSalesButton != null)
            _runTicketSalesButton.Click += RunTicketSales_Click;
            
        if (_runMaintenanceButton != null)
            _runMaintenanceButton.Click += RunMaintenance_Click;
            
        if (_runReportsButton != null)
            _runReportsButton.Click += RunReports_Click;
            
        if (_openCentralViewButton != null)
            _openCentralViewButton.Click += OpenCentralView_Click;
            
        if (_systemSettingsButton != null)
            _systemSettingsButton.Click += SystemSettings_Click;
            
        if (_createBackupButton != null)
            _createBackupButton.Click += CreateBackup_Click;
    }
    
    private void SetupCommandButtons()
    {
        // Connect command buttons from XAML
        _okButton = this.FindControl<Button>("OKButton");
        _exitButton = this.FindControl<Button>("ExitButton");
        _helpButton = this.FindControl<Button>("HelpButton");
        
        // Attach event handlers to command buttons
        if (_okButton != null)
            _okButton.Click += OKButton_Click;
            
        if (_exitButton != null)
            _exitButton.Click += ExitButton_Click;
            
        if (_helpButton != null)
            _helpButton.Click += HelpButton_Click;
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
            WindowStartupLocation = WindowStartupLocation.CenterScreen
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

    // Event handlers for command buttons
    private void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        // Default action - could launch the selected utility
    }
    
    private void ExitButton_Click(object? sender, RoutedEventArgs e)
    {
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
    
}
