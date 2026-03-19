using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using Serilog;
using System;
using System.ComponentModel;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Controls;

/// <summary>
/// Reusable Avalonia UserControl that hosts the full traditional login wizard
/// (username/password → validation → 2FA → success).
/// Extracted from AuthWindow so it can be embedded in both standalone Avalonia
/// Windows and MAUI pages via AvaloniaView.
///
/// Raises <see cref="AuthCompleted"/> when the flow finishes.
/// </summary>
public partial class TraditionalLoginControl : UserControl
{
    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Raised on the UI thread when login completes (success=true) or is cancelled.</summary>
    public event EventHandler<bool>? AuthCompleted;

    // ── Private state ────────────────────────────────────────────────────

    private AuthViewModel? _vm;
    private ContentControl? _stepContentArea;
    private Button? _nextButton;
    private Button? _backButton;
    private Button? _cancelButton;
    private TextBlock? _stepTitleText;
    private TextBlock? _progressText;
    private TextBlock? _statusText;

    // ── Constructor ──────────────────────────────────────────────────────

    public TraditionalLoginControl()
    {
        InitializeComponent();

        _stepContentArea = this.FindControl<ContentControl>("StepContentArea");
        _nextButton = this.FindControl<Button>("NextButton");
        _backButton = this.FindControl<Button>("BackButton");
        _cancelButton = this.FindControl<Button>("CancelButton");
        _stepTitleText = this.FindControl<TextBlock>("StepTitleText");
        _progressText = this.FindControl<TextBlock>("ProgressText");
        _statusText = this.FindControl<TextBlock>("StatusText");

        _vm = new AuthViewModel();
        DataContext = _vm;

        _vm.LoginCompleted += OnLoginCompleted;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        UpdateStepContent(_vm.CurrentStep);
        SyncHeaderAndButtons();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ── ViewModel event handlers ─────────────────────────────────────────

    private void OnLoginCompleted(object? sender, bool success)
    {
        Log.Information("[TraditionalLoginControl] LoginCompleted: {Success}", success);
        Dispatcher.UIThread.Post(() => AuthCompleted?.Invoke(this, success));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuthViewModel.CurrentStep))
        {
            UpdateStepContent(_vm!.CurrentStep);
            SyncHeaderAndButtons();
        }
        else if (e.PropertyName is nameof(AuthViewModel.CanGoBack)
                                 or nameof(AuthViewModel.CanGoForward)
                                 or nameof(AuthViewModel.IsLoading)
                                 or nameof(AuthViewModel.NextButtonText)
                                 or nameof(AuthViewModel.StatusMessage)
                                 or nameof(AuthViewModel.StepTitle)
                                 or nameof(AuthViewModel.ProgressText))
        {
            SyncHeaderAndButtons();
        }
    }

    // ── Step content ─────────────────────────────────────────────────────

    private void UpdateStepContent(int step)
    {
        if (_stepContentArea == null || _vm == null) return;

        // Delegate to the same factory methods used by AuthWindow
        // so the UI is identical whether hosted in a Window or embedded via AvaloniaView.
        // We create a temporary AuthWindow instance purely to call its factory methods.
        // This avoids duplicating the complex step-content construction logic.
        // The window is never shown — it's used only as a factory.
        try
        {
            var factory = new AuthWindowContentFactory(_vm);
            _stepContentArea.Content = step switch
            {
                1 => factory.CreateLoginStep(),
                2 => factory.CreateAuthorizationStep(),
                3 => factory.CreateTwoFactorStep(),
                4 => factory.CreateSuccessStep(),
                _ => new TextBlock { Text = $"Неизвестный шаг: {step}" }
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[TraditionalLoginControl] Error creating step {Step} content", step);
            _stepContentArea.Content = new TextBlock { Text = $"Ошибка рендеринга шага {step}" };
        }
    }

    private void SyncHeaderAndButtons()
    {
        if (_vm == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_stepTitleText != null) _stepTitleText.Text = _vm.StepTitle;
            if (_progressText != null) _progressText.Text = _vm.ProgressText;
            if (_statusText != null) _statusText.Text = _vm.StatusMessage;
            if (_nextButton != null)
            {
                _nextButton.IsEnabled = _vm.CanGoForward;
                _nextButton.Content = _vm.NextButtonText;
            }
            if (_backButton != null) _backButton.IsEnabled = _vm.CanGoBack;
            if (_cancelButton != null) _cancelButton.IsEnabled = !_vm.IsLoading;
        });
    }

    // ── Button handlers ──────────────────────────────────────────────────

    private void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm?.GoToNextStepCommand.CanExecute(null) == true)
            _vm.GoToNextStepCommand.Execute(null);
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm?.GoToPreviousStepCommand.CanExecute(null) == true)
            _vm.GoToPreviousStepCommand.Execute(null);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Log.Information("[TraditionalLoginControl] User cancelled");
        AuthCompleted?.Invoke(this, false);
    }
}
