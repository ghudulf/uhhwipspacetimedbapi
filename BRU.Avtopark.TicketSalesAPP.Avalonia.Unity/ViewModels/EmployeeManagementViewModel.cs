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
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;
using System.Collections.Generic;
using Avalonia.Controls.ApplicationLifetimes;
using SpacetimeDB.Types;
using System.Text.Json.Nodes;
using System.Globalization;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public partial class EmployeeDisplayModel : ObservableObject
    {
        [ObservableProperty]
        private Employee _employee;

        [ObservableProperty]
        private Job? _job;

        public EmployeeDisplayModel(Employee employee, Job? job = null)
        {
            _employee = employee;
            _job = job;
        }

        public uint EmployeeId => Employee.EmployeeId;
        public string Name => Employee.Name;
        public string Surname => Employee.Surname;
        public string? Patronym => Employee.Patronym;
        public uint JobId => Employee.JobId;
        public string JobTitle => Job?.JobTitle ?? "Должность не указана";
        
        public string EmployedSinceDisplay
        {
            get
            {
                if (Employee.EmployedSince == 0) return "Не указано";
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds((long)Employee.EmployedSince);
                    return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to format EmployedSince timestamp {Timestamp}", Employee.EmployedSince);
                    return "Ошибка даты";
                }
            }
        }
    }

    public partial class EmployeeManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private List<EmployeeDisplayModel> _allEmployees = new();
        private ObservableCollection<EmployeeDisplayModel> _employees = new();
        public ObservableCollection<EmployeeDisplayModel> Employees
        {
            get => _employees;
            set => this.RaiseAndSetIfChanged(ref _employees, value);
        }

        private List<Job> _allJobs = new();
        private ObservableCollection<Job> _jobs = new();
        public ObservableCollection<Job> Jobs
        {
            get => _jobs;
            set => this.RaiseAndSetIfChanged(ref _jobs, value);
        }

        private EmployeeDisplayModel? _selectedEmployee;
        public EmployeeDisplayModel? SelectedEmployee
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
            _baseUrl = ApiClientService.Instance.CurrentBaseUrl?.TrimEnd('/') ?? "http://localhost:5000/api";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };

            // Subscribe to auth token changes
            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) =>
            {
                Log.Information("Auth token changed in EmployeeManagementViewModel. Recreating HttpClient and reloading data.");
                // Create a new client with the updated token
                _httpClient.Dispose();
                _httpClient = ApiClientService.Instance.CreateClient();
                // Reload data with the new token
                LoadData().ConfigureAwait(false);
            };

            // Only load data if token is already set, otherwise wait for OnAuthTokenChanged event
            if (!string.IsNullOrEmpty(ApiClientService.Instance.AuthToken))
            {
                Log.Information("Token already set in EmployeeManagementViewModel constructor, loading data");
                LoadData().ConfigureAwait(false);
            }
            else
            {
                Log.Warning("Token not set in EmployeeManagementViewModel constructor, waiting for OnAuthTokenChanged event");
            }
        }

        private async Task LoadData()
        {
            Log.Information("Starting LoadData for EmployeeManagementViewModel");
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                // --- Fetch all required data concurrently ---
                Log.Debug("Initiating API calls for Employees and Jobs");
                Task<HttpResponseMessage> employeesTask = _httpClient.GetAsync($"{_baseUrl}/Employees");
                Task<HttpResponseMessage> jobsTask = _httpClient.GetAsync($"{_baseUrl}/Jobs");

                // Await all tasks
                await Task.WhenAll(employeesTask, jobsTask);
                Log.Debug("All API calls completed for EmployeeManagementViewModel.");

                // --- Process Responses with Manual Parsing and Logging ---
                
                // 1. Process Jobs Response first (since Employees need Job data for display/filtering)
                List<Job> loadedJobs = new();
                var jobsResponse = await jobsTask;
                Log.Information("Processing Jobs response. Status: {StatusCode}", jobsResponse.StatusCode);
                
                var jobsJsonString = await jobsResponse.Content.ReadAsStringAsync();
                Log.Verbose("Raw Jobs response received: {RawResponse}", jobsJsonString);

                if (jobsResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        var jobsArray = JsonReferenceHelper.ParseArrayWithReferences(jobsJsonString, "Job");
                        if (jobsArray != null)
                        {
                            Log.Information("Parsing {Count} job objects from JSON array.", jobsArray.Count);
                            foreach(var jobNode in jobsArray)
                            {
                                if (jobNode is JsonObject jobObj)
                                {
                                    Log.Verbose("--- Parsing Job Object: {JobJson} ---", jobObj.ToJsonString());
                                    
                                    var job = jobObj.ParseJob();
                                    if (job == null)
                                    {
                                        Log.Warning("Failed to parse job object, skipping");
                                        continue;
                                    }

                                    loadedJobs.Add(job);
                                    Log.Verbose("Parsed Job: Id={JobId}, Title='{JobTitle}'", job.JobId, job.JobTitle);
                                }
                                else
                                {
                                    Log.Warning("Item in jobs array was not a JSON object: {Node}", jobNode?.ToJsonString());
                                }
                            }
                            Log.Information("Successfully parsed {Count} valid jobs.", loadedJobs.Count);
                        }
                        else
                        {
                            Log.Error("Jobs JSON could not be parsed as array. Raw JSON: {RawJson}", jobsJsonString);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Log.Error(jsonEx, "Failed to parse Jobs JSON: {RawJson}", jobsJsonString);
                        HasError = true;
                        ErrorMessage = "Ошибка загрузки списка должностей.";
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Unexpected error during manual job parsing.");
                        HasError = true;
                        ErrorMessage = "Непредвиденная ошибка загрузки списка должностей.";
                    }
                }
                else
                {
                    var error = await jobsResponse.Content.ReadAsStringAsync();
                    Log.Error("Failed to load jobs. Status: {StatusCode}, Error: {Error}", jobsResponse.StatusCode, error);
                    HasError = true;
                    ErrorMessage = $"Критическая ошибка загрузки должностей: {jobsResponse.StatusCode}";
                }

                _allJobs = loadedJobs;
                Jobs = new ObservableCollection<Job>(_allJobs);
                Log.Debug("Updated Jobs collection. Count: {Count}", Jobs.Count);

                // 2. Process Employees Response
                List<EmployeeDisplayModel> loadedEmployees = new();
                var employeesResponse = await employeesTask;
                Log.Information("Processing Employees response. Status: {StatusCode}", employeesResponse.StatusCode);

                var employeesJsonString = await employeesResponse.Content.ReadAsStringAsync();
                Log.Verbose("Raw Employees response received: {RawResponse}", employeesJsonString);

                if (employeesResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        var employeesArray = JsonReferenceHelper.ParseArrayWithReferences(employeesJsonString, "Employee");
                        if (employeesArray != null)
                        {
                            Log.Information("Parsing {Count} employee objects from JSON array.", employeesArray.Count);
                            foreach(var employeeNode in employeesArray)
                            {
                                if (employeeNode is JsonObject employeeObj)
                                {
                                    Log.Verbose("--- Parsing Employee Object: {EmployeeJson} ---", employeeObj.ToJsonString());
                                    
                                    var employee = employeeObj.ParseEmployee();
                                    if (employee == null)
                                    {
                                        Log.Warning("Failed to parse employee object, skipping");
                                        continue;
                                    }

                                    if (_allJobs.All(j => j.JobId != employee.JobId))
                                    {
                                        Log.Warning("Employee {EmpId} has JobId {JobId} which was not found in the loaded jobs list.", employee.EmployeeId, employee.JobId);
                                    }

                                    Log.Verbose("Parsed Employee: Id={EmployeeId}, Name='{Name}', Surname='{Surname}', JobId={JobId}",
                                        employee.EmployeeId, employee.Name, employee.Surname, employee.JobId);

                                    loadedEmployees.Add(new EmployeeDisplayModel(
                                        employee,
                                        _allJobs.FirstOrDefault(j => j.JobId == employee.JobId)
                                    ));
                                }
                                else
                                {
                                    Log.Warning("Item in employees array was not a JSON object: {Node}", employeeNode?.ToJsonString());
                                }
                            }
                            Log.Information("Successfully parsed {Count} valid employees.", loadedEmployees.Count);
                        }
                        else
                        {
                            Log.Error("Employees JSON could not be parsed as array. Raw JSON: {RawJson}", employeesJsonString);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Log.Error(jsonEx, "Failed to parse Employees JSON: {RawJson}", employeesJsonString);
                        throw new Exception("Failed to parse employees data.", jsonEx);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Unexpected error during manual employee parsing.");
                        throw;
                    }
                }
                else
                {
                    var error = await employeesResponse.Content.ReadAsStringAsync();
                    Log.Error("Failed to load employees. Status: {StatusCode}, Error: {Error}", employeesResponse.StatusCode, error);
                    throw new Exception($"Failed to load primary employees data. Status: {employeesResponse.StatusCode}");
                }

                // Update the observable collections
                _allEmployees = loadedEmployees;
                Employees = new ObservableCollection<EmployeeDisplayModel>(_allEmployees);

                Log.Information("Finished processing data. Displaying {JobCount} jobs and {EmployeeCount} employees.", 
                    Jobs.Count, Employees.Count);
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Критическая ошибка загрузки данных: {ex.Message}";
                Log.Fatal(ex, "Fatal error loading data in EmployeeManagementViewModel");
                _allJobs = new List<Job>();
                Jobs = new ObservableCollection<Job>();
                _allEmployees = new List<EmployeeDisplayModel>();
                Employees = new ObservableCollection<EmployeeDisplayModel>();
            }
            finally
            {
                IsBusy = false;
                Log.Information("LoadData finished for EmployeeManagementViewModel.");
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

                var surnameBox = new TextBox { PlaceholderText = "Surname" };
                var nameBox = new TextBox { PlaceholderText = "Name" };
                var patronymBox = new TextBox { PlaceholderText = "Patronym" };
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
                        Patronym = patronymBox.Text,
                        JobId = selectedJob.JobId
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

            Log.Information("Edit command initiated for employee: {EmployeeId}", SelectedEmployee.EmployeeId);

            try
            {
                var dialog = new Window
                {
                    Title = $"Редактировать сотрудника: {SelectedEmployee.Surname} {SelectedEmployee.Name}",
                    Width = 400,
                    Height = 350,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    Margin = new Thickness(10)
                };

                grid.Children.Add(new TextBlock { Text = "Фамилия:", Margin = new Thickness(0, 5), VerticalAlignment = VerticalAlignment.Center });
                var surnameBox = new TextBox { Text = SelectedEmployee.Surname };
                Grid.SetRow(surnameBox, 0); Grid.SetColumn(surnameBox, 1);
                grid.Children.Add(surnameBox);

                grid.Children.Add(new TextBlock { Text = "Имя:", Margin = new Thickness(0, 5), VerticalAlignment = VerticalAlignment.Center });
                var nameBox = new TextBox { Text = SelectedEmployee.Name };
                Grid.SetRow(nameBox, 1); Grid.SetColumn(nameBox, 1);
                grid.Children.Add(nameBox);

                grid.Children.Add(new TextBlock { Text = "Отчество:", Margin = new Thickness(0, 5), VerticalAlignment = VerticalAlignment.Center });
                var patronymBox = new TextBox { Text = SelectedEmployee.Patronym ?? "" };
                Grid.SetRow(patronymBox, 2); Grid.SetColumn(patronymBox, 1);
                grid.Children.Add(patronymBox);

                grid.Children.Add(new TextBlock { Text = "Должность:", Margin = new Thickness(0, 5), VerticalAlignment = VerticalAlignment.Center });
                var jobComboBox = new ComboBox
                {
                    ItemsSource = Jobs,
                    DisplayMemberBinding = new global::Avalonia.Data.Binding("JobTitle"),
                    SelectedItem = Jobs.FirstOrDefault(j => j.JobId == SelectedEmployee.JobId)
                };
                Grid.SetRow(jobComboBox, 3); Grid.SetColumn(jobComboBox, 1);
                grid.Children.Add(jobComboBox);

                grid.Children.Add(new TextBlock { Text = "Дата приема:", Margin = new Thickness(0, 5), VerticalAlignment = VerticalAlignment.Center });
                var employedSinceBox = new TextBox { Text = SelectedEmployee.EmployedSinceDisplay, IsReadOnly = true };
                Grid.SetRow(employedSinceBox, 4); Grid.SetColumn(employedSinceBox, 1);
                grid.Children.Add(employedSinceBox);

                var updateButton = new Button
                {
                    Content = "Обновить",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 15, 0, 0)
                };
                Grid.SetRow(updateButton, 6); Grid.SetColumnSpan(updateButton, 2);
                grid.Children.Add(updateButton);

                dialog.Content = grid;

                updateButton.Click += async (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(surnameBox.Text) || string.IsNullOrWhiteSpace(nameBox.Text))
                    {
                        var errorDialog = MessageBoxManager.GetMessageBoxStandard(
                                "Ошибка", "Фамилия и Имя обязательны.", ButtonEnum.Ok, Icon.Error);
                        var mw = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                        if (mw != null) await errorDialog.ShowAsync();
                        return;
                    }

                    var selectedJob = jobComboBox.SelectedItem as Job;
                    if (selectedJob == null)
                    {
                         var errorDialog = MessageBoxManager.GetMessageBoxStandard(
                                "Ошибка", "Необходимо выбрать должность.", ButtonEnum.Ok, Icon.Error);
                        var mw = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                        if (mw != null) await errorDialog.ShowAsync();
                        return;
                    }

                    var updateEmployee = new
                    {
                        Surname = surnameBox.Text,
                        Name = nameBox.Text,
                        Patronym = string.IsNullOrWhiteSpace(patronymBox.Text) ? null : patronymBox.Text,
                        JobId = selectedJob.JobId
                    };

                    var json = JsonSerializer.Serialize(updateEmployee, _jsonOptions);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    Log.Information("Sending PUT request to update employee {EmployeeId}: {JsonPayload}", SelectedEmployee.EmployeeId, json);
                    var response = await _httpClient.PutAsync($"{_baseUrl}/Employees/{SelectedEmployee.EmployeeId}", content);

                    if (response.IsSuccessStatusCode)
                    {
                        Log.Information("Successfully updated employee {EmployeeId}", SelectedEmployee.EmployeeId);
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        Log.Error("Failed to update employee {EmployeeId}. Status: {StatusCode}, Error: {Error}", SelectedEmployee.EmployeeId, response.StatusCode, error);
                        ErrorMessage = $"Ошибка обновления сотрудника: {error}";
                        HasError = true;

                        var errorDialog = MessageBoxManager.GetMessageBoxStandard(
                                "Ошибка", $"Не удалось обновить сотрудника: {error}", ButtonEnum.Ok, Icon.Error);
                        var mw = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                        if (mw != null) await errorDialog.ShowAsync();
                    }
                };

                var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                if (ownerWindow != null)
                {
                     Log.Debug("Showing edit dialog for employee {EmployeeId}", SelectedEmployee.EmployeeId);
                     await dialog.ShowDialog(ownerWindow);
                }
                else
                {
                    Log.Error("Could not find main window to show edit dialog");
                }
            }
            catch (Exception ex)
            {
                 Log.Error(ex, "Exception occurred during edit operation for employee {EmployeeId}", SelectedEmployee?.EmployeeId ?? 0);
                 HasError = true;
                 ErrorMessage = $"Ошибка редактирования: {ex.Message}";
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
            Log.Debug("Search text changed: '{SearchText}'", value);
            if (string.IsNullOrWhiteSpace(value))
            {
                Log.Debug("Search text is empty, resetting filter.");
                Employees = new ObservableCollection<EmployeeDisplayModel>(_allEmployees);
                return;
            }

            var lowerCaseValue = value.ToLowerInvariant();
            var filteredEmployees = _allEmployees.Where(e =>
                (e.EmployeeId.ToString().Contains(lowerCaseValue)) ||
                (e.Surname?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (e.Name?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (e.Patronym?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (_allJobs.FirstOrDefault(j => j.JobId == e.JobId)?.JobTitle?.ToLowerInvariant().Contains(lowerCaseValue) ?? false)
            ).ToList();

            Log.Information("Filtering complete. Found {Count} employees matching '{SearchText}'", filteredEmployees.Count, value);
            Employees = new ObservableCollection<EmployeeDisplayModel>(filteredEmployees);
        }
    }
} 