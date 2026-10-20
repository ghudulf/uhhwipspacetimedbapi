using System.Threading;
using Avalonia.Threading;
using BRU.Avtopark.Contracts;
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
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    // Wrapper class to hold Bus and associated counts
    public partial class BusDisplayModel : ObservableObject
    {
        [ObservableProperty]
        private BusRow _bus; // The original Bus object

        [ObservableProperty]
        private int? _routeCount;

        [ObservableProperty]
        private int? _maintenanceCount;

        public BusDisplayModel(BusRow bus)
        {
            _bus = bus;
        }

        partial void OnBusChanged(BusRow value)
        {
            foreach (var name in new[] { nameof(BusId), nameof(Model), nameof(BusType), nameof(Year), nameof(RegistrationNumber),
                nameof(IsActive), nameof(Capacity), nameof(Vin), nameof(LicensePlate), nameof(CurrentStatus) }) OnPropertyChanged(name);
        }

        // Expose Bus properties for easier binding in DataGrid if needed
        [JsonIgnore] // Prevent serialization issues if this model is ever serialized
        public uint BusId => Bus.BusId;
        [JsonIgnore]
        public string Model => Bus.Model;
        [JsonIgnore]
        public string? BusType => Bus.BusType;
        [JsonIgnore]
        public uint Year => Bus.Year;
        [JsonIgnore]
        public string? RegistrationNumber => Bus.RegistrationNumber;
        [JsonIgnore]
        public bool IsActive => Bus.IsActive;
        [JsonIgnore]
        public uint Capacity => Bus.Capacity;
        [JsonIgnore]
        public string? Vin => Bus.Vin;
        [JsonIgnore]
        public string? LicensePlate => Bus.LicensePlate;
        [JsonIgnore]
        public string? CurrentStatus => Bus.CurrentStatus;
        // ... add other frequently used Bus properties if direct binding is preferred ...
    }

    public partial class BusManagementViewModel : ReactiveObject
    {


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
            set { this.RaiseAndSetIfChanged(ref _selectedBus, value); NotifyAvailability(); }
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
            set { this.RaiseAndSetIfChanged(ref _isBusy, value); NotifyPaging(); }
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
            set { this.RaiseAndSetIfChanged(ref _hasError, value); NotifyAvailability(); }
        }

        private readonly Func<BusQuery, CancellationToken, Task<BusPageResult>> _load;
        private bool _active;
        private Task? _activation;
        private CancellationTokenSource? _cancellation;
        private long _generation;
        private string? _sessionToken;
        private int _page = 1;
        private int _totalCount;
        private bool _canCreate, _canEdit, _canDelete;
        public string PageInfo => $"Страница {_page} · Всего: {_totalCount}";
        public bool CanGoPrevious => !IsBusy && _page > 1;
        public bool CanGoNext => !IsBusy && _page * 100 < _totalCount;

        public BusManagementViewModel() : this((query, ct) => ApplicationQueryClient.ReadAsync<BusQuery, BusPageResult>("buses.list", query, ct)) { }
        public BusManagementViewModel(Func<BusQuery, CancellationToken, Task<BusPageResult>> load)
        {
            _load = load;

        }
        public Task ActivateAsync()
        {
            if (_active) return _activation ?? Task.CompletedTask;
            _active = true;
            ApiClientService.Instance.OnAuthTokenChanged += OnSessionChanged;
            if (_sessionToken != ApiClientService.Instance.AuthToken) ClearSessionData();
            _sessionToken = ApiClientService.Instance.AuthToken;
            return _activation = LoadData();
        }
        public void Deactivate()
        {
            _active = false;
            _generation++;
            _cancellation?.Cancel();
            IsBusy = false;
            ApiClientService.Instance.OnAuthTokenChanged -= OnSessionChanged;
        }
        private void OnSessionChanged(object? sender, string? token) => Dispatcher.UIThread.Post(() =>
        {
            if (!_active) return;
            _sessionToken = token;
            ClearSessionData();


            _activation = LoadData();
        });
        private void ClearSessionData()
        {
            SelectedBus = null;
            _allBuses.Clear(); Buses.Clear();
            _page = 1; _totalCount = 0;
            _canCreate = _canEdit = _canDelete = false;
            NotifyPaging();
        }
        private void NotifyPaging()
        {
            NotifyAvailability();
            this.RaisePropertyChanged(nameof(PageInfo));
            this.RaisePropertyChanged(nameof(CanGoPrevious));
            this.RaisePropertyChanged(nameof(CanGoNext));
        }
        [RelayCommand]
        private async Task PreviousPage() { if (!CanGoPrevious) return; _page--; await LoadData(); }
        [RelayCommand]
        private async Task NextPage() { if (!CanGoNext) return; _page++; await LoadData(); }

        [RelayCommand]
        private async Task LoadData()
        {
            if (!_active) return;
            _cancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            var generation = ++_generation;
            IsBusy = true; HasError = false;
            try
            {
                var result = await _load(new BusQuery(_page, 100, SearchText), cancellation.Token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_active || generation != _generation || cancellation.IsCancellationRequested) return;
                    var selected = SelectedBus?.BusId;
                    var existing = Buses.ToDictionary(b => b.BusId);
                    var updated = result.Items.Select(row =>
                    {
                        if (!existing.TryGetValue(row.BusId, out var item)) item = new BusDisplayModel(row);
                        item.Bus = row; item.RouteCount = row.ActiveRouteCount; item.MaintenanceCount = row.MaintenanceCount;
                        return item;
                    }).ToArray();
                    for (var i = Buses.Count - 1; i >= 0; i--) if (!updated.Contains(Buses[i])) Buses.RemoveAt(i);
                    for (var i = 0; i < updated.Length; i++)
                    {
                        var current = Buses.IndexOf(updated[i]);
                        if (current < 0) Buses.Insert(i, updated[i]); else if (current != i) Buses.Move(current, i);
                    }
                    _allBuses = updated.ToList();
                    SelectedBus = Buses.FirstOrDefault(b => b.BusId == selected);
                    _page = result.Page; _totalCount = result.TotalCount;
                    _canCreate = result.CanCreate; _canEdit = result.CanEdit; _canDelete = result.CanDelete;
                    NotifyPaging();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning("Bus query failed: {ErrorType}", ex.GetType().Name);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_active || generation != _generation) return;
                    if (ex is BRU.Avtopark.Client.ApplicationCommandException { Code: "forbidden" or "unauthenticated" or "session_expired" }) ClearSessionData();
                    HasError = true; ErrorMessage = "Не удалось обновить автобусы. Показаны ранее загруженные данные. Повторите загрузку.";
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => { if (generation == _generation) IsBusy = false; });
                if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
                cancellation.Dispose();
            }
        }

        [RelayCommand(CanExecute = nameof(CanCreate))]
        private async Task Add()
        {
            if (!CanCreate) return;
            var token = ApiClientService.Instance.AuthToken!;
            var origin = ApiClientService.Instance.CurrentBaseUrl!;
            var operationId = Guid.NewGuid().ToString("N");
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
                var modelBox = new TextBox { PlaceholderText = "Модель" };
                var regNumBox = new TextBox { PlaceholderText = "Регистрационный номер (необязательно)" };
                var capacityBox = new NumericUpDown { PlaceholderText = "Вместимость", Minimum = 0, Increment = 1 };
                var busTypeBox = new TextBox { PlaceholderText = "Тип автобуса (Городской, Троллейбус...)" };
                var yearBox = new NumericUpDown { PlaceholderText = "Год выпуска", Minimum = 1950, Maximum = DateTime.Now.Year + 1, Increment = 1 };
                var vinBox = new TextBox { PlaceholderText = "VIN (необязательно)" };
                var plateBox = new TextBox { PlaceholderText = "Гос. номер (необязательно)" };
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

                    var payload = new CreateBusCommand(modelBox.Text!, string.IsNullOrWhiteSpace(regNumBox.Text) ? null : regNumBox.Text,
                        new BusDetails((uint?)capacityBox.Value, busTypeBox.Text, (uint?)yearBox.Value, vinBox.Text, plateBox.Text,
                            statusBox.SelectedItem as string, isActiveCheck.IsChecked));
                    addButton.IsEnabled = false;
                    try
                    {
                        await ApplicationMutationClient.ExecuteAsync("buses.create", payload, operationId, origin, token, CancellationToken.None);
                        await LoadData();
                        dialog.Close();
                    }
                    catch (Exception ex)
                    {
                        await ShowMutationFailureAsync(ex);
                        addButton.IsEnabled = CanRetryMutation(ex);
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


        [RelayCommand(CanExecute = nameof(CanEdit))]
        private async Task Edit()
        {
            if (!CanEdit) return;
            var token = ApiClientService.Instance.AuthToken!;
            var origin = ApiClientService.Instance.CurrentBaseUrl!;
            var operationId = Guid.NewGuid().ToString("N");
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
                var modelBox = new TextBox { Text = busToEdit.Model, PlaceholderText = "Модель" };
                var regNumBox = new TextBox { Text = busToEdit.RegistrationNumber, PlaceholderText = "Регистрационный номер" };
                var capacityBox = new NumericUpDown { Value = busToEdit.Capacity, PlaceholderText = "Вместимость", Minimum = 0, Increment = 1 };
                var busTypeBox = new TextBox { Text = busToEdit.BusType, PlaceholderText = "Тип автобуса" };
                var yearBox = new NumericUpDown { Value = busToEdit.Year, PlaceholderText = "Год выпуска", Minimum = 1950, Maximum = DateTime.Now.Year + 1, Increment = 1 };
                var vinBox = new TextBox { Text = busToEdit.Vin, PlaceholderText = "VIN" };
                var plateBox = new TextBox { Text = busToEdit.LicensePlate, PlaceholderText = "Гос. номер" };
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

                    var payload = new UpdateBusCommand(busToEdit.BusId, busToEdit.Revision, modelBox.Text, regNumBox.Text,
                        new BusDetails((uint?)capacityBox.Value, busTypeBox.Text, (uint?)yearBox.Value, vinBox.Text, plateBox.Text,
                            statusComboBox.SelectedItem as string, isActiveCheck.IsChecked));
                    saveButton.IsEnabled = false;
                    try
                    {
                        await ApplicationMutationClient.ExecuteAsync("buses.update", payload, operationId, origin, token, CancellationToken.None);
                        await LoadData();
                        dialog.Close();
                    }
                    catch (Exception ex)
                    {
                        await ShowMutationFailureAsync(ex);
                        saveButton.IsEnabled = CanRetryMutation(ex);
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


        [RelayCommand(CanExecute = nameof(CanDelete))]
        private async Task Delete()
        {
            if (!CanDelete) return;
            var token = ApiClientService.Instance.AuthToken!;
            var origin = ApiClientService.Instance.CurrentBaseUrl!;
            var operationId = Guid.NewGuid().ToString("N");
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
                    await ApplicationMutationClient.ExecuteAsync("buses.delete", new DeleteBusCommand(busToDelete.BusId, busToDelete.Bus.Revision),
                        operationId, origin, token, CancellationToken.None);
                    await LoadData();
                }
                catch (Exception ex) { await ShowMutationFailureAsync(ex); }
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

        public bool CanMutate => _active && !IsBusy && !HasError && !string.IsNullOrEmpty(_sessionToken)
            && _sessionToken == ApiClientService.Instance.AuthToken;
        public bool CanCreate => CanMutate && _canCreate;
        public bool CanEdit => CanMutate && _canEdit && SelectedBus != null;
        public bool CanDelete => CanMutate && _canDelete && SelectedBus != null;
        private void NotifyAvailability()
        {
            this.RaisePropertyChanged(nameof(CanMutate));
            this.RaisePropertyChanged(nameof(CanCreate));
            this.RaisePropertyChanged(nameof(CanEdit));
            this.RaisePropertyChanged(nameof(CanDelete));
            AddCommand.NotifyCanExecuteChanged(); EditCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged();
        }
        private static bool CanRetryMutation(Exception ex) => ex is BRU.Avtopark.Client.ApplicationCommandException { Code: "validation_failed" or "forbidden" };
        private async Task ShowMutationFailureAsync(Exception ex)
        {
            Log.Warning("Fleet mutation failed: {ErrorType}, {Code}", ex.GetType().Name,
                ex is BRU.Avtopark.Client.ApplicationCommandException command ? command.Code : "transport_failure");
            ErrorMessage = ex is BRU.Avtopark.Client.ApplicationCommandException { Code: "conflict" }
                ? "Изменение отклонено: данные изменились или есть связанные записи. Черновик сохранён в окне. Обновите список и сравните данные перед повтором."
                : "Изменение не подтверждено. Черновик сохранён в окне. Проверьте результат через обновление списка перед повтором.";
            HasError = true;
            await MessageBoxManager.GetMessageBoxStandard("Изменение автобуса", ErrorMessage, ButtonEnum.Ok, Icon.Warning).ShowAsync();
        }

        private void OnSearchTextChanged(string value)
        {
            _page = 1;
            if (_active) _activation = LoadData();
        }
    }
}
