using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Text.Json;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;

using Avalonia.Controls;
using System.Linq;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using Serilog;

using System.Collections.Generic;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml.Templates;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using SpacetimeDB.Types;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public partial class UserManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private ObservableCollection<UserProfile> _users = new();
        public ObservableCollection<UserProfile> Users
        {
            get => _users;
            set => this.RaiseAndSetIfChanged(ref _users, value);
        }

        private ObservableCollection<Role> _roles = new();
        public ObservableCollection<Role> Roles
        {
            get => _roles;
            set => this.RaiseAndSetIfChanged(ref _roles, value);
        }

        private ObservableCollection<Permission> _permissions = new();
        public ObservableCollection<Permission> Permissions
        {
            get => _permissions;
            set => this.RaiseAndSetIfChanged(ref _permissions, value);
        }

        private UserProfile? _selectedUser;
        public UserProfile? SelectedUser
        {
            get => _selectedUser;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedUser, value);
                LoadUserRolesAndPermissions().ConfigureAwait(false);
            }
        }

        private Role? _selectedRole;
        public Role? SelectedRole
        {
            get => _selectedRole;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedRole, value);
                LoadRolePermissions().ConfigureAwait(false);
            }
        }

        private ObservableCollection<Permission> _selectedUserPermissions = new();
        public ObservableCollection<Permission> SelectedUserPermissions
        {
            get => _selectedUserPermissions;
            set => this.RaiseAndSetIfChanged(ref _selectedUserPermissions, value);
        }

        private ObservableCollection<Permission> _selectedRolePermissions = new();
        public ObservableCollection<Permission> SelectedRolePermissions
        {
            get => _selectedRolePermissions;
            set => this.RaiseAndSetIfChanged(ref _selectedRolePermissions, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                this.RaiseAndSetIfChanged(ref _searchText, value);
                OnSearchTextChanged(value);
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set => this.RaiseAndSetIfChanged(ref _hasError, value);
        }

        public UserManagementViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = "http://localhost:5000/api";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };

            // Subscribe to auth token changes
            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) =>
            {
                // Create a new client with the updated token
                _httpClient.Dispose();
                _httpClient = ApiClientService.Instance.CreateClient();
                // Reload data with the new token
                LoadData().ConfigureAwait(false);
            };

            LoadData().ConfigureAwait(false);
        }

        [RelayCommand]
        private async Task LoadData()
        {
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                // Load Users
                var usersResponse = await _httpClient.GetAsync($"{_baseUrl}/Users");
                if (usersResponse.IsSuccessStatusCode)
                {
                    var jsonString = await usersResponse.Content.ReadAsStringAsync();
                    var loadedUsers = JsonSerializer.Deserialize<List<UserProfile>>(jsonString, _jsonOptions);
                    if (loadedUsers != null)
                    {
                        Users = new ObservableCollection<UserProfile>(loadedUsers);
                    }
                }

                // Load Roles
                var rolesResponse = await _httpClient.GetAsync($"{_baseUrl}/Roles");
                if (rolesResponse.IsSuccessStatusCode)
                {
                    var jsonString = await rolesResponse.Content.ReadAsStringAsync();
                    var loadedRoles = JsonSerializer.Deserialize<List<Role>>(jsonString, _jsonOptions);
                    if (loadedRoles != null)
                    {
                        Roles = new ObservableCollection<Role>(loadedRoles);
                    }
                }

                // Load Permissions
                var permissionsResponse = await _httpClient.GetAsync($"{_baseUrl}/Permissions");
                if (permissionsResponse.IsSuccessStatusCode)
                {
                    var jsonString = await permissionsResponse.Content.ReadAsStringAsync();
                    var loadedPermissions = JsonSerializer.Deserialize<List<Permission>>(jsonString, _jsonOptions);
                    if (loadedPermissions != null)
                    {
                        Permissions = new ObservableCollection<Permission>(loadedPermissions);
                    }
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error loading data: {ex.Message}";
                Log.Error(ex, "Error loading data");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadUserRolesAndPermissions()
        {
            if (SelectedUser == null) return;

            try
            {
                // Load user permissions
                var permissionsResponse = await _httpClient.GetAsync($"{_baseUrl}/Users/{SelectedUser.LegacyUserId}/permissions");
                if (permissionsResponse.IsSuccessStatusCode)
                {
                    var jsonString = await permissionsResponse.Content.ReadAsStringAsync();
                    var permissions = JsonSerializer.Deserialize<List<Permission>>(jsonString, _jsonOptions);
                    if (permissions != null)
                    {
                        SelectedUserPermissions = new ObservableCollection<Permission>(permissions);
                    }
                }
                else
                {
                    Log.Warning("Failed to load permissions for user {UserId}. Status: {StatusCode}", 
                        SelectedUser.LegacyUserId, permissionsResponse.StatusCode);
                }

                // Load user roles
                var rolesResponse = await _httpClient.GetAsync($"{_baseUrl}/Users/{SelectedUser.LegacyUserId}/roles");
                if (rolesResponse.IsSuccessStatusCode)
                {
                    var jsonString = await rolesResponse.Content.ReadAsStringAsync();
                    var userRoles = JsonSerializer.Deserialize<List<Role>>(jsonString, _jsonOptions);
                    if (userRoles != null)
                    {
                        // Update the roles collection to highlight the user's roles
                        var allRoles = Roles.ToList();
                        foreach (var role in allRoles)
                        {
                            role.IsActive = userRoles.Any(ur => ur.RoleId == role.RoleId);
                        }
                        Roles = new ObservableCollection<Role>(allRoles);
                    }
                }
                else
                {
                    Log.Warning("Failed to load roles for user {UserId}. Status: {StatusCode}", 
                        SelectedUser.LegacyUserId, rolesResponse.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading user roles and permissions for user {UserId}", SelectedUser.LegacyUserId);
                HasError = true;
                ErrorMessage = $"Error loading user roles and permissions: {ex.Message}";
            }
        }

        private async Task LoadRolePermissions()
        {
            if (SelectedRole == null) return;

            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/Roles/{SelectedRole.RoleId}/permissions");
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var permissions = JsonSerializer.Deserialize<List<Permission>>(jsonString, _jsonOptions);
                    if (permissions != null)
                    {
                        SelectedRolePermissions = new ObservableCollection<Permission>(permissions);
                    }
                }
                else
                {
                    Log.Warning("Failed to load permissions for role {RoleId}. Status: {StatusCode}", 
                        SelectedRole.RoleId, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading role permissions for role {RoleId}", SelectedRole.RoleId);
                HasError = true;
                ErrorMessage = $"Error loading role permissions: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task AssignRole(Role role)
        {
            if (SelectedUser == null || role == null) return;

            try
            {
                var model = new { RoleId = role.RoleId };
                var json = JsonSerializer.Serialize(model);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(
                    $"{_baseUrl}/Users/{SelectedUser.LegacyUserId}/roles", 
                    content);

                if (response.IsSuccessStatusCode)
                {
                    await LoadUserRolesAndPermissions();
                    Log.Information("Successfully assigned role {RoleId} to user {UserId}", 
                        role.RoleId, SelectedUser.LegacyUserId);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Log.Warning("Failed to assign role {RoleId} to user {UserId}. Error: {Error}", 
                        role.RoleId, SelectedUser.LegacyUserId, error);
                    HasError = true;
                    ErrorMessage = $"Failed to assign role: {error}";

                    var errorDialog = MessageBoxManager
                        .GetMessageBoxStandard(
                            "Ошибка",
                            $"Не удалось назначить роль: {error}",
                            ButtonEnum.Ok,
                            Icon.Error);

                    var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;

                    if (mainWindow != null)
                    {
                        await errorDialog.ShowAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error assigning role {RoleId} to user {UserId}", 
                    role.RoleId, SelectedUser.LegacyUserId);
                HasError = true;
                ErrorMessage = $"Error assigning role: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task RemoveRole(Role role)
        {
            if (SelectedUser == null || role == null) return;

            try
            {
                var response = await _httpClient.DeleteAsync(
                    $"{_baseUrl}/Users/{SelectedUser.LegacyUserId}/roles/{role.RoleId}");

                if (response.IsSuccessStatusCode)
                {
                    await LoadUserRolesAndPermissions();
                    Log.Information("Successfully removed role {RoleId} from user {UserId}", 
                        role.RoleId, SelectedUser.LegacyUserId);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Log.Warning("Failed to remove role {RoleId} from user {UserId}. Error: {Error}", 
                        role.RoleId, SelectedUser.LegacyUserId, error);
                    HasError = true;
                    ErrorMessage = $"Failed to remove role: {error}";

                    var errorDialog = MessageBoxManager
                        .GetMessageBoxStandard(
                            "Ошибка",
                            $"Не удалось удалить роль: {error}",
                            ButtonEnum.Ok,
                            Icon.Error);

                    var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;

                    if (mainWindow != null)
                    {
                        await errorDialog.ShowAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error removing role {RoleId} from user {UserId}", 
                    role.RoleId, SelectedUser.LegacyUserId);
                HasError = true;
                ErrorMessage = $"Error removing role: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task Add()
        {
            try
            {
                var dialog = new Window
                {
                    Title = "Add User",
                    Width = 400,
                    Height = 350,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                // Login field
                var loginLabel = new TextBlock { Text = "Login:", Margin = new Thickness(0, 0, 0, 5) };
                var loginBox = new TextBox { Watermark = "Enter login" };
                grid.Children.Add(loginLabel);
                Grid.SetRow(loginLabel, 0);
                grid.Children.Add(loginBox);
                Grid.SetRow(loginBox, 1);

                // Password field
                var passwordLabel = new TextBlock { Text = "Password:", Margin = new Thickness(0, 10, 0, 5) };
                var passwordBox = new TextBox { Watermark = "Enter password", PasswordChar = '*' };
                grid.Children.Add(passwordLabel);
                Grid.SetRow(passwordLabel, 2);
                grid.Children.Add(passwordBox);
                Grid.SetRow(passwordBox, 3);

                // Role selection
                var rolePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10), Spacing = 10 };
                
                // Legacy role selection
                var legacyRolePanel = new StackPanel { Orientation = Orientation.Vertical, Width = 180 };
                var legacyRoleLabel = new TextBlock { Text = "Legacy Role:", Margin = new Thickness(0, 0, 0, 5) };
                var legacyRoleComboBox = new ComboBox();
                var legacyRoles = new[] { "User", "Admin" };
                foreach (var role in legacyRoles)
                {
                    legacyRoleComboBox.Items.Add(role);
                }
                legacyRoleComboBox.SelectedIndex = 0;
                legacyRolePanel.Children.Add(legacyRoleLabel);
                legacyRolePanel.Children.Add(legacyRoleComboBox);

                // New role selection
                var newRolePanel = new StackPanel { Orientation = Orientation.Vertical, Width = 180 };
                var newRoleLabel = new TextBlock { Text = "New Role:", Margin = new Thickness(0, 0, 0, 5) };
                var newRoleComboBox = new ComboBox 
                { 
                    MinWidth = 150,
                    MaxDropDownHeight = 300,
                    ItemsSource = Roles,
                    DisplayMemberBinding = new Binding("Name")
                };

                newRoleComboBox.SelectedIndex = 0;
                newRolePanel.Children.Add(newRoleLabel);
                newRolePanel.Children.Add(newRoleComboBox);

                rolePanel.Children.Add(legacyRolePanel);
                rolePanel.Children.Add(newRolePanel);
                grid.Children.Add(rolePanel);
                Grid.SetRow(rolePanel, 4);

                // Add button
                var addButton = new Button
                {
                    Content = "Add User",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                grid.Children.Add(addButton);
                Grid.SetRow(addButton, 5);

                dialog.Content = grid;

                addButton.Click += async (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(loginBox.Text) || string.IsNullOrWhiteSpace(passwordBox.Text))
                    {
                        ErrorMessage = "Login and password are required";
                        return;
                    }

                    var selectedRole = newRoleComboBox.SelectedItem as Role;
                    if (selectedRole == null)
                    {
                        ErrorMessage = "Please select a role";
                        return;
                    }

                    var newUser = new
                    {
                        Login = loginBox.Text,
                        Password = passwordBox.Text,
                        Role = legacyRoleComboBox.SelectedIndex,
                        RoleId = selectedRole.RoleId
                    };

                    var json = JsonSerializer.Serialize(newUser);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync($"{_baseUrl}/Users", content);
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        ErrorMessage = $"Failed to add user: {error}";

                        var errorDialog = MessageBoxManager
                            .GetMessageBoxStandard(
                                "Ошибка",
                                $"Не удалось создать пользователя: {error}",
                                ButtonEnum.Ok,
                                Icon.Error);

                        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                            ? desktop.MainWindow
                            : null;

                        if (mainWindow != null)
                        {
                            await errorDialog.ShowAsync();
                        }
                    }
                };

                // Get the main window as owner
                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                }
                else
                {
                    Log.Error("Could not find main window for dialog");
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error adding user: {ex.Message}";
                Log.Error(ex, "Error adding user");
            }
        }

        [RelayCommand]
        private async Task Edit()
        {
            if (SelectedUser == null) return;

            try
            {
                var dialog = new Window
                {
                    Title = "Редактировать пользователя",
                    Width = 400,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10),
                    ColumnDefinitions = new ColumnDefinitions("Auto,*")
                };

                // Login
                grid.Children.Add(new TextBlock { Text = "Логин:", Margin = new Thickness(0, 5) });
                var loginBox = new TextBox { Text = SelectedUser.Login };
                Grid.SetRow(loginBox, 0); Grid.SetColumn(loginBox, 1);
                grid.Children.Add(loginBox);

                // Password
                grid.Children.Add(new TextBlock { Text = "Новый пароль:", Margin = new Thickness(0, 5) });
                var passwordBox = new TextBox { Watermark = "Оставьте пустым, чтобы не менять", PasswordChar = '*' };
                Grid.SetRow(passwordBox, 1); Grid.SetColumn(passwordBox, 1);
                grid.Children.Add(passwordBox);

                // Legacy Role (Keep for now if API still uses it, otherwise remove)
                grid.Children.Add(new TextBlock { Text = "Legacy Role:", Margin = new Thickness(0, 5) });
                var legacyRoleComboBox = new ComboBox();
                var legacyRoles = new[] { "User", "Admin" };
                //legacyRoleComboBox.Items = legacyRoles;
                //legacyRoleComboBox.SelectedIndex = SelectedUser.Role;
                Grid.SetRow(legacyRoleComboBox, 2); Grid.SetColumn(legacyRoleComboBox, 1);
                grid.Children.Add(legacyRoleComboBox);

                // New Role
                grid.Children.Add(new TextBlock { Text = "Роль:", Margin = new Thickness(0, 5) });
                var newRoleComboBox = new ComboBox
                {
                    ItemsSource = Roles,
                    DisplayMemberBinding = new Binding("Name"),
                    //SelectedItem = Roles.FirstOrDefault(r => r.RoleId == SelectedUser.RoleId)
                };
                Grid.SetRow(newRoleComboBox, 3); Grid.SetColumn(newRoleComboBox, 1);
                grid.Children.Add(newRoleComboBox);

                // Email
                grid.Children.Add(new TextBlock { Text = "Email:", Margin = new Thickness(0, 5) });
                var emailBox = new TextBox { Text = SelectedUser.Email ?? "" };
                Grid.SetRow(emailBox, 4); Grid.SetColumn(emailBox, 1);
                grid.Children.Add(emailBox);

                // Phone Number
                grid.Children.Add(new TextBlock { Text = "Телефон:", Margin = new Thickness(0, 5) });
                var phoneBox = new TextBox { Text = SelectedUser.PhoneNumber ?? "" };
                Grid.SetRow(phoneBox, 5); Grid.SetColumn(phoneBox, 1);
                grid.Children.Add(phoneBox);

                // Is Active
                grid.Children.Add(new TextBlock { Text = "Активен:", Margin = new Thickness(0, 5) });
                var isActiveCheckBox = new CheckBox { IsChecked = SelectedUser.IsActive };
                Grid.SetRow(isActiveCheckBox, 6); Grid.SetColumn(isActiveCheckBox, 1);
                grid.Children.Add(isActiveCheckBox);

                var updateButton = new Button
                {
                    Content = "Обновить",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 15, 0, 0)
                };
                 Grid.SetRow(updateButton, 8); Grid.SetColumnSpan(updateButton, 2);
                grid.Children.Add(updateButton);

                dialog.Content = grid;

                updateButton.Click += async (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(loginBox.Text))
                    {
                        var errorDialog = MessageBoxManager
                            .GetMessageBoxStandard(
                                "Ошибка",
                                "Логин обязателен",
                                ButtonEnum.Ok,
                                Icon.Error);

                        // Get the main window for error dialog
                        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
                            ? lifetime.MainWindow
                            : null;

                        if (mainWindow != null)
                        {
                            await errorDialog.ShowAsync();
                        }
                        return;
                    }

                    var selectedRole = newRoleComboBox.SelectedItem as Role;
                    if (selectedRole == null)
                    {
                        // Handle error: Role must be selected
                        return;
                    }

                    var updateUser = new
                    {
                        Login = loginBox.Text,
                        Password = string.IsNullOrWhiteSpace(passwordBox.Text) ? null : passwordBox.Text,
                        RoleId = selectedRole.RoleId,
                        Email = string.IsNullOrWhiteSpace(emailBox.Text) ? null : emailBox.Text,
                        PhoneNumber = string.IsNullOrWhiteSpace(phoneBox.Text) ? null : phoneBox.Text,
                        IsActive = isActiveCheckBox.IsChecked
                    };

                    var json = JsonSerializer.Serialize(updateUser, _jsonOptions);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PutAsync($"{_baseUrl}/Users/{SelectedUser.LegacyUserId}", content);
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                        {
                            var errorDialog = MessageBoxManager
                                .GetMessageBoxStandard(
                                    "Ошибка",
                                    error,
                                    ButtonEnum.Ok,
                                    Icon.Error);

                            // Get the main window for error dialog
                            var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime app
                                ? app.MainWindow
                                : null;

                            if (mainWindow != null)
                            {
                                await errorDialog.ShowAsync();
                            }
                        }
                        else
                        {
                            ErrorMessage = $"Failed to update user: {error}";
                        }
                    }
                };

                // Get the main window as owner for edit dialog
                var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (ownerWindow != null)
                {
                    await dialog.ShowDialog(ownerWindow);
                }
                else
                {
                    Log.Error("Could not find main window for dialog");
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error updating user: {ex.Message}";
                Log.Error(ex, "Error updating user");
            }
        }

        [RelayCommand]
        private async Task Delete()
        {
            if (SelectedUser == null) return;

            try
            {
                // Get current user ID from stored token
                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(ApiClientService.Instance.AuthToken);
                var currentUserIdStr = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

                // Prevent deleting yourself
                if (SelectedUser.LegacyUserId.ToString() == currentUserIdStr)
                {
                    var errorDialog = MessageBoxManager
                        .GetMessageBoxStandard(
                            "Ошибка",
                            "Вы не можете удалить свою собственную учетную запись.",
                            ButtonEnum.Ok,
                            Icon.Error);

                    // Get the main window as owner
                    var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;

                    if (mainWindow != null)
                    {
                        await errorDialog.ShowAsync();
                    }
                    return;
                }

                var dialog = new Window
                {
                    Title = "Подтверждение удаления",
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto"),
                    Margin = new Thickness(20)
                };

                var messageText = new TextBlock
                {
                    Text = $"Вы уверены, что хотите удалить пользователя {SelectedUser.Login}?",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 20)
                };

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 10
                };

                var yesButton = new Button { Content = "Да" };
                var noButton = new Button { Content = "Нет" };

                buttonPanel.Children.Add(yesButton);
                buttonPanel.Children.Add(noButton);

                grid.Children.Add(messageText);
                Grid.SetRow(messageText, 0);
                grid.Children.Add(buttonPanel);
                Grid.SetRow(buttonPanel, 1);

                dialog.Content = grid;

                yesButton.Click += async (s, e) =>
                {
                    var response = await _httpClient.DeleteAsync($"{_baseUrl}/Users/{SelectedUser.LegacyUserId}");
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                        {
                            var errorMessageBox = MessageBoxManager
                                .GetMessageBoxStandard(
                                    "Ошибка",
                                    error,
                                    ButtonEnum.Ok,
                                    Icon.Error);

                            // Get the main window as owner for error dialog
                            var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
                                ? lifetime.MainWindow
                                : null;

                            if (mainWindow != null)
                            {
                                await errorMessageBox.ShowAsync();
                            }
                        }
                        else
                        {
                            ErrorMessage = $"Failed to delete user: {error}";
                        }
                    }
                };

                noButton.Click += (s, e) => dialog.Close();

                // Get the main window as owner for confirmation dialog
                var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime app
                    ? app.MainWindow
                    : null;

                if (ownerWindow != null)
                {
                    await dialog.ShowDialog(ownerWindow);
                }
                else
                {
                    Log.Error("Could not find main window for dialog");
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error deleting user: {ex.Message}";
                Log.Error(ex, "Error deleting user");
            }
        }

        private void OnSearchTextChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                LoadData().ConfigureAwait(false);
                return;
            }

            var filteredUsers = Users.Where(u => 
                u.Login.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                u.LegacyUserId.ToString().Contains(value) ||
                (u.Email?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.PhoneNumber?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();

            Users = new ObservableCollection<UserProfile>(filteredUsers);
        }
    }
}