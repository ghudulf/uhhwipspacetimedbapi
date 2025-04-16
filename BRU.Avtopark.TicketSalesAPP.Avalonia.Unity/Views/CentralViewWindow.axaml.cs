using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using Material.Icons;
using Reactive.Bindings;
using ReDocking;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class CentralViewWindow : Window
{
    private readonly CentralViewWindowViewModel _viewModel;

    private Button? _minimizeButton;
    private Button? _maximizeButton;
    private Path? _maximizeIcon;
    private Button? _closeButton;
    private Grid? _titleBarDragArea;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public CentralViewWindow()
    {
        ExtendClientAreaChromeHints =
            ExtendClientAreaChromeHints.SystemChrome | ExtendClientAreaChromeHints.OSXThickTitleBar;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 40;
        _viewModel = new CentralViewWindowViewModel();
        _viewModel.FloatingWindows.CollectionChanged += FloatingWindowsOnCollectionChanged;
        DataContext = _viewModel;

        InitializeComponent();
        
        // Setup title bar after components are initialized
        SetupTitleBar();
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
        if (DataContext is not CentralViewWindowViewModel viewModel) return;
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

    internal static ReactiveCollection<ToolWindowViewModel> GetItemsSource(CentralViewWindowViewModel viewModel,
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

    private static ReactiveProperty<ToolWindowViewModel?> GetSelectedItem(CentralViewWindowViewModel viewModel,
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
        if (DataContext is not CentralViewWindowViewModel viewModel) return;
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
        e.Handled = true;
        var flyout = new CustomSideBarButtonMenuFlyout(e.DockHost);
        if (e.Button.DockLocation?.LeftRight == SideBarLocation.Left)
        {
            flyout.Placement = PlacementMode.Right;
        }
        else
        {
            flyout.Placement = PlacementMode.Left;
        }

        flyout.ShowAt(e.Button);
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
            _maximizeButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

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
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void SubscribeToWindowState()
    {
        this.GetObservable(WindowStateProperty).Subscribe(s =>
        {
            if (s != WindowState.Maximized)
            {
                if (_maximizeIcon != null)
                    _maximizeIcon.Data = Geometry.Parse("M2048 2048v-2048h-2048v2048h2048zM1843 1843h-1638v-1638h1638v1638z");

                if (_maximizeButton != null)
                    _maximizeButton.SetValue(ToolTip.TipProperty, "Развернуть");

                Padding = new Thickness(0, 0, 0, 0);
            }
            if (s == WindowState.Maximized)
            {
                if (_maximizeIcon != null)
                    _maximizeIcon.Data = Geometry.Parse("M2048 1638h-410v410h-1638v-1638h410v-410h1638v1638zm-614-1024h-1229v1229h1229v-1229zm409-409h-1229v205h1024v1024h205v-1229z");

                if (_maximizeButton != null)
                    _maximizeButton.SetValue(ToolTip.TipProperty, "Восстановить");

                Padding = new Thickness(7, 7, 7, 7);
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
                var vm = DataContext as ViewModels.MainWindowViewModel;
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
        var aboutWindow = new AboutWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        await aboutWindow.ShowDialog(this);
    }

    private async void HelpButton_Click(object? sender, RoutedEventArgs e)
    {
        var helpWindow = new HelpWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        await helpWindow.ShowDialog(this);
    }
}
