using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
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
using Serilog;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using System.Collections.Generic;
using Avalonia.Controls.ApplicationLifetimes;
using SpacetimeDB.Types;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public partial class EmployeeManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private ObservableCollection<Employee> _employees = new();
        public ObservableCollection<Employee> Employees
        {
            get => _employees;
            set => this.RaiseAndSetIfChanged(ref _employees, value);
        }

        private ObservableCollection<Job> _jobs = new();
        public ObservableCollection<Job> Jobs
        {
            get => _jobs;
            set => this.RaiseAndSetIfChanged(ref _jobs, value);
        }

        private Employee? _selectedEmployee;
        public Employee? SelectedEmployee
        {
            get => _selectedEmployee;
            set => this.RaiseAndSetIfChanged(ref _selectedEmployee, value);
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

        public EmployeeManagementViewModel()
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

                // Load jobs first
                var jobsResponse = await _httpClient.GetAsync($"{_baseUrl}/Jobs");
                if (jobsResponse.IsSuccessStatusCode)
                {
                    var jsonString = await jobsResponse.Content.ReadAsStringAsync();
                    var loadedJobs = JsonSerializer.Deserialize<List<Job>>(jsonString, _jsonOptions);
                    if (loadedJobs != null)
                    {
                        Jobs = new ObservableCollection<Job>(loadedJobs);
                    }
                }

                // Then load employees
                var response = await _httpClient.GetAsync($"{_baseUrl}/Employees");
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var loadedEmployees = JsonSerializer.Deserialize<List<Employee>>(jsonString, _jsonOptions);
                    if (loadedEmployees != null)
                    {
                        Employees = new ObservableCollection<Employee>(loadedEmployees);
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Log.Error("Failed to load employees. Status: {StatusCode}, Error: {Error}", 
                        response.StatusCode, error);
                    throw new Exception($"Failed to load employees. Status: {response.StatusCode}, Error: {error}");
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

        [RelayCommand]
        private async Task Add()
        {
            try
            {
                var dialog = new Window
                {
                    Title = "Add Employee",
                    Width = 400,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                var surnameBox = new TextBox { Watermark = "Surname" };
                var nameBox = new TextBox { Watermark = "Name" };
                var patronymBox = new TextBox { Watermark = "Patronym" };
                var employedSincePicker = new DatePicker { SelectedDate = DateTimeOffset.Now };
                var jobComboBox = new ComboBox
                {
                    ItemsSource = Jobs,
                    DisplayMemberBinding = new global::Avalonia.Data.Binding("JobTitle")
                };

                var addButton = new Button
                {
                    Content = "Add",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                grid.Children.Add(surnameBox);
                Grid.SetRow(surnameBox, 0);
                grid.Children.Add(nameBox);
                Grid.SetRow(nameBox, 1);
                grid.Children.Add(patronymBox);
                Grid.SetRow(patronymBox, 2);
                grid.Children.Add(employedSincePicker);
                Grid.SetRow(employedSincePicker, 3);
                grid.Children.Add(jobComboBox);
                Grid.SetRow(jobComboBox, 4);
                grid.Children.Add(addButton);
                Grid.SetRow(addButton, 5);

                dialog.Content = grid;

                addButton.Click += async (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(surnameBox.Text) || 
                        string.IsNullOrWhiteSpace(nameBox.Text) ||
                        jobComboBox.SelectedItem == null)
                    {
                        ErrorMessage = "Surname, name and job are required";
                        return;
                    }

                    var selectedJob = jobComboBox.SelectedItem as Job;
                    var newEmployee = new
                    {
                        Surname = surnameBox.Text,
                        Name = nameBox.Text,
                        Patronym = patronymBox.Text ?? string.Empty,
                        JobId = selectedJob.JobId,
                    };

                    try 
                    {
                        var json = JsonSerializer.Serialize(newEmployee, _jsonOptions);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PostAsync($"{_baseUrl}/Employees", content);
                        if (response.IsSuccessStatusCode)
                        {
                            await LoadData();
                            dialog.Close();
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            ErrorMessage = $"Failed to add employee: {error}";
                            Log.Error("Failed to add employee. Status: {StatusCode}, Error: {Error}", 
                                response.StatusCode, error);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Error adding employee: {ex.Message}";
                        Log.Error(ex, "Error adding employee");
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
                ErrorMessage = $"Error adding employee: {ex.Message}";
                Log.Error(ex, "Error adding employee");
            }
        }

        [RelayCommand]
        private async Task Edit()
        {
            if (SelectedEmployee == null) return;

            try
            {
                var dialog = new Window
                {
                    Title = "Edit Employee",
                    Width = 400,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                var surnameBox = new TextBox { Text = SelectedEmployee.Surname, Watermark = "Surname" };
                var nameBox = new TextBox { Text = SelectedEmployee.Name, Watermark = "Name" };
                var patronymBox = new TextBox { Text = SelectedEmployee.Patronym, Watermark = "Patronym" };
                var employedSincePicker = new DatePicker 
                { 
                    SelectedDate = DateTimeOffset.Now
                };
                var jobComboBox = new ComboBox
                {
                    ItemsSource = Jobs,
                    DisplayMemberBinding = new global::Avalonia.Data.Binding("JobTitle"),
                    SelectedItem = Jobs.FirstOrDefault(j => j.JobId == SelectedEmployee.JobId)
                };

                var updateButton = new Button
                {
                    Content = "Update",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                grid.Children.Add(surnameBox);
                Grid.SetRow(surnameBox, 0);
                grid.Children.Add(nameBox);
                Grid.SetRow(nameBox, 1);
                grid.Children.Add(patronymBox);
                Grid.SetRow(patronymBox, 2);
                grid.Children.Add(employedSincePicker);
                Grid.SetRow(employedSincePicker, 3);
                grid.Children.Add(jobComboBox);
                Grid.SetRow(jobComboBox, 4);
                grid.Children.Add(updateButton);
                Grid.SetRow(updateButton, 5);

                dialog.Content = grid;

                updateButton.Click += async (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(surnameBox.Text) || 
                        string.IsNullOrWhiteSpace(nameBox.Text) ||
                        jobComboBox.SelectedItem == null)
                    {
                        ErrorMessage = "Surname, name and job are required";
                        return;
                    }

                    var selectedJob = jobComboBox.SelectedItem as Job;
                    var updatedEmployee = new
                    {
                        Surname = surnameBox.Text,
                        Name = nameBox.Text,
                        Patronym = patronymBox.Text,
                        JobId = selectedJob.JobId,
                    };

                    try
                    {
                        var json = JsonSerializer.Serialize(updatedEmployee, _jsonOptions);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PutAsync($"{_baseUrl}/Employees/{SelectedEmployee.EmployeeId}", content);
                        if (response.IsSuccessStatusCode)
                        {
                            await LoadData();
                            dialog.Close();
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            ErrorMessage = $"Failed to update employee: {error}";
                            Log.Error("Failed to update employee. Status: {StatusCode}, Error: {Error}", 
                                response.StatusCode, error);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Error updating employee: {ex.Message}";
                        Log.Error(ex, "Error updating employee");
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
                ErrorMessage = $"Error updating employee: {ex.Message}";
                Log.Error(ex, "Error updating employee");
            }
        }

        [RelayCommand]
        private async Task Delete()
        {
            if (SelectedEmployee == null) return;

            try
            {
                var dialog = new Window
                {
                    Title = "Confirm Delete",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("*,Auto"),
                    Margin = new Thickness(10)
                };

                var messageText = new TextBlock
                {
                    Text = $"Are you sure you want to delete employee {SelectedEmployee.Surname} {SelectedEmployee.Name}?",
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 10
                };

                var yesButton = new Button { Content = "Yes" };
                var noButton = new Button { Content = "No" };

                buttonPanel.Children.Add(yesButton);
                buttonPanel.Children.Add(noButton);

                grid.Children.Add(messageText);
                Grid.SetRow(messageText, 0);
                grid.Children.Add(buttonPanel);
                Grid.SetRow(buttonPanel, 1);

                dialog.Content = grid;

                yesButton.Click += async (s, e) =>
                {
                    var response = await _httpClient.DeleteAsync($"{_baseUrl}/Employees/{SelectedEmployee.EmployeeId}");
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        ErrorMessage = $"Failed to delete employee: {error}";
                    }
                };

                noButton.Click += (s, e) => dialog.Close();

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
                ErrorMessage = $"Error deleting employee: {ex.Message}";
                Log.Error(ex, "Error deleting employee");
            }
        }

        private void OnSearchTextChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                LoadData().ConfigureAwait(false);
                return;
            }

            var filteredEmployees = Employees.Where(e => 
                e.Surname.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                e.Patronym.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                (Jobs.FirstOrDefault(j => j.JobId == e.JobId)?.JobTitle.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();

            Employees = new ObservableCollection<Employee>(filteredEmployees);
        }
    }
} 