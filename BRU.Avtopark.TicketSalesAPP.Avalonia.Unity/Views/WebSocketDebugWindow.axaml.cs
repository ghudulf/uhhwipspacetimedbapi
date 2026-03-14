using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using System.Collections.Specialized;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

public partial class WebSocketDebugWindow : Window
{
    private readonly WebSocketDebugViewModel _viewModel;
    private ScrollViewer? _eventLogScrollViewer;

    public WebSocketDebugWindow()
    {
        InitializeComponent();
        _viewModel = new WebSocketDebugViewModel();
        DataContext = _viewModel;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        // Find the ScrollViewer for auto-scroll
        _eventLogScrollViewer = this.FindControl<ScrollViewer>("EventLogScrollViewer");
        
        // Subscribe to EventLog changes for auto-scroll
        if (_viewModel.EventLog is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += EventLog_CollectionChanged;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        
        // Unsubscribe from collection changes
        if (_viewModel.EventLog is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged -= EventLog_CollectionChanged;
        }
        
        // Clean up WebSocket resources with proper error handling
        var disconnectTask = _viewModel.DisconnectWebSocketCommand.ExecuteAsync(null);
        disconnectTask.ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                // Log the exception (in production, use proper logging)
                System.Diagnostics.Debug.WriteLine($"Error during WebSocket disconnect: {t.Exception.InnerException?.Message ?? t.Exception.Message}");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
        
        _viewModel.Dispose();
    }

    private void EventLog_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _eventLogScrollViewer != null)
        {
            // Scroll to bottom when new items are added
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _eventLogScrollViewer.ScrollToEnd();
            });
        }
    }
}
