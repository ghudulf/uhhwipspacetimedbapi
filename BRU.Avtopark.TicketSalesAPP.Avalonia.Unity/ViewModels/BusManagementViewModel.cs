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
using System.Text.Json.Serialization;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.Text.Json.Nodes;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    // Wrapper class to hold Bus and associated counts
    public partial class BusDisplayModel : ObservableObject
    {
        [ObservableProperty]
        private Bus _bus; // The original Bus object

        [ObservableProperty]
        private int _routeCount;

        [ObservableProperty]
        private int _maintenanceCount;

        public BusDisplayModel(Bus bus)
        {
            _bus = bus;
        }

        // Expose Bus properties for easier binding in DataGrid if needed
        [JsonIgnore] // Prevent serialization issues if this model is ever serialized
        public uint BusId => Bus.BusId;
        [JsonIgnore]
        public string Model => Bus.Model;
        [JsonIgnore]
        public string? RegistrationNumber => Bus.RegistrationNumber;
        [JsonIgnore]
        public bool IsActive => Bus.IsActive;
        [JsonIgnore]
        public uint Capacity => Bus.Capacity;
        // ... add other frequently used Bus properties if direct binding is preferred ...
    }

    public partial class BusManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;

        // Store the original full list for filtering
        private List<BusDisplayModel> _allBuses = new();

        private ObservableCollection<BusDisplayModel> _buses = new(); // Changed type
        public ObservableCollection<BusDisplayModel> Buses // Changed type
        {
            get => _buses;
            set => this.RaiseAndSetIfChanged(ref _buses, value);
        }

        private BusDisplayModel? _selectedBus; // Changed type
        public BusDisplayModel? SelectedBus // Changed type
        {
            get => _selectedBus;
            set => this.RaiseAndSetIfChanged(ref _selectedBus, value);
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

        public BusManagementViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = "http://localhost:5000/api";

            // Subscribe to auth token changes
            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) =>
            {
                Log.Information("Auth token changed in BusManagementViewModel. Recreating HttpClient and reloading data.");
                // Create a new client with the updated token
                _httpClient.Dispose();
                _httpClient = ApiClientService.Instance.CreateClient();
                // Reload data with the new token
                LoadData().ConfigureAwait(false);
            };

            LoadData().ConfigureAwait(false);
        }

        private async Task LoadData()
        {
            Log.Information("Starting LoadData for BusManagementViewModel");
            try
        {
            IsBusy = true;
            HasError = false;
            ErrorMessage = string.Empty;

                 // --- Fetch all required data concurrently ---
                 Log.Debug("Initiating API calls for Buses, Routes, Maintenance");
                 Task<HttpResponseMessage> busesTask = _httpClient.GetAsync($"{_baseUrl}/Buses");
                 Task<HttpResponseMessage> routesTask = _httpClient.GetAsync($"{_baseUrl}/Routes");
                 Task<HttpResponseMessage> maintenanceTask = _httpClient.GetAsync($"{_baseUrl}/Maintenance");

                // Await all tasks
                await Task.WhenAll(busesTask, routesTask, maintenanceTask);
                Log.Debug("All API calls completed for BusManagementViewModel.");

                // --- Process Responses with Manual Parsing and Logging ---

                // 1. Process Buses Response
                List<Bus> loadedBuses = new();
                var busesResponse = await busesTask;
                 Log.Information("Processing Buses response. Status: {StatusCode}", busesResponse.StatusCode);
                 
                 // Log the raw response content
                 var busJsonString = await busesResponse.Content.ReadAsStringAsync();
                 Log.Debug("Raw Buses response received: {RawResponse}", busJsonString);
                 
                 if (busesResponse.IsSuccessStatusCode)
                 {
                      Log.Debug("Raw Buses JSON received: {RawJson}", busJsonString);
                      try
                      {
                           JsonNode? busesNode = JsonNode.Parse(busJsonString);
                           if (busesNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var busesValuesNode) && busesValuesNode is JsonArray busesArray)
                           {
                                Log.Information("Parsing {Count} bus objects from JSON array.", busesArray.Count);
                                foreach(var busNode in busesArray)
                                {
                                     if (busNode is JsonObject busObj)
                                     {
                                          Log.Verbose("--- Parsing Bus Object: {BusJson} ---", busObj.ToJsonString());
                                          uint busId = busObj["busId"]?.GetValue<uint>() ?? 0;
                                          if (busId == 0)
                                          {
                                               Log.Error("CRITICAL DATA ISSUE: Bus object has BusId 0. Filtering out. JSON: {BusJson}", busObj.ToJsonString());
                                               continue; // Filter out
                                          }
                                          string model = busObj["model"]?.GetValue<string>() ?? "N/A";
                                          string? regNum = busObj["registrationNumber"]?.GetValue<string>();
                                          uint capacity = busObj["capacity"]?.GetValue<uint>() ?? 0;
                                          bool busIsActive = busObj["isActive"]?.GetValue<bool>() ?? false;
                                          string? busType = busObj["busType"]?.GetValue<string>();
                                          // Add other fields as needed from Bus model
                                          uint year = busObj["year"]?.GetValue<uint>() ?? 0;
                                          string? vin = busObj["vin"]?.GetValue<string>();
                                          string? licensePlate = busObj["licensePlate"]?.GetValue<string>();
                                          string? currentStatus = busObj["currentStatus"]?.GetValue<string>();

                                           Log.Verbose("Parsed Bus: Id={BusId}, Model='{Model}', Reg='{RegNum}', Active={IsActive}, Capacity={Capacity}, Type='{Type}'",
                                               busId, model, regNum, busIsActive, capacity, busType);

                                           // Create SpacetimeDB.Types.Bus object
                                           loadedBuses.Add(new Bus {
                                                 BusId = busId,
                                                 Model = model,
                                                 RegistrationNumber = regNum,
                                                 Capacity = capacity,
                                                 IsActive = busIsActive,
                                                 BusType = busType ?? string.Empty,
                                                 Year = year,
                                                 Vin = vin,
                                                 LicensePlate = licensePlate,
                                                 CurrentStatus = currentStatus
                                                 // Map other properties as needed
                                            });
                                     }
                                      else { Log.Warning("Item in buses array was not a JSON object: {Node}", busNode?.ToJsonString()); }
                                }
                                 Log.Information("Successfully parsed and filtered {Count} valid buses.", loadedBuses.Count);
                           }
                            else { Log.Error("Buses JSON root was not an object with a '$values' array or the structure was unexpected. Root node type: {NodeType}, Raw JSON: {RawJson}", busesNode?.GetType().Name, busJsonString); }
                      }
                      catch (JsonException jsonEx)
                      {
                           Log.Error(jsonEx, "Failed to parse Buses JSON: {RawJson}", busJsonString);
                           throw new Exception("Failed to parse bus data.", jsonEx);
                      }
                     catch (Exception ex)
                     {
                          Log.Error(ex, "Unexpected error during manual bus parsing.");
                          throw; // Re-throw unexpected errors
                     }
                 }
                 else
                 {
                     var error = await busesResponse.Content.ReadAsStringAsync();
                     Log.Error("Failed to load buses. Status: {StatusCode}, Error: {Error}", busesResponse.StatusCode, error);
                     throw new Exception($"Failed to load primary bus data. Status: {busesResponse.StatusCode}");
                 }


                // 2. Process Routes Response (for counts)
                 Dictionary<uint, int> routeCounts = new Dictionary<uint, int>();
                 var routesResponse = await routesTask;
                 Log.Information("Processing Routes response (for counts). Status: {StatusCode}", routesResponse.StatusCode);
                 if (routesResponse.IsSuccessStatusCode)
                 {
                      var routeJsonString = await routesResponse.Content.ReadAsStringAsync();
                      Log.Debug("Raw Routes JSON received (for counts): {RawJson}", routeJsonString);
                      try
                      {
                           JsonNode? routesNode = JsonNode.Parse(routeJsonString);
                           if (routesNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var routeValuesNode) && routeValuesNode is JsonArray routesArray)
                           {
                                Log.Information("Parsing {Count} route objects from JSON array (for counts).", routesArray.Count);
                                foreach(var routeNode in routesArray)
                                {
                                     if (routeNode is JsonObject routeObj)
                                     {
                                          uint busId = routeObj["busId"]?.GetValue<uint>() ?? 0;
                                          bool routeIsActive = routeObj["isActive"]?.GetValue<bool>() ?? false; // Assuming API sends isActive
                                          if (busId > 0 && routeIsActive) // Count only active routes for valid buses
                                          {
                                               routeCounts[busId] = routeCounts.GetValueOrDefault(busId, 0) + 1;
                                          }
                                          else { Log.Verbose("Skipping route count for BusId {BusId}, IsActive={IsActive}", busId, routeIsActive); }
                                     }
                                      else { Log.Warning("Item in routes array was not a JSON object: {Node}", routeNode?.ToJsonString()); }
                                }
                                 Log.Information("Successfully calculated route counts for {Count} buses.", routeCounts.Count);
                           }
                            else { Log.Error("Routes JSON root was not an object with a '$values' array or the structure was unexpected. Root node type: {NodeType}, Raw JSON: {RawJson}", routesNode?.GetType().Name, routeJsonString); }
                      }
                      catch (JsonException jsonEx)
                      {
                           Log.Error(jsonEx, "Failed to parse Routes JSON (for counts): {RawJson}", routeJsonString);
                      }
                     catch (Exception ex)
                     {
                          Log.Error(ex, "Unexpected error during manual route parsing (for counts).");
                     }
                 }
                 else
                 {
                     var error = await routesResponse.Content.ReadAsStringAsync();
                     Log.Warning("Failed to load routes (for counts). Status: {StatusCode}, Error: {Error}. Route counts may be inaccurate.", routesResponse.StatusCode, error);
                 }

                // 3. Process Maintenance Response (for counts)
                Dictionary<uint, int> maintenanceCounts = new Dictionary<uint, int>();
                var maintenanceResponse = await maintenanceTask;
                Log.Information("Processing Maintenance response (for counts). Status: {StatusCode}", maintenanceResponse.StatusCode);
                if (maintenanceResponse.IsSuccessStatusCode)
                {
                    var maintenanceJsonString = await maintenanceResponse.Content.ReadAsStringAsync();
                    Log.Debug("Raw Maintenance JSON received (for counts): {RawJson}", maintenanceJsonString);
                    try
                    {
                         JsonNode? maintenanceNode = JsonNode.Parse(maintenanceJsonString);
                         if (maintenanceNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var maintValuesNode) && maintValuesNode is JsonArray maintenanceArray)
                         {
                              Log.Information("Parsing {Count} maintenance objects from JSON array (for counts).", maintenanceArray.Count);
                              foreach(var maintNode in maintenanceArray)
                              {
                                   if (maintNode is JsonObject maintObj)
                                   {
                                        uint busId = maintObj["busId"]?.GetValue<uint>() ?? 0;
                                        // Decide if you need to filter maintenance records (e.g., only active/recent)
                                        if (busId > 0)
                                        {
                                             maintenanceCounts[busId] = maintenanceCounts.GetValueOrDefault(busId, 0) + 1;
                                        }
                                         else { Log.Verbose("Skipping maintenance count for BusId {BusId}", busId); }
                                   }
                                    else { Log.Warning("Item in maintenance array was not a JSON object: {Node}", maintNode?.ToJsonString()); }
                              }
                               Log.Information("Successfully calculated maintenance counts for {Count} buses.", maintenanceCounts.Count);
                         }
                          else { Log.Error("Maintenance JSON root was not an object with a '$values' array or the structure was unexpected. Root node type: {NodeType}, Raw JSON: {RawJson}", maintenanceNode?.GetType().Name, maintenanceJsonString); }
                    }
                    catch (JsonException jsonEx)
                    {
                         Log.Error(jsonEx, "Failed to parse Maintenance JSON (for counts): {RawJson}", maintenanceJsonString);
                    }
                     catch (Exception ex)
                     {
                          Log.Error(ex, "Unexpected error during manual maintenance parsing (for counts).");
                    }
                }
                else
                {
                    var error = await maintenanceResponse.Content.ReadAsStringAsync();
                    Log.Warning("Failed to load maintenance records (for counts). Status: {StatusCode}, Error: {Error}. Maintenance counts may be inaccurate.", maintenanceResponse.StatusCode, error);
                }

                // 4. Create Display Models
                Log.Debug("Creating BusDisplayModels from parsed data...");
                var displayBuses = new List<BusDisplayModel>();
                foreach (var bus in loadedBuses) // Iterate over the filtered list
                {
                    var displayModel = new BusDisplayModel(bus)
                    {
                        RouteCount = routeCounts.TryGetValue(bus.BusId, out var rc) ? rc : 0,
                        MaintenanceCount = maintenanceCounts.TryGetValue(bus.BusId, out var mc) ? mc : 0
                    };
                    displayBuses.Add(displayModel);
                    Log.Verbose("Created BusDisplayModel for BusId {BusId}: RouteCount={RouteCount}, MaintenanceCount={MaintenanceCount}",
                        bus.BusId, displayModel.RouteCount, displayModel.MaintenanceCount);
                }

                _allBuses = displayBuses; // Store the full, filtered list
                Buses = new ObservableCollection<BusDisplayModel>(_allBuses); // Update the displayed list
                Log.Information("Finished processing data. Displaying {Count} buses.", Buses.Count);

            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Критическая ошибка загрузки данных: {ex.Message}";
                Log.Fatal(ex, "Fatal error loading data in BusManagementViewModel");
                 // Clear collections on fatal error
                 Buses = new ObservableCollection<BusDisplayModel>();
                 _allBuses = new List<BusDisplayModel>();
            }
            finally
            {
                IsBusy = false;
                Log.Information("LoadData finished for BusManagementViewModel.");
            }
        }

        [RelayCommand]
        private async Task Add()
        {
             Log.Information("Add Bus command initiated.");
            try
            {
                // Define the dialog content structure
                var dialog = new Window
                {
                    Title = "Добавить автобус",
                    Width = 450, // Adjusted width for more fields
                    Height = 600, // Adjusted height
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Padding = new Thickness(15)
                };

                var sp = new StackPanel { Spacing = 10 };

                // Input Fields based on Bus model and CreateBusModel
                var modelBox = new TextBox { Watermark = "Модель" };
                var regNumBox = new TextBox { Watermark = "Регистрационный номер (необязательно)" };
                var capacityBox = new NumericUpDown { Watermark = "Вместимость", Minimum = 0, Increment = 1 };
                var busTypeBox = new TextBox { Watermark = "Тип автобуса (Городской, Троллейбус...)" };
                var yearBox = new NumericUpDown { Watermark = "Год выпуска", Minimum = 1950, Maximum = DateTime.Now.Year + 1, Increment = 1 };
                var vinBox = new TextBox { Watermark = "VIN (необязательно)" };
                var plateBox = new TextBox { Watermark = "Гос. номер (необязательно)" };
                var statusBox = new ComboBox
                {
                    PlaceholderText = "Текущий статус",
                    ItemsSource = new List<string> { "In Service", "Maintenance", "Out of Service" } // Example statuses
                };
                var isActiveCheck = new CheckBox { Content = "Активен", IsChecked = true };

                 // Add controls to StackPanel
                 sp.Children.Add(new TextBlock { Text = "Модель:" }); sp.Children.Add(modelBox);
                 sp.Children.Add(new TextBlock { Text = "Рег. номер:" }); sp.Children.Add(regNumBox);
                 sp.Children.Add(new TextBlock { Text = "Вместимость:" }); sp.Children.Add(capacityBox);
                 sp.Children.Add(new TextBlock { Text = "Тип автобуса:" }); sp.Children.Add(busTypeBox);
                 sp.Children.Add(new TextBlock { Text = "Год выпуска:" }); sp.Children.Add(yearBox);
                 sp.Children.Add(new TextBlock { Text = "VIN:" }); sp.Children.Add(vinBox);
                 sp.Children.Add(new TextBlock { Text = "Гос. номер:" }); sp.Children.Add(plateBox);
                 sp.Children.Add(new TextBlock { Text = "Статус:" }); sp.Children.Add(statusBox);
                 sp.Children.Add(isActiveCheck);

                // Buttons
                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 15, 0, 0)
                };
                var addButton = new Button { Content = "Добавить", Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
                var cancelButton = new Button { Content = "Отмена", IsCancel = true };
                buttonsPanel.Children.Add(addButton);
                buttonsPanel.Children.Add(cancelButton);

                sp.Children.Add(buttonsPanel);
                dialog.Content = new ScrollViewer { Content = sp }; // Add ScrollViewer

                cancelButton.Click += (s, e) =>
                {
                    Log.Debug("Add Bus dialog cancelled.");
                    dialog.Close();
                };

                addButton.Click += async (s, e) =>
                {
                    Log.Debug("Attempting to add new bus.");
                    // Validation
                    if (string.IsNullOrWhiteSpace(modelBox.Text) || !capacityBox.Value.HasValue || !yearBox.Value.HasValue)
                    {
                        ErrorMessage = "Модель, Вместимость и Год выпуска обязательны.";
                        Log.Warning("Add Bus validation failed: Missing required fields.");
                         var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Warning);
                         var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                         if (mainWindow != null) await box.ShowAsync();
                        return;
                    }

                    // Create payload matching API's CreateBusModel
                    var newBusPayload = new
                    {
                        Model = modelBox.Text,
                        RegistrationNumber = string.IsNullOrWhiteSpace(regNumBox.Text) ? null : regNumBox.Text, // Null if empty
                        Capacity = (uint)(capacityBox.Value ?? 0),
                        BusType = string.IsNullOrWhiteSpace(busTypeBox.Text) ? "Regular" : busTypeBox.Text, // Default if empty?
                        Year = (uint)(yearBox.Value ?? 0),
                        VIN = string.IsNullOrWhiteSpace(vinBox.Text) ? null : vinBox.Text,
                        LicensePlate = string.IsNullOrWhiteSpace(plateBox.Text) ? null : plateBox.Text,
                        CurrentStatus = statusBox.SelectedItem as string, // Or handle null selection
                        IsActive = isActiveCheck.IsChecked ?? true
                        // Add other fields if the API expects them
                    };

                    Log.Information("Sending request to add bus: {@BusPayload}", newBusPayload);
                    try
                    {
                         // Use default options or configure as needed
                        var json = JsonSerializer.Serialize(newBusPayload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PostAsync($"{_baseUrl}/Buses", content);

                        Log.Information("Add Bus API Response Status: {StatusCode}", response.StatusCode);

                        if (response.IsSuccessStatusCode)
                        {
                            Log.Information("Successfully added bus via API.");
                            await LoadData();
                            dialog.Close();
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            ErrorMessage = $"Не удалось добавить автобус: ({response.StatusCode}) {error}";
                            Log.Error("Failed to add bus via API. Status: {StatusCode}, Error: {Error}",
                                response.StatusCode, error);
                             var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                             var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                             if (mainWindow != null) await box.ShowAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Ошибка при добавлении автобуса: {ex.Message}";
                        Log.Error(ex, "Exception occurred while adding bus.");
                         var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                         var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                         if (mainWindow != null) await box.ShowAsync();
                    }
                };

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                if (mainWindow != null)
                {
                    Log.Debug("Showing Add Bus dialog.");
                    await dialog.ShowDialog(mainWindow);
                }
                else
                {
                    Log.Error("Could not find main window to show Add Bus dialog.");
                    ErrorMessage = "Не удалось отобразить диалог добавления.";
                     var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                     var app = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                     if (app?.MainWindow != null) await box.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Ошибка при инициации добавления автобуса: {ex.Message}";
                Log.Error(ex, "Error initiating Add Bus command");
                 var box = MessageBoxManager.GetMessageBoxStandard("Фатальная ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                 var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                 if (mainWindow != null) await box.ShowAsync();
            }
        }


        [RelayCommand]
        private async Task Edit()
        {
            if (SelectedBus == null)
            {
                Log.Warning("Edit Bus command initiated but no bus selected.");
                return;
            }

             var busDisplayToEdit = SelectedBus; // Keep reference to the display model
             var busToEdit = busDisplayToEdit.Bus; // Get underlying SpacetimeDB Bus object

             Log.Information("Edit Bus command initiated for BusId: {BusId}", busToEdit.BusId);

            try
            {
                var dialog = new Window
                {
                    Title = $"Редактировать автобус: {busToEdit.Model} (ID: {busToEdit.BusId})",
                    Width = 450,
                    Height = 600, // Adjusted height
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Padding = new Thickness(15)
                };

                var sp = new StackPanel { Spacing = 10 };

                // Pre-populate fields from busToEdit
                var modelBox = new TextBox { Text = busToEdit.Model, Watermark = "Модель" };
                var regNumBox = new TextBox { Text = busToEdit.RegistrationNumber, Watermark = "Регистрационный номер" };
                var capacityBox = new NumericUpDown { Value = busToEdit.Capacity, Watermark = "Вместимость", Minimum = 0, Increment = 1 };
                var busTypeBox = new TextBox { Text = busToEdit.BusType, Watermark = "Тип автобуса" };
                var yearBox = new NumericUpDown { Value = busToEdit.Year, Watermark = "Год выпуска", Minimum = 1950, Maximum = DateTime.Now.Year + 1, Increment = 1 };
                var vinBox = new TextBox { Text = busToEdit.Vin, Watermark = "VIN" };
                var plateBox = new TextBox { Text = busToEdit.LicensePlate, Watermark = "Гос. номер" };
                var statusComboBox = new ComboBox
                {
                    PlaceholderText = "Текущий статус",
                    ItemsSource = new List<string> { "In Service", "Maintenance", "Out of Service" }, // Example statuses
                    SelectedItem = busToEdit.CurrentStatus
                };
                var isActiveCheck = new CheckBox { Content = "Активен", IsChecked = busToEdit.IsActive };

                 // Add controls to StackPanel
                 sp.Children.Add(new TextBlock { Text = "Модель:" }); sp.Children.Add(modelBox);
                 sp.Children.Add(new TextBlock { Text = "Рег. номер:" }); sp.Children.Add(regNumBox);
                 sp.Children.Add(new TextBlock { Text = "Вместимость:" }); sp.Children.Add(capacityBox);
                 sp.Children.Add(new TextBlock { Text = "Тип автобуса:" }); sp.Children.Add(busTypeBox);
                 sp.Children.Add(new TextBlock { Text = "Год выпуска:" }); sp.Children.Add(yearBox);
                 sp.Children.Add(new TextBlock { Text = "VIN:" }); sp.Children.Add(vinBox);
                 sp.Children.Add(new TextBlock { Text = "Гос. номер:" }); sp.Children.Add(plateBox);
                 sp.Children.Add(new TextBlock { Text = "Статус:" }); sp.Children.Add(statusComboBox);
                 sp.Children.Add(isActiveCheck);

                // Buttons
                var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
                var saveButton = new Button { Content = "Сохранить", Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
                var cancelButton = new Button { Content = "Отмена", IsCancel = true };
                buttonsPanel.Children.Add(saveButton);
                buttonsPanel.Children.Add(cancelButton);
                sp.Children.Add(buttonsPanel);

                dialog.Content = new ScrollViewer { Content = sp }; // Add ScrollViewer

                cancelButton.Click += (s, e) =>
                {
                    Log.Debug("Edit Bus dialog cancelled for BusId: {BusId}", busToEdit.BusId);
                    dialog.Close();
                };

                saveButton.Click += async (s, e) =>
                {
                    Log.Debug("Attempting to save changes for BusId: {BusId}", busToEdit.BusId);
                    // Validation
                    if (string.IsNullOrWhiteSpace(modelBox.Text) || !capacityBox.Value.HasValue || !yearBox.Value.HasValue)
                    {
                        ErrorMessage = "Модель, Вместимость и Год выпуска обязательны.";
                        Log.Warning("Edit Bus validation failed: Missing required fields for BusId: {BusId}", busToEdit.BusId);
                         var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Warning);
                         var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                         if (mainWindow != null) await box.ShowAsync();
                        return;
                    }

                    // Create payload matching API's UpdateBusModel
                    // **IMPORTANT**: Check BusesController UpdateBusModel for expected fields!
                    var updatePayload = new
                    {
                        // Send only fields that changed or that the API expects
                        Model = modelBox.Text, // Assuming Model can be updated
                        RegistrationNumber = regNumBox.Text, // Nullable
                         // Add other fields from Bus model if API supports updating them
                         Capacity = (uint?)capacityBox.Value,
                         BusType = busTypeBox.Text,
                         Year = (uint?)yearBox.Value,
                         VIN = vinBox.Text,
                         LicensePlate = plateBox.Text,
                         CurrentStatus = statusComboBox.SelectedItem as string,
                         IsActive = isActiveCheck.IsChecked
                    };

                    Log.Information("Sending request to update bus {BusId}: {@UpdatePayload}", busToEdit.BusId, updatePayload);

                    try
                    {
                        // Use default options or configure as needed
                        var json = JsonSerializer.Serialize(updatePayload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PutAsync($"{_baseUrl}/Buses/{busToEdit.BusId}", content);

                        Log.Information("Update Bus API Response Status: {StatusCode}", response.StatusCode);

                        if (response.IsSuccessStatusCode)
                        {
                            Log.Information("Successfully updated bus {BusId} via API.", busToEdit.BusId);
                            await LoadData();
                            dialog.Close();
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            ErrorMessage = "Не удалось найти автобус для обновления.";
                            Log.Warning("Bus {BusId} not found for update via API.", busToEdit.BusId);
                             var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Warning);
                             var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                             if (mainWindow != null) await box.ShowAsync();
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            ErrorMessage = $"Не удалось обновить автобус: ({response.StatusCode}) {error}";
                            Log.Error("Failed to update bus {BusId} via API. Status: {StatusCode}, Error: {Error}",
                                busToEdit.BusId, response.StatusCode, error);
                             var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                             var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                             if (mainWindow != null) await box.ShowAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Ошибка при обновлении автобуса: {ex.Message}";
                        Log.Error(ex, "Exception occurred while updating bus {BusId}.", busToEdit.BusId);
                         var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                         var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                         if (mainWindow != null) await box.ShowAsync();
                    }
                };

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                if (mainWindow != null)
                {
                    Log.Debug("Showing Edit Bus dialog for BusId: {BusId}", busToEdit.BusId);
                    await dialog.ShowDialog(mainWindow);
                }
                else
                {
                    Log.Error("Could not find main window to show Edit Bus dialog.");
                    ErrorMessage = "Не удалось отобразить диалог редактирования.";
                     var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                     var app = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                     if (app?.MainWindow != null) await box.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Ошибка при инициации редактирования автобуса: {ex.Message}";
                Log.Error(ex, "Error initiating Edit Bus command for BusId: {BusId}", SelectedBus?.BusId);
                 var box = MessageBoxManager.GetMessageBoxStandard("Фатальная ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                 var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                 if (mainWindow != null) await box.ShowAsync();
            }
        }


        [RelayCommand]
        private async Task Delete()
        {
            if (SelectedBus == null)
            {
                Log.Warning("Delete Bus command initiated but no bus selected.");
                return;
            }
            var busToDelete = SelectedBus; // Keep reference

            Log.Information("Delete Bus command initiated for BusId: {BusId}", busToDelete.BusId);

            // Confirmation Dialog
            var box = MessageBoxManager.GetMessageBoxStandard(
                "Подтверждение удаления",
                $"Вы уверены, что хотите удалить автобус '{busToDelete.Model}' (ID: {busToDelete.BusId})? Это действие не может быть отменено.",
                ButtonEnum.YesNo,
                Icon.Warning);

            var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
            if (mainWindow == null)
            {
                Log.Error("Could not find main window to show Delete Confirmation dialog.");
                ErrorMessage = "Не удалось отобразить диалог подтверждения.";
                 var errorBox = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                 var app = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                 if (app?.MainWindow != null) await errorBox.ShowAsync();
                return;
            }

            var result = await box.ShowAsync();

            if (result == ButtonResult.Yes)
            {
                Log.Debug("Deletion confirmed for BusId: {BusId}", busToDelete.BusId);
                IsBusy = true;
                Log.Information("Sending request to delete bus {BusId}", busToDelete.BusId);
                try
                {
                    var response = await _httpClient.DeleteAsync($"{_baseUrl}/Buses/{busToDelete.BusId}");

                    Log.Information("Delete Bus API Response Status: {StatusCode}", response.StatusCode);

                    if (response.IsSuccessStatusCode)
                    {
                         Log.Information("Successfully deleted bus {BusId} via API.", busToDelete.BusId);
                        await LoadData();
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        ErrorMessage = "Не удалось найти автобус для удаления.";
                        Log.Warning("Bus {BusId} not found for deletion via API.", busToDelete.BusId);
                         var errorBox = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Warning);
                         await errorBox.ShowAsync();
                    }
                     else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest) // Example: Handle constraints (bus in use)
                    {
                         var error = await response.Content.ReadAsStringAsync();
                         ErrorMessage = $"Не удалось удалить автобус: {error}"; // Show specific reason if API provides it
                         Log.Warning("Failed to delete bus {BusId} due to constraint: {Error}", busToDelete.BusId, error);
                         var errorBox = MessageBoxManager.GetMessageBoxStandard("Ошибка удаления", ErrorMessage, ButtonEnum.Ok, Icon.Warning);
                         await errorBox.ShowAsync();
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                        ErrorMessage = $"Не удалось удалить автобус: ({response.StatusCode}) {error}";
                        Log.Error("Failed to delete bus {BusId} via API. Status: {StatusCode}, Error: {Error}",
                            busToDelete.BusId, response.StatusCode, error);
                         var errorBox = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                         await errorBox.ShowAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Ошибка при удалении автобуса: {ex.Message}";
                    Log.Error(ex, "Exception occurred while deleting bus {BusId}.", busToDelete.BusId);
                     var errorBox = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                     await errorBox.ShowAsync();
                }
                finally
                {
                    IsBusy = false;
                }
            }
            else
            {
                Log.Debug("Deletion cancelled for BusId: {BusId}", busToDelete.BusId);
            }
        }

        private void OnSearchTextChanged(string value)
        {
            Log.Debug("Bus search text changed: {SearchText}", value);
            if (string.IsNullOrWhiteSpace(value))
            {
                Log.Verbose("Bus search text empty, showing all ({Count}) buses.", _allBuses.Count);
                Buses = new ObservableCollection<BusDisplayModel>(_allBuses);
            }
            else
            {
                var lowerCaseValue = value.ToLowerInvariant();
                 // Search on BusDisplayModel properties
                var filtered = _allBuses.Where(bdm =>
                     (bdm.Model?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                     (bdm.RegistrationNumber?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                     (bdm.Bus.BusType?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) || // Search underlying Bus type
                     (bdm.BusId.ToString().Contains(lowerCaseValue)) // Search ID
            ).ToList();
                 Log.Verbose("Filtering complete. Found {Count} buses matching '{SearchText}'.", filtered.Count, value);
                Buses = new ObservableCollection<BusDisplayModel>(filtered);
            }
        }
    }
} 