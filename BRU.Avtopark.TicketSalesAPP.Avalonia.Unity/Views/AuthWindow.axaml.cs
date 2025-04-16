using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Data.Converters; // For IValueConverter
using Avalonia.Data; // For Binding
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using Material.Icons.Avalonia; // Keep if MaterialIcon is used
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;
using System;
using System.Globalization; // For CultureInfo
using System.Threading.Tasks; // For Task
using System.Reactive.Disposables;
using System.ComponentModel;
using Avalonia.Threading;
using System.Diagnostics;
using System.Linq;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views
{
public partial class AuthWindow : Window
{
        private AuthViewModel? _viewModel; // Nullable viewmodel reference
        private CompositeDisposable _disposables;
        private ContentControl? _stepContentArea;
        private Button? _nextButton;
        private Button? _backButton;
        private Button? _cancelButton;

    public AuthWindow()
    {
        Log.Information("AuthWindow constructor started");
        InitializeComponent();
#if DEBUG
            this.AttachDevTools();
            Log.Debug("DEBUG: DevTools attached to AuthWindow");
#endif
            _disposables = new CompositeDisposable();
            Log.Debug("CompositeDisposable initialized");

            // Register this window instance with the converter
            StepToContentConverter.RegisterWindow(this);
            Log.Information("Window registered with StepToContentConverter");

            // Initialize view model - Ensure DataContext is set correctly
            _viewModel = new AuthViewModel();
            DataContext = _viewModel;
            Log.Information("AuthViewModel created and set as DataContext");

            // Find important controls
            _stepContentArea = this.FindControl<ContentControl>("StepContentArea");
            Log.Debug("StepContentArea found: {Found}", _stepContentArea != null);
            
            var titleBar = this.FindControl<Panel>("TitleBarDragArea");
            Log.Debug("TitleBarDragArea found: {Found}", titleBar != null);
            
            var closeButton = this.FindControl<Button>("CloseButton");
            Log.Debug("CloseButton found: {Found}", closeButton != null);
            
            var minimizeButton = this.FindControl<Button>("MinimizeButton");
            Log.Debug("MinimizeButton found: {Found}", minimizeButton != null);
            
            _nextButton = this.FindControl<Button>("NextButton");
            if (_nextButton != null)
            {
                // Don't clear the command binding from XAML
                Log.Debug("NextButton found");
            }
            else
            {
                Log.Warning("NextButton not found - navigation may not work correctly");
            }
            
            _backButton = this.FindControl<Button>("BackButton");
            if (_backButton != null)
            {
                // Don't clear the command binding from XAML
                Log.Debug("BackButton found");
            }
            else
            {
                Log.Warning("BackButton not found - navigation may not work correctly");
            }
            
            _cancelButton = this.FindControl<Button>("CancelButton");
            if (_cancelButton != null)
            {
                // Don't clear the command binding from XAML
                Log.Debug("CancelButton found");
            }
            else
            {
                Log.Warning("CancelButton not found - navigation may not work correctly");
            }

            // Log button discovery
            Log.Debug("Found buttons: NextButton={Next}, BackButton={Back}, CancelButton={Cancel}", 
                _nextButton != null, _backButton != null, _cancelButton != null);

            // Set up title bar events
            if (titleBar != null) {
                titleBar.PointerPressed += TitleBar_PointerPressed;
                Log.Debug("TitleBar_PointerPressed event handler attached");
            } else {
                Log.Warning("Could not attach TitleBar_PointerPressed handler - titleBar is null");
            }
            
            if (closeButton != null) {
                closeButton.Click += CloseButton_Click;
                Log.Debug("CloseButton_Click event handler attached");
            } else {
                Log.Warning("Could not attach CloseButton_Click handler - closeButton is null");
            }
            
            if (minimizeButton != null) {
                minimizeButton.Click += MinimizeButton_Click;
                Log.Debug("MinimizeButton_Click event handler attached");
            } else {
                Log.Warning("Could not attach MinimizeButton_Click handler - minimizeButton is null");
            }

            // Subscribe to the ViewModel's event to handle completion
            if (_viewModel != null)
            {
                _viewModel.LoginCompleted += ViewModel_LoginCompleted;
                Log.Debug("LoginCompleted event handler attached to ViewModel");
                
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                Log.Debug("PropertyChanged event handler attached to ViewModel");
                
                // Manually update button states initially
                UpdateButtonStates();
                Log.Debug("Initial button states updated");
            }
            else
            {
                Log.Error("CRITICAL: ViewModel is null after initialization");
            }

            Log.Information("AuthWindow constructor completed successfully");
        }

        protected override void OnOpened(EventArgs e)
        {
            Log.Information("AuthWindow.OnOpened called");
            base.OnOpened(e);
            
            PositionWindowInTopLeftCorner();
            Log.Debug("Window positioned in top-left corner");
            
            int currentStep = _viewModel?.CurrentStep ?? 1;
            Log.Debug("Initializing with step {Step}", currentStep);
            UpdateStepContent(currentStep);
            
            UpdateButtonStates(); // Make sure buttons are properly enabled on startup
            Log.Debug("Button states updated on window open");
            
            Log.Information("AuthWindow opened and positioned successfully");
        }

        // Manual button state update
        private void UpdateButtonStates()
        {
            Log.Debug("View.UpdateButtonStates called - Manually updating button controls from ViewModel state");
            if (_viewModel == null) {
                Log.Warning("View.UpdateButtonStates: ViewModel is null, cannot update buttons");
                return;
            }

            // Ensure updates happen on the UI thread
            Dispatcher.UIThread.Post(() =>
            {
                Log.Debug("View.UpdateButtonStates: Updating button states on UI thread");

                if (_nextButton != null)
                {
                    bool vmCanGoForward = _viewModel.CanGoForward;
                    string vmNextText = _viewModel.NextButtonText;
                    
                    bool wasEnabled = _nextButton.IsEnabled;
                    string? oldContent = _nextButton.Content?.ToString();

                    _nextButton.IsEnabled = vmCanGoForward;
                    _nextButton.Content = vmNextText;

                    Log.Debug("View.UpdateButtonStates: Next button updated - IsEnabled: {NewEnabled} (was {OldEnabled}), Content: '{NewContent}' (was '{OldContent}')", 
                        _nextButton.IsEnabled, wasEnabled, _nextButton.Content, oldContent ?? "null");
                }
                else
                {
                    Log.Warning("View.UpdateButtonStates: Cannot update NextButton - control is null");
                }

                if (_backButton != null)
                {
                    bool vmCanGoBack = _viewModel.CanGoBack;
                    bool wasEnabled = _backButton.IsEnabled;
                    
                    _backButton.IsEnabled = vmCanGoBack;
                    
                    Log.Debug("View.UpdateButtonStates: Back button updated - IsEnabled: {NewEnabled} (was {OldEnabled})", 
                        _backButton.IsEnabled, wasEnabled);
                }
                else
                {
                    Log.Warning("View.UpdateButtonStates: Cannot update BackButton - control is null");
                }

                if (_cancelButton != null)
                {
                    bool vmIsLoading = _viewModel.IsLoading;
                    bool wasEnabled = _cancelButton.IsEnabled;
                    
                    _cancelButton.IsEnabled = !vmIsLoading; // Cancel is enabled when not loading
                    
                    Log.Debug("View.UpdateButtonStates: Cancel button updated - IsEnabled: {NewEnabled} (was {OldEnabled})", 
                        _cancelButton.IsEnabled, wasEnabled);
                }
                else
                {
                    Log.Warning("View.UpdateButtonStates: Cannot update CancelButton - control is null");
                }

                Log.Debug("View.UpdateButtonStates: Button state update completed on UI thread");
            });

            Log.Debug("View.UpdateButtonStates: Scheduled button update on UI thread");
        }

        /// <summary>
        /// Position the window in the top-left corner of the screen with a small margin
        /// </summary>
        private void PositionWindowInTopLeftCorner()
        {
            Log.Debug("PositionWindowInTopLeftCorner called");
            if (Screens.Primary != null)
            {
                var screen = Screens.Primary.WorkingArea;
                Log.Debug("Primary screen found: WorkingArea={X},{Y},{Width},{Height}", 
                    screen.X, screen.Y, screen.Width, screen.Height);
                
                // Position in the top-left corner with a slight margin for OS/2 style
                var oldPosition = Position;
                Position = new PixelPoint(Math.Max(0, screen.X + 25), Math.Max(0, screen.Y + 25));
                
                Log.Debug("Window position changed from {OldX},{OldY} to {X},{Y}", 
                    oldPosition.X, oldPosition.Y, Position.X, Position.Y);
            }
            else
            {
                Log.Warning("Primary screen not found, cannot auto-position window.");
            }
        }

        /// <summary>
        /// Apply classic OS/2 and ArcaOS style window properties
        /// </summary>
        private void ApplyClassicWindowStyle()
        {
            Log.Debug("ApplyClassicWindowStyle called");
            
            // Ensure window has the correct style
            var oldDecorations = SystemDecorations;
            SystemDecorations = SystemDecorations.BorderOnly;
            Log.Debug("SystemDecorations changed from {Old} to {New}", oldDecorations, SystemDecorations);
            
            // Find the main panels and ensure they have the correct styles
            var centralPanel = this.FindControl<ContentControl>("StepContentArea");
            Log.Debug("StepContentArea found for styling: {Found}", centralPanel != null);
            
            var progressBar = this.FindControl<ProgressBar>(".ClassicProgressBar");
            Log.Debug("ClassicProgressBar found for styling: {Found}", progressBar != null);
            
            if (progressBar != null)
            {
                var oldRadius = progressBar.CornerRadius;
                progressBar.CornerRadius = new CornerRadius(0);
                Log.Debug("ProgressBar corner radius changed from {Old} to {New}", oldRadius, progressBar.CornerRadius);
            }
            
            Log.Information("Classic window style applied successfully");
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            Log.Debug("TitleBar_PointerPressed event triggered");
            // Allow window dragging only with left mouse button
            var point = e.GetCurrentPoint(this);
            bool isLeftButton = point.Properties.IsLeftButtonPressed;
            Log.Debug("Pointer properties: IsLeftButtonPressed={IsLeftButton}, Position={X},{Y}", 
                isLeftButton, point.Position.X, point.Position.Y);
                
            if (isLeftButton)
            {
                Log.Debug("Left button pressed - beginning window drag operation");
                BeginMoveDrag(e);
            }
            else
            {
                Log.Debug("Not left button - ignoring drag attempt");
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Log.Information("Close button clicked");
            if (_viewModel?.CancelCommand.CanExecute(null) == true)
            {
                Log.Debug("CancelCommand is available and enabled, executing it");
                _viewModel.CancelCommand.Execute(null);
            }
            else
            {
                // Fallback direct close
                Log.Warning("CancelCommand not available or disabled, forcing window close");
                Close();
            }
        }

        private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
        {
            Log.Information("Minimize button clicked");
            var oldState = WindowState;
            WindowState = WindowState.Minimized;
            Log.Debug("Window state changed from {OldState} to {NewState}", oldState, WindowState);
        }

        private void ViewModel_LoginCompleted(object? sender, bool success)
        {
            Log.Information("LoginCompleted event received in AuthWindow. Success: {Success}", success);
            
            // This will be handled by App.xaml.cs, just close this window
            Log.Debug("Scheduling window close on UI thread");
            Dispatcher.UIThread.Post(() => {
                Log.Debug("Executing window close from LoginCompleted event");
                Close();
                Log.Information("Window closed after login completion");
            });
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Log.Debug("ViewModel_PropertyChanged: Property '{PropertyName}' changed", e.PropertyName);

            if (_viewModel == null) {
                Log.Warning("ViewModel_PropertyChanged called but _viewModel is null");
                return;
            }

            // Only react to CurrentStep change to update the content area
            if (e.PropertyName == nameof(AuthViewModel.CurrentStep))
            {
                Log.Information("CurrentStep changed to {Step}, updating content and button states", _viewModel.CurrentStep);
                // Update the content displayed
                UpdateStepContent(_viewModel.CurrentStep); 
                // Explicitly update button states AFTER content has been updated for the new step
                UpdateButtonStates(); 
            }
            // IMPORTANT: Removed reactions to CanGoBack, CanGoForward, IsLoading, NextButtonText 
            // to prevent the recursive loop. Button states are now updated explicitly when needed (e.g., after step change).
            else 
            {
                 Log.Debug("ViewModel_PropertyChanged: Ignoring change in property '{PropertyName}' for direct updates", e.PropertyName);
            }
        }

        private void UpdateStepContent(int step)
        {
            Log.Information("UpdateStepContent called for step {Step}", step);
            
            if (_stepContentArea == null)
            {
                Log.Error("Cannot update step content: _stepContentArea is null");
                return;
            }
            
            if (_viewModel == null)
            {
                Log.Error("Cannot update step content: _viewModel is null");
                return;
            }

            Log.Debug("Creating content for step {Step}", step);
            Control content;

            try
            {
                switch (step)
                {
                    case 1:
                        Log.Debug("Creating login step content (Step 1)");
                        content = CreateLoginStepContent(_viewModel);
                        Log.Debug("Login step content created successfully");
                        break;
                    case 2:
                        Log.Debug("Creating authorization step content (Step 2)");
                        content = CreateAuthorizationStepContent(_viewModel);
                        Log.Debug("Authorization step content created successfully");
                        break;
                    case 3:
                        Log.Debug("Creating two-factor step content (Step 3)");
                        content = CreateTwoFactorStepContent(_viewModel);
                        Log.Debug("Two-factor step content created successfully");
                        break;
                    case 4:
                        Log.Debug("Creating success step content (Step 4)");
                        content = CreateSuccessStepContent(_viewModel);
                        Log.Debug("Success step content created successfully");
                        break;
                    default:
                        Log.Warning("Unknown step number: {Step}", step);
                        content = new TextBlock { Text = $"Ошибка: Неизвестный шаг ({step})" };
                        Log.Debug("Created error content for unknown step");
                        break;
                }

                var oldContent = _stepContentArea.Content;
                _stepContentArea.Content = content;
                Log.Debug("StepContentArea content updated from {OldType} to {NewType}", 
                    oldContent?.GetType().Name ?? "null", content.GetType().Name);
                
                // Update button states AFTER content change is complete
                // This is one of the explicit points where buttons should refresh
                UpdateButtonStates(); 
                Log.Debug("Button states updated after content change in UpdateStepContent");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating content for step {Step}", step);
                _stepContentArea.Content = new TextBlock { Text = $"Ошибка рендеринга шага {step}" };
                Log.Debug("Set error message in content area due to exception");
            }
            
            Log.Information("Step content update completed for step {Step}", step);
        }

        // --- Methods to Create Step Content (Called by Converter) ---

        // Step 1: Login Form
        public Control CreateLoginStepContent(AuthViewModel vm)
        {
            Log.Debug("CreateLoginStepContent called");
            
            var panel = new Grid { Margin = new Thickness(15) };
            Log.Debug("Created Grid panel with margin 15");
            
            panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Description
            panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Username Label
            panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Username TextBox
            panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Password Label
            panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Password TextBox
            panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Additional Info
            panel.RowDefinitions.Add(new RowDefinition(GridLength.Star)); // Spacer
            Log.Debug("Added 7 row definitions to Grid");

            // Description box with classic style inspired by OS/2 and ArcaOS installers
            var descriptionBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#F0F0F0")),
                BorderBrush = new SolidColorBrush(Color.Parse("#808080")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 20)
            };
            Log.Debug("Created description border with classic styling");
            
            var descriptionText = new TextBlock
            {
                Text = "Введите ваши учетные данные для доступа к системе БРУ Автопарк.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#000000"))
            };
            Log.Debug("Created description text block");
            
            descriptionBorder.Child = descriptionText;
            Grid.SetRow(descriptionBorder, 0);
            panel.Children.Add(descriptionBorder);
            Log.Debug("Added description border to row 0");

            // Username Label
            var usernameLabel = new TextBlock 
            { 
                Text = "Имя пользователя:", 
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(Color.Parse("#000000"))
            };
            Grid.SetRow(usernameLabel, 1);
            panel.Children.Add(usernameLabel);
            Log.Debug("Added username label to row 1");

            // Username TextBox
            var usernameTextBox = new TextBox
            {
                Watermark = "Логин",
                Margin = new Thickness(0, 0, 0, 12),
                Classes = { "ClassicTextBox" },
                [!TextBox.TextProperty] = new Binding("Username", BindingMode.TwoWay)
            };
            Grid.SetRow(usernameTextBox, 2);
            panel.Children.Add(usernameTextBox);
            Log.Debug("Added username textbox to row 2 with binding to Username property");

            // Password Label
            var passwordLabel = new TextBlock 
            { 
                Text = "Пароль:", 
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(Color.Parse("#000000"))
            };
            Grid.SetRow(passwordLabel, 3);
            panel.Children.Add(passwordLabel);
            Log.Debug("Added password label to row 3");

            // Password TextBox
            var passwordTextBox = new TextBox
            {
                Watermark = "Пароль",
                PasswordChar = '•',
                Margin = new Thickness(0, 0, 0, 15),
                Classes = { "ClassicTextBox" },
                [!TextBox.TextProperty] = new Binding("Password", BindingMode.TwoWay)
            };
            Grid.SetRow(passwordTextBox, 4);
            panel.Children.Add(passwordTextBox);
            Log.Debug("Added password textbox to row 4 with binding to Password property");
            
            // Additional info (OS/2 style note)
            var additionalInfoPanel = new StackPanel 
            { 
                Spacing = 5,
                Margin = new Thickness(0, 15, 0, 0)
            };
            Log.Debug("Created additional info panel");
            
            var noteIcon = new MaterialIcon
            {
                Kind = Material.Icons.MaterialIconKind.Information,
                Width = 16,
                Height = 16,
                Foreground = new SolidColorBrush(Color.Parse("#0055AA"))
            };
            Log.Debug("Created information icon for note");
            
            var noteText = new TextBlock
            {
                Text = "Для доступа к системе используйте учетную запись администратора.",
                FontSize = 11,
                Opacity = 0.85,
                Margin = new Thickness(5, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            Log.Debug("Created note text");
            
            var notePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5
            };
            
            notePanel.Children.Add(noteIcon);
            notePanel.Children.Add(noteText);
            additionalInfoPanel.Children.Add(notePanel);
            Log.Debug("Assembled note panel with icon and text");
            
            Grid.SetRow(additionalInfoPanel, 5);
            panel.Children.Add(additionalInfoPanel);
            Log.Debug("Added note panel to row 5");

            // DEBUGGING: Add handler for when the user presses Enter in the password field
            passwordTextBox.KeyDown += (s, e) => {
                Log.Debug("KeyDown event in password field: Key={Key}", e.Key);
                if (e.Key == Key.Enter && _viewModel != null && _viewModel.CanGoForward)
                {
                    Log.Information("Enter key pressed in password field, executing GoToNextStepCommand");
                    if (_viewModel.GoToNextStepCommand.CanExecute(null))
                    {
                        _viewModel.GoToNextStepCommand.Execute(null);
                    }
                }
            };
            Log.Debug("Added KeyDown handler to password textbox for Enter key");

            Log.Information("Login step content created successfully");
            return panel;
        }

        // Step 2: Validation Progress
        public Control CreateAuthorizationStepContent(AuthViewModel vm)
        {
            Log.Debug("CreateAuthorizationStepContent called");
            
            var panel = new StackPanel
            {
                Spacing = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20)
            };
            Log.Debug("Created main StackPanel for authorization step");

            var processingBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#F0F0F0")),
                BorderBrush = new SolidColorBrush(Color.Parse("#808080")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20),
                Width = 300
            };
            Log.Debug("Created processing border with width 300");
            
            var processingContent = new StackPanel
            {
                Spacing = 15,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Log.Debug("Created processing content StackPanel");
            
            var processingIcon = new MaterialIcon
            {
                Kind = Material.Icons.MaterialIconKind.CloudSync,
                Width = 48,
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#0066CC"))
            };
            Log.Debug("Created CloudSync icon for processing");
            
            var processingText = new TextBlock
            {
                Text = "Проверка учетных данных...",
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#000000")),
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center
            };
            Log.Debug("Created processing text block");
            
            var spinner = new ProgressBar
            {
                IsIndeterminate = true,
                Width = 200,
                Height = 15,
                Classes = { "ClassicProgressBar" },
                HorizontalAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(0)
            };
            Log.Debug("Created indeterminate progress bar");
            
            var statusText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0),
                [!TextBlock.TextProperty] = new Binding("StatusMessage")
            };
            Log.Debug("Created status text with binding to StatusMessage");

            processingContent.Children.Add(processingIcon);
            processingContent.Children.Add(processingText);
            processingContent.Children.Add(spinner);
            processingContent.Children.Add(statusText);
            Log.Debug("Added all elements to processing content");
            
            processingBorder.Child = processingContent;
            panel.Children.Add(processingBorder);
            Log.Debug("Added processing border to main panel");

            Log.Information("Authorization step content created successfully");
            return panel;
        }

         // Step 3: 2FA Input
        public Control CreateTwoFactorStepContent(AuthViewModel vm)
        {
            Log.Information("CreateTwoFactorStepContent called for type: {TwoFactorType}", vm.TwoFactorType);
            
            var panel = new StackPanel { Spacing = 15, Margin = new Thickness(15) };
            Log.Debug("Created main StackPanel for 2FA step");

            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#F0F0F0")),
                BorderBrush = new SolidColorBrush(Color.Parse("#808080")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 15)
            };
            Log.Debug("Created header border for 2FA step");
            
            var headerText = new TextBlock
            {
                Text = $"Для повышения безопасности требуется дополнительная проверка ({vm.TwoFactorType}).",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#000000"))
            };
            Log.Debug("Created header text with 2FA type: {Type}", vm.TwoFactorType);
            
            headerBorder.Child = headerText;
            panel.Children.Add(headerBorder);
            Log.Debug("Added header border to main panel");

            // Content based on 2FA Type ---
            if (vm.TwoFactorType?.ToLowerInvariant() == "totp")
            {
                Log.Debug("Creating TOTP input for 2FA");
                var totpInput = CreateTotpInput(vm);
                panel.Children.Add(totpInput);
                Log.Debug("TOTP input added to panel");
            }
            else if (vm.TwoFactorType?.ToLowerInvariant() == "webauthn")
            {
                Log.Debug("Creating WebAuthn prompt for 2FA");
                var webAuthnPrompt = CreateWebAuthnPrompt(vm);
                panel.Children.Add(webAuthnPrompt);
                Log.Debug("WebAuthn prompt added to panel");
            }
            else
            {
                Log.Warning("Unknown 2FA type: {Type}", vm.TwoFactorType);
                panel.Children.Add(new TextBlock 
                { 
                    Text = "Неизвестный тип двухфакторной аутентификации.",
                    Foreground = new SolidColorBrush(Color.Parse("#CC0000"))
                });
                Log.Debug("Error message added for unknown 2FA type");
            }

            // --- Add Verify Button (if needed, or rely on main Next button) ---
            // Example: Add a specific verify button for TOTP
            if (vm.TwoFactorType?.ToLowerInvariant() == "totp")
            {
                Log.Debug("Adding verify button for TOTP");
                var verifyButton = new Button
                {
                    Content = "Проверить код",
                    Classes = {"CommandButton"}, // Use standard button style
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 15, 0, 0),
                    [!Button.CommandProperty] = new Binding("VerifyTotpCommand") // Bind to specific TOTP verify command
                };
                panel.Children.Add(verifyButton);
                Log.Debug("TOTP verify button added with binding to VerifyTotpCommand");
            }

            Log.Information("Two-factor step content created successfully for type: {Type}", vm.TwoFactorType);
            return panel;
        }

       
         private Control CreateTotpInput(AuthViewModel vm)
         {
            Log.Debug("CreateTotpInput method called");
            var panel = new StackPanel { Spacing = 12 };
            Log.Debug("Created StackPanel for TOTP input with spacing 12");
            
            var instructionsText = new TextBlock 
            { 
                Text = "Введите 6-значный код из вашего приложения аутентификации:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5),
                Foreground = new SolidColorBrush(Color.Parse("#000000"))
            };
            Log.Debug("Created instructions TextBlock for TOTP input");
            panel.Children.Add(instructionsText);
            Log.Debug("Added instructions TextBlock to panel");
            
            var codeBox = new TextBox
            {
                Watermark = "Код TOTP",
                MaxLength = 6,
                Width = 150,
                HorizontalAlignment = HorizontalAlignment.Left,
                Classes = { "ClassicTextBox" },
                [!TextBox.TextProperty] = new Binding("TotpCode", BindingMode.TwoWay)
            };
            Log.Debug("Created TextBox for TOTP code with TwoWay binding to TotpCode property");
            panel.Children.Add(codeBox);
            Log.Debug("Added TOTP code TextBox to panel");
            
            // Note
            var notePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Margin = new Thickness(0, 15, 0, 0)
            };
            Log.Debug("Created horizontal StackPanel for TOTP note");
            
            var noteIcon = new MaterialIcon
            {
                Kind = Material.Icons.MaterialIconKind.Information,
                Width = 16,
                Height = 16,
                Foreground = new SolidColorBrush(Color.Parse("#0055AA"))
            };
            Log.Debug("Created information icon for TOTP note");
            
            var noteText = new TextBlock
            {
                Text = "Код будет действителен только 30 секунд. Если не успеете ввести, запросите новый код.",
                FontSize = 11,
                Opacity = 0.85,
                TextWrapping = TextWrapping.Wrap
            };
            Log.Debug("Created note text about TOTP code expiration");
            
            notePanel.Children.Add(noteIcon);
            Log.Debug("Added note icon to note panel");
            notePanel.Children.Add(noteText);
            Log.Debug("Added note text to note panel");
            panel.Children.Add(notePanel);
            Log.Debug("Added complete note panel to main TOTP panel");
            
            Log.Information("TOTP input control created successfully");
            return panel;
         }

        private Control CreateWebAuthnPrompt(AuthViewModel vm)
         {
            Log.Debug("CreateWebAuthnPrompt method called");
            var panel = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#F0F0F0")),
                BorderBrush = new SolidColorBrush(Color.Parse("#808080")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20),
                Width = 300,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Log.Debug("Created Border for WebAuthn prompt with width 300");
            
            var content = new StackPanel 
            { 
                Spacing = 15, 
                HorizontalAlignment = HorizontalAlignment.Center 
            };
            Log.Debug("Created centered StackPanel for WebAuthn content");
            
            var securityKeyIcon = new MaterialIcon 
            { 
                Kind = Material.Icons.MaterialIconKind.UsbPort, 
                Width = 48, 
                Height = 48,
                Foreground = new SolidColorBrush(Color.Parse("#0066CC")),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Log.Debug("Created USB port icon for security key visualization");
            
            var promptText = new TextBlock 
            { 
                Text = "Подключите ваш ключ безопасности и нажмите на нем кнопку, когда она начнет мигать",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Log.Debug("Created prompt text with instructions for security key");
            
            var waitingText = new TextBlock 
            { 
                Text = "Ожидание ключа безопасности...",
                FontSize = 11,
                Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Log.Debug("Created waiting text for security key status");
            
            content.Children.Add(securityKeyIcon);
            Log.Debug("Added security key icon to content panel");
            content.Children.Add(promptText);
            Log.Debug("Added prompt text to content panel");
            content.Children.Add(waitingText);
            Log.Debug("Added waiting text to content panel");
            
            panel.Child = content;
            Log.Debug("Set content panel as child of main border");
            
            Log.Information("WebAuthn prompt control created successfully");
            return panel;
         }


        // Step 4: Success Confirmation
        public Control CreateSuccessStepContent(AuthViewModel vm)
        {
            Log.Information("CreateSuccessStepContent method called");
            var panel = new StackPanel
            {
                Spacing = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20)
            };
            Log.Debug("Created main StackPanel for success step with spacing 20");

            var successBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#F0F0F0")),
                BorderBrush = new SolidColorBrush(Color.Parse("#808080")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(25),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Log.Debug("Created centered Border for success content");
            
            var successContent = new StackPanel
            {
                Spacing = 15,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Log.Debug("Created centered StackPanel for success content items");
            
            var successIcon = new MaterialIcon
            {
                Kind = Material.Icons.MaterialIconKind.CheckCircle,
                Width = 48,
                Height = 48,
                Foreground = new SolidColorBrush(Color.Parse("#008800")),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Log.Debug("Created green check circle icon for success visualization");
            
            var successTitle = new TextBlock
            {
                Text = "Авторизация успешно завершена!",
                FontWeight = FontWeight.Bold,
                FontSize = 16,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#000000"))
            };
            Log.Debug("Created bold success title text with explicit black color");
            
            var successMessage = new TextBlock
            {
                Text = "Вы успешно вошли в систему управления БРУ Автопарк.",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#000000"))
            };
            Log.Debug("Created success message text with explicit black color");
            
            var separator = new Separator
            {
                Height = 1,
                Background = new SolidColorBrush(Color.Parse("#CCCCCC")),
                Margin = new Thickness(0, 10, 0, 10)
            };
            Log.Debug("Created separator for visual division");
            
            var userInfoText = new TextBlock
            {
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#000000")),
                [!TextBlock.TextProperty] = new Binding("UserInfo")
            };
            Log.Debug("Created user info text with binding to UserInfo property and explicit black color");
            
            successContent.Children.Add(successIcon);
            Log.Debug("Added success icon to content");
            successContent.Children.Add(successTitle);
            Log.Debug("Added success title to content");
            successContent.Children.Add(successMessage);
            Log.Debug("Added success message to content");
            successContent.Children.Add(separator);
            Log.Debug("Added separator to content");
            successContent.Children.Add(userInfoText);
            Log.Debug("Added user info text to content");
            
            successBorder.Child = successContent;
            Log.Debug("Set success content as child of border");
            panel.Children.Add(successBorder);
            Log.Debug("Added success border to main panel");

            Log.Information("Success step content created successfully");
            return panel;
        }

        // Button handlers that directly call VM methods - DELETED, now using Command bindings from XAML
        // All code from here to the next class has been removed
    }

    // StepEqualityConverter for determining active step in sidebar
    public class StepEqualityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int currentStep && parameter is string stepStr && int.TryParse(stepStr, out int compareStep))
            {
                Log.Debug("StepEqualityConverter: Comparing {CurrentStep} with {CompareStep} = {Result}", 
                    currentStep, compareStep, currentStep == compareStep);
                return currentStep == compareStep;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException("StepEqualityConverter does not support ConvertBack");
        }
    }

    // StepToContentConverter - This is the key converter the user wants to keep
    public class StepToContentConverter : IValueConverter
    {
        // Static instance reference that can be set during window initialization
        private static AuthWindow? _currentAuthWindow;

        // Method to register the window instance for the converter to use
        public static void RegisterWindow(AuthWindow window)
        {
            _currentAuthWindow = window;
            Log.Debug("StepToContentConverter: AuthWindow instance registered");
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            Log.Debug("StepToContentConverter.Convert called with value: {Value}", value);
            if (!(value is int step))
            {
                Log.Warning("StepToContentConverter: Expected int value but got {ValueType}", value?.GetType()?.Name ?? "null");
                return new TextBlock { Text = "Ошибка: Неверный тип данных для шага" };
            }

            // Get the active AuthWindow instance
            var window = GetActiveAuthWindow();
            if (window == null)
            {
                Log.Warning("StepToContentConverter: Couldn't find AuthWindow");
                return new TextBlock { Text = "Ошибка: Не удалось найти окно аутентификации" };
            }

            var vm = window.DataContext as AuthViewModel;
            if (vm == null)
            {
                Log.Warning("StepToContentConverter: No AuthViewModel found in DataContext");
                return new TextBlock { Text = "Ошибка: Не удалось получить модель представления" };
            }
            Log.Debug("StepToContentConverter: Found AuthViewModel in DataContext");

            try
            {
                switch (step)
                {
                    case 1:
                        Log.Debug("StepToContentConverter: Creating login step content");
                        return window.CreateLoginStepContent(vm);
                    case 2:
                        Log.Debug("StepToContentConverter: Creating authorization step content");
                        return window.CreateAuthorizationStepContent(vm);
                    case 3:
                        Log.Debug("StepToContentConverter: Creating two-factor step content");
                        return window.CreateTwoFactorStepContent(vm);
                    case 4:
                        Log.Debug("StepToContentConverter: Creating success step content");
                        return window.CreateSuccessStepContent(vm);
                    default:
                        Log.Warning("StepToContentConverter: Unknown step {Step}", step);
                        return new TextBlock { Text = $"Ошибка: Неизвестный шаг ({step})" };
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in StepToContentConverter for step {Step}", step);
                return new TextBlock { Text = $"Ошибка рендеринга шага {step}" };
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            Log.Debug("StepToContentConverter.ConvertBack called (not supported)");
            throw new NotSupportedException("StepToContentConverter does not support ConvertBack");
        }

        private static AuthWindow? GetActiveAuthWindow()
        {
            Log.Debug("GetActiveAuthWindow method called");
            
            // First try to use the registered window instance (most reliable)
            if (_currentAuthWindow != null)
            {
                Log.Debug("Using registered AuthWindow instance");
                return _currentAuthWindow;
            }
            
            // Fallback method: Look for window in application lifetime
            try {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    Log.Debug("Searching in {Count} desktop windows", desktop.Windows.Count);
                    
                    // Try to find AuthWindow instance
                    var authWindow = desktop.Windows.OfType<AuthWindow>().FirstOrDefault();
                    if (authWindow != null)
                    {
                        Log.Debug("Found AuthWindow in desktop.Windows collection");
                        return authWindow;
                    }
                    
                    Log.Debug("No AuthWindow found in desktop.Windows collection");
                }
                else
                {
                    Log.Debug("Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime");
                }
                
                Log.Warning("Could not find AuthWindow using any method");
                return null;
            }
            catch (Exception ex) {
                Log.Error(ex, "Error during GetActiveAuthWindow");
                return null;
            }
        }
    }

    // Extension methods to help with dynamic content creation
    public static class AuthWindowExtensions
    {
        public static Control CreateLoginStepContent(this AuthWindow window, AuthViewModel vm)
        {
            Log.Debug("AuthWindowExtensions.CreateLoginStepContent called");
            // Create login form UI
            var content = window.FindControl<ContentControl>("StepContentArea")?.Content as Control;
            if (content != null)
            {
                Log.Debug("Using existing content from StepContentArea");
                return content;
            }
            
            Log.Debug("Creating default login content");
            return CreateDefaultLoginContent(vm);
        }

        public static Control CreateAuthorizationStepContent(this AuthWindow window, AuthViewModel vm)
        {
            Log.Debug("AuthWindowExtensions.CreateAuthorizationStepContent called");
            // Create validation step UI
            var content = window.FindControl<ContentControl>("StepContentArea")?.Content as Control;
            if (content != null)
            {
                Log.Debug("Using existing content from StepContentArea");
                return content;
            }
            
            Log.Debug("Creating default processing content");
            return CreateDefaultProcessingContent(vm);
        }

        public static Control CreateTwoFactorStepContent(this AuthWindow window, AuthViewModel vm)
        {
            Log.Debug("AuthWindowExtensions.CreateTwoFactorStepContent called");
            // Create 2FA step UI
            var content = window.FindControl<ContentControl>("StepContentArea")?.Content as Control;
            if (content != null)
            {
                Log.Debug("Using existing content from StepContentArea");
                return content;
            }
            
            Log.Debug("Creating default two-factor content");
            return CreateDefaultTwoFactorContent(vm);
        }

        public static Control CreateSuccessStepContent(this AuthWindow window, AuthViewModel vm)
        {
            Log.Debug("AuthWindowExtensions.CreateSuccessStepContent called");
            // Create success step UI
            var content = window.FindControl<ContentControl>("StepContentArea")?.Content as Control;
            if (content != null)
            {
                Log.Debug("Using existing content from StepContentArea");
                return content;
            }
            
            Log.Debug("Creating default success content");
            return CreateDefaultSuccessContent(vm);
        }

        // Default content factories
        private static Control CreateDefaultLoginContent(AuthViewModel vm)
        {
            Log.Debug("Creating default login content TextBlock");
            return new TextBlock { Text = "Форма входа" };
        }

        private static Control CreateDefaultProcessingContent(AuthViewModel vm)
        {
            Log.Debug("Creating default processing content TextBlock");
            return new TextBlock { Text = "Проверка учетных данных..." };
        }

        private static Control CreateDefaultTwoFactorContent(AuthViewModel vm)
        {
            Log.Debug("Creating default two-factor content TextBlock");
            return new TextBlock { Text = "Двухфакторная аутентификация" };
        }

        private static Control CreateDefaultSuccessContent(AuthViewModel vm)
        {
            Log.Debug("Creating default success content TextBlock");
            return new TextBlock { Text = "Вход успешно выполнен" };
        }
    }
}