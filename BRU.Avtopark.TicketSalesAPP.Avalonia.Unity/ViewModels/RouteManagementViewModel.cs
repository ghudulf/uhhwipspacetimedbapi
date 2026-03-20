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
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using SpacetimeDB.Types;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Avalonia.Controls.Templates;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    // Wrapper class to hold Route and associated/looked-up data
    public partial class RouteDisplayModel : ObservableObject
    {
        [ObservableProperty]
        private Route _route; // The original Route object

        [ObservableProperty]
        private string? _busModel; // Looked up from Buses

        [ObservableProperty]
        private string? _driverName; // Looked up from Employees (Drivers)

        [ObservableProperty]
        private int _ticketCount; // Calculated from Tickets

        public RouteDisplayModel(Route route)
        {
            _route = route;
        }

        // Expose Route properties for easier binding
        [JsonIgnore]
        public uint RouteId => Route.RouteId;
        [JsonIgnore]
        public string RouteNumber => Route.RouteNumber;
        [JsonIgnore]
        public string StartPoint => Route.StartPoint;
        [JsonIgnore]
        public string EndPoint => Route.EndPoint;
        [JsonIgnore]
        public string? TravelTime => Route.TravelTime;
        [JsonIgnore]
        public uint BusId => Route.BusId;
        [JsonIgnore]
        public uint DriverId => Route.DriverId;
        [JsonIgnore]
        public uint StopCount => Route.StopCount;
        [JsonIgnore]
        public double RouteLength => Route.RouteLength;
        [JsonIgnore]
        public string? RouteType => Route.RouteType;
        [JsonIgnore]
        public bool IsActive => Route.IsActive;
        [JsonIgnore]
        public string? RouteDescription => Route.RouteDescription;

        // Format RouteLength for display
        public string RouteLengthDisplay => $"{RouteLength:N1} км";
    }

    public partial class RouteManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;

        // Store the full list for filtering
        private List<RouteDisplayModel> _allRoutes = new();

        private ObservableCollection<RouteDisplayModel> _routes = new(); // Changed type
        public ObservableCollection<RouteDisplayModel> Routes // Changed type
        {
            get => _routes;
            set => this.RaiseAndSetIfChanged(ref _routes, value);
        }

        // Keep original collections for ComboBoxes in Add/Edit dialogs
        private ObservableCollection<Bus> _availableBuses = new();
        public ObservableCollection<Bus> AvailableBuses
        {
            get => _availableBuses;
            set => this.RaiseAndSetIfChanged(ref _availableBuses, value);
        }

        private ObservableCollection<Employee> _availableDrivers = new();
        public ObservableCollection<Employee> AvailableDrivers
        {
            get => _availableDrivers;
            set => this.RaiseAndSetIfChanged(ref _availableDrivers, value);
        }

        private RouteDisplayModel? _selectedRoute; // Changed type
        public RouteDisplayModel? SelectedRoute // Changed type
        {
            get => _selectedRoute;
            set => this.RaiseAndSetIfChanged(ref _selectedRoute, value);
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

        // True when embedded inside a MAUI host — dialogs/windows are not available
        public static bool IsMauiHost => AppContext.GetData("MAUI_HOST") as bool? == true;

        // Inverse — used by AXAML to show/hide dialog-dependent buttons
        public static bool IsDialogCapable => !IsMauiHost;

        public RouteManagementViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = ApiClientService.Instance.CurrentBaseUrl?.TrimEnd('/') ?? "http://localhost:5000/api";

            // Subscribe to auth token changes
            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) =>
            {
                Log.Information("Auth token changed in RouteManagementViewModel. Recreating HttpClient and reloading data.");
                // Create a new client with the updated token
                _httpClient.Dispose();
                _httpClient = ApiClientService.Instance.CreateClient();
                // Reload data with the new token
                LoadData().ConfigureAwait(false);
            };

            // Only load data if token is already set, otherwise wait for OnAuthTokenChanged event
            if (!string.IsNullOrEmpty(ApiClientService.Instance.AuthToken))
            {
                Log.Information("Token already set in RouteManagementViewModel constructor, loading data");
                LoadData().ConfigureAwait(false);
            }
            else
            {
                Log.Warning("Token not set in RouteManagementViewModel constructor, waiting for OnAuthTokenChanged event");
            }
        }

        [RelayCommand]
        private async Task LoadData()
        {
            Log.Information("Starting LoadData for RouteManagementViewModel");
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                // --- Fetch all required data concurrently ---
                Log.Debug("Initiating API calls for Routes, Buses, Drivers, Tickets");
                Task<HttpResponseMessage> routesTask = _httpClient.GetAsync($"{_baseUrl}/Routes");
                Task<HttpResponseMessage> busesTask = _httpClient.GetAsync($"{_baseUrl}/Buses");
                Task<HttpResponseMessage> driversTask = _httpClient.GetAsync($"{_baseUrl}/Employees/drivers");
                Task<HttpResponseMessage> ticketsTask = _httpClient.GetAsync($"{_baseUrl}/Tickets");

                // Await all tasks
                await Task.WhenAll(routesTask, busesTask, driversTask, ticketsTask);
                Log.Debug("All API calls completed for RouteManagementViewModel.");

                // --- Process Responses with Manual Parsing and Logging ---

                // 1. Process Routes Response
                List<RouteDisplayModel> parsedRoutes = new List<RouteDisplayModel>();
                Dictionary<uint, string> busLookup = new Dictionary<uint, string>();
                Dictionary<uint, string> driverLookup = new Dictionary<uint, string>();

                var routesResponse = await routesTask;
                Log.Information("Processing Routes response. Status: {StatusCode}", routesResponse.StatusCode);

                // Log the raw response content
                var routesJsonString = await routesResponse.Content.ReadAsStringAsync();
                Log.Debug("Raw Routes response received: {RawResponse}", routesJsonString);

                if (routesResponse.IsSuccessStatusCode)
                {
                    Log.Debug("Raw Routes JSON received: {RawJson}", routesJsonString);
                    try
                    {
                        var routesArray = JsonReferenceHelper.ParseArrayWithReferences(routesJsonString, "Route");
                        Log.Information("Parsing {Count} route objects from JSON array.", routesArray.Count);
                        
                        foreach (var routeNode in routesArray)
                        {
                            if (routeNode is JsonObject routeObj)
                            {
                                var route = routeObj.ParseRoute();
                                if (route == null) continue;

                                // Extract nested Bus info if present
                                string busModel = "Неизвестный автобус";
                                if (routeObj["bus"] is JsonObject busObj)
                                {
                                    var nestedBus = busObj.ParseBus();
                                    if (nestedBus != null)
                                    {
                                        busModel = nestedBus.Model;
                                        if (!busLookup.ContainsKey(nestedBus.BusId))
                                        {
                                            busLookup[nestedBus.BusId] = busModel;
                                        }
                                    }
                                }

                                // Extract nested Driver info if present
                                string driverName = "Неизвестный водитель";
                                if (routeObj["driver"] is JsonObject driverObj)
                                {
                                    var nestedDriver = driverObj.ParseEmployee();
                                    if (nestedDriver != null)
                                    {
                                        driverName = $"{nestedDriver.Surname} {nestedDriver.Name}".Trim();
                                        if (!driverLookup.ContainsKey(nestedDriver.EmployeeId))
                                        {
                                            driverLookup[nestedDriver.EmployeeId] = driverName;
                                        }
                                    }
                                }

                                // Create Display Model
                                var displayModel = new RouteDisplayModel(route)
                                {
                                    BusModel = busModel,
                                    DriverName = driverName,
                                    TicketCount = 0
                                };
                                parsedRoutes.Add(displayModel);
                                Log.Verbose("Added RouteDisplayModel: Id={RouteId}, Bus='{BusModel}', Driver='{DriverName}'", 
                                    displayModel.RouteId, displayModel.BusModel, displayModel.DriverName);
                            }
                        }
                        Log.Information("Successfully parsed {Count} valid route objects.", parsedRoutes.Count);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to parse Routes JSON: {RawJson}", routesJsonString);
                        throw new Exception("Failed to parse route data.", ex);
                    }
                }
                else
                {
                    var error = await routesResponse.Content.ReadAsStringAsync();
                    Log.Error("Failed to load routes. Status: {StatusCode}, Error: {Error}", routesResponse.StatusCode, error);
                    throw new Exception($"Failed to load primary route data. Status: {routesResponse.StatusCode}");
                }

                // 2. Process Buses Response (for AvailableBuses ComboBox)
                List<Bus> loadedBuses = new();
                var busesResponse = await busesTask;
                Log.Information("Processing Buses response. Status: {StatusCode}", busesResponse.StatusCode);
                if (busesResponse.IsSuccessStatusCode)
                {
                    var busJsonString = await busesResponse.Content.ReadAsStringAsync();
                    Log.Debug("Raw Buses JSON received (for ComboBox): {RawJson}", busJsonString);
                    try
                    {
                        var busesArray = JsonReferenceHelper.ParseArrayWithReferences(busJsonString, "Bus");
                        Log.Information("Parsing {Count} bus objects from JSON array (for ComboBox).", busesArray.Count);
                        
                        foreach (var busNode in busesArray)
                        {
                            if (busNode is JsonObject busObj)
                            {
                                var bus = busObj.ParseBus();
                                if (bus == null) continue;
                                
                                loadedBuses.Add(bus);
                                
                                // Update lookup if not already present from route data
                                if (!busLookup.ContainsKey(bus.BusId))
                                {
                                    busLookup[bus.BusId] = bus.Model;
                                    Log.Verbose("Added BusId {BusId} ('{Model}') to lookup from Bus list.", bus.BusId, bus.Model);
                                }
                            }
                        }
                        Log.Information("Successfully parsed and filtered {Count} valid buses (for ComboBox).", loadedBuses.Count);
                        AvailableBuses = new ObservableCollection<Bus>(loadedBuses);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to parse Buses JSON: {RawJson}", busJsonString);
                        AvailableBuses = new ObservableCollection<Bus>();
                    }
                }
                else
                {
                    var error = await busesResponse.Content.ReadAsStringAsync();
                    Log.Warning("Failed to load buses. Status: {StatusCode}, Error: {Error}. Bus selection dialogs will be empty.", busesResponse.StatusCode, error);
                    AvailableBuses = new ObservableCollection<Bus>(); // Ensure collection is empty
                }

                // 3. Process Drivers Response (for AvailableDrivers ComboBox)
                 List<Employee> loadedDrivers = new();
                var driversResponse = await driversTask;
                 Log.Information("Processing Drivers/Employees response. Status: {StatusCode}", driversResponse.StatusCode);
                if (driversResponse.IsSuccessStatusCode)
                {
                    var driverJsonString = await driversResponse.Content.ReadAsStringAsync();
                    Log.Debug("Raw Drivers JSON received (for ComboBox): {RawJson}", driverJsonString);
                    try
                    {
                        var driversArray = JsonReferenceHelper.ParseArrayWithReferences(driverJsonString, "Employee");
                        Log.Information("Parsing {Count} driver objects from JSON array (for ComboBox).", driversArray.Count);
                        
                        foreach (var driverNode in driversArray)
                        {
                            if (driverNode is JsonObject driverObj)
                            {
                                var driver = driverObj.ParseEmployee();
                                if (driver == null) continue;
                                
                                loadedDrivers.Add(driver);
                                
                                // Update lookup if not already present from route data
                                string fullName = $"{driver.Surname} {driver.Name}".Trim();
                                
                                if (!driverLookup.ContainsKey(driver.EmployeeId))
                                {
                                    driverLookup[driver.EmployeeId] = fullName;
                                    Log.Verbose("Added EmployeeId {EmpId} ('{FullName}') to lookup from Driver list.", driver.EmployeeId, fullName);
                                }
                            }
                        }
                        Log.Information("Successfully parsed and filtered {Count} valid drivers (for ComboBox).", loadedDrivers.Count);
                        AvailableDrivers = new ObservableCollection<Employee>(loadedDrivers);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to parse Drivers JSON: {RawJson}", driverJsonString);
                        AvailableDrivers = new ObservableCollection<Employee>();
                    }
                }
                else
                {
                    var error = await driversResponse.Content.ReadAsStringAsync();
                    Log.Error("Failed to load drivers. Status: {StatusCode}, Error: {Error}", driversResponse.StatusCode, error);
                    AvailableDrivers = new ObservableCollection<Employee>(); // Clear the collection on failure
                    ErrorMessage = $"Ошибка загрузки водителей: {driversResponse.ReasonPhrase}";
                    HasError = true; // Set error flag if drivers fail to load
                }


                 // 4. Process Tickets Response (for counts)
                Dictionary<uint, int> ticketCounts = new Dictionary<uint, int>();
                var ticketsResponse = await ticketsTask;
                Log.Information("Processing Tickets response. Status: {StatusCode}", ticketsResponse.StatusCode);
                if (ticketsResponse.IsSuccessStatusCode)
                {
                    var ticketJsonString = await ticketsResponse.Content.ReadAsStringAsync();
                    Log.Debug("Raw Tickets JSON received (for counts): {RawJson}", ticketJsonString);
                    try
                    {
                        var ticketsArray = JsonReferenceHelper.ParseArrayWithReferences(ticketJsonString, "Ticket");
                        Log.Information("Parsing {Count} ticket objects from JSON array (for counts).", ticketsArray.Count);
                        
                        foreach (var ticketNode in ticketsArray)
                        {
                            if (ticketNode is JsonObject ticketObj)
                            {
                                var ticket = ticketObj.ParseTicket();
                                if (ticket == null) continue;
                                
                                if (ticket.RouteId > 0 && ticket.IsActive)
                                {
                                    ticketCounts[ticket.RouteId] = ticketCounts.GetValueOrDefault(ticket.RouteId, 0) + 1;
                                }
                            }
                        }
                        Log.Information("Successfully calculated ticket counts for {Count} routes.", ticketCounts.Count);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to parse Tickets JSON: {RawJson}", ticketJsonString);
                    }
                }
                else
                {
                    var error = await ticketsResponse.Content.ReadAsStringAsync();
                    Log.Warning("Failed to load tickets. Status: {StatusCode}, Error: {Error}. Ticket counts may be inaccurate.", ticketsResponse.StatusCode, error);
                    // Ticket counts will remain empty or incomplete
                }


                // --- Combine Data and Update UI ---
                Log.Debug("Combining parsed data and updating UI...");

                 // Update Display Models with lookups and counts
                 foreach (var displayModel in parsedRoutes)
                 {
                      // Update names/models from lookups created during bus/driver list parsing
                      displayModel.BusModel = busLookup.TryGetValue(displayModel.BusId, out var busModel) ? busModel : "Неизвестный автобус";
                      displayModel.DriverName = driverLookup.TryGetValue(displayModel.DriverId, out var driverName) ? driverName : "Неизвестный водитель";
                      displayModel.TicketCount = ticketCounts.TryGetValue(displayModel.RouteId, out var tc) ? tc : 0;

                      Log.Verbose("Final RouteDisplayModel: Id={RouteId}, Bus='{BusModel}', Driver='{DriverName}', Tickets={TicketCount}",
                           displayModel.RouteId, displayModel.BusModel, displayModel.DriverName, displayModel.TicketCount);
                 }


                _allRoutes = parsedRoutes; // Store the filtered & parsed list
                Routes = new ObservableCollection<RouteDisplayModel>(_allRoutes); // Update the displayed list
                Log.Information("Finished processing all data. Displaying {Count} routes.", Routes.Count);
                 Log.Information("Available Buses for ComboBox: {Count}", AvailableBuses.Count);
                 Log.Information("Available Drivers for ComboBox: {Count}", AvailableDrivers.Count);

            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Критическая ошибка загрузки данных: {ex.Message}";
                Log.Fatal(ex, "Fatal error loading data in RouteManagementViewModel");
                // Clear collections on fatal error
                 Routes = new ObservableCollection<RouteDisplayModel>();
                 AvailableBuses = new ObservableCollection<Bus>();
                 AvailableDrivers = new ObservableCollection<Employee>();
                 _allRoutes = new List<RouteDisplayModel>();
            }
            finally
            {
                IsBusy = false;
                Log.Information("LoadData finished for RouteManagementViewModel.");
            }
        }


        [RelayCommand]
        private async Task Add()
        {
            if (IsMauiHost)
            {
                Log.Information("Add Route skipped: running under MAUI host, dialogs not supported.");
                return;
            }
            Log.Information("Add Route command initiated.");
            // Ensure helper data is loaded (optional check, LoadData should run first)
            if (!AvailableBuses.Any() || !AvailableDrivers.Any())
            {
                Log.Warning("Cannot add route: Available buses or drivers not loaded. Attempting reload.");
                 HasError = true; // Indicate potential issue
                 ErrorMessage = "Данные для выбора автобусов или водителей не загружены. Повторная загрузка...";
                 await LoadData(); // Attempt to reload data
                 if (!AvailableBuses.Any() || !AvailableDrivers.Any()) // Check again
                 {
                      ErrorMessage = "Не удалось загрузить данные для выбора автобусов или водителей. Добавление маршрута невозможно.";
                      var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                      var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                      if (mainWindow != null) await box.ShowAsync();
                      return; // Exit if still not loaded
                 }
                 HasError = false; // Clear error if reload succeeded
                 ErrorMessage = string.Empty;
            }

            try
            {
                 // Use SpacetimeDB Types for selection
                 var selectedBus = (Bus?)null;
                 var selectedDriver = (Employee?)null;

                 // Create Dialog Controls
                var dialog = new Window
                {
                    Title = "Добавить маршрут",
                    Width = 450,
                    Height = 550, // Adjusted height
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Padding = new Thickness(15)
                };

                var sp = new StackPanel { Spacing = 10 };

                // Input Fields
                var routeNumBox = new TextBox { PlaceholderText = "Номер маршрута" };
                var startPointBox = new TextBox { PlaceholderText = "Начальная точка" };
                var endPointBox = new TextBox { PlaceholderText = "Конечная точка" };
                var travelTimeBox = new TextBox { PlaceholderText = "Время в пути (чч:мм)" };
                var stopCountBox = new NumericUpDown { PlaceholderText = "Количество остановок", Minimum = 0, Increment = 1 };
                var lengthBox = new NumericUpDown { PlaceholderText = "Длина (км)", Minimum = 0, Increment = 0.1M, FormatString = "N1" };
                var descBox = new TextBox { PlaceholderText = "Описание (необязательно)", AcceptsReturn = true, Height = 60 };
                var typeBox = new TextBox { PlaceholderText = "Тип маршрута (Городской, Пригородный и т.д.)" };
                var isActiveCheck = new CheckBox { Content = "Активен", IsChecked = true };

                 // Use SpacetimeDB.Types.Bus for Bus ComboBox ItemsSource
                var busComboBox = new ComboBox
                {
                    PlaceholderText = "Выберите автобус",
                    ItemsSource = AvailableBuses, // Should contain SpacetimeDB.Types.Bus
                    DisplayMemberBinding = new global::Avalonia.Data.Binding("Model") // Bind to Model property of Bus
                };

                 // Use SpacetimeDB.Types.Employee for Driver ComboBox ItemsSource
                var driverComboBox = new ComboBox
                {
                    PlaceholderText = "Выберите водителя",
                    ItemsSource = AvailableDrivers, // Should contain SpacetimeDB.Types.Employee
                    // Create a display binding combining Surname and Name
                    ItemTemplate = new FuncDataTemplate<Employee>((emp, ns) =>
                         new TextBlock { Text = $"{emp?.Surname} {emp?.Name}".Trim() } // Display Full Name
                    )
                };

                sp.Children.Add(new TextBlock { Text = "Номер маршрута:" }); sp.Children.Add(routeNumBox);
                sp.Children.Add(new TextBlock { Text = "Начальная точка:" }); sp.Children.Add(startPointBox);
                sp.Children.Add(new TextBlock { Text = "Конечная точка:" }); sp.Children.Add(endPointBox);
                sp.Children.Add(new TextBlock { Text = "Время в пути:" }); sp.Children.Add(travelTimeBox);
                sp.Children.Add(new TextBlock { Text = "Остановки:" }); sp.Children.Add(stopCountBox);
                sp.Children.Add(new TextBlock { Text = "Длина (км):" }); sp.Children.Add(lengthBox);
                sp.Children.Add(new TextBlock { Text = "Описание:" }); sp.Children.Add(descBox);
                sp.Children.Add(new TextBlock { Text = "Тип:" }); sp.Children.Add(typeBox);
                sp.Children.Add(new TextBlock { Text = "Автобус:" }); sp.Children.Add(busComboBox);
                sp.Children.Add(new TextBlock { Text = "Водитель:" }); sp.Children.Add(driverComboBox);
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
                    Log.Debug("Add Route dialog cancelled.");
                    dialog.Close();
                };

                addButton.Click += async (s, e) =>
                {
                    Log.Debug("Attempting to add new route.");
                    // Validation
                    if (string.IsNullOrWhiteSpace(routeNumBox.Text) ||
                        string.IsNullOrWhiteSpace(startPointBox.Text) ||
                        string.IsNullOrWhiteSpace(endPointBox.Text) ||
                        !stopCountBox.Value.HasValue ||
                        !lengthBox.Value.HasValue ||
                        busComboBox.SelectedItem == null ||
                        driverComboBox.SelectedItem == null)
                    {
                        ErrorMessage = "Номер, Начало, Конец, Остановки, Длина, Автобус и Водитель обязательны.";
                        Log.Warning("Add Route validation failed: Missing required fields.");
                         var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Warning);
                         var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                         if (mainWindow != null) await box.ShowAsync();
                        return;
                    }

                     // Cast selected items to the correct SpacetimeDB types
                     selectedBus = busComboBox.SelectedItem as Bus;
                     selectedDriver = driverComboBox.SelectedItem as Employee;

                    if (selectedBus == null || selectedDriver == null)
                    {
                        ErrorMessage = "Некорректный выбор автобуса или водителя.";
                        Log.Error("Add Route failed: Could not cast selected items to Bus/Employee. Bus: {@BusItem}, Driver: {@DriverItem}", busComboBox.SelectedItem, driverComboBox.SelectedItem);
                         var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                         var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                         if (mainWindow != null) await box.ShowAsync();
                        return;
                    }

                    // Create payload matching API's CreateRouteModel (adjust if needed)
                    // **NOTE:** Ensure the API expects the correct model structure.
                    // This payload assumes the API expects the structure defined in RoutesController.cs
                    var newRoutePayload = new
                    {
                        RouteNumber = routeNumBox.Text, // Added RouteNumber if needed by API
                        StartPoint = startPointBox.Text,
                        EndPoint = endPointBox.Text,
                        DriverId = selectedDriver.EmployeeId, // Send Driver's EmployeeId
                        BusId = selectedBus.BusId, // Send BusId
                        TravelTime = string.IsNullOrWhiteSpace(travelTimeBox.Text) ? null : travelTimeBox.Text,
                        StopCount = (uint)(stopCountBox.Value ?? 0), // Added StopCount
                        RouteDescription = string.IsNullOrWhiteSpace(descBox.Text) ? null : descBox.Text, // Added Description
                        RouteLength = (double)(lengthBox.Value ?? 0), // Added Length
                        IsActive = isActiveCheck.IsChecked ?? true,
                        RouteType = string.IsNullOrWhiteSpace(typeBox.Text) ? null : typeBox.Text, // Added Type
                    };

                    Log.Information("Sending request to add route: {@RoutePayload}", newRoutePayload);
                    try
                    {
                        // Use default JsonSerializerOptions or configure as needed
                        var json = JsonSerializer.Serialize(newRoutePayload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync($"{_baseUrl}/Routes", content);

                         Log.Information("Add Route API Response Status: {StatusCode}", response.StatusCode);

                    if (response.IsSuccessStatusCode)
                    {
                             Log.Information("Successfully added route via API.");
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                            ErrorMessage = $"Не удалось добавить маршрут: ({response.StatusCode}) {error}";
                            Log.Error("Failed to add route via API. Status: {StatusCode}, Error: {Error}",
                                response.StatusCode, error);
                             var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                             var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                             if (mainWindow != null) await box.ShowAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Ошибка при добавлении маршрута: {ex.Message}";
                        Log.Error(ex, "Exception occurred while adding route.");
                         var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                         var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                         if (mainWindow != null) await box.ShowAsync();
                    }
                };

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                if (mainWindow != null)
                {
                    Log.Debug("Showing Add Route dialog.");
                    await dialog.ShowDialog(mainWindow);
                }
                else
                {
                    Log.Error("Could not find main window to show Add Route dialog.");
                    ErrorMessage = "Не удалось отобразить диалог добавления.";
                     var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                     var app = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                     if (app?.MainWindow != null) await box.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Ошибка при инициации добавления маршрута: {ex.Message}";
                Log.Error(ex, "Error initiating Add Route command");
                 var box = MessageBoxManager.GetMessageBoxStandard("Фатальная ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                 var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                 if (mainWindow != null) await box.ShowAsync();
            }
        }


        [RelayCommand]
        private async Task Edit()
        {
             if (IsMauiHost)
             {
                 Log.Information("Edit Route skipped: running under MAUI host, dialogs not supported.");
                 return;
             }
             if (SelectedRoute == null)
             {
                  Log.Warning("Edit Route command initiated but no route selected.");
                  return;
             }

            var routeDisplayToEdit = SelectedRoute; // Keep reference to the display model
             var routeToEdit = routeDisplayToEdit.Route; // Get underlying SpacetimeDB Route object

             Log.Information("Edit Route command initiated for RouteId: {RouteId}", routeToEdit.RouteId);

            // Ensure helper data is loaded
            if (!AvailableBuses.Any() || !AvailableDrivers.Any())
            {
                 Log.Warning("Cannot edit route: Available buses or drivers not loaded. Attempting reload.");
                 HasError = true; // Indicate potential issue
                 ErrorMessage = "Данные для выбора автобусов или водителей не загружены. Повторная загрузка...";
                 await LoadData(); // Attempt to reload data
                 if (!AvailableBuses.Any() || !AvailableDrivers.Any()) // Check again
                 {
                      ErrorMessage = "Не удалось загрузить данные для выбора автобусов или водителей. Редактирование маршрута невозможно.";
                      var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                      var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                      if (mainWindow != null) await box.ShowAsync();
                      return; // Exit if still not loaded
                 }
                 HasError = false; // Clear error if reload succeeded
                 ErrorMessage = string.Empty;
            }

            try
            {
                 var selectedBus = AvailableBuses.FirstOrDefault(b => b.BusId == routeToEdit.BusId);
                 var selectedDriver = AvailableDrivers.FirstOrDefault(d => d.EmployeeId == routeToEdit.DriverId);

                var dialog = new Window
                {
                     Title = $"Редактировать маршрут: {routeToEdit.RouteNumber} (ID: {routeToEdit.RouteId})",
                    Width = 450,
                    Height = 550,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Padding = new Thickness(15)
                };

                var sp = new StackPanel { Spacing = 10 };

                // Pre-populate fields using the SpacetimeDB Route object
                var routeNumBox = new TextBox { Text = routeToEdit.RouteNumber, PlaceholderText = "Номер маршрута" };
                var startPointBox = new TextBox { Text = routeToEdit.StartPoint, PlaceholderText = "Начальная точка" };
                var endPointBox = new TextBox { Text = routeToEdit.EndPoint, PlaceholderText = "Конечная точка" };
                var travelTimeBox = new TextBox { Text = routeToEdit.TravelTime, PlaceholderText = "Время в пути (чч:мм)" };
                var stopCountBox = new NumericUpDown { Value = routeToEdit.StopCount, PlaceholderText = "Количество остановок", Minimum = 0, Increment = 1 };
                var lengthBox = new NumericUpDown { Value = (decimal?)routeToEdit.RouteLength, PlaceholderText = "Длина (км)", Minimum = 0, Increment = 0.1M, FormatString = "N1" };
                var descBox = new TextBox { Text = routeToEdit.RouteDescription, PlaceholderText = "Описание", AcceptsReturn = true, Height = 60 };
                var typeBox = new TextBox { Text = routeToEdit.RouteType, PlaceholderText = "Тип маршрута" };
                var isActiveCheck = new CheckBox { Content = "Активен", IsChecked = routeToEdit.IsActive };

                // Setup ComboBoxes
                var busComboBox = new ComboBox
                {
                    PlaceholderText = "Выберите автобус",
                    ItemsSource = AvailableBuses,
                    DisplayMemberBinding = new global::Avalonia.Data.Binding("Model"),
                    SelectedItem = selectedBus // Pre-select using the found Bus object
                };

                var driverComboBox = new ComboBox
                {
                    PlaceholderText = "Выберите водителя",
                    ItemsSource = AvailableDrivers,
                    ItemTemplate = new FuncDataTemplate<Employee>((emp, ns) =>
                         new TextBlock { Text = $"{emp?.Surname} {emp?.Name}".Trim() } // Display Full Name
                    ),
                    SelectedItem = selectedDriver // Pre-select using the found Employee object
                };


                // Add controls to StackPanel
                sp.Children.Add(new TextBlock { Text = "Номер маршрута:" }); sp.Children.Add(routeNumBox);
                sp.Children.Add(new TextBlock { Text = "Начальная точка:" }); sp.Children.Add(startPointBox);
                sp.Children.Add(new TextBlock { Text = "Конечная точка:" }); sp.Children.Add(endPointBox);
                sp.Children.Add(new TextBlock { Text = "Время в пути:" }); sp.Children.Add(travelTimeBox);
                sp.Children.Add(new TextBlock { Text = "Остановки:" }); sp.Children.Add(stopCountBox);
                sp.Children.Add(new TextBlock { Text = "Длина (км):" }); sp.Children.Add(lengthBox);
                sp.Children.Add(new TextBlock { Text = "Описание:" }); sp.Children.Add(descBox);
                sp.Children.Add(new TextBlock { Text = "Тип:" }); sp.Children.Add(typeBox);
                sp.Children.Add(new TextBlock { Text = "Автобус:" }); sp.Children.Add(busComboBox);
                sp.Children.Add(new TextBlock { Text = "Водитель:" }); sp.Children.Add(driverComboBox);
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
                    Log.Debug("Edit Route dialog cancelled for RouteId: {RouteId}", routeToEdit.RouteId);
                    dialog.Close();
                };

                saveButton.Click += async (s, e) =>
                {
                    Log.Debug("Attempting to save changes for RouteId: {RouteId}", routeToEdit.RouteId);
                    // Validation
                    if (string.IsNullOrWhiteSpace(routeNumBox.Text) ||
                        string.IsNullOrWhiteSpace(startPointBox.Text) ||
                        string.IsNullOrWhiteSpace(endPointBox.Text) ||
                        !stopCountBox.Value.HasValue ||
                        !lengthBox.Value.HasValue ||
                        busComboBox.SelectedItem == null ||
                        driverComboBox.SelectedItem == null)
                    {
                        ErrorMessage = "Номер, Начало, Конец, Остановки, Длина, Автобус и Водитель обязательны.";
                        Log.Warning("Edit Route validation failed: Missing required fields for RouteId: {RouteId}", routeToEdit.RouteId);
                         var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Warning);
                         var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                         if (mainWindow != null) await box.ShowAsync();
                        return;
                    }

                    var currentSelectedBus = busComboBox.SelectedItem as Bus;
                    var currentSelectedDriver = driverComboBox.SelectedItem as Employee;

                    if (currentSelectedBus == null || currentSelectedDriver == null)
                    {
                        ErrorMessage = "Некорректный выбор автобуса или водителя.";
                        Log.Error("Edit Route failed: Could not cast selected items to Bus/Employee for RouteId: {RouteId}.", routeToEdit.RouteId);
                         var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                         var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                         if (mainWindow != null) await box.ShowAsync();
                        return;
                    }

                    // Create payload matching API's UpdateRouteModel
                    // **IMPORTANT**: Verify the API expects this structure!
                    var updatePayload = new
                    {
                        StartPoint = startPointBox.Text, // Nullable in API? Assume it can be updated
                        EndPoint = endPointBox.Text,     // Nullable in API? Assume it can be updated
                        BusId = (uint?)currentSelectedBus.BusId,        // Send as uint?
                        DriverId = (uint?)currentSelectedDriver.EmployeeId,  // Send as uint?
                        TravelTime = string.IsNullOrWhiteSpace(travelTimeBox.Text) ? null : travelTimeBox.Text, // Nullable string
                        // Add other fields the API expects for update, check UpdateRouteModel in RoutesController
                        // For example, if RouteNumber can be updated:
                        // RouteNumber = routeNumBox.Text,
                        // StopCount = (uint?)(stopCountBox.Value ?? 0),
                        // RouteLength = (double?)(lengthBox.Value ?? 0.0),
                        // RouteDescription = descBox.Text,
                         IsActive = isActiveCheck.IsChecked // Send IsActive? Check API model.
                        // RouteType = typeBox.Text,
                    };


                    Log.Information("Sending request to update route {RouteId}: {@UpdatePayload}", routeToEdit.RouteId, updatePayload);

                    try
                    {
                         // Use default options or configure as needed
                        var json = JsonSerializer.Serialize(updatePayload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PutAsync($"{_baseUrl}/Routes/{routeToEdit.RouteId}", content);

                        Log.Information("Update Route API Response Status: {StatusCode}", response.StatusCode);

                        if (response.IsSuccessStatusCode)
                        {
                             Log.Information("Successfully updated route {RouteId} via API.", routeToEdit.RouteId);
                        await LoadData();
                        dialog.Close();
                    }
                        else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            ErrorMessage = "Не удалось найти маршрут для обновления.";
                            Log.Warning("Route {RouteId} not found for update via API.", routeToEdit.RouteId);
                             var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Warning);
                             var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                             if (mainWindow != null) await box.ShowAsync();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                            ErrorMessage = $"Не удалось обновить маршрут: ({response.StatusCode}) {error}";
                            Log.Error("Failed to update route {RouteId} via API. Status: {StatusCode}, Error: {Error}",
                                routeToEdit.RouteId, response.StatusCode, error);
                             var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                             var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                             if (mainWindow != null) await box.ShowAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Ошибка при обновлении маршрута: {ex.Message}";
                        Log.Error(ex, "Exception occurred while updating route {RouteId}.", routeToEdit.RouteId);
                         var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                         var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                         if (mainWindow != null) await box.ShowAsync();
                    }
                };

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                if (mainWindow != null)
                {
                    Log.Debug("Showing Edit Route dialog for RouteId: {RouteId}", routeToEdit.RouteId);
                    await dialog.ShowDialog(mainWindow);
                }
                else
                {
                    Log.Error("Could not find main window to show Edit Route dialog.");
                    ErrorMessage = "Не удалось отобразить диалог редактирования.";
                    var box = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                    var app = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    if (app?.MainWindow != null) await box.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Ошибка при инициации редактирования маршрута: {ex.Message}";
                Log.Error(ex, "Error initiating Edit Route command for RouteId: {RouteId}", SelectedRoute?.RouteId);
                 var box = MessageBoxManager.GetMessageBoxStandard("Фатальная ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                 var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                 if (mainWindow != null) await box.ShowAsync();
            }
        }


        [RelayCommand]
        private async Task Delete()
        {
            if (IsMauiHost)
            {
                Log.Information("Delete Route skipped: running under MAUI host, dialogs not supported.");
                return;
            }
            if (SelectedRoute == null)
            {
                Log.Warning("Delete Route command initiated but no route selected.");
                return;
            }
            var routeToDelete = SelectedRoute; // Keep reference

            Log.Information("Delete Route command initiated for RouteId: {RouteId}", routeToDelete.RouteId);

            // Confirmation Dialog
            var box = MessageBoxManager.GetMessageBoxStandard(
                "Подтверждение удаления",
                 $"Вы уверены, что хотите удалить маршрут '{routeToDelete.RouteNumber}' (ID: {routeToDelete.RouteId})?",
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
                Log.Debug("Deletion confirmed for RouteId: {RouteId}", routeToDelete.RouteId);
                IsBusy = true;
                Log.Information("Sending request to delete route {RouteId}", routeToDelete.RouteId);
                try
                {
                    var response = await _httpClient.DeleteAsync($"{_baseUrl}/Routes/{routeToDelete.RouteId}");

                     Log.Information("Delete Route API Response Status: {StatusCode}", response.StatusCode);

                    if (response.IsSuccessStatusCode)
                    {
                         Log.Information("Successfully deleted route {RouteId} via API.", routeToDelete.RouteId);
                            await LoadData();
                        }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        ErrorMessage = "Не удалось найти маршрут для удаления.";
                        Log.Warning("Route {RouteId} not found for deletion via API.", routeToDelete.RouteId);
                         var errorBox = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Warning);
                         await errorBox.ShowAsync();
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                        ErrorMessage = $"Не удалось удалить маршрут: ({response.StatusCode}) {error}";
                        Log.Error("Failed to delete route {RouteId} via API. Status: {StatusCode}, Error: {Error}",
                            routeToDelete.RouteId, response.StatusCode, error);
                         var errorBox = MessageBoxManager.GetMessageBoxStandard("Ошибка", ErrorMessage, ButtonEnum.Ok, Icon.Error);
                         await errorBox.ShowAsync();
                    }
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"Ошибка при удалении маршрута: {ex.Message}";
                    Log.Error(ex, "Exception occurred while deleting route {RouteId}.", routeToDelete.RouteId);
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
                Log.Debug("Deletion cancelled for RouteId: {RouteId}", routeToDelete.RouteId);
            }
        }


        private void OnSearchTextChanged(string value)
        {
            Log.Debug("Search text changed: {SearchText}", value);
            if (string.IsNullOrWhiteSpace(value))
            {
                Log.Verbose("Search text empty, showing all ({Count}) routes.", _allRoutes.Count);
                Routes = new ObservableCollection<RouteDisplayModel>(_allRoutes);
            }
            else
            {
                var lowerCaseValue = value.ToLowerInvariant();
                 // Search on RouteDisplayModel properties
                var filtered = _allRoutes.Where(rdm =>
                     (rdm.RouteNumber?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                     (rdm.StartPoint?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                     (rdm.EndPoint?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                     (rdm.BusModel?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) ||
                     (rdm.DriverName?.ToLowerInvariant().Contains(lowerCaseValue) ?? false)
            ).ToList();
                 Log.Verbose("Filtering complete. Found {Count} routes matching '{SearchText}'.", filtered.Count, value);
                Routes = new ObservableCollection<RouteDisplayModel>(filtered);
            }
        }
    }
} 