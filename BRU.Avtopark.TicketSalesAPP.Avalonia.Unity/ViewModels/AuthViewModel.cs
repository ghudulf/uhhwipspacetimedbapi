using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input; // Keep for ICommand if needed, but RelayCommand handles it
using ReactiveUI; // Keep for ReactiveObject
using CommunityToolkit.Mvvm.ComponentModel; // Use ObservableObject instead of ReactiveObject for CommunityToolkit MVVM
using CommunityToolkit.Mvvm.Input; // Use RelayCommand
using Serilog;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Layout;
using System.Security.Claims;
//using System.Reactive; // No longer needed for ReactiveCommand<Unit, Unit>
using Avalonia.Threading;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services; // Assuming this namespace is correct

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    // Inherit from ObservableObject for CommunityToolkit MVVM or keep ReactiveObject if other ReactiveUI features are used extensively elsewhere
    public partial class AuthViewModel : ReactiveObject // Or ObservableObject
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isLoading;
        private bool _requiresTwoFactor;
        private string _twoFactorType = string.Empty;
        private string _tempToken = string.Empty; // Store temp token for 2FA validation
        private int _currentStep = 1;
        private string _stepTitle = "Вход в систему";
        private string _statusMessage = "Готов к авторизации";
        private string _nextButtonText = "Далее";
        private double _progressValue = 0;
        private string _progressText = "Шаг 1 из 4"; // Adjusted to 4 steps
        private string _userInfo = string.Empty;
        private bool _hasError = false;
        private bool _canGoBack;
        private bool _canGoForward;

        // Event for login completion
        public event EventHandler<bool>? LoginCompleted; // Make EventHandler nullable

        public string Username
        {
            get => _username;
            set
            {
                if (_username != value)
                {
                    _username = value;
                    this.RaisePropertyChanged(nameof(Username));
                    RecalculateButtonStates();
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (_password != value)
                {
                    _password = value;
                    this.RaisePropertyChanged(nameof(Password));
                    RecalculateButtonStates();
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                this.RaiseAndSetIfChanged(ref _errorMessage, value);
                HasError = !string.IsNullOrEmpty(value);
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    this.RaisePropertyChanged(nameof(IsLoading));
                    RecalculateButtonStates();
                }
            }
        }

        // No change needed for these properties structurally
        public bool RequiresTwoFactor { get => _requiresTwoFactor; private set => this.RaiseAndSetIfChanged(ref _requiresTwoFactor, value); }
        public string TwoFactorType { get => _twoFactorType; private set => this.RaiseAndSetIfChanged(ref _twoFactorType, value); }
        public bool IsAuthenticated { get; private set; } // Keep internal setter logic
        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                if (_currentStep != value)
                {
                    Log.Debug("CurrentStep changing from {Old} to {New}", _currentStep, value);
                    _currentStep = value;
                    this.RaisePropertyChanged(nameof(CurrentStep));
                    UpdateStepContentDirect(value);
                    RecalculateButtonStates();
                }
            }
        }
        public string StepTitle { get => _stepTitle; private set => this.RaiseAndSetIfChanged(ref _stepTitle, value); }
        public double ProgressValue { get => _progressValue; private set => this.RaiseAndSetIfChanged(ref _progressValue, value); }
        public string ProgressText { get => _progressText; private set => this.RaiseAndSetIfChanged(ref _progressText, value); }
        public string StatusMessage { get => _statusMessage; private set => this.RaiseAndSetIfChanged(ref _statusMessage, value); }
        public string NextButtonText { get => _nextButtonText; private set => this.RaiseAndSetIfChanged(ref _nextButtonText, value); }
        public bool HasError { get => _hasError; private set => this.RaiseAndSetIfChanged(ref _hasError, value); }
        public string UserInfo { get => _userInfo; private set => this.RaiseAndSetIfChanged(ref _userInfo, value); }

        // Computed properties for command CanExecute
        public bool CanGoBack
        {
            get => _canGoBack;
            private set
            {
                this.RaiseAndSetIfChanged(ref _canGoBack, value);
                GoToPreviousStepCommand.NotifyCanExecuteChanged();
            }
        }
        
        public bool CanGoForward 
        {
            get => _canGoForward;
            private set
            {
                this.RaiseAndSetIfChanged(ref _canGoForward, value);
                GoToNextStepCommand.NotifyCanExecuteChanged();
                TriggerLoginAndAdvanceCommand.NotifyCanExecuteChanged();
            }
        }

        // Constructor
        public AuthViewModel()
        {
             _httpClient = ApiClientService.Instance.CreateClient(); // Use injected or singleton service
             
            // _baseUrl = "http://localhost:5000/api"; // Fallback for testing
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            };

            Log.Debug("AuthViewModel initialized");
            InitializeSteps(); // Set initial state
        }

        public void InitializeSteps()
        {
            _currentStep = 1;
            _progressValue = 25;
            _progressText = "Шаг 1 из 4";
            _stepTitle = "Вход в систему";
            _statusMessage = "Введите ваши учетные данные";
            _nextButtonText = "Далее";
            _username = string.Empty;
            _password = string.Empty;
            _errorMessage = string.Empty;
            _isLoading = false;
            _requiresTwoFactor = false;
            IsAuthenticated = false;
            
            this.RaisePropertyChanged(nameof(CurrentStep));
            this.RaisePropertyChanged(nameof(ProgressValue));
            this.RaisePropertyChanged(nameof(ProgressText));
            this.RaisePropertyChanged(nameof(StepTitle));
            this.RaisePropertyChanged(nameof(StatusMessage));
            this.RaisePropertyChanged(nameof(NextButtonText));
            this.RaisePropertyChanged(nameof(Username));
            this.RaisePropertyChanged(nameof(Password));
            this.RaisePropertyChanged(nameof(ErrorMessage));
            this.RaisePropertyChanged(nameof(IsLoading));
            this.RaisePropertyChanged(nameof(RequiresTwoFactor));
            
            UpdateButtonStates();
        }
        
        public void UpdateButtonStates()
        {
            // Only log if state actually changes
            bool oldCanGoBack = _canGoBack;
            bool oldCanGoForward = _canGoForward;

            _canGoBack = CurrentStep > 1 && CurrentStep < 4 && !IsLoading;
            _canGoForward = CanGoToNextStep(); // Use helper method
                
            // Raise property changed only if the value actually changed
            if (_canGoBack != oldCanGoBack)
            {
                Log.Debug("ViewModel.UpdateButtonStates: CanGoBack changed from {Old} to {New}", oldCanGoBack, _canGoBack);
                this.RaisePropertyChanged(nameof(CanGoBack));
                GoToPreviousStepCommand.NotifyCanExecuteChanged();
            }
            
            if (_canGoForward != oldCanGoForward)
            {
                 Log.Debug("ViewModel.UpdateButtonStates: CanGoForward changed from {Old} to {New}", oldCanGoForward, _canGoForward);
                this.RaisePropertyChanged(nameof(CanGoForward));
                GoToNextStepCommand.NotifyCanExecuteChanged();
                TriggerLoginAndAdvanceCommand.NotifyCanExecuteChanged(); // Ensure this is still notified
            }
            
            // Ensure Cancel command state reflects loading state
            CancelCommand.NotifyCanExecuteChanged();

            // NextButtonText is handled by UpdateStepContentDirect
            // We only need to notify if it changes there
            // this.RaisePropertyChanged(nameof(NextButtonText)); 
        }

        /// <summary>
        /// Helper to determine if the user can proceed to the next step based on current state.
        /// </summary>
        private bool CanGoToNextStep()
        {
            if (IsLoading) return false;

            return CurrentStep switch
            {
                1 => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password),
                2 => false, // Step 2 is transient, should auto-advance or fail back to 1
                3 => CanVerifyTotp(), // Check if TOTP can be verified (assuming TOTP for now)
                4 => true, // Can always finish from the success screen
                _ => false,
            };
        }

        /// <summary>
        /// Recalculates button states and forces relevant UI updates.
        /// Called when underlying data influencing button state changes.
        /// </summary>
        public void RecalculateButtonStates()
        {
            Log.Debug("ViewModel.RecalculateButtonStates called");
            
            // Force update of button state flags and notify if changed
            UpdateButtonStates(); 
            
            // We don't need to raise IsLoading changed here, its own setter handles it.
            // this.RaisePropertyChanged(nameof(IsLoading));
            
            // Command notifications are handled within UpdateButtonStates where changes are detected
            Log.Debug("ViewModel.RecalculateButtonStates completed");
        }

        // --- Commands using CommunityToolkit.Mvvm ---

        [RelayCommand(CanExecute = nameof(CanTriggerLogin))]
        private async Task TriggerLoginAndAdvanceAsync()
        {
            Log.Debug("Attempting login for user {Username}", Username);
                IsLoading = true;
                ErrorMessage = string.Empty;
                RequiresTwoFactor = false;
                TwoFactorType = string.Empty;
            StatusMessage = "Подключение к серверу...";

            // Update button states
            UpdateButtonStates();

            try
                {
                var loginRequest = new
                {
                    username = Username,
                    password = Password,
                    skipTwoFactor = false // Start by requesting 2FA if applicable
                };

                Log.Information("LOGIN REQUEST DETAILS: Username: {Username}, Password: [REDACTED], SkipTwoFactor: {SkipTwoFactor}",
                    loginRequest.username, loginRequest.skipTwoFactor);

                StatusMessage = "Ожидание ответа от сервера...";
                HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/auth/login", loginRequest, _jsonOptions);
                string responseContent = await response.Content.ReadAsStringAsync();

                Log.Debug("Raw response content: {Content}", responseContent);
                StatusMessage = "Обработка ответа...";

                if (response.IsSuccessStatusCode)
                {
                    ApiResponse<LoginResponse>? result = null;
                    try
                    {
                        result = JsonSerializer.Deserialize<ApiResponse<LoginResponse>>(responseContent, _jsonOptions);
                        Log.Debug("Deserialized response: {@Result}", result);
                    }
                    catch (JsonException jsonEx)
                    {
                        Log.Error(jsonEx, "JSON deserialization error during login success path. Raw content: {Content}", responseContent);
                        ErrorMessage = "Ошибка обработки ответа сервера (неверный формат).";
                        await GoToPreviousStepAsync(true); // Go back to step 1 on error
                        return;
                    }

                    if (result?.Success == false || result?.Data == null)
                    {
                        ErrorMessage = result?.Message ?? "Ошибка входа: Неверные учетные данные.";
                        Log.Warning("Login failed (Success=false or Data=null): {Message}", ErrorMessage);
                        await GoToPreviousStepAsync(true); // Go back to step 1
                            return;
                        }

                    // --- Transition to Step 2: Validation ---
                    // This step is shown AFTER successful API response, before checking 2FA/Token
                    Log.Information("API login successful, transitioning to validation step (Step 2) for user {Username}", Username);
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        CurrentStep = 2; // Explicitly move to validation step
                        StepTitle = "Авторизация";
                        StatusMessage = "Проверка прав доступа...";
                        ProgressValue = 50; // 2 of 4 steps
                        ProgressText = "Шаг 2 из 4";
                        NextButtonText = "Далее"; // Keep it consistent, but it will be disabled

                        // Re-evaluate command states for Step 2
                        TriggerLoginAndAdvanceCommand.NotifyCanExecuteChanged();
                        GoToNextStepCommand.NotifyCanExecuteChanged();
                        GoToPreviousStepCommand.NotifyCanExecuteChanged();
                        this.RaisePropertyChanged(nameof(CanGoBack));
                        this.RaisePropertyChanged(nameof(CanGoForward));
                    });

                    // --- Show Validation Step for a duration ---
                    Log.Debug("Displaying validation step for 10 seconds...");
                    await Task.Delay(10000); // Wait for 10 seconds
                    Log.Debug("Validation step delay complete.");

                    // --- NOW, decide the next step based on the API response ---
                    IsLoading = false; // Allow interaction or transition from Step 2 completed

                    // --- Handle 2FA ---
                    if (result.Data.RequiresTwoFactor)
                    {
                        Log.Information("Proceeding to Step 3 (2FA) after validation for user {Username}", Username);
                            RequiresTwoFactor = true;
                            TwoFactorType = result.Data.TwoFactorType;
                        _tempToken = result.Data.TempToken; // Store temp token

                        await Dispatcher.UIThread.InvokeAsync(async () => {
                            CurrentStep = 3; // Move to 2FA Step
                            StepTitle = "Двухфакторная аутентификация";
                            StatusMessage = $"Требуется {TwoFactorType}.";
                            ProgressValue = 75; // 3 of 4 steps
                            ProgressText = "Шаг 3 из 4";
                            NextButtonText = "Подтвердить"; // Or specific text based on 2FA type

                            // Re-evaluate command states for Step 3
                            TriggerLoginAndAdvanceCommand.NotifyCanExecuteChanged();
                            GoToNextStepCommand.NotifyCanExecuteChanged();
                            GoToPreviousStepCommand.NotifyCanExecuteChanged();
                            this.RaisePropertyChanged(nameof(CanGoBack));
                            this.RaisePropertyChanged(nameof(CanGoForward));

                            // Trigger the specific 2FA handling UI/logic
                            await HandleTwoFactorAuthentication(TwoFactorType);
                        });
                        // Don't proceed further here; 2FA handler will complete or fail.
                        // IsLoading = false; // Already set before this block
                        this.RaisePropertyChanged(nameof(CanGoForward)); // Re-evaluate after loading changes
                        return; // Exit after initiating 2FA flow
                    }

                    // --- Handle Success without 2FA ---
                    else if (!string.IsNullOrEmpty(result.Data.Token))
                    {
                        var handler = new JwtSecurityTokenHandler();
                        JwtSecurityToken token;
                        try
                        {
                           token = handler.ReadJwtToken(result.Data.Token);
                            Log.Debug("JWT token claims: {@Claims}", token.Claims.Select(c => new { c.Type, c.Value }));
                        }
                        catch(Exception ex)
                        {
                            Log.Error(ex, "Failed to parse JWT token received from server for user {Username}", Username);
                            ErrorMessage = "Ошибка обработки токена доступа.";
                            await GoToPreviousStepAsync(true); // Go back to step 1
                            return;
                        }

                        // --- Admin Check ---
                        bool isAdmin = CheckAdminPermissions(token.Claims);
                        int userRole = GetUserRole(token.Claims);
                        Log.Information("User role determined: {Role} (Admin: {IsAdmin})", userRole, isAdmin);

                        // Store both token and role information in ApiClientService
                        ApiClientService.Instance.AuthToken = result.Data.Token;
                        ApiClientService.Instance.IsAdmin = isAdmin;
                        ApiClientService.Instance.UserRole = userRole;

                            if (!isAdmin)
                            {
                                Log.Warning("Non-admin user {Username} attempted to access admin interface", Username);
                            ErrorMessage = "Доступ запрещен: Требуются права администратора."; // Set error message
                            await ShowNonAdminErrorAsync(); // Show message box
                            // Don't close window, let user see the error on the login screen
                            await GoToPreviousStepAsync(true); // Force back to step 1
                            return; // Stop execution
                        }

                        // --- Admin Login Successful ---
                        IsAuthenticated = true;
                        Log.Information("User successfully authenticated: {Username} (Role: {Role})", Username, userRole);
                        UserInfo = $"Пользователь: {Username}\nРоль: {ApiClientService.Instance.RoleName}"; // Use RoleName from service

                        // Move to final success step
                        await Dispatcher.UIThread.InvokeAsync(() => {
                            CurrentStep = 4;
                            StepTitle = "Вход успешно выполнен";
                            StatusMessage = "Авторизация прошла успешно.";
                            ProgressValue = 100; // 4 of 4 steps
                            ProgressText = "Шаг 4 из 4";
                            NextButtonText = "Завершить";

                            // Re-evaluate command states
                            TriggerLoginAndAdvanceCommand.NotifyCanExecuteChanged();
                            GoToNextStepCommand.NotifyCanExecuteChanged();
                            GoToPreviousStepCommand.NotifyCanExecuteChanged();
                            this.RaisePropertyChanged(nameof(CanGoBack));
                            this.RaisePropertyChanged(nameof(CanGoForward));
                        });
                    }
                    else // Handle unexpected success state (no 2FA, no token)
                    {
                         Log.Warning("Login successful according to API, but no token and no 2FA required for user {Username}", Username);
                         ErrorMessage = "Ошибка входа: Не удалось получить токен доступа или информацию о 2FA.";
                         await GoToPreviousStepAsync(true); // Go back to step 1
                    }
                }
                else // Handle non-success status code (API error or HTTP error)
                {
                    ApiResponse<object>? errorResponse = null;
                    if (!string.IsNullOrWhiteSpace(responseContent) && responseContent.TrimStart().StartsWith("{"))
                    {
                        try
                        {
                            errorResponse = JsonSerializer.Deserialize<ApiResponse<object>>(responseContent, _jsonOptions);
                            ErrorMessage = errorResponse?.Message ?? $"Ошибка сервера: {(int)response.StatusCode} ({response.ReasonPhrase})";
                            Log.Warning("Authentication failed (API Error): User={Username}, Status={StatusCode}, Reason={Reason}, Message={ApiMessage}",
                                Username, response.StatusCode, response.ReasonPhrase, errorResponse?.Message);
                        }
                        catch (JsonException jsonEx)
                        {
                            // Log the parsing error but provide a more generic error message to the user
                            Log.Error(jsonEx, "Error parsing JSON error response. Status: {StatusCode}, Raw content: {Content}", response.StatusCode, responseContent);
                            ErrorMessage = $"Ошибка аутентификации: {(int)response.StatusCode} ({response.ReasonPhrase})";
                        }
                    }
                    else
                    {
                        // Handle empty or non-JSON error responses (like 404 Not Found HTML)
                        ErrorMessage = $"Ошибка связи с сервером: {(int)response.StatusCode} ({response.ReasonPhrase})";
                        Log.Warning("Authentication failed (Non-JSON Response): User={Username}, Status={StatusCode}, Reason={Reason}, ContentType={ContentType}", 
                            Username, response.StatusCode, response.ReasonPhrase, response.Content.Headers.ContentType);
                    }
                     await GoToPreviousStepAsync(true); // Go back to step 1
                }
            }
            catch (HttpRequestException httpEx)
            {
                Log.Error(httpEx, "HTTP request error during login for user {Username}", Username);
                ErrorMessage = "Ошибка сети: Не удалось подключиться к серверу.";
                await GoToPreviousStepAsync(true); // Go back to step 1
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CRITICAL AUTHENTICATION ERROR during login: User={Username}", Username);
                ErrorMessage = "Критическая ошибка при аутентификации.";
                // Optionally rethrow if needed: throw;
                await GoToPreviousStepAsync(true); // Go back to step 1
            }
            finally
            {
                IsLoading = false;
                // Re-evaluate commands that depend on IsLoading
                TriggerLoginAndAdvanceCommand.NotifyCanExecuteChanged();
                GoToNextStepCommand.NotifyCanExecuteChanged();
                GoToPreviousStepCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                this.RaisePropertyChanged(nameof(CanGoForward));
                this.RaisePropertyChanged(nameof(CanGoBack));
                Log.Debug("Login attempt completed for user {Username}. IsLoading={IsLoading}", Username, IsLoading);
            }
        }
        private bool CanTriggerLogin() => !IsLoading && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

        [RelayCommand(CanExecute = nameof(CanExecuteNext))]
        private async Task GoToNextStepAsync()
        {
            Log.Debug("GoToNextStep called. CurrentStep: {CurrentStep}", CurrentStep);
            
            if (CurrentStep == 1)
            {
                // Step 1 "Next" should trigger the login API call
                await TriggerLoginAndAdvanceAsync();
                return; // Important: return here to avoid further execution
            }
            // This command is now mainly for Step 4 -> Completion or potentially 2FA confirmation if redesigned
            else if (CurrentStep == 4)
            {
                Log.Information("Login process completed by user action.");
                LoginCompleted?.Invoke(this, true); // Signal success to App.xaml.cs
                CloseAuthWindow(); // Close the window
            }
            else if (CurrentStep == 3) // Example: If 2FA step needed separate confirmation
            {
                // If 2FA required a separate confirm button, logic would go here.
                Log.Debug("Handling next step from 2FA screen (if applicable)");
                // This might trigger the TOTP verification if not done via dialog button
                await VerifyTotpAsync();
            }
            else if (CurrentStep == 2)
            {
                // This case should technically not be reachable if TriggerLoginAndAdvance handles the flow
                Log.Debug("GoToNextStepAsync called from Step 2.");
                // If somehow stuck here after API success, move to success step
                if(IsAuthenticated && !RequiresTwoFactor)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        CurrentStep = 4;
                        StepTitle = "Вход успешно выполнен";
                        StatusMessage = "Авторизация прошла успешно.";
                        ProgressValue = 100; // 4 of 4 steps
                        ProgressText = "Шаг 4 из 4";
                        NextButtonText = "Завершить";
                        this.RaisePropertyChanged(nameof(CanGoBack));
                        this.RaisePropertyChanged(nameof(CanGoForward));
                    });
                }
                else
                {
                    // If not authenticated and not requiring 2FA, return to step 1
                    await GoToPreviousStepAsync(true);
                }
            }
        }
        private bool CanExecuteNext() => CanGoForward; // Use the computed property


        [RelayCommand(CanExecute = nameof(CanExecuteBack))]
        private async Task GoToPreviousStepAsync(bool force = false) // Add force parameter to bypass CanExecute check
        {
             if (!CanExecuteBack() && !force)
             {
                  Log.Debug("Cannot go back from step {CurrentStep} or while loading.", CurrentStep);
                  return;
             }

            Log.Debug("GoToPreviousStep called. CurrentStep: {CurrentStep}", CurrentStep);
            IsLoading = true; // Prevent further actions during transition
            ErrorMessage = string.Empty; // Clear errors when going back

             await Dispatcher.UIThread.InvokeAsync(() => { // Ensure UI updates happen on UI thread
                if (CurrentStep == 2 || CurrentStep == 3) // Go back to Step 1 from Validation or 2FA steps
                {
                    CurrentStep = 1;
                    StepTitle = "Вход в систему";
                    StatusMessage = "Введите ваши учетные данные";
                    ProgressValue = 25; // 1 of 4 steps
                    ProgressText = "Шаг 1 из 4";
                    NextButtonText = "Далее";
                    RequiresTwoFactor = false; // Reset 2FA state
                    _tempToken = string.Empty;
                }
                // Cannot go back from step 1 or 4
                // Update command states and properties
                TriggerLoginAndAdvanceCommand.NotifyCanExecuteChanged();
                GoToNextStepCommand.NotifyCanExecuteChanged();
                GoToPreviousStepCommand.NotifyCanExecuteChanged();
                this.RaisePropertyChanged(nameof(CanGoBack));
                this.RaisePropertyChanged(nameof(CanGoForward));
            });

            IsLoading = false;
            Log.Debug("Finished going back. CurrentStep: {CurrentStep}", CurrentStep);
        }
        private bool CanExecuteBack() => CanGoBack; // Use the computed property


        [RelayCommand(CanExecute = nameof(CanExecuteCancel))]
        private void Cancel()
        {
            Log.Information("Login cancelled by user.");
            LoginCompleted?.Invoke(this, false); // Signal cancellation
            CloseAuthWindow();
        }
        private bool CanExecuteCancel() => !IsLoading;

        [RelayCommand]
        private void ShowHelp()
        {
            // Show help based on current step
            string helpMessage = CurrentStep switch
            {
                1 => "Введите имя пользователя и пароль, предоставленные администратором.",
                2 => "Система проверяет ваши учетные данные и права доступа на сервере.",
                3 => $"Введите код из приложения аутентификации ({TwoFactorType}) или используйте ваш ключ безопасности.",
                4 => "Авторизация успешно завершена. Нажмите 'Завершить' для входа.",
                _ => "Система авторизации администратора."
            };

            StatusMessage = $"Справка: {helpMessage}"; // Update status bar with help
            Log.Information("Showing help for step {CurrentStep}: {HelpMessage}", CurrentStep, helpMessage);
            // Optionally show a message box instead:
            // MessageBoxManager.GetMessageBoxStandard("Помощь", helpMessage, ButtonEnum.Ok, Icon.Info).ShowAsync();
        }

        // --- Helper Methods ---

        private bool CheckAdminPermissions(IEnumerable<Claim> claims)
        {
            // Consolidate admin checks
            bool isAdmin = claims.Any(c =>
                (c.Type == ClaimTypes.Role && c.Value.Equals("Administrator", StringComparison.OrdinalIgnoreCase)) ||
                (c.Type == "role" && c.Value == "1") || // Admin role ID
                (c.Type == "primary_role" && c.Value == "1") || // Another potential role claim
                (c.Type == "permission" && c.Value.Contains("Admin", StringComparison.OrdinalIgnoreCase)) // Permission-based
            );
            Log.Debug("Admin permission check result: {IsAdmin}", isAdmin);
            return isAdmin;
        }

        private int GetUserRole(IEnumerable<Claim> claims)
        {
            // Try to get role from claims
            var roleClaim = claims.FirstOrDefault(c => 
                c.Type == "role" || 
                c.Type == "primary_role" || 
                c.Type == ClaimTypes.Role);

            if (roleClaim != null && int.TryParse(roleClaim.Value, out int role))
            {
                Log.Debug("Found numeric role in claims: {Role}", role);
                return role;
            }

            // Map string roles to integers if necessary
            var roleString = roleClaim?.Value?.ToLowerInvariant();
            int mappedRole = roleString switch
            {
                "administrator" => 1,
                "manager" => 2,
                "dispatcher" => 3,
                "cashier" => 4,
                "driver" => 5,
                "conductor" => 6,
                "mechanic" => 7,
                "engineer" => 8,
                "controller" => 9,
                "inspector" => 10,
                _ => 0 // Default to User role
            };
            Log.Debug("Mapped string role '{RoleString}' to numeric role: {Role}", roleString, mappedRole);
            return mappedRole;
        }

        private async Task ShowNonAdminErrorAsync()
        {
             await Dispatcher.UIThread.InvokeAsync(async () =>
             {
                var messageBox = MessageBoxManager.GetMessageBoxStandard(
                    "Доступ запрещен",
                    "Это приложение предназначено только для администраторов. Пожалуйста, используйте приложение для пользователей.",
                    ButtonEnum.Ok,
                    Icon.Error);
                await messageBox.ShowAsync();
             });
        }


        private async Task HandleTwoFactorAuthentication(string twoFactorType)
        {
            // Called after API confirms 2FA is needed and CurrentStep is set to 3
            Log.Information("Handling 2FA process: Type={Type}, User={Username}", twoFactorType, Username);
            IsLoading = false; // Allow interaction with 2FA step

            try
            {
                switch (twoFactorType.ToLowerInvariant())
                {
                    case "totp":
                        Log.Debug("Handling TOTP authentication for user {Username}", Username);
                        // The UI for TOTP input should be part of Step 3 content
                        // The "Next/Confirm" button for Step 3 will trigger the validation
                         StatusMessage = "Введите код из приложения аутентификации.";
                        // We need a way to get the code entered by the user. Let's add a property.
                        break;
                    case "webauthn":
                        Log.Debug("Handling WebAuthn authentication for user {Username}", Username);
                        // Trigger WebAuthn flow (needs platform-specific interop or library)
                        StatusMessage = "Используйте ваш ключ безопасности WebAuthn.";
                        await HandleWebAuthnAuthenticationInternal(); // Call stub/implementation
                        break;
                    default:
                        Log.Warning("Unsupported 2FA type: {Type} for user {Username}", twoFactorType, Username);
                        ErrorMessage = "Неподдерживаемый тип двухфакторной аутентификации.";
                        await GoToPreviousStepAsync(true); // Go back if 2FA type is wrong
                        break;
                }
            }
            catch (Exception ex)
            {
                 Log.Error(ex, "Error initiating 2FA handler for type {Type}, user {Username}", twoFactorType, Username);
                 ErrorMessage = "Ошибка инициализации двухфакторной аутентификации.";
                 await GoToPreviousStepAsync(true); // Go back on error
            }
             finally
             {
                 // Ensure loading is false after initiation attempt
                 IsLoading = false;
                 GoToNextStepCommand.NotifyCanExecuteChanged(); // Update command state
                 this.RaisePropertyChanged(nameof(CanGoForward));
             }
        }

        // Add property for TOTP code input
        private string _totpCode = string.Empty;
        public string TotpCode
        {
            get => _totpCode;
            set => this.RaiseAndSetIfChanged(ref _totpCode, value);
        }

        // Command specifically for verifying the TOTP code (could be triggered by Step 3 Next button)
        [RelayCommand(CanExecute = nameof(CanVerifyTotp))]
        private async Task VerifyTotpAsync()
        {
            if (string.IsNullOrWhiteSpace(TotpCode))
            {
                ErrorMessage = "Введите код TOTP.";
                return;
            }

            Log.Information("Attempting TOTP verification: User={Username}, Code={Code}, TempToken={TempToken}", Username, TotpCode, _tempToken);
            IsLoading = true;
            StatusMessage = "Проверка кода TOTP...";
             ErrorMessage = string.Empty; // Clear previous errors

            try
            {
                var validateRequest = new { tempToken = _tempToken, code = TotpCode };
                Log.Debug("TOTP VALIDATION REQUEST: {RequestJson}", JsonSerializer.Serialize(validateRequest, _jsonOptions));

                    var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/Auth/totp/validate", validateRequest, _jsonOptions);
                    var responseContent = await response.Content.ReadAsStringAsync();
                Log.Debug("TOTP VALIDATION RESPONSE Status: {StatusCode}, Content: {ResponseContent}", response.StatusCode, responseContent);

                    if (response.IsSuccessStatusCode)
                    {
                     ValidateTotpResponse? result = null;
                     try
                     {
                         result = JsonSerializer.Deserialize<ValidateTotpResponse>(responseContent, _jsonOptions);
                     }
                      catch (JsonException jsonEx)
                     {
                         Log.Error(jsonEx, "JSON deserialization error during TOTP validation success path. Raw content: {Content}", responseContent);
                         ErrorMessage = "Ошибка обработки ответа сервера TOTP.";
                         IsLoading = false;
                         return; // Stay on step 3
                     }
                        
                        if (result?.Token != null)
                        {
                        Log.Information("TOTP validation successful for user {Username}", Username);
                            ApiClientService.Instance.AuthToken = result.Token;
                            IsAuthenticated = true;
                        UserInfo = $"Пользователь: {Username}\nРоль: Администратор"; // Update user info if needed

                         // Move to final success step
                        await Dispatcher.UIThread.InvokeAsync(() => {
                            CurrentStep = 4;
                            StepTitle = "Вход успешно выполнен";
                            StatusMessage = "Аутентификация TOTP прошла успешно.";
                            ProgressValue = 100; // 4 of 4 steps
                            ProgressText = "Шаг 4 из 4";
                            NextButtonText = "Завершить";
                            this.RaisePropertyChanged(nameof(CanGoBack));
                            this.RaisePropertyChanged(nameof(CanGoForward));
                        });
                    }
                    else
                    {
                        Log.Warning("TOTP validation API success but no token received for user {Username}", Username);
                        ErrorMessage = "Неверный код TOTP или ошибка сервера.";
                    }
                }
                else
                {
                    ErrorMessage = "Неверный код TOTP.";
                    Log.Error("TOTP validation failed: User={Username}, Code={Code}, StatusCode={StatusCode}, Response={Response}", Username, TotpCode, response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                 Log.Error(ex, "Exception during TOTP validation for user {Username}", Username);
                 ErrorMessage = "Ошибка проверки кода TOTP.";
            }
            finally
            {
                IsLoading = false;
                VerifyTotpCommand.NotifyCanExecuteChanged(); // Re-evaluate CanExecute
                 GoToNextStepCommand.NotifyCanExecuteChanged(); // Might change CanGoForward
                 this.RaisePropertyChanged(nameof(CanGoForward));
            }
        }
        private bool CanVerifyTotp() => !IsLoading && CurrentStep == 3 && TwoFactorType?.ToLowerInvariant() == "totp" && !string.IsNullOrWhiteSpace(TotpCode);


        private async Task HandleWebAuthnAuthenticationInternal()
        {
             // --- WebAuthn Stub ---
             Log.Warning("WebAuthn authentication flow is not implemented. User={Username}", Username);
             StatusMessage = "Аутентификация WebAuthn не реализована.";
             // Simulate failure or keep user on Step 3
             ErrorMessage = "WebAuthn временно недоступен.";
             await Task.Delay(1500); // Simulate thinking time
             // Maybe go back? Or just stay on step 3 with the error.
             // await GoToPreviousStepAsync(true);
             IsLoading = false; // Ensure loading is off
        }


        private void CloseAuthWindow()
        {
            Log.Debug("Requesting AuthWindow close.");
            // The actual closing is often handled by the Application lifetime logic
            // listening to the LoginCompleted event. This method is mostly informational now.
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            {
                 Dispatcher.UIThread.Post(() => // Ensure UI operations are on the correct thread
                 {
                     var authWindow = lifetime.Windows.FirstOrDefault(w => w.DataContext == this); // Find window by DataContext
                if (authWindow != null)
                {
                         Log.Information("Auth window found via DataContext, closing: {WindowType}", authWindow.GetType().Name);
                    authWindow.Close();
                }
                else
                {
                         Log.Warning("Could not find AuthWindow instance via DataContext to close.");
                }
                 }, DispatcherPriority.Background);
            }
        }

        // --- DTO Classes (Keep as nested or move to separate files) ---
        private class ApiResponse<T>
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
            public List<string>? Errors { get; set; }
            public T? Data { get; set; }
        }

        private class LoginResponse
        {
            public string? Token { get; set; }
            public bool RequiresTwoFactor { get; set; }
            public string TwoFactorType { get; set; } = string.Empty;
            public string TempToken { get; set; } = string.Empty;
            public UserDto? User { get; set; }
        }

        private class ValidateTotpResponse
        {
            public string Token { get; set; } = string.Empty;
            public UserDto? User { get; set; }
        }

        public class UserDto // Make public if needed elsewhere, otherwise keep private/internal
        {
            public uint Id { get; set; }
            public string Username { get; set; } = string.Empty;
            public string? Email { get; set; }
            public string? PhoneNumber { get; set; }
            public int Role { get; set; } // Assuming Role is an int from your original code
        }

        private void UpdateStepContentDirect(int step)
        {
            switch (step)
            {
                case 1:
                    _stepTitle = "Вход в систему";
                    _statusMessage = "Введите ваши учетные данные";
                    _progressValue = 25;
                    _progressText = "Шаг 1 из 4";
                    _nextButtonText = "Далее";
                    break;
                case 2:
                    _stepTitle = "Авторизация";
                    _statusMessage = "Проверка учетных данных...";
                    _progressValue = 50;
                    _progressText = "Шаг 2 из 4";
                    _nextButtonText = "Далее";
                    break;
                case 3:
                    _stepTitle = "Двухфакторная аутентификация";
                    _statusMessage = $"Требуется {TwoFactorType}.";
                    _progressValue = 75;
                    _progressText = "Шаг 3 из 4";
                    _nextButtonText = "Подтвердить";
                    break;
                case 4:
                    _stepTitle = "Вход успешно выполнен";
                    _statusMessage = "Авторизация прошла успешно.";
                    _progressValue = 100;
                    _progressText = "Шаг 4 из 4";
                    _nextButtonText = "Завершить";
                    break;
            }
            
            this.RaisePropertyChanged(nameof(StepTitle));
            this.RaisePropertyChanged(nameof(StatusMessage));
            this.RaisePropertyChanged(nameof(ProgressValue));
            this.RaisePropertyChanged(nameof(ProgressText));
            this.RaisePropertyChanged(nameof(NextButtonText));
            
            Log.Debug("Updated step content for step {Step}", step);
        }

        public void UpdateStepContent(int step)
        {
            UpdateStepContentDirect(step);
        }
    }
} 