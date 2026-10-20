using System.ComponentModel;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Pages;

public partial class RoutesPage : ContentPage
{
    private RouteManagementViewModel? _viewModel;
    private CancellationTokenSource? _appearance;

    public RoutesPage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _appearance?.Cancel();
        _appearance = new CancellationTokenSource();
        _ = InitialiseAvaloniaViewAsync(_appearance.Token);
    }

    protected override void OnDisappearing()
    {
        _appearance?.Cancel();
        _appearance?.Dispose();
        _appearance = null;
        var previous = _viewModel;
        if (previous != null)
        {
            previous.PropertyChanged -= StateChanged;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(previous.Deactivate);
        }
        _viewModel = null;
        base.OnDisappearing();
    }

    private async Task InitialiseAvaloniaViewAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ready = await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync<Task>(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var viewModel = RouteManagementControl.DataContext as RouteManagementViewModel
                    ?? throw new InvalidOperationException("Route view model was not initialized.");
                if (_viewModel != null) _viewModel.PropertyChanged -= StateChanged;
                _viewModel = viewModel;
                viewModel.PropertyChanged += StateChanged;
                await viewModel.ActivateAsync();
                cancellationToken.ThrowIfCancellationRequested();
                UpdateOverlay(viewModel);
            });
            await ready;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning("Route host initialization failed: {ExceptionType}", ex.GetType().Name);
            if (cancellationToken.IsCancellationRequested) return;
            Dispatcher.Dispatch(() =>
            {
                LoadingOverlay.IsVisible = false;
                ErrorLabel.Text = "Не удалось загрузить маршруты. Проверьте соединение и повторите.";
                ErrorOverlay.IsVisible = true;
            });
        }
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is RouteManagementViewModel viewModel && args.PropertyName is nameof(RouteManagementViewModel.IsBusy)
            or nameof(RouteManagementViewModel.HasError) or nameof(RouteManagementViewModel.ErrorMessage)) UpdateOverlay(viewModel);
    }

    private void UpdateOverlay(RouteManagementViewModel viewModel)
    {
        var busy = viewModel.IsBusy;
        var error = viewModel.HasError;
        var message = viewModel.ErrorMessage;
        Dispatcher.Dispatch(() =>
        {
            if (_appearance?.IsCancellationRequested != false || !ReferenceEquals(_viewModel, viewModel)) return;
            LoadingOverlay.IsVisible = busy;
            ErrorOverlay.IsVisible = !busy && error;
            ErrorLabel.Text = message;
        });
    }

    private void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_viewModel is not { } viewModel) return;
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => viewModel.LoadDataCommand.Execute(null));
    }
}
