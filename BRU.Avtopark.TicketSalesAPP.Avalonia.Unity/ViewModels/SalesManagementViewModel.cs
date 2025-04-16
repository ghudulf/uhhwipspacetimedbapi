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
using Avalonia.Controls.Templates;

using System.Windows.Input;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public partial class SalesManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private ObservableCollection<Sale> _sales = new();
        public ObservableCollection<Sale> Sales
        {
            get => _sales;
            set => this.RaiseAndSetIfChanged(ref _sales, value);
        }

        private ObservableCollection<Ticket> _availableTickets = new();
        public ObservableCollection<Ticket> AvailableTickets
        {
            get => _availableTickets;
            set => this.RaiseAndSetIfChanged(ref _availableTickets, value);
        }

        private Sale? _selectedSale;
        public Sale? SelectedSale
        {
            get => _selectedSale;
            set => this.RaiseAndSetIfChanged(ref _selectedSale, value);
        }

        private DateTimeOffset _startDate = DateTimeOffset.Now.AddMonths(-1);
        public DateTimeOffset StartDate
        {
            get => _startDate;
            set
            {
                this.RaiseAndSetIfChanged(ref _startDate, value);
                LoadData().ConfigureAwait(false);
            }
        }

        private DateTimeOffset _endDate = DateTimeOffset.Now;
        public DateTimeOffset EndDate
        {
            get => _endDate;
            set
            {
                this.RaiseAndSetIfChanged(ref _endDate, value);
                LoadData().ConfigureAwait(false);
            }
        }

        private decimal _totalIncome;
        public decimal TotalIncome
        {
            get => _totalIncome;
            set => this.RaiseAndSetIfChanged(ref _totalIncome, value);
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

        public SalesManagementViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = "http://localhost:5000/api";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };

            ApiClientService.Instance.OnAuthTokenChanged += (_, token) =>
            {
                _httpClient = ApiClientService.Instance.CreateClient();
                LoadData().ConfigureAwait(false);
            };

            LoadData().ConfigureAwait(false);
        }

        private async Task<UserProfile?> GetCurrentUserAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/Users/current");
                if (response.IsSuccessStatusCode)
                {
                    var userJson = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<UserProfile>(userJson, _jsonOptions);
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting current user");
                return null;
            }
        }

        [RelayCommand]
        private async Task LoadData()
        {
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                // First get all routes
                var routesResponse = await _httpClient.GetAsync($"{_baseUrl}/Routes");
                if (!routesResponse.IsSuccessStatusCode)
                {
                    var error = await routesResponse.Content.ReadAsStringAsync();
                    ErrorMessage = $"Failed to load routes: {error}";
                    HasError = true;
                    Log.Error("Failed to load routes: {Error}", error);
                    return;
                }

                var routesJson = await routesResponse.Content.ReadAsStringAsync();
                var routes = JsonSerializer.Deserialize<List<Route>>(routesJson, _jsonOptions);

                if (routes == null || !routes.Any())
                {
                    ErrorMessage = "No routes available";
                    HasError = true;
                    return;
                }

                // Get sales for date range
                var salesResponse = await _httpClient.GetAsync(
                    $"{_baseUrl}/TicketSales/search?startDate={StartDate.Date:yyyy-MM-dd}&endDate={EndDate.Date:yyyy-MM-dd}");

                if (salesResponse.IsSuccessStatusCode)
                {
                    var jsonString = await salesResponse.Content.ReadAsStringAsync();
                    var sales = JsonSerializer.Deserialize<List<Sale>>(jsonString, _jsonOptions);

                    if (sales != null)
                    {
                        // Fetch all tickets to get prices for income calculation
                        var allTicketsResponse = await _httpClient.GetAsync($"{_baseUrl}/Tickets");
                        List<Ticket> allTickets = new();
                        if (allTicketsResponse.IsSuccessStatusCode)
                        {
                            var ticketsJson = await allTicketsResponse.Content.ReadAsStringAsync();
                            allTickets = JsonSerializer.Deserialize<List<Ticket>>(ticketsJson, _jsonOptions) ?? new();
                        }

                        Sales = new ObservableCollection<Sale>(sales.OrderByDescending(s => s.SaleDate));
                        TotalIncome = (decimal)sales.Sum(s => allTickets.FirstOrDefault(t => t.TicketId == s.TicketId)?.TicketPrice ?? 0);
                        Log.Information("Loaded {Count} sales with total income {Income}", sales.Count, TotalIncome);
                    }
                }
                else
                {
                    var error = await salesResponse.Content.ReadAsStringAsync();
                    ErrorMessage = $"Failed to load sales: {error}";
                    HasError = true;
                    Log.Error("Failed to load sales: {Error}", error);
                }

                // Get available tickets (using dedicated endpoint)
                var availableTicketsResponse = await _httpClient.GetAsync($"{_baseUrl}/Tickets/available");
                if (availableTicketsResponse.IsSuccessStatusCode)
                {
                    var ticketsJson = await availableTicketsResponse.Content.ReadAsStringAsync();
                    var availableTicketsList = JsonSerializer.Deserialize<List<Ticket>>(ticketsJson, _jsonOptions) ?? new();
                    
                    // Need route info for display, fetch all routes
                    // Consider optimizing this if performance is an issue
                    var allRoutesResponse = await _httpClient.GetAsync($"{_baseUrl}/Routes");
                    var routesJsonS = await allRoutesResponse.Content.ReadAsStringAsync();
                    List<Route> allRoutes = new();
                    if (allRoutesResponse.IsSuccessStatusCode)
                    {
                         
                         allRoutes = JsonSerializer.Deserialize<List<Route>>(routesJsonS, _jsonOptions) ?? new();
                    }

                    // Order tickets by route start/end points
                    var orderedTickets = availableTicketsList
                        .Select(t => new { Ticket = t, Route = allRoutes.FirstOrDefault(r => r.RouteId == t.RouteId) })
                        .Where(x => x.Route != null) // Ensure route exists
                        .OrderBy(x => x.Route!.StartPoint)
                        .ThenBy(x => x.Route!.EndPoint)
                        .Select(x => x.Ticket)
                        .ToList();

                    AvailableTickets = new ObservableCollection<Ticket>(orderedTickets);
                    Log.Information("Loaded {Count} available tickets across {RouteCount} routes", 
                        orderedTickets.Count, routes.Count);
                }
                 else
                {
                    var error = await availableTicketsResponse.Content.ReadAsStringAsync();
                    ErrorMessage = $"Failed to load available tickets: {error}";
                    HasError = true;
                    Log.Error("Failed to load available tickets: {Error}", error);
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error loading data: {ex.Message}";
                Log.Error(ex, "Error loading sales data");
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
                Log.Debug("Starting Add() method for new ticket sale");
                
                var currentUser = await GetCurrentUserAsync();
                if (currentUser == null)
                {
                    Log.Error("Failed to get current user information");
                    HasError = true;
                    ErrorMessage = "Failed to get current user information";
                    return;
                }

                // Get all available tickets
                var ticketsResponse = await _httpClient.GetAsync($"{_baseUrl}/Tickets/available");
                if (!ticketsResponse.IsSuccessStatusCode)
                {
                    Log.Error("Failed to get available tickets");
                    HasError = true;
                    ErrorMessage = "Failed to get available tickets";
                    return;
                }

                var ticketsJson = await ticketsResponse.Content.ReadAsStringAsync();
                var availableTickets = JsonSerializer.Deserialize<List<Ticket>>(ticketsJson, _jsonOptions);

                if (availableTickets == null || !availableTickets.Any())
                {
                    Log.Warning("No available tickets found");
                    HasError = true;
                    ErrorMessage = "No available tickets found";
                    return;
                }

                Log.Debug("Creating add sale dialog window");
                var dialog = new Window
                {
                    Title = "Добавить продажу",
                    Width = 500,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
                    Margin = new Thickness(10)
                };

                Log.Debug("Setting up ticket selection combobox with {Count} available tickets", availableTickets.Count);
                var ticketComboBox = new ComboBox
                {
                    PlaceholderText = "Выберите билет",
                    ItemsSource = availableTickets,
                    Width = 400,
                    ItemTemplate = new FuncDataTemplate<Ticket>((ticket, ns) =>
                    {
                        var routeText = $"Route ID: {ticket.RouteId}";
                        return new TextBlock { Text = $"{routeText}, Seat: {ticket.SeatNumber}, Price: {ticket.TicketPrice:C}" };
                    })
                };

                var routeInfoTextBlock = new TextBlock
                {
                    Text = "",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 5, 0, 5)
                };

                ticketComboBox.SelectionChanged += (s, e) =>
                {
                    if (ticketComboBox.SelectedItem is Ticket selectedTicket)
                    {
                        Log.Debug("Ticket selected: ID {TicketId}, Price: {Price}", 
                            selectedTicket.TicketId,
                            selectedTicket.TicketPrice);
                            
                        routeInfoTextBlock.Text = $"Ticket ID: {selectedTicket.TicketId}\nSeat: {selectedTicket.SeatNumber}\nPrice: {selectedTicket.TicketPrice:F2}";
                    }
                    else
                    {
                        Log.Debug("No ticket selected in combobox");
                        routeInfoTextBlock.Text = "";
                    }
                };

                Log.Debug("Setting up date picker with default date {Date}", DateTimeOffset.Now);
                var datePicker = new DatePicker
                {
                    SelectedDate = DateTimeOffset.Now
                };

                Log.Debug("Setting up sale type textbox with default value");
                var saleTypeTextBox = new TextBox
                {
                    Watermark = "Покупатель",
                    Text = "ФИЗ.ПРОДАЖА",
                    Margin = new Thickness(0, 10, 0, 0)
                };

                Log.Debug("Setting up phone textbox with user's phone: {Phone}", currentUser.PhoneNumber ?? "");
                var phoneTextBox = new TextBox
                {
                    Watermark = "Телефон покупателя",
                    Text = currentUser.PhoneNumber ?? "",
                    Margin = new Thickness(0, 10, 0, 0)
                };

                var addButton = new Button
                {
                    Content = "Добавить",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                Log.Debug("Adding controls to dialog grid");
                grid.Children.Add(ticketComboBox);
                Grid.SetRow(ticketComboBox, 0);
                grid.Children.Add(routeInfoTextBlock);
                Grid.SetRow(routeInfoTextBlock, 1);
                grid.Children.Add(datePicker);
                Grid.SetRow(datePicker, 2);
                grid.Children.Add(saleTypeTextBox);
                Grid.SetRow(saleTypeTextBox, 3);
                grid.Children.Add(phoneTextBox);
                Grid.SetRow(phoneTextBox, 4);
                grid.Children.Add(addButton);
                Grid.SetRow(addButton, 5);

                dialog.Content = grid;

                addButton.Click += async (s, e) =>
                {
                    Log.Debug("Add button clicked");
                    
                    if (ticketComboBox.SelectedItem is not Ticket selectedTicket)
                    {
                        Log.Warning("Add attempted without selecting a ticket");
                        ErrorMessage = "Пожалуйста, выберите билет";
                        return;
                    }

                    Log.Debug("Creating new sale for ticket {TicketId}", selectedTicket.TicketId);
                    var newSale = new
                    {
                        TicketId = selectedTicket.TicketId,
                        SaleDate = datePicker.SelectedDate ?? DateTimeOffset.Now,
                        TicketSoldToUser = string.IsNullOrWhiteSpace(saleTypeTextBox.Text) ? "ФИЗ.ПРОДАЖА" : saleTypeTextBox.Text,
                        TicketSoldToUserPhone = string.IsNullOrWhiteSpace(phoneTextBox.Text) ? currentUser.PhoneNumber : phoneTextBox.Text
                    };

                    Log.Debug("Sending POST request to create sale: {@SaleData}", newSale);
                    var content = new StringContent(
                        JsonSerializer.Serialize(newSale, _jsonOptions),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync($"{_baseUrl}/TicketSales", content);
                    if (response.IsSuccessStatusCode)
                    {
                        Log.Information("Successfully created new ticket sale for ticket {TicketId}", selectedTicket.TicketId);
                        await LoadData();
                        dialog.Close();
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        Log.Error("Failed to create ticket sale: {Error}", error);
                        ErrorMessage = $"Failed to add sale: {error}";
                        HasError = true;
                    }
                };

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    Log.Debug("Showing add sale dialog");
                    await dialog.ShowDialog(mainWindow);
                }
                else
                {
                    Log.Error("Could not find main window for dialog");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in Add() method");
                HasError = true;
                ErrorMessage = $"Error adding sale: {ex.Message}";
                Log.Error(ex, "Error adding sale");
            }
        }

        [RelayCommand]
        private async Task Delete()
        {
            try
            {
                if (SelectedSale == null)
                {
                    ErrorMessage = "Выберите продажу для возврата";
                    HasError = true;
                    return;
                }

                // Need to fetch Ticket and Route data to display info
                var ticket = AvailableTickets.FirstOrDefault(t => t.TicketId == SelectedSale.TicketId) ?? 
                             (await _httpClient.GetFromJsonAsync<Ticket>($"{_baseUrl}/Tickets/{SelectedSale.TicketId}"));
                var route = ticket != null ? (await _httpClient.GetFromJsonAsync<Route>($"{_baseUrl}/Routes/{ticket.RouteId}")) : null;

                var dialog = new Window
                {
                    Title = "Подтверждение возврата",
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
                    Text = ticket != null && route != null ? 
                           $"Вы уверены, что хотите вернуть билет?\n\nМаршрут: {route.StartPoint} - {route.EndPoint}\nЦена: {ticket.TicketPrice:C}" :
                           "Вы уверены, что хотите вернуть этот билет? (Детали не загружены)",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 10
                };

                var yesButton = new Button 
                { 
                    Content = "Да",
                    Background = new SolidColorBrush(Color.Parse("#e74c3c")),
                    Foreground = Brushes.White
                };
                
                var noButton = new Button 
                { 
                    Content = "Нет",
                    Background = new SolidColorBrush(Color.Parse("#7f8c8d")),
                    Foreground = Brushes.White
                };

                buttonsPanel.Children.Add(yesButton);
                buttonsPanel.Children.Add(noButton);

                grid.Children.Add(textBlock);
                Grid.SetRow(textBlock, 0);
                grid.Children.Add(buttonsPanel);
                Grid.SetRow(buttonsPanel, 1);

                dialog.Content = grid;

                var tcs = new TaskCompletionSource<bool>();

                yesButton.Click += async (s, e) =>
                {
                    try
                    {
                        var saleId = SelectedSale.SaleId;
                        var response = await _httpClient.DeleteAsync($"{_baseUrl}/TicketSales/{saleId}");
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Log.Information("Successfully deleted sale {SaleId}", saleId);
                            await LoadData();
                            dialog.Close();
                            ErrorMessage = string.Empty;
                            HasError = false;
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            ErrorMessage = $"Ошибка возврата: {error}";
                            HasError = true;
                            Log.Error("Failed to delete sale: {Error}", error);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Ошибка возврата: {ex.Message}";
                        HasError = true;
                        Log.Error(ex, "Error deleting sale");
                    }
                    finally
                    {
                        tcs.TrySetResult(true);
                    }
                };

                noButton.Click += (s, e) =>
                {
                    dialog.Close();
                    tcs.TrySetResult(false);
                };

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                    await tcs.Task; // Wait for dialog result
                }
                else
                {
                    Log.Error("Could not find main window for dialog");
                    ErrorMessage = "Системная ошибка: не найдено главное окно";
                    HasError = true;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка возврата: {ex.Message}";
                HasError = true;
                Log.Error(ex, "Error in Delete method");
            }
        }
    }
} 