using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using System.Globalization;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    // Wrapper class to hold Ticket and associated/looked-up data
    public partial class TicketDisplayModel : ObservableObject
    {
        [ObservableProperty]
        private Ticket _ticket; // The original Ticket object

        [ObservableProperty]
        private Route? _route; // Looked up from Routes

        public TicketDisplayModel(Ticket ticket, Route? route)
        {
            _ticket = ticket;
            _route = route;
        }

        // Expose properties for easier binding
        public uint TicketId => Ticket.TicketId;
        public double TicketPrice => Ticket.TicketPrice;
        public uint SeatNumber => Ticket.SeatNumber;
        public bool IsActive => Ticket.IsActive;
        public ulong PurchaseTime => Ticket.PurchaseTime;
        public ulong CreatedAt => Ticket.CreatedAt;
        public string RouteDisplay => Route != null ? $"{Route.RouteNumber} ({Route.StartPoint} - {Route.EndPoint})" : "Маршрут не найден";
        public string? StartPoint => Route?.StartPoint;
        public string? EndPoint => Route?.EndPoint;
        public string? TravelTime => Route?.TravelTime;
    }

    public partial class TicketManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        // Change to use the display model
        private List<TicketDisplayModel> _allTickets = new();
        private ObservableCollection<TicketDisplayModel> _tickets = new();
        public ObservableCollection<TicketDisplayModel> Tickets
        {
            get => _tickets;
            set => this.RaiseAndSetIfChanged(ref _tickets, value);
        }

        private List<Route> _allRoutes = new();
        private ObservableCollection<Route> _routes = new();
        public ObservableCollection<Route> Routes
        {
            get => _routes;
            set => this.RaiseAndSetIfChanged(ref _routes, value);
        }

        // Change selected item type
        private TicketDisplayModel? _selectedTicket;
        public TicketDisplayModel? SelectedTicket
        {
            get => _selectedTicket;
            set => this.RaiseAndSetIfChanged(ref _selectedTicket, value);
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

        public TicketManagementViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = "http://localhost:5000/api";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) =>
            {
                Log.Information("Auth token changed in TicketManagementViewModel. Recreating HttpClient and reloading data.");
                _httpClient.Dispose();
                _httpClient = ApiClientService.Instance.CreateClient();
                LoadData().ConfigureAwait(false);
            };

            // Only load data if token is already set, otherwise wait for OnAuthTokenChanged event
            if (!string.IsNullOrEmpty(ApiClientService.Instance.AuthToken))
            {
                Log.Information("Token already set in TicketManagementViewModel constructor, loading data");
                LoadData().ConfigureAwait(false);
            }
            else
            {
                Log.Warning("Token not set in TicketManagementViewModel constructor, waiting for OnAuthTokenChanged event");
            }
        }

        private async Task LoadData()
        {
            Log.Information("Starting LoadData for TicketManagementViewModel");
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                Log.Debug("Initiating API calls for Tickets and Routes");
                Task<HttpResponseMessage> ticketsTask = _httpClient.GetAsync($"{_baseUrl}/Tickets");
                Task<HttpResponseMessage> routesTask = _httpClient.GetAsync($"{_baseUrl}/Routes");

                await Task.WhenAll(ticketsTask, routesTask);
                Log.Debug("All API calls completed for TicketManagementViewModel.");

                // --- Process Routes first to build lookup ---
                var routesResponse = await routesTask;
                Log.Information("Processing Routes response. Status: {StatusCode}", routesResponse.StatusCode);
                List<Route> loadedRoutes = new();
                Dictionary<uint, Route> routeLookup = new Dictionary<uint, Route>(); // For linking tickets

                if (routesResponse.IsSuccessStatusCode)
                {
                    var routeJsonString = await routesResponse.Content.ReadAsStringAsync();
                    Log.Verbose("Raw Routes JSON received: {RawJson}", routeJsonString);
                    try
                    {
                        var routesArray = JsonReferenceHelper.ParseArrayWithReferences(routeJsonString, "Route");
                        Log.Information("Parsing {Count} route objects from JSON array.", routesArray.Count);
                        
                        foreach (var routeNode in routesArray)
                        {
                            if (routeNode is JsonObject routeObj)
                            {
                                var route = routeObj.ParseRoute();
                                if (route == null) continue;
                                
                                loadedRoutes.Add(route);
                                if (!routeLookup.ContainsKey(route.RouteId))
                                {
                                    routeLookup.Add(route.RouteId, route);
                                }
                                Log.Verbose("Parsed Route: Id={RouteId}, Num='{RouteNum}', Start='{StartPoint}', End='{EndPoint}', Active={IsActive}", 
                                    route.RouteId, route.RouteNumber, route.StartPoint, route.EndPoint, route.IsActive);
                            }
                        }
                        Log.Information("Successfully parsed {Count} valid routes.", loadedRoutes.Count);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to parse Routes JSON: {RawJson}", routeJsonString);
                        HasError = true;
                        ErrorMessage = "Ошибка загрузки списка маршрутов.";
                    }
                }
                else
                {
                    var error = await routesResponse.Content.ReadAsStringAsync();
                    Log.Warning("Failed to load routes. Status: {StatusCode}, Error: {Error}. Routes data may be incomplete.", routesResponse.StatusCode, error);
                    HasError = true;
                    ErrorMessage = $"Ошибка загрузки маршрутов: {routesResponse.StatusCode}";
                }
                 // Update Routes collection (used by ComboBoxes)
                 _allRoutes = loadedRoutes;
                 Routes = new ObservableCollection<Route>(_allRoutes);

                // --- Process Tickets Response ---
                var ticketsResponse = await ticketsTask;
                Log.Information("Processing Tickets response. Status: {StatusCode}", ticketsResponse.StatusCode);
                List<Ticket> loadedTickets = new(); // Store raw Ticket objects
                var ticketsJsonString = await ticketsResponse.Content.ReadAsStringAsync();
                Log.Verbose("Raw Tickets response received: {RawResponse}", ticketsJsonString);

                if (ticketsResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        var ticketsArray = JsonReferenceHelper.ParseArrayWithReferences(ticketsJsonString, "Ticket");
                        Log.Information("Parsing {Count} ticket objects from JSON array.", ticketsArray.Count);
                        
                        foreach (var ticketNode in ticketsArray)
                        {
                            if (ticketNode is JsonObject ticketObj)
                            {
                                var ticket = ticketObj.ParseTicket();
                                if (ticket == null) continue;
                                
                                // Check if route is embedded in the ticket JSON
                                if (ticketObj.TryGetPropertyValue("route", out var routeNode2) && routeNode2 is JsonObject embeddedRouteObj)
                                {
                                    var embeddedRoute = embeddedRouteObj.ParseRoute();
                                    if (embeddedRoute != null && !routeLookup.ContainsKey(embeddedRoute.RouteId))
                                    {
                                        routeLookup.Add(embeddedRoute.RouteId, embeddedRoute);
                                        Log.Information("Added embedded route from ticket: Id={RouteId}, Start='{StartPoint}', End='{EndPoint}'", 
                                            embeddedRoute.RouteId, embeddedRoute.StartPoint, embeddedRoute.EndPoint);
                                    }
                                }
                                
                                loadedTickets.Add(ticket);
                                Log.Verbose("Parsed Ticket: Id={TicketId}, RouteId={RouteId}, Price={Price}, Seat={Seat}, Active={IsActive}",
                                    ticket.TicketId, ticket.RouteId, ticket.TicketPrice, ticket.SeatNumber, ticket.IsActive);
                            }
                        }
                        Log.Information("Successfully parsed {Count} valid tickets.", loadedTickets.Count);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to parse Tickets JSON: {RawJson}", ticketsJsonString);
                        throw new Exception("Failed to parse ticket data.", ex);
                    }
                }
                else
                {
                    var error = await ticketsResponse.Content.ReadAsStringAsync();
                    Log.Error("Failed to load tickets. Status: {StatusCode}, Error: {Error}", ticketsResponse.StatusCode, error);
                    throw new Exception($"Failed to load primary ticket data. Status: {ticketsResponse.StatusCode}");
                }

                // --- Create TicketDisplayModels by combining Tickets and Routes ---
                List<TicketDisplayModel> displayTickets = new List<TicketDisplayModel>();
                Log.Information("Creating TicketDisplayModels. Parsed Tickets: {TicketCount}, Parsed Routes: {RouteCount}", loadedTickets.Count, routeLookup.Count);
                foreach (var ticket in loadedTickets)
                {
                    routeLookup.TryGetValue(ticket.RouteId, out var route);
                    if (route == null)
                    {
                        Log.Warning("Route lookup failed for TicketId {TicketId} with RouteId {RouteId}", ticket.TicketId, ticket.RouteId);
                    }
                    displayTickets.Add(new TicketDisplayModel(ticket, route));
                    Log.Verbose("Created display model for TicketId {TicketId} with Route: {RouteInfo}", ticket.TicketId, route?.RouteNumber ?? "null");
                }

                // Update the main collections
                _allTickets = displayTickets; // Store the full list
                Tickets = new ObservableCollection<TicketDisplayModel>(_allTickets); // Update the displayed collection

                Log.Information("Finished processing data. Displaying {TicketCount} tickets and {RouteCount} routes.", Tickets.Count, Routes.Count);
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Критическая ошибка загрузки данных: {ex.Message}";
                Log.Fatal(ex, "Fatal error loading data in TicketManagementViewModel");
                _allTickets = new List<TicketDisplayModel>();
                Tickets = new ObservableCollection<TicketDisplayModel>();
                _allRoutes = new List<Route>();
                Routes = new ObservableCollection<Route>();
            }
            finally
            {
                IsBusy = false;
                Log.Information("LoadData finished for TicketManagementViewModel.");
            }
        }

        [RelayCommand]
        private async Task Add()
        {
            try
            {
                var dialog = new Window
                {
                    Title = "Добавить билет",
                    Width = 400,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                var routeComboBox = new ComboBox
                {
                    PlaceholderText = "Выберите маршрут",
                    ItemsSource = Routes,
                    DisplayMemberBinding = new global::Avalonia.Data.Binding(".") { Converter = RouteDisplayConverter.Instance },
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var priceBox = new NumericUpDown
                {
                    Watermark = "Цена билета (BYN)",
                    FormatString = "C2",
                    Increment = 0.5M,
                    Minimum = 0,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var seatBox = new NumericUpDown
                {
                    Watermark = "Номер места",
                    Increment = 1,
                    Minimum = 1,
                    Maximum = 100,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var addButton = new Button
                {
                    Content = "Добавить",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                grid.Children.Add(routeComboBox);
                Grid.SetRow(routeComboBox, 0);
                grid.Children.Add(priceBox);
                Grid.SetRow(priceBox, 1);
                grid.Children.Add(seatBox);
                Grid.SetRow(seatBox, 2);
                grid.Children.Add(addButton);
                Grid.SetRow(addButton, 3);

                dialog.Content = grid;

                addButton.Click += async (s, e) =>
                {
                    if (routeComboBox.SelectedItem is not Route selectedRoute)
                    {
                        ErrorMessage = "Пожалуйста, выберите маршрут";
                        return;
                    }

                    if (!priceBox.Value.HasValue || priceBox.Value.Value < 0)
                    {
                        ErrorMessage = "Пожалуйста, введите корректную цену (>= 0)";
                        HasError = true;
                        return;
                    }
                    decimal price = priceBox.Value.Value;

                    if (!seatBox.Value.HasValue || seatBox.Value.Value < 1)
                    {
                        ErrorMessage = "Пожалуйста, введите корректный номер места (>= 1)";
                        HasError = true;
                        return;
                    }
                    uint seat = (uint)seatBox.Value.Value;

                    var newTicket = new
                    {
                        RouteId = selectedRoute.RouteId,
                        TicketPrice = (double)price,
                        SeatNumber = seat,
                        PaymentMethod = "Cash"
                    };

                    var content = new StringContent(
                        JsonSerializer.Serialize(newTicket, _jsonOptions),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync($"{_baseUrl}/Tickets", content);
                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        ErrorMessage = $"Ошибка добавления билета: {response.StatusCode} - {error}";
                        HasError = true;
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
                ErrorMessage = $"Error adding ticket: {ex.Message}";
                Log.Error(ex, "Error adding ticket");
            }
        }

        [RelayCommand]
        private async Task Edit()
        {
            if (SelectedTicket?.Ticket == null) return; // Check nested Ticket
            var ticketToEdit = SelectedTicket.Ticket; // Get the original Ticket object
            var routeToEdit = SelectedTicket.Route; // Get the associated Route object

            try
            {
                var dialog = new Window
                {
                    Title = "Редактировать билет",
                    Width = 400,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                var routeComboBox = new ComboBox
                {
                    PlaceholderText = "Выберите маршрут",
                    ItemsSource = Routes,
                    DisplayMemberBinding = new global::Avalonia.Data.Binding(".") { Converter = RouteDisplayConverter.Instance },
                    SelectedItem = Routes.FirstOrDefault(r => r.RouteId == ticketToEdit.RouteId),
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var priceBox = new NumericUpDown
                {
                    Value = (decimal)ticketToEdit.TicketPrice,
                    Watermark = "Цена билета (BYN)",
                    FormatString = "C2",
                    Increment = 0.5M,
                    Minimum = 0,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var seatBox = new NumericUpDown
                {
                    Value = ticketToEdit.SeatNumber,
                    Watermark = "Номер места",
                    Increment = 1,
                    Minimum = 1,
                    Maximum = 100,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var updateButton = new Button
                {
                    Content = "Обновить",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                grid.Children.Add(routeComboBox);
                Grid.SetRow(routeComboBox, 0);
                grid.Children.Add(priceBox);
                Grid.SetRow(priceBox, 1);
                grid.Children.Add(seatBox);
                Grid.SetRow(seatBox, 2);
                grid.Children.Add(updateButton);
                Grid.SetRow(updateButton, 3);

                dialog.Content = grid;

                updateButton.Click += async (s, e) =>
                {
                    if (routeComboBox.SelectedItem is not Route selectedRoute)
                    {
                        ErrorMessage = "Пожалуйста, выберите маршрут";
                        return;
                    }

                    if (!priceBox.Value.HasValue || priceBox.Value.Value < 0)
                    {
                        ErrorMessage = "Пожалуйста, введите корректную цену (>= 0)";
                        HasError = true;
                        return;
                    }
                    decimal price = priceBox.Value.Value;

                    if (!seatBox.Value.HasValue || seatBox.Value.Value < 1)
                    {
                        ErrorMessage = "Пожалуйста, введите корректный номер места (>= 1)";
                        HasError = true;
                        return;
                    }
                    uint seat = (uint)seatBox.Value.Value;

                    var updatedTicket = new
                    {
                        RouteId = selectedRoute.RouteId,
                        TicketPrice = (double)price,
                        SeatNumber = seat,
                        PaymentMethod = ticketToEdit.PaymentMethod,
                        IsActive = ticketToEdit.IsActive
                    };

                    var content = new StringContent(
                        JsonSerializer.Serialize(updatedTicket, _jsonOptions),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PutAsync(
                        $"{_baseUrl}/Tickets/{ticketToEdit.TicketId}",
                        content);

                    if (response.IsSuccessStatusCode)
                    {
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        ErrorMessage = $"Ошибка обновления билета: {response.StatusCode} - {error}";
                        HasError = true;
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
                ErrorMessage = $"Error updating ticket: {ex.Message}";
                Log.Error(ex, "Error updating ticket");
            }
        }

        [RelayCommand]
        private async Task Delete()
        {
            if (SelectedTicket?.Ticket == null) return;
            var ticketToDelete = SelectedTicket.Ticket; // Get original ticket

            try
            {
                var dialog = new Window
                {
                    Title = $"Подтверждение удаления билета ID {ticketToDelete.TicketId}",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto"),
                    Margin = new Thickness(10)
                };

                var textBlock = new TextBlock
                {
                    Text = "Вы уверены, что хотите удалить этот билет?",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
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
                    Log.Information("User confirmed deletion for TicketId: {TicketId}", ticketToDelete.TicketId);
                    var response = await _httpClient.DeleteAsync($"{_baseUrl}/Tickets/{ticketToDelete.TicketId}");
                    if (response.IsSuccessStatusCode)
                    {
                        Log.Information("Successfully deleted TicketId: {TicketId}", ticketToDelete.TicketId);
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        Log.Error("Failed to delete ticket {TicketId}. Status: {StatusCode}, Error: {Error}", ticketToDelete.TicketId, response.StatusCode, error);
                        ErrorMessage = $"Ошибка удаления билета: {response.StatusCode} - {error}";
                        HasError = true;
                    }
                };

                noButton.Click += (s, e) => {
                    Log.Information("User cancelled deletion for TicketId: {TicketId}", ticketToDelete.TicketId);
                    dialog.Close();
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
                ErrorMessage = $"Error deleting ticket: {ex.Message}";
                Log.Error(ex, "Error deleting ticket");
            }
        }

        private void OnSearchTextChanged(string value)
        {
            Log.Debug("Search text changed: '{SearchText}'", value);
            if (string.IsNullOrWhiteSpace(value))
            {
                Log.Debug("Search text is empty, resetting filter.");
                Tickets = new ObservableCollection<TicketDisplayModel>(_allTickets);
                return;
            }

            var lowerCaseValue = value.ToLowerInvariant();
            var filteredTickets = _allTickets.Where(tdm =>
                (tdm.TicketId.ToString().Contains(lowerCaseValue)) ||
                (tdm.SeatNumber.ToString().Contains(lowerCaseValue)) ||
                (tdm.TicketPrice.ToString("F2").Contains(lowerCaseValue)) ||
                (tdm.Ticket.PaymentMethod?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) || // Access nested Ticket
                (tdm.Ticket.TicketStatus?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) || // Access nested Ticket
                (tdm.Route?.StartPoint?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) || // Access nested Route
                (tdm.Route?.EndPoint?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) || // Access nested Route
                (tdm.Route?.RouteNumber?.ToLowerInvariant().Contains(lowerCaseValue) ?? false) // Access nested Route
            ).ToList();

            Log.Information("Filtering complete. Found {Count} tickets matching '{SearchText}'", filteredTickets.Count, value);
            Tickets = new ObservableCollection<TicketDisplayModel>(filteredTickets);
        }

        public class RouteDisplayConverter : global::Avalonia.Data.Converters.IValueConverter
        {
            public static readonly RouteDisplayConverter Instance = new();

            public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                if (value is Route route)
                {
                    return $"{route.RouteNumber} ({route.StartPoint} - {route.EndPoint})";
                }
                return value;
            }

            public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
} 