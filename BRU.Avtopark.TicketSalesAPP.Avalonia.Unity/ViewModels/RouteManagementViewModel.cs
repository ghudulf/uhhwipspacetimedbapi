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
        // ... add others if needed

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

        public RouteManagementViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = "http://localhost:5000/api";

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

            LoadData().ConfigureAwait(false);
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
                        JsonNode? routesNode = JsonNode.Parse(routesJsonString);
                        if (routesNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var routesValuesNode) && routesValuesNode is JsonArray routesArray)
                        {
                            Log.Information("Parsing {Count} route objects from JSON array.", routesArray.Count);
                            foreach (var routeNode in routesArray)
                            {
                                if (routeNode is JsonObject routeObj)
                                {
                                    Log.Verbose("--- Parsing Route Object: {RouteJson} ---", routeObj.ToJsonString());

                                    // Extract Primitive Route Properties
                                    uint routeId = routeObj["routeId"]?.GetValue<uint>() ?? 0;
                                    if (routeId == 0)
                                    {
                                        Log.Error("CRITICAL DATA ISSUE: Found route object with RouteId 0. Skipping. JSON: {RouteJson}", routeObj.ToJsonString());
                                        continue; // Skip this invalid route
                                    }

                                    string routeNumber = routeObj["routeNumber"]?.GetValue<string>() ?? "N/A";
                                    string startPoint = routeObj["startPoint"]?.GetValue<string>() ?? "N/A";
                                    string endPoint = routeObj["endPoint"]?.GetValue<string>() ?? "N/A";
                                    string? travelTime = routeObj["travelTime"]?.GetValue<string>();
                                    bool isActive = routeObj["isActive"]?.GetValue<bool>() ?? false;
                                    uint stopCount = routeObj["stopCount"]?.GetValue<uint>() ?? 0;
                                    double routeLength = routeObj["routeLength"]?.GetValue<double>() ?? 0.0;
                                    string? routeDesc = routeObj["routeDescription"]?.GetValue<string>();
                                    string? routeType = routeObj["routeType"]?.GetValue<string>();


                                    uint busIdFromRoute = routeObj["busId"]?.GetValue<uint>() ?? 0;
                                    uint driverIdFromRoute = routeObj["driverId"]?.GetValue<uint>() ?? 0;
                                    Log.Verbose("Extracted Route Primitives: Id={RouteId}, Num='{RouteNumber}', Start='{Start}', End='{End}', BusId={BusId}, DriverId={DriverId}, Active={IsActive}",
                                        routeId, routeNumber, startPoint, endPoint, busIdFromRoute, driverIdFromRoute, isActive);

                                    // Extract Nested Bus Info
                                    string busModel = "Неизвестный автобус";
                                    uint nestedBusId = 0;
                                    if (routeObj["bus"] is JsonObject busObj)
                                    {
                                        nestedBusId = busObj["busId"]?.GetValue<uint>() ?? 0;
                                        busModel = busObj["model"]?.GetValue<string>() ?? busModel;
                                        string? regNum = busObj["registrationNumber"]?.GetValue<string>();
                                        Log.Verbose("Nested Bus Info: Id={BusId}, Model='{Model}', Reg='{RegNum}'", nestedBusId, busModel, regNum);
                                        if (nestedBusId == 0) Log.Warning("Nested Bus object has BusId 0 for RouteId {RouteId}", routeId);
                                        if (nestedBusId != busIdFromRoute) Log.Warning("Mismatch between Route.BusId ({RouteBusId}) and nested Bus.BusId ({NestedBusId}) for RouteId {RouteId}", busIdFromRoute, nestedBusId, routeId);
                                        // Add to lookup if valid and not already present
                                         if (nestedBusId > 0 && !busLookup.ContainsKey(nestedBusId))
                                         {
                                              busLookup[nestedBusId] = busModel;
                                         }
                                    }
                                    else { Log.Warning("No nested 'Bus' object found for RouteId {RouteId}", routeId); }


                                    // Extract Nested Driver Info
                                    string driverName = "Неизвестный водитель";
                                     uint nestedDriverId = 0;
                                    if (routeObj["driver"] is JsonObject driverObj)
                                    {
                                        nestedDriverId = driverObj["employeeId"]?.GetValue<uint>() ?? 0;
                                        string? name = driverObj["name"]?.GetValue<string>();
                                        string? surname = driverObj["surname"]?.GetValue<string>();
                                        driverName = (!string.IsNullOrWhiteSpace(surname) || !string.IsNullOrWhiteSpace(name)) ? $"{surname} {name}".Trim() : driverName;
                                        Log.Verbose("Nested Driver Info: Id={EmpId}, Name='{DriverName}'", nestedDriverId, driverName);
                                        if (nestedDriverId == 0) Log.Warning("Nested Driver object has EmployeeId 0 for RouteId {RouteId}", routeId);
                                        if (nestedDriverId != driverIdFromRoute) Log.Warning("Mismatch between Route.DriverId ({RouteDriverId}) and nested Driver.EmployeeId ({NestedDriverId}) for RouteId {RouteId}", driverIdFromRoute, nestedDriverId, routeId);
                                         // Add to lookup if valid and not already present
                                         if (nestedDriverId > 0 && !driverLookup.ContainsKey(nestedDriverId))
                                         {
                                              driverLookup[nestedDriverId] = driverName;
                                         }
                                    }
                                     else { Log.Warning("No nested 'Driver' object found for RouteId {RouteId}", routeId); }

                                    // Create SpacetimeDB Route object manually (no nested objects)
                                    var spdRoute = new Route
                                    {
                                        RouteId = routeId,
                                        RouteNumber = routeNumber,
                                        StartPoint = startPoint,
                                        EndPoint = endPoint,
                                        DriverId = driverIdFromRoute, // Use ID from the route level
                                        BusId = busIdFromRoute,       // Use ID from the route level
                                        TravelTime = travelTime,
                                        IsActive = isActive,
                                        StopCount = stopCount,
                                        RouteLength = routeLength,
                                        RouteDescription = routeDesc,
                                        RouteType = routeType,
                                        // Initialize other properties as needed
                                    };

                                    // Create Display Model
                                    var displayModel = new RouteDisplayModel(spdRoute)
                                    {
                                        // Assign extracted nested info
                                        BusModel = busModel,
                                        DriverName = driverName,
                                        TicketCount = 0 // Will be updated later after tickets are processed
                                    };
                                    parsedRoutes.Add(displayModel);
                                    Log.Verbose("Added RouteDisplayModel: Id={RouteId}, Bus='{BusModel}', Driver='{DriverName}'", displayModel.RouteId, displayModel.BusModel, displayModel.DriverName);
                                }
                                else { Log.Warning("Item in routes array was not a JSON object: {Node}", routeNode?.ToJsonString()); }
                            }
                            Log.Information("Successfully parsed {Count} valid route objects.", parsedRoutes.Count);
                        }
                        else { Log.Error("Routes JSON root was not an object with a '$values' array or the structure was unexpected. Root node type: {NodeType}, Raw JSON: {RawJson}", routesNode?.GetType().Name, routesJsonString); }
                    }
                    catch (JsonException jsonEx)
                    {
                        Log.Error(jsonEx, "Failed to parse Routes JSON: {RawJson}", routesJsonString);
                        throw new Exception("Failed to parse route data.", jsonEx);
                    }
                     catch (Exception ex)
                    {
                        Log.Error(ex, "Unexpected error during manual route parsing.");
                        throw; // Re-throw unexpected errors
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
                          JsonNode? busesNode = JsonNode.Parse(busJsonString);
                          if (busesNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var busesValuesNode) && busesValuesNode is JsonArray busesArray)
                          {
                               Log.Information("Parsing {Count} bus objects from JSON array (for ComboBox).", busesArray.Count);
                               foreach(var busNode in busesArray)
                               {
                                    if (busNode is JsonObject busObj)
                                    {
                                         uint busId = busObj["busId"]?.GetValue<uint>() ?? 0;
                                         if (busId == 0)
                                         {
                                              Log.Error("CRITICAL DATA ISSUE: Bus object (for ComboBox) has BusId 0. Filtering out. JSON: {BusJson}", busObj.ToJsonString());
                                              continue; // Filter out
                                         }
                                         string model = busObj["model"]?.GetValue<string>() ?? "N/A";
                                         string? regNum = busObj["registrationNumber"]?.GetValue<string>();
                                         uint capacity = busObj["capacity"]?.GetValue<uint>() ?? 0;
                                         bool busIsActive = busObj["isActive"]?.GetValue<bool>() ?? false;
                                         string? busType = busObj["busType"]?.GetValue<string>();
                                          Log.Verbose("Parsed Bus (for ComboBox): Id={BusId}, Model='{Model}', Reg='{RegNum}', Active={IsActive}", busId, model, regNum, busIsActive);

                                          // Create Bus object
                                          loadedBuses.Add(new Bus {
                                                BusId = busId,
                                                Model = model,
                                                RegistrationNumber = regNum,
                                                Capacity = capacity,
                                                IsActive = busIsActive,
                                                BusType = busType ?? string.Empty
                                                // Map other properties as needed
                                           });

                                          // Update lookup if not already present from route data
                                          if (!busLookup.ContainsKey(busId))
                                          {
                                               busLookup[busId] = model;
                                               Log.Verbose("Added BusId {BusId} ('{Model}') to lookup from Bus list.", busId, model);
                                          }
                                    }
                                     else { Log.Warning("Item in buses array was not a JSON object: {Node}", busNode?.ToJsonString()); }
                               }
                                Log.Information("Successfully parsed and filtered {Count} valid buses (for ComboBox).", loadedBuses.Count);
                          }
                           else { Log.Error("Buses JSON root was not an object with a '$values' array or the structure was unexpected. Root node type: {NodeType}, Raw JSON: {RawJson}", busesNode?.GetType().Name, busJsonString); }

                         AvailableBuses = new ObservableCollection<Bus>(loadedBuses); // Populate ComboBox with FILTERED list
                     }
                     catch (JsonException jsonEx)
                     {
                           Log.Error(jsonEx, "Failed to parse Buses JSON: {RawJson}", busJsonString);
                           AvailableBuses = new ObservableCollection<Bus>(); // Ensure empty on error
                     }
                     catch (Exception ex)
                     {
                          Log.Error(ex, "Unexpected error during manual bus parsing.");
                          AvailableBuses = new ObservableCollection<Bus>(); // Ensure empty on error
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
                         JsonNode? driversNode = JsonNode.Parse(driverJsonString);
                         if (driversNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var driversValuesNode) && driversValuesNode is JsonArray driversArray)
                         {
                              Log.Information("Parsing {Count} driver objects from JSON array (for ComboBox).", driversArray.Count);
                              foreach(var driverNode in driversArray)
                              {
                                   if (driverNode is JsonObject driverObj)
                                   {
                                        uint empId = driverObj["employeeId"]?.GetValue<uint>() ?? 0;
                                        if (empId == 0)
                                        {
                                            Log.Error("CRITICAL DATA ISSUE: Driver object (for ComboBox) has EmployeeId 0. Filtering out. JSON: {DriverJson}", driverObj.ToJsonString());
                                            continue; // Filter out
                                        }
                                        string name = driverObj["name"]?.GetValue<string>() ?? "N/A";
                                        string surname = driverObj["surname"]?.GetValue<string>() ?? "N/A";
                                        string? patronym = driverObj["patronym"]?.GetValue<string>();
                                        uint jobId = driverObj["jobId"]?.GetValue<uint>() ?? 0;
                                         Log.Verbose("Parsed Driver (for ComboBox): Id={EmpId}, Name='{Name}', Surname='{Surname}', JobId={JobId}", empId, name, surname, jobId);

                                        // Create Employee object
                                        loadedDrivers.Add(new Employee {
                                             EmployeeId = empId,
                                             Name = name,
                                             Surname = surname,
                                             Patronym = patronym,
                                             JobId = jobId
                                             // Map other properties as needed
                                        });

                                         // Update lookup if not already present from route data
                                         string fullName = $"{surname} {name}".Trim();
                                         if (!driverLookup.ContainsKey(empId))
                                         {
                                              driverLookup[empId] = fullName;
                                              Log.Verbose("Added EmployeeId {EmpId} ('{FullName}') to lookup from Driver list.", empId, fullName);
                                         }
                                   }
                                    else { Log.Warning("Item in drivers array was not a JSON object: {Node}", driverNode?.ToJsonString()); }
                              }
                               Log.Information("Successfully parsed and filtered {Count} valid drivers (for ComboBox).", loadedDrivers.Count);
                         }
                          else { Log.Error("Drivers JSON root was not an object with a '$values' array or the structure was unexpected. Root node type: {NodeType}, Raw JSON: {RawJson}", driversNode?.GetType().Name, driverJsonString); }

                         AvailableDrivers = new ObservableCollection<Employee>(loadedDrivers); // Populate ComboBox with FILTERED list
                     }
                     catch (JsonException jsonEx)
                     {
                          Log.Error(jsonEx, "Failed to parse Drivers JSON: {RawJson}", driverJsonString);
                          AvailableDrivers = new ObservableCollection<Employee>(); // Ensure empty on error
                     }
                    catch (Exception ex)
                    {
                         Log.Error(ex, "Unexpected error during manual driver parsing.");
                         AvailableDrivers = new ObservableCollection<Employee>(); // Ensure empty on error
                    }
                }
                else
                {
                    var error = await driversResponse.Content.ReadAsStringAsync();
                    Log.Error("Failed to load drivers. Status: {StatusCode}, Error: {Error}", driversResponse.StatusCode, error);
                    AvailableDrivers = new ObservableCollection<Employee>();
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
                         JsonNode? ticketsNode = JsonNode.Parse(ticketJsonString);
                         if (ticketsNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var ticketsValuesNode) && ticketsValuesNode is JsonArray ticketsArray)
                         {
                             Log.Information("Parsing {Count} ticket objects from JSON array (for counts).", ticketsArray.Count);
                             foreach(var ticketNode in ticketsArray)
                             {
                                  if (ticketNode is JsonObject ticketObj)
                                  {
                                       uint routeId = ticketObj["routeId"]?.GetValue<uint>() ?? 0;
                                       bool ticketIsActive = ticketObj["isActive"]?.GetValue<bool>() ?? false; // Assuming API sends isActive
                                       if (routeId > 0 && ticketIsActive) // Count only active tickets for valid routes
                                       {
                                            ticketCounts[routeId] = ticketCounts.GetValueOrDefault(routeId, 0) + 1;
                                       }
                                        else { Log.Verbose("Skipping ticket count for RouteId {RouteId}, IsActive={IsActive}", routeId, ticketIsActive); }
                                  }
                                   else { Log.Warning("Item in tickets array was not a JSON object: {Node}", ticketNode?.ToJsonString()); }
                             }
                               Log.Information("Successfully calculated ticket counts for {Count} routes.", ticketCounts.Count);
                         }
                          else { Log.Error("Tickets JSON root was not an object with a '$values' array or the structure was unexpected. Root node type: {NodeType}, Raw JSON: {RawJson}", ticketsNode?.GetType().Name, ticketJsonString); }
                     }
                     catch (JsonException jsonEx)
                     {
                          Log.Error(jsonEx, "Failed to parse Tickets JSON: {RawJson}", ticketJsonString);
                     }
                     catch (Exception ex)
                     {
                          Log.Error(ex, "Unexpected error during manual ticket parsing.");
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
                var routeNumBox = new TextBox { Watermark = "Номер маршрута" };
                var startPointBox = new TextBox { Watermark = "Начальная точка" };
                var endPointBox = new TextBox { Watermark = "Конечная точка" };
                var travelTimeBox = new TextBox { Watermark = "Время в пути (чч:мм)" };
                var stopCountBox = new NumericUpDown { Watermark = "Количество остановок", Minimum = 0, Increment = 1 };
                var lengthBox = new NumericUpDown { Watermark = "Длина (км)", Minimum = 0, Increment = 0.1M, FormatString = "N1" };
                var descBox = new TextBox { Watermark = "Описание (необязательно)", AcceptsReturn = true, Height = 60 };
                var typeBox = new TextBox { Watermark = "Тип маршрута (Городской, Пригородный и т.д.)" };
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
                var routeNumBox = new TextBox { Text = routeToEdit.RouteNumber, Watermark = "Номер маршрута" };
                var startPointBox = new TextBox { Text = routeToEdit.StartPoint, Watermark = "Начальная точка" };
                var endPointBox = new TextBox { Text = routeToEdit.EndPoint, Watermark = "Конечная точка" };
                var travelTimeBox = new TextBox { Text = routeToEdit.TravelTime, Watermark = "Время в пути (чч:мм)" };
                var stopCountBox = new NumericUpDown { Value = routeToEdit.StopCount, Watermark = "Количество остановок", Minimum = 0, Increment = 1 };
                var lengthBox = new NumericUpDown { Value = (decimal?)routeToEdit.RouteLength, Watermark = "Длина (км)", Minimum = 0, Increment = 0.1M, FormatString = "N1" };
                var descBox = new TextBox { Text = routeToEdit.RouteDescription, Watermark = "Описание", AcceptsReturn = true, Height = 60 };
                var typeBox = new TextBox { Text = routeToEdit.RouteType, Watermark = "Тип маршрута" };
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