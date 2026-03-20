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
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Globalization;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public partial class MaintenanceDisplayModel : ObservableObject
    {
        [ObservableProperty]
        private Maintenance _maintenance;

        [ObservableProperty]
        private Bus? _bus;

        public MaintenanceDisplayModel(Maintenance maintenance, Bus? bus = null)
        {
            _maintenance = maintenance;
            _bus = bus;
        }

        public uint MaintenanceId => Maintenance.MaintenanceId;
        public string? ServiceEngineer => Maintenance.ServiceEngineer;
        public string? FoundIssues => Maintenance.FoundIssues;
        public string? Roadworthiness => Maintenance.Roadworthiness;
        public string? MaintenanceType => Maintenance.MaintenanceType;
        public string? MileageThreshold => Maintenance.MileageThreshold;
        public double MaintenanceCost => Maintenance.MaintenanceCost;
        public double? PartsCost => Maintenance.PartsCost;
        public double? LaborCost => Maintenance.LaborCost;
        public string? MaintenanceStatus => Maintenance.MaintenanceStatus;

        public string? BusModel => Bus?.Model ?? "Неизвестный автобус";
        public string? BusRegistrationNumber => Bus?.RegistrationNumber ?? "Н/Д";

        public string LastServiceDateDisplay
        {
            get
            {
                if (Maintenance.LastServiceDate == 0) return "Не указано";
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds((long)Maintenance.LastServiceDate);
                    return date.ToString("dd.MM.yyyy HH:mm");
                }
                catch
                {
                    return "Ошибка даты";
                }
            }
        }

        public string NextServiceDateDisplay
        {
            get
            {
                if (Maintenance.NextServiceDate == 0) return "Не указано";
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds((long)Maintenance.NextServiceDate);
                    return date.ToString("dd.MM.yyyy HH:mm");
                }
                catch
                {
                    return "Ошибка даты";
                }
            }
        }

        public double TotalMaintenanceCost
        {
            get
            {
                double total = MaintenanceCost;
                if (LaborCost.HasValue) total += LaborCost.Value;
                if (PartsCost.HasValue) total += PartsCost.Value;
                return total;
            }
        }
    }

    public partial class MaintenanceManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private List<MaintenanceDisplayModel> _allMaintenanceRecords = new();
        private ObservableCollection<MaintenanceDisplayModel> _maintenanceRecords = new();
        public ObservableCollection<MaintenanceDisplayModel> MaintenanceRecords
        {
            get => _maintenanceRecords;
            set => this.RaiseAndSetIfChanged(ref _maintenanceRecords, value);
        }

        private List<Bus> _allBuses = new();
        private ObservableCollection<Bus> _buses = new();
        public ObservableCollection<Bus> Buses
        {
            get => _buses;
            set => this.RaiseAndSetIfChanged(ref _buses, value);
        }

        private MaintenanceDisplayModel? _selectedRecord;
        public MaintenanceDisplayModel? SelectedRecord
        {
            get => _selectedRecord;
            set => this.RaiseAndSetIfChanged(ref _selectedRecord, value);
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

        public MaintenanceManagementViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = ApiClientService.Instance.CurrentBaseUrl?.TrimEnd('/') ?? "http://localhost:5000/api";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };

            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) =>
            {
                Log.Information("Auth token changed in MaintenanceManagementViewModel. Recreating HttpClient and reloading data.");
                _httpClient.Dispose();
                _httpClient = ApiClientService.Instance.CreateClient();
                LoadData().ConfigureAwait(false);
            };

            // Only load data if token is already set, otherwise wait for OnAuthTokenChanged event
            if (!string.IsNullOrEmpty(ApiClientService.Instance.AuthToken))
            {
                Log.Information("Token already set in MaintenanceManagementViewModel constructor, loading data");
                LoadData().ConfigureAwait(false);
            }
            else
            {
                Log.Warning("Token not set in MaintenanceManagementViewModel constructor, waiting for OnAuthTokenChanged event");
            }
        }

        [RelayCommand]
        private async Task LoadData()
        {
            Log.Information("Starting LoadData for MaintenanceManagementViewModel");
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                Log.Debug("Initiating API calls for Maintenance and Buses");
                Task<HttpResponseMessage> maintenanceTask = _httpClient.GetAsync($"{_baseUrl}/Maintenance");
                Task<HttpResponseMessage> busesTask = _httpClient.GetAsync($"{_baseUrl}/Buses");

                await Task.WhenAll(maintenanceTask, busesTask);
                Log.Debug("All API calls completed for MaintenanceManagementViewModel.");

                // --- 1. Process Buses Response first ---
                List<Bus> loadedBuses = new();
                var busesResponse = await busesTask;
                Log.Information("Processing Buses response. Status: {StatusCode}", busesResponse.StatusCode);
                var busesJsonString = await busesResponse.Content.ReadAsStringAsync();
                Log.Verbose("Raw Buses JSON received: {RawJson}", busesJsonString);

                if (busesResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        var busesArray = JsonReferenceHelper.ParseArrayWithReferences(busesJsonString, "Bus");
                        Log.Information("Parsing {Count} bus objects from JSON array.", busesArray.Count);
                        
                        foreach (var busNode in busesArray)
                        {
                            if (busNode is JsonObject busObj)
                            {
                                var bus = busObj.ParseBus();
                                if (bus == null) continue;
                                
                                loadedBuses.Add(bus);
                            }
                        }
                        Log.Information("Successfully parsed {Count} valid buses.", loadedBuses.Count);
                    }
                    catch (JsonException jsonEx)
                    {
                        Log.Error(jsonEx, "Failed to parse Buses JSON: {RawJson}", busesJsonString);
                         HasError = true;
                         ErrorMessage = "Ошибка загрузки списка автобусов.";
                    }
                     catch (Exception ex)
                     {
                          Log.Error(ex, "Unexpected error during manual bus parsing.");
                          HasError = true;
                          ErrorMessage = "Непредвиденная ошибка загрузки списка автобусов.";
                     }
                }
                else
                {
                    var error = await busesResponse.Content.ReadAsStringAsync();
                    Log.Error("Failed to load buses. Status: {StatusCode}, Error: {Error}", busesResponse.StatusCode, error);
                     HasError = true;
                     ErrorMessage = $"Критическая ошибка загрузки автобусов: {busesResponse.StatusCode}";
                    // Optionally throw if buses are absolutely required
                }
                _allBuses = loadedBuses;
                Buses = new ObservableCollection<Bus>(_allBuses);
                Log.Debug("Updated Buses collection. Count: {Count}", Buses.Count);

                // --- 2. Process Maintenance Response ---
                List<Maintenance> loadedMaintenance = new();
                var maintenanceResponse = await maintenanceTask;
                Log.Information("Processing Maintenance response. Status: {StatusCode}", maintenanceResponse.StatusCode);

                var maintenanceJsonString = await maintenanceResponse.Content.ReadAsStringAsync();
                Log.Verbose("Raw Maintenance response received: {RawResponse}", maintenanceJsonString);

                if (maintenanceResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        var maintenanceArray = JsonReferenceHelper.ParseArrayWithReferences(maintenanceJsonString, "Maintenance");
                        if (maintenanceArray != null)
                        {
                            Log.Information("Parsing {Count} maintenance objects from JSON array.", maintenanceArray.Count);
                            foreach(var maintNode in maintenanceArray)
                            {
                                if (maintNode is JsonObject maintObj)
                                {
                                    Log.Verbose("--- Parsing Maintenance Object: {MaintenanceJson} ---", maintObj.ToJsonString());
                                    
                                    // Use the ParseMaintenance extension method for comprehensive parsing
                                    var maintenance = maintObj.ParseMaintenance();
                                    if (maintenance == null)
                                    {
                                        Log.Warning("Failed to parse maintenance object, skipping");
                                        continue;
                                    }
                                    
                                    loadedMaintenance.Add(maintenance);
                                    Log.Verbose("Successfully parsed Maintenance: Id={MaintenanceId}, BusId={BusId}, Type='{Type}', Cost={Cost}",
                                        maintenance.MaintenanceId, maintenance.BusId, maintenance.MaintenanceType, maintenance.MaintenanceCost);

                                }
                                else
                                {
                                    Log.Warning("Item in maintenance array was not a JSON object: {Node}", maintNode?.ToJsonString());
                                }
                            }
                            Log.Information("Successfully parsed {Count} valid maintenance records.", loadedMaintenance.Count);
                        }
                        else
                        {
                            Log.Error("Maintenance JSON could not be parsed as array. Raw JSON: {RawJson}", maintenanceJsonString);
                            HasError = true;
                            ErrorMessage = "Ошибка структуры данных обслуживания.";
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Log.Error(jsonEx, "Failed to parse Maintenance JSON: {RawJson}", maintenanceJsonString);
                        HasError = true;
                        ErrorMessage = "Ошибка чтения данных обслуживания.";
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Unexpected error during manual maintenance parsing.");
                        HasError = true;
                        ErrorMessage = "Непредвиденная ошибка обработки данных обслуживания.";
                    }
                }
                else
                {
                    var error = await maintenanceResponse.Content.ReadAsStringAsync();
                    Log.Error("Failed to load maintenance records. Status: {StatusCode}, Error: {Error}", maintenanceResponse.StatusCode, error);
                    HasError = true;
                    ErrorMessage = $"Критическая ошибка загрузки записей обслуживания: {maintenanceResponse.StatusCode}";
                }

                // Update collections
                _allMaintenanceRecords = loadedMaintenance.Select(m => new MaintenanceDisplayModel(m, _allBuses.FirstOrDefault(b => b.BusId == m.BusId))).ToList();
                MaintenanceRecords = new ObservableCollection<MaintenanceDisplayModel>(_allMaintenanceRecords);
                Log.Information("Finished processing data. Displaying {MaintCount} maintenance records and {BusCount} buses.", MaintenanceRecords.Count, Buses.Count);
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Критическая ошибка загрузки данных: {ex.Message}";
                Log.Fatal(ex, "Fatal error loading data in MaintenanceManagementViewModel");
                // Clear collections on fatal error
                _allBuses = new List<Bus>();
                Buses = new ObservableCollection<Bus>();
                _allMaintenanceRecords = new List<MaintenanceDisplayModel>();
                MaintenanceRecords = new ObservableCollection<MaintenanceDisplayModel>();
            }
            finally
            {
                IsBusy = false;
                Log.Information("LoadData finished for MaintenanceManagementViewModel.");
            }
        }

        [RelayCommand]
        private async Task Add()
        {
            try
            {
                var dialog = new Window
                {
                    Title = "Добавить запись обслуживания",
                    Width = 500,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                var busComboBox = new ComboBox
                {
                    PlaceholderText = "Выберите автобус",
                    ItemsSource = Buses,
                    DisplayMemberBinding = new global::Avalonia.Data.Binding("Model")
                };

                var lastServiceDatePicker = new DatePicker { SelectedDate = DateTimeOffset.Now };
                var nextServiceDatePicker = new DatePicker { SelectedDate = DateTimeOffset.Now.AddMonths(1) };
                var serviceEngineerBox = new TextBox { PlaceholderText = "Инженер" };
                var foundIssuesBox = new TextBox { PlaceholderText = "Найденные проблемы" };
                var roadworthinessBox = new TextBox { PlaceholderText = "Состояние (напр., 'Good', 'Needs Repair')" };

                var addButton = new Button
                {
                    Content = "Добавить",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                grid.Children.Add(busComboBox);
                Grid.SetRow(busComboBox, 0);
                grid.Children.Add(lastServiceDatePicker);
                Grid.SetRow(lastServiceDatePicker, 1);
                grid.Children.Add(nextServiceDatePicker);
                Grid.SetRow(nextServiceDatePicker, 2);
                grid.Children.Add(serviceEngineerBox);
                Grid.SetRow(serviceEngineerBox, 3);
                grid.Children.Add(foundIssuesBox);
                Grid.SetRow(foundIssuesBox, 4);
                grid.Children.Add(roadworthinessBox);
                Grid.SetRow(roadworthinessBox, 5);
                grid.Children.Add(addButton);
                Grid.SetRow(addButton, 7);

                dialog.Content = grid;

                addButton.Click += async (s, e) =>
                {
                    if (busComboBox.SelectedItem == null ||
                        string.IsNullOrWhiteSpace(serviceEngineerBox.Text) ||
                        string.IsNullOrWhiteSpace(foundIssuesBox.Text) ||
                        string.IsNullOrWhiteSpace(roadworthinessBox.Text))
                    {
                        ErrorMessage = "Все поля обязательны для заполнения";
                        return;
                    }

                    var selectedBus = busComboBox.SelectedItem as Bus;

                    var maintenance = new
                    {
                        BusId = selectedBus!.BusId,
                        LastServiceDate = lastServiceDatePicker.SelectedDate?.DateTime ?? DateTime.Now,
                        NextServiceDate = nextServiceDatePicker.SelectedDate?.DateTime ?? DateTime.Now.AddMonths(1),
                        ServiceEngineer = serviceEngineerBox.Text,
                        FoundIssues = foundIssuesBox.Text,
                        Roadworthiness = roadworthinessBox.Text
                    };

                    var content = new StringContent(
                        JsonSerializer.Serialize(maintenance),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync($"{_baseUrl}/Maintenance", content);
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        ErrorMessage = $"Failed to add maintenance record: {error}";
                    }
                };

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
                ErrorMessage = $"Error adding maintenance record: {ex.Message}";
                Log.Error(ex, "Error adding maintenance record");
            }
        }

        [RelayCommand]
        private async Task Edit()
        {
            if (SelectedRecord == null) return;

            // Convert ulong timestamps back to DateTimeOffset for DatePicker
            DateTimeOffset lastServiceDto = DateTimeOffset.MinValue;
            DateTimeOffset nextServiceDto = DateTimeOffset.MinValue;
            try {
                if (SelectedRecord.Maintenance.LastServiceDate > 0)
                    lastServiceDto = DateTimeOffset.FromUnixTimeMilliseconds((long)SelectedRecord.Maintenance.LastServiceDate);
            } catch (ArgumentOutOfRangeException ex) {
                 Log.Warning(ex, "Failed to convert LastServiceDate timestamp {Timestamp} to DateTimeOffset for editing", SelectedRecord.Maintenance.LastServiceDate);
                 // Keep MinValue
            }
            try {
                 if (SelectedRecord.Maintenance.NextServiceDate > 0)
                    nextServiceDto = DateTimeOffset.FromUnixTimeMilliseconds((long)SelectedRecord.Maintenance.NextServiceDate);
            } catch (ArgumentOutOfRangeException ex) {
                 Log.Warning(ex, "Failed to convert NextServiceDate timestamp {Timestamp} to DateTimeOffset for editing", SelectedRecord.Maintenance.NextServiceDate);
                 // Keep MinValue
            }

            try
            {
                var dialog = new Window
                {
                    Title = "Редактировать запись обслуживания",
                    Width = 500,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                var busComboBox = new ComboBox
                {
                    PlaceholderText = "Выберите автобус",
                    ItemsSource = Buses,
                    DisplayMemberBinding = new global::Avalonia.Data.Binding("Model"),
                    SelectedItem = Buses.FirstOrDefault(b => b.BusId == SelectedRecord.Maintenance.BusId)
                };

                var lastServiceDatePicker = new DatePicker
                {
                    SelectedDate = lastServiceDto != DateTimeOffset.MinValue ? lastServiceDto : (DateTimeOffset?)null // Handle MinValue case
                };

                var nextServiceDatePicker = new DatePicker
                {
                     SelectedDate = nextServiceDto != DateTimeOffset.MinValue ? nextServiceDto : (DateTimeOffset?)null // Handle MinValue case
                };

                var serviceEngineerBox = new TextBox
                {
                    Text = SelectedRecord.Maintenance.ServiceEngineer,
                    PlaceholderText = "Инженер"
                };

                var foundIssuesBox = new TextBox
                {
                    Text = SelectedRecord.Maintenance.FoundIssues,
                    PlaceholderText = "Найденные проблемы"
                };

                var roadworthinessBox = new TextBox
                {
                    Text = SelectedRecord.Maintenance.Roadworthiness,
                    PlaceholderText = "Состояние"
                };

                var updateButton = new Button
                {
                    Content = "Обновить",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                grid.Children.Add(busComboBox);
                Grid.SetRow(busComboBox, 0);
                grid.Children.Add(lastServiceDatePicker);
                Grid.SetRow(lastServiceDatePicker, 1);
                grid.Children.Add(nextServiceDatePicker);
                Grid.SetRow(nextServiceDatePicker, 2);
                grid.Children.Add(serviceEngineerBox);
                Grid.SetRow(serviceEngineerBox, 3);
                grid.Children.Add(foundIssuesBox);
                Grid.SetRow(foundIssuesBox, 4);
                grid.Children.Add(roadworthinessBox);
                Grid.SetRow(roadworthinessBox, 5);
                grid.Children.Add(updateButton);
                Grid.SetRow(updateButton, 7);

                dialog.Content = grid;

                updateButton.Click += async (s, e) =>
                {
                    if (busComboBox.SelectedItem == null ||
                        string.IsNullOrWhiteSpace(serviceEngineerBox.Text) ||
                        string.IsNullOrWhiteSpace(foundIssuesBox.Text) ||
                        string.IsNullOrWhiteSpace(roadworthinessBox.Text))
                    {
                        ErrorMessage = "Все поля обязательны для заполнения";
                        return;
                    }

                    var selectedBus = busComboBox.SelectedItem as Bus;

                    // Convert DateTimeOffset back to ulong
                    ulong lastServiceUnix = 0;
                    if (lastServiceDatePicker.SelectedDate.HasValue) {
                        try {
                            lastServiceUnix = (ulong)lastServiceDatePicker.SelectedDate.Value.ToUnixTimeMilliseconds();
                        } catch (Exception ex) {
                            Log.Error(ex, "Failed to convert selected LastServiceDate back to timestamp");
                            ErrorMessage = "Неверный формат даты последнего обслуживания.";
                            return;
                        }
                    }

                    ulong nextServiceUnix = 0;
                     if (nextServiceDatePicker.SelectedDate.HasValue) {
                        try {
                            nextServiceUnix = (ulong)nextServiceDatePicker.SelectedDate.Value.ToUnixTimeMilliseconds();
                        } catch (Exception ex) {
                            Log.Error(ex, "Failed to convert selected NextServiceDate back to timestamp");
                            ErrorMessage = "Неверный формат даты следующего обслуживания.";
                            return;
                        }
                    }

                    var maintenance = new
                    {
                        BusId = selectedBus!.BusId,
                        LastServiceDate = lastServiceUnix, // Send timestamp
                        NextServiceDate = nextServiceUnix, // Send timestamp
                        ServiceEngineer = serviceEngineerBox.Text,
                        FoundIssues = foundIssuesBox.Text,
                        Roadworthiness = roadworthinessBox.Text
                        // Add other fields if the PUT endpoint expects them
                    };

                    var content = new StringContent(
                        JsonSerializer.Serialize(maintenance, _jsonOptions), // Use options if needed
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PutAsync(
                        $"{_baseUrl}/Maintenance/{SelectedRecord.Maintenance.MaintenanceId}",
                        content);

                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        ErrorMessage = $"Failed to update maintenance record: {error}";
                    }
                };

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
                ErrorMessage = $"Error updating maintenance record: {ex.Message}";
                Log.Error(ex, "Error updating maintenance record");
            }
        }

        [RelayCommand]
        private async Task Delete()
        {
            if (SelectedRecord == null) return;

            try
            {
                var dialog = new Window
                {
                    Title = "Подтверждение удаления",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("*,Auto"),
                    Margin = new Thickness(10)
                };

                var textBlock = new TextBlock
                {
                    Text = "Вы уверены, что хотите удалить эту запись обслуживания?",
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 10
                };

                var yesButton = new Button { Content = "Да" };
                var noButton = new Button { Content = "Нет" };

                buttonsPanel.Children.Add(yesButton);
                buttonsPanel.Children.Add(noButton);

                grid.Children.Add(textBlock);
                Grid.SetRow(textBlock, 0);
                grid.Children.Add(buttonsPanel);
                Grid.SetRow(buttonsPanel, 1);

                dialog.Content = grid;

                yesButton.Click += async (s, e) =>
                {
                    var response = await _httpClient.DeleteAsync($"{_baseUrl}/Maintenance/{SelectedRecord.Maintenance.MaintenanceId}");
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        ErrorMessage = $"Failed to delete maintenance record: {error}";
                    }
                };

                noButton.Click += (s, e) => dialog.Close();

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
                ErrorMessage = $"Error deleting maintenance record: {ex.Message}";
                Log.Error(ex, "Error deleting maintenance record");
            }
        }

        private void OnSearchTextChanged(string value)
        {
            Log.Debug("Search text changed: '{SearchText}'", value);
            
            if (string.IsNullOrWhiteSpace(value))
            {
                Log.Debug("Search text is empty, resetting filter.");
                MaintenanceRecords = new ObservableCollection<MaintenanceDisplayModel>(_allMaintenanceRecords);
                return;
            }

            var lowerCaseValue = value.ToLowerInvariant();
            var filteredRecords = _allMaintenanceRecords.Where(m =>
                (m.MaintenanceId.ToString().Contains(lowerCaseValue)) ||
                (m.Maintenance.BusId.ToString().Contains(lowerCaseValue)) ||
                (m.Maintenance.MaintenanceType?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (m.Maintenance.ServiceEngineer?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (m.Maintenance.FoundIssues?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (m.Maintenance.Roadworthiness?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (m.Maintenance.MaintenanceStatus?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (m.Maintenance.MaintenanceLocation?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (m.Maintenance.MaintenanceNotes?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                (_allBuses.FirstOrDefault(b => b.BusId == m.Maintenance.BusId)?.Model?.ToLowerInvariant().Contains(lowerCaseValue) ?? false)
            ).ToList();

            Log.Information("Filtering complete. Found {Count} maintenance records matching '{SearchText}'", filteredRecords.Count, value);
            MaintenanceRecords = new ObservableCollection<MaintenanceDisplayModel>(filteredRecords);
        }
    }
} 