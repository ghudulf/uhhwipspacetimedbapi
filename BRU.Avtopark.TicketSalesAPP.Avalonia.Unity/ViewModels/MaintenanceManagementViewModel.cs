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
    public partial class MaintenanceManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private ObservableCollection<Maintenance> _maintenanceRecords = new();
        public ObservableCollection<Maintenance> MaintenanceRecords
        {
            get => _maintenanceRecords;
            set => this.RaiseAndSetIfChanged(ref _maintenanceRecords, value);
        }

        private ObservableCollection<Bus> _buses = new();
        public ObservableCollection<Bus> Buses
        {
            get => _buses;
            set => this.RaiseAndSetIfChanged(ref _buses, value);
        }

        private Maintenance? _selectedRecord;
        public Maintenance? SelectedRecord
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

        [RelayCommand]
        private async Task LoadData()
        {
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                // Get raw response first
                var maintenanceResponse = await _httpClient.GetAsync($"{_baseUrl}/Maintenance");
                var busesResponse = await _httpClient.GetAsync($"{_baseUrl}/Buses");

                if (maintenanceResponse.IsSuccessStatusCode)
                {
                    var jsonString = await maintenanceResponse.Content.ReadAsStringAsync();
                    var maintenanceRecords = JsonSerializer.Deserialize<List<Maintenance>>(jsonString, _jsonOptions);

                    if (maintenanceRecords != null)
                        MaintenanceRecords = new ObservableCollection<Maintenance>(maintenanceRecords);
                }

                if (busesResponse.IsSuccessStatusCode)
                {
                    var jsonString = await busesResponse.Content.ReadAsStringAsync();
                    var buses = JsonSerializer.Deserialize<List<Bus>>(jsonString, _jsonOptions);

                    if (buses != null)
                        Buses = new ObservableCollection<Bus>(buses);
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error loading data: {ex.Message}";
                Log.Error(ex, "Error loading maintenance data");
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
                var serviceEngineerBox = new TextBox { Watermark = "Инженер" };
                var foundIssuesBox = new TextBox { Watermark = "Найденные проблемы" };
                var roadworthinessBox = new TextBox { Watermark = "Состояние (напр., 'Good', 'Needs Repair')" };

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
                    SelectedItem = Buses.FirstOrDefault(b => b.BusId == SelectedRecord.BusId)
                };

                var lastServiceDatePicker = new DatePicker
                {
                    SelectedDate = DateTimeOffset.FromUnixTimeMilliseconds((long)SelectedRecord.LastServiceDate)
                };

                var nextServiceDatePicker = new DatePicker
                {
                    SelectedDate = DateTimeOffset.FromUnixTimeMilliseconds((long)SelectedRecord.NextServiceDate)
                };

                var serviceEngineerBox = new TextBox
                {
                    Text = SelectedRecord.ServiceEngineer,
                    Watermark = "Инженер"
                };

                var foundIssuesBox = new TextBox
                {
                    Text = SelectedRecord.FoundIssues,
                    Watermark = "Найденные проблемы"
                };

                var roadworthinessBox = new TextBox
                {
                    Text = SelectedRecord.Roadworthiness,
                    Watermark = "Состояние"
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

                    var maintenance = new
                    {
                        BusId = selectedBus!.BusId,
                        LastServiceDate = lastServiceDatePicker.SelectedDate?.DateTime,
                        NextServiceDate = nextServiceDatePicker.SelectedDate?.DateTime,
                        ServiceEngineer = serviceEngineerBox.Text,
                        FoundIssues = foundIssuesBox.Text,
                        Roadworthiness = roadworthinessBox.Text
                    };

                    var content = new StringContent(
                        JsonSerializer.Serialize(maintenance),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PutAsync(
                        $"{_baseUrl}/Maintenance/{SelectedRecord.MaintenanceId}",
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
                    var response = await _httpClient.DeleteAsync($"{_baseUrl}/Maintenance/{SelectedRecord.MaintenanceId}");
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
            if (string.IsNullOrWhiteSpace(value))
            {
                LoadData().ConfigureAwait(false);
                return;
            }

            var filteredRecords = MaintenanceRecords.Where(m =>
                m.ServiceEngineer.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                m.FoundIssues.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                m.Roadworthiness.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                (Buses.FirstOrDefault(b => b.BusId == m.BusId)?.Model.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();

            MaintenanceRecords = new ObservableCollection<Maintenance>(filteredRecords);
        }
    }
} 