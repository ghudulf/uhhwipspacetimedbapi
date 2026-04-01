using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using ReactiveUI;
using System.Linq;
using Serilog;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SpacetimeDB.Types;
using System.Text.Json.Nodes;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public partial class RouteScheduleDisplayModel : ObservableObject
    {
        [ObservableProperty]
        private RouteSchedule _schedule;

        public RouteScheduleDisplayModel(RouteSchedule schedule)
        {
            _schedule = schedule;
            Log.Debug("RouteScheduleDisplayModel created for schedule: ScheduleId={ScheduleId}, Route={Start}-{End}", 
                schedule.ScheduleId, schedule.StartPoint, schedule.EndPoint);
        }

        // Expose all RouteSchedule properties for binding
        public uint ScheduleId
        {
            get
            {
                Log.Verbose("RouteScheduleDisplayModel.ScheduleId accessed: {Value}", Schedule.ScheduleId);
                return Schedule.ScheduleId;
            }
        }

        public uint RouteId
        {
            get
            {
                Log.Verbose("RouteScheduleDisplayModel.RouteId accessed: {Value}", Schedule.RouteId);
                return Schedule.RouteId;
            }
        }

        public string? StartPoint
        {
            get
            {
                Log.Verbose("RouteScheduleDisplayModel.StartPoint accessed: {Value}", Schedule.StartPoint);
                return Schedule.StartPoint;
            }
        }

        public List<string>? RouteStops
        {
            get
            {
                var stops = Schedule.RouteStops;
                Log.Debug("RouteScheduleDisplayModel.RouteStops accessed for schedule {ScheduleId}: {StopsCount} stops, Stops={Stops}",
                    Schedule.ScheduleId,
                    stops?.Count ?? 0,
                    stops != null ? string.Join(", ", stops) : "null");
                return stops;
            }
        }
        public string? EndPoint => Schedule.EndPoint;
        public double Price => Schedule.Price;
        public uint AvailableSeats => Schedule.AvailableSeats;
        public bool IsActive => Schedule.IsActive;
        public ulong DepartureTime => Schedule.DepartureTime;
        public ulong ArrivalTime => Schedule.ArrivalTime;
        public uint? SeatedCapacity => Schedule.SeatedCapacity;
        public uint? StandingCapacity => Schedule.StandingCapacity;
        public List<string>? DaysOfWeek => Schedule.DaysOfWeek;
        public List<string>? BusTypes => Schedule.BusTypes;
        public ulong ValidFrom => Schedule.ValidFrom;
        public ulong? ValidUntil => Schedule.ValidUntil;
        public uint? StopDurationMinutes => Schedule.StopDurationMinutes;
        public bool IsRecurring => Schedule.IsRecurring;
        public List<string>? EstimatedStopTimes => Schedule.EstimatedStopTimes;
        public List<double>? StopDistances => Schedule.StopDistances;
        public string? Notes => Schedule.Notes;
        public ulong CreatedAt => Schedule.CreatedAt;
        public ulong? UpdatedAt => Schedule.UpdatedAt;
        public string? UpdatedBy => Schedule.UpdatedBy;
        public double? PeakHourLoad => Schedule.PeakHourLoad;
        public double? OffPeakHourLoad => Schedule.OffPeakHourLoad;
        public bool? IsSpecialEvent => Schedule.IsSpecialEvent;
        public string? SpecialEventName => Schedule.SpecialEventName;
        public bool? IsHoliday => Schedule.IsHoliday;
        public string? HolidayName => Schedule.HolidayName;
        public bool? IsWeekend => Schedule.IsWeekend;
        public uint? SeatConfigurationId => Schedule.SeatConfigurationId;
        public bool? RequiresSeatReservation => Schedule.RequiresSeatReservation;
        public string? RouteType => Schedule.RouteType;

        public string DepartureTimeDisplay
        {
            get
            {
                if (Schedule.DepartureTime == 0) return "Не указано";
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds((long)Schedule.DepartureTime);
                    var formatted = date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
                    Log.Verbose("RouteScheduleDisplayModel.DepartureTimeDisplay accessed: {Value}", formatted);
                    return formatted;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to format DepartureTime timestamp {Timestamp}", Schedule.DepartureTime);
                    return "Ошибка даты";
                }
            }
        }

        public string ArrivalTimeDisplay
        {
            get
            {
                if (Schedule.ArrivalTime == 0) return "Не указано";
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds((long)Schedule.ArrivalTime);
                    var formatted = date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
                    Log.Verbose("RouteScheduleDisplayModel.ArrivalTimeDisplay accessed: {Value}", formatted);
                    return formatted;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to format ArrivalTime timestamp {Timestamp}", Schedule.ArrivalTime);
                    return "Ошибка даты";
                }
            }
        }

        // Method to create a complete RouteSchedule from this DisplayModel
        public RouteSchedule ToRouteSchedule()
        {
            Log.Debug("RouteScheduleDisplayModel.ToRouteSchedule called for schedule: {ScheduleId}", Schedule.ScheduleId);
            return new RouteSchedule
            {
                ScheduleId = Schedule.ScheduleId,
                RouteId = Schedule.RouteId,
                StartPoint = Schedule.StartPoint,
                RouteStops = Schedule.RouteStops,
                EndPoint = Schedule.EndPoint,
                DepartureTime = Schedule.DepartureTime,
                ArrivalTime = Schedule.ArrivalTime,
                Price = Schedule.Price,
                AvailableSeats = Schedule.AvailableSeats,
                SeatedCapacity = Schedule.SeatedCapacity,
                StandingCapacity = Schedule.StandingCapacity,
                DaysOfWeek = Schedule.DaysOfWeek,
                BusTypes = Schedule.BusTypes,
                IsActive = Schedule.IsActive,
                ValidFrom = Schedule.ValidFrom,
                ValidUntil = Schedule.ValidUntil,
                StopDurationMinutes = Schedule.StopDurationMinutes,
                IsRecurring = Schedule.IsRecurring,
                EstimatedStopTimes = Schedule.EstimatedStopTimes,
                StopDistances = Schedule.StopDistances,
                Notes = Schedule.Notes,
                CreatedAt = Schedule.CreatedAt,
                UpdatedAt = Schedule.UpdatedAt,
                UpdatedBy = Schedule.UpdatedBy,
                PeakHourLoad = Schedule.PeakHourLoad,
                OffPeakHourLoad = Schedule.OffPeakHourLoad,
                IsSpecialEvent = Schedule.IsSpecialEvent,
                SpecialEventName = Schedule.SpecialEventName,
                IsHoliday = Schedule.IsHoliday,
                HolidayName = Schedule.HolidayName,
                IsWeekend = Schedule.IsWeekend,
                SeatConfigurationId = Schedule.SeatConfigurationId,
                RequiresSeatReservation = Schedule.RequiresSeatReservation,
                RouteType = Schedule.RouteType
            };
        }
    }

    public partial class RouteSchedulesManagementViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private string _baseUrl => ApiClientService.Instance.CurrentBaseUrl?.TrimEnd('/') ?? "http://localhost:5000/api";
        private readonly JsonSerializerOptions _jsonOptions;

        private ObservableCollection<Route> _routes = new();
        public ObservableCollection<Route> Routes
        {
            get => _routes;
            set => this.RaiseAndSetIfChanged(ref _routes, value);
        }

        private Route? _selectedRoute;
        public Route? SelectedRoute
        {
            get => _selectedRoute;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedRoute, value);
                if (value != null)
                {
                    CurrentPage = 1; // Reset to first page when route changes
                    LoadSchedules().ConfigureAwait(false);
                }
            }
        }

        private DateTimeOffset _selectedDate = DateTimeOffset.Now;
        public DateTimeOffset SelectedDate
        {
            get => _selectedDate;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedDate, value);
                if (SelectedRoute != null)
                {
                    CurrentPage = 1; // Reset to first page when date changes
                    LoadSchedules().ConfigureAwait(false);
                }
            }
        }

        private ObservableCollection<RouteScheduleDisplayModel> _schedules = new();
        public ObservableCollection<RouteScheduleDisplayModel> Schedules
        {
            get => _schedules;
            set => this.RaiseAndSetIfChanged(ref _schedules, value);
        }

        private RouteScheduleDisplayModel? _selectedSchedule;
        public RouteScheduleDisplayModel? SelectedSchedule
        {
            get => _selectedSchedule;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedSchedule, value);
                if (value != null)
                {
                    Log.Information("Schedule selected: ScheduleId={ScheduleId}, Route={Start}-{End}, RouteStops count={StopsCount}",
                        value.ScheduleId, value.StartPoint, value.EndPoint, value.RouteStops?.Count ?? 0);
                    if (value.RouteStops != null && value.RouteStops.Count > 0)
                    {
                        Log.Debug("RouteStops for selected schedule: {Stops}", string.Join(", ", value.RouteStops));
                    }
                    else
                    {
                        Log.Warning("Selected schedule {ScheduleId} has null or empty RouteStops", value.ScheduleId);
                    }
                }
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

        public RouteSchedulesManagementViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };

            // Subscribe to auth token changes
            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) =>
            {
                var oldClient = _httpClient;
                _httpClient = ApiClientService.Instance.CreateClient();
                oldClient.Dispose();
                LoadData().ConfigureAwait(false);
            };

            // Only load data if token is already set, otherwise wait for OnAuthTokenChanged event
            if (!string.IsNullOrEmpty(ApiClientService.Instance.AuthToken))
            {
                Log.Information("Token already set in RouteSchedulesManagementViewModel constructor, loading data");
                LoadData().ConfigureAwait(false);
            }
            else
            {
                Log.Warning("Token not set in RouteSchedulesManagementViewModel constructor, waiting for OnAuthTokenChanged event");
            }
        }

        private async Task LoadData()
        {
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                Log.Information("Starting LoadData for RouteSchedulesManagementViewModel");

                // First load routes
                var routesResponse = await _httpClient.GetAsync($"{_baseUrl}/Routes");
                if (!routesResponse.IsSuccessStatusCode)
                {
                    var error = await routesResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to load routes: {error}");
                }

                var routesJsonString = await routesResponse.Content.ReadAsStringAsync();
                Log.Debug("Raw Routes response received: {RawResponse}", routesJsonString);

                try
                {
                    var routesArray = JsonReferenceHelper.ParseArrayWithReferences(routesJsonString, "Route");
                    if (routesArray != null)
                    {
                        var routes = new List<Route>();
                        foreach (var routeNode in routesArray)
                        {
                            if (routeNode is JsonObject routeObj)
                            {
                                var route = routeObj.ParseRoute();
                                if (route != null)
                                {
                                    routes.Add(route);
                                }
                            }
                        }
                        Routes = new ObservableCollection<Route>(routes);
                        Log.Information("Successfully loaded {Count} routes", routes.Count);
                    }
                    else
                    {
                        Log.Error("Routes JSON could not be parsed as array");
                        throw new Exception("Invalid routes data format");
                    }
                }
                catch (JsonException jsonEx)
                {
                    Log.Error(jsonEx, "Failed to parse Routes JSON: {RawJson}", routesJsonString);
                    throw new Exception("Failed to parse route data", jsonEx);
                }

                // Then load schedules if a route is selected
                if (SelectedRoute != null)
                {
                    await LoadSchedules();
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error loading data: {ex.Message}";
                Log.Error(ex, "Error loading data");
                Routes.Clear();
                Schedules.Clear();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private int _currentPage = 1;
        private const int PageSize = 50;
        private int _totalSchedules = 0;

        public int CurrentPage
        {
            get => _currentPage;
            set => this.RaiseAndSetIfChanged(ref _currentPage, value);
        }

        public int TotalPages => (_totalSchedules + PageSize - 1) / PageSize;

        public string PageInfo => $"Page {CurrentPage} of {TotalPages} ({_totalSchedules} total)";

        private async Task LoadSchedules()
        {
            if (SelectedRoute == null)
            {
                Schedules.Clear();
                _totalSchedules = 0;
                this.RaisePropertyChanged(nameof(PageInfo));
                this.RaisePropertyChanged(nameof(TotalPages));
                return;
            }

            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                Log.Information("Loading schedules for route {RouteId} on date {Date}, page {Page}", 
                    SelectedRoute.RouteId, SelectedDate.Date.ToString("yyyy-MM-dd"), CurrentPage);

                // Use pagination to avoid loading all schedules at once
                var response = await _httpClient.GetAsync(
                    $"{_baseUrl}/RouteSchedules/search?routeId={SelectedRoute.RouteId}&date={SelectedDate.Date:yyyy-MM-dd}&page={CurrentPage}&pageSize={PageSize}");

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to load schedules: {error}");
                }

                // Read pagination metadata from headers
                if (response.Headers.TryGetValues("X-Pagination", out var paginationValues))
                {
                    var paginationJson = paginationValues.FirstOrDefault();
                    if (!string.IsNullOrEmpty(paginationJson))
                    {
                        try
                        {
                            var paginationData = JsonSerializer.Deserialize<JsonElement>(paginationJson);
                            _totalSchedules = paginationData.GetProperty("TotalCount").GetInt32();
                            Log.Debug("Pagination metadata: TotalCount={TotalCount}, CurrentPage={CurrentPage}, TotalPages={TotalPages}",
                                _totalSchedules, CurrentPage, TotalPages);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to parse pagination metadata");
                        }
                    }
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                Log.Debug("Raw Schedules response received (page {Page}): {RawResponse}", CurrentPage, jsonString.Substring(0, Math.Min(500, jsonString.Length)));

                try
                {
                    var schedulesArray = JsonReferenceHelper.ParseArrayWithReferences(jsonString, "RouteSchedule");
                    if (schedulesArray != null)
                    {
                        var schedules = new List<RouteSchedule>();
                        foreach (var scheduleNode in schedulesArray)
                        {
                            if (scheduleNode is JsonObject scheduleObj)
                            {
                                var schedule = scheduleObj.ParseRouteSchedule();
                                if (schedule != null)
                                {
                                    schedules.Add(schedule);
                                    Log.Debug("Successfully parsed schedule {ScheduleId} for route {RouteId}", 
                                        schedule.ScheduleId, schedule.RouteId);
                                }
                            }
                        }

                        Schedules = new ObservableCollection<RouteScheduleDisplayModel>(
                            schedules.OrderBy(s => s.DepartureTime).Select(s => new RouteScheduleDisplayModel(s)));
                        
                        this.RaisePropertyChanged(nameof(PageInfo));
                        this.RaisePropertyChanged(nameof(TotalPages));
                        
                        Log.Information("Successfully loaded {Count} schedules for route {RouteId} (page {Page} of {TotalPages})", 
                            schedules.Count, SelectedRoute.RouteId, CurrentPage, TotalPages);
                    }
                    else
                    {
                        Log.Error("Schedules JSON could not be parsed as array");
                        throw new Exception("Invalid schedules data format");
                    }
                }
                catch (JsonException jsonEx)
                {
                    Log.Error(jsonEx, "Failed to parse Schedules JSON: {RawJson}", jsonString.Substring(0, Math.Min(1000, jsonString.Length)));
                    throw new Exception("Failed to parse schedule data", jsonEx);
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error loading schedules: {ex.Message}";
                Log.Error(ex, "Error loading schedules");
                Schedules.Clear();
                _totalSchedules = 0;
                this.RaisePropertyChanged(nameof(PageInfo));
                this.RaisePropertyChanged(nameof(TotalPages));
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task NextPage()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                await LoadSchedules();
            }
        }

        [RelayCommand]
        private async Task PreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                await LoadSchedules();
            }
        }

        [RelayCommand]
        private async Task FirstPage()
        {
            if (CurrentPage != 1)
            {
                CurrentPage = 1;
                await LoadSchedules();
            }
        }

        [RelayCommand]
        private async Task LastPage()
        {
            if (CurrentPage != TotalPages && TotalPages > 0)
            {
                CurrentPage = TotalPages;
                await LoadSchedules();
            }
        }

        /// <summary>
        /// Failsafe method to get route stops configuration when server data is incomplete or missing.
        /// Returns predefined route configurations based on start/end points.
        /// Matches the actual routes defined in the database InitializeRoutes reducer.
        /// </summary>
        private (string start, string end, string[] stops)? GetRouteConfiguration(Route route)
        {
            // Define route configurations as failsafe when server data doesn't provide proper stops
            // These match the actual routes in server/INITreducers.cs InitializeRoutes method
            var routeConfigs = new Dictionary<(string start, string end), string[]>
            {
                // Additional city and suburban routes (from DB init)
                {("Вейнянка", "Фатина"), new[] {"Вейнянка", "Площадь Орджоникидзе", "Областная больница", "Фатина"}},
                {("Мал. Боровка", "Солтановка"), new[] {"Мал. Боровка", "Машековка", "Центр", "Солтановка"}},
                {("Вокзал", "Спутник"), new[] {"Вокзал", "Площадь Ленина", "Универмаг", "Спутник"}},
                {("Мясокомбинат", "Заводская"), new[] {"Мясокомбинат", "Димитрова", "Юбилейный", "Заводская"}},
                {("Броды", "Казимировка"), new[] {"Броды", "Центр", "Площадь Славы", "Казимировка"}},
                {("Гребеневский рынок", "Холмы"), new[] {"Гребеневский рынок", "Площадь Орджоникидзе", "Мир", "Холмы"}},
                {("Автовокзал", "Полыковичи"), new[] {"Автовокзал", "Площадь Ленина", "Димитрова", "Полыковичи"}},
                {("Центр", "Сидоровичи"), new[] {"Центр", "Площадь Славы", "Заднепровье", "Сидоровичи"}},
                {("Площадь Славы", "Буйничи"), new[] {"Площадь Славы", "Областная больница", "Зоосад", "Буйничи"}},
                {("Заднепровье", "Химволокно"), new[] {"Заднепровье", "Центр", "Юбилейный", "Химволокно"}},
                {("Вокзал", "Соломинка"), new[] {"Вокзал", "Центр", "Димитрова", "Соломинка"}},
                {("Площадь Ленина", "Чаусы"), new[] {"Площадь Ленина", "Центр", "Заднепровье", "Чаусы"}},
                {("Могилев-2", "Дашковка"), new[] {"Могилев-2", "Центр", "Юбилейный", "Дашковка"}},
                {("Кожзавод", "Сухари"), new[] {"Кожзавод", "Центр", "Площадь Славы", "Сухари"}},
                {("Гребеневский рынок", "Любуж"), new[] {"Гребеневский рынок", "Центр", "Заднепровье", "Любуж"}},
                
                // Main city routes (from DB init - internal city routes)
                {("ул. Фатина", "завод «Могилевтрансмаш»"), new[] {"ул. Фатина", "Центр", "пл. Ленина", "завод «Могилевтрансмаш»"}},
                {("Могилевская больница №1", "пос. Броды-1"), new[] {"Могилевская больница №1", "Центр", "пос. Броды-1"}},
                {("Автовокзал", "Могилевоблнефтепродут"), new[] {"Автовокзал", "Центр", "Могилевоблнефтепродут"}},
                {("м-н Казимировка", "ул. Златоустовского"), new[] {"м-н Казимировка", "Центр", "ул. Златоустовского"}},
                {("Областная больница", "пл. Ленина"), new[] {"Областная больница", "Центр", "пл. Ленина"}},
                {("Любужский лесопарк", "Железнодорожный вокзал"), new[] {"Любужский лесопарк", "Центр", "Железнодорожный вокзал"}},
                {("ул. Симонова", "завод «Могилевтрансмаш»"), new[] {"ул. Симонова", "Центр", "завод «Могилевтрансмаш»"}},
                {("Средняя школа №13", "железнодорожный вокзал"), new[] {"Средняя школа №13", "Центр", "железнодорожный вокзал"}},
                {("Поселок Пашково", "ул. 30 лет Победы"), new[] {"Поселок Пашково", "Центр", "ул. 30 лет Победы"}},
                {("пл. Космонавтов", "завод «Могилевлифтмаш»"), new[] {"пл. Космонавтов", "Центр", "завод «Могилевлифтмаш»"}},
                {("бул. Днепровский", "поселок Гребенево"), new[] {"бул. Днепровский", "Центр", "поселок Гребенево"}},
                {("м-н Юбилейный", "Областная больница"), new[] {"м-н Юбилейный", "Центр", "Областная больница"}},
                {("ж/д вокзал", "Больница мед. реабилитации"), new[] {"ж/д вокзал", "Центр", "Больница мед. реабилитации"}},
                {("пл. Орджоникидзе", "Любужский лесопарк"), new[] {"пл. Орджоникидзе", "Центр", "Любужский лесопарк"}},
                {("Могилевоблнефтепродукт", "Могилевская больница №1"), new[] {"Могилевоблнефтепродукт", "Центр", "Могилевская больница №1"}},
                {("пер. Ватутина (Переезд)", "м-н Казимировка"), new[] {"пер. Ватутина (Переезд)", "Центр", "м-н Казимировка"}},
                {("Городская ветеринарная станция", "м-н Казимировка"), new[] {"Городская ветеринарная станция", "Центр", "м-н Казимировка"}},
                {("ОАО «Техноприбор»", "ОАО «Техноприбор»"), new[] {"ОАО «Техноприбор»", "Центр", "пл. Ленина", "Центр", "ОАО «Техноприбор»"}},
                {("ул. 30 лет Победы", "ул. Фатина"), new[] {"ул. 30 лет Победы", "Центр", "ул. Фатина"}},
                {("Поселок Броды-1", "Могилевская больница №1"), new[] {"Поселок Броды-1", "Центр", "Могилевская больница №1"}},
                {("ул. Пионерская", "завод «Вентзаготовок»"), new[] {"ул. Пионерская", "Центр", "завод «Вентзаготовок»"}},
                {("Автовокзал", "деревня Новоселки"), new[] {"Автовокзал", "Центр", "деревня Новоселки"}},
                {("м-н Казимировка", "поселок Ямницкий"), new[] {"м-н Казимировка", "Центр", "поселок Ямницкий"}},
                {("Средняя школа №13", "м-н Соломинка"), new[] {"Средняя школа №13", "Центр", "м-н Соломинка"}},
                {("Поселок Любуж", "железнодорожный вокзал"), new[] {"Поселок Любуж", "Центр", "железнодорожный вокзал"}},
                {("ул. Маневича (поселок Малая Боровка)", "железнодорожный вокзал"), new[] {"ул. Маневича (поселок Малая Боровка)", "Центр", "железнодорожный вокзал"}},
                {("Областная больница", "Облтипо"), new[] {"Областная больница", "Центр", "Облтипо"}},
                {("м-н Юбилейный", "железнодорожный вокзал"), new[] {"м-н Юбилейный", "Центр", "железнодорожный вокзал"}},
                {("м-н Казимировка", "железнодорожный вокзал"), new[] {"м-н Казимировка", "Центр", "железнодорожный вокзал"}},
                {("м-н Заря", "железнодорожный вокзал"), new[] {"м-н Заря", "Центр", "железнодорожный вокзал"}},
                
                // Trolleybus routes (from DB init)
                {("м-н Казимировка", "Автовокзал"), new[] {"м-н Казимировка", "Центр", "Автовокзал"}},
                {("м-н Казимировка", "Железнодорожный вокзал"), new[] {"м-н Казимировка", "Центр", "Железнодорожный вокзал"}},
                {("м-н Казимировка", "ул. Фатина"), new[] {"м-н Казимировка", "Центр", "ул. Фатина"}},
                {("м-н Казимировка", "ул. Крупской"), new[] {"м-н Казимировка", "Центр", "ул. Крупской"}},
                {("м-н Казимировка", "ул. Габровская"), new[] {"м-н Казимировка", "Центр", "ул. Габровская"}},
                {("м-н Казимировка", "м-н Юбилейный"), new[] {"м-н Казимировка", "Центр", "м-н Юбилейный"}},
                
                // Intercity routes (from DB init)
                {("Могилев", "Минск"), new[] {"Могилев", "Буйничи", "Минск"}},
                {("Могилев", "Гомель"), new[] {"Могилев", "Быхов", "Гомель"}},
                {("Могилев", "Москва"), new[] {"Могилев", "Минск", "Смоленск", "Москва"}},
                {("Могилев", "Смоленск"), new[] {"Могилев", "Мстиславль", "Смоленск"}},
                {("Могилев", "Бобруйск"), new[] {"Могилев", "Осиповичи", "Бобруйск"}},
                {("Могилев", "Горки"), new[] {"Могилев", "Горки"}},
                {("Могилев", "Витебск"), new[] {"Могилев", "Орша", "Витебск"}},
                {("Могилев", "Славгород"), new[] {"Могилев", "Славгород"}},
                {("Могилев", "Мстиславль"), new[] {"Могилев", "Мстиславль"}},
                
                // Suburban routes (from DB init)
                {("Могилев", "Шклов"), new[] {"Могилев", "Шклов"}},
                {("Могилев", "Быхов"), new[] {"Могилев", "Быхов"}},
                {("Могилев", "Круглое"), new[] {"Могилев", "Круглое"}}
            };

            var key = routeConfigs.Keys.FirstOrDefault(k => k.start == route.StartPoint && k.end == route.EndPoint);
            if (key != default)
            {
                Log.Debug("Using predefined route configuration for {Start} - {End}", key.start, key.end);
                return (key.start, key.end, routeConfigs[key]);
            }

            Log.Debug("No predefined route configuration found for {Start} - {End}", route.StartPoint, route.EndPoint);
            return null;
        }

        /// <summary>
        /// Gets route stops with failsafe fallback. Tries multiple sources in order:
        /// 1. Predefined route configuration (most reliable)
        /// 2. Parse from route StartPoint/EndPoint (fallback)
        /// 3. Default stops (last resort)
        /// </summary>
        private string[] GetRouteStopsWithFailsafe(Route route)
        {
            // Try predefined configuration first (most reliable)
            var routeConfig = GetRouteConfiguration(route);
            if (routeConfig != null)
            {
                Log.Information("Using predefined stops for route {RouteId}: {Stops}", 
                    route.RouteId, string.Join(", ", routeConfig.Value.stops));
                return routeConfig.Value.stops;
            }

            // Fallback: try to parse from StartPoint/EndPoint
            var parsedStops = route.StartPoint?.Split(',')
                .Concat(route.EndPoint?.Split(',') ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct()
                .ToArray();

            if (parsedStops != null && parsedStops.Length >= 2)
            {
                Log.Warning("Using parsed stops for route {RouteId} (no predefined config): {Stops}", 
                    route.RouteId, string.Join(", ", parsedStops));
                return parsedStops;
            }

            // Last resort: use start and end points only
            var defaultStops = new[] { route.StartPoint ?? "Начало", route.EndPoint ?? "Конец" };
            Log.Error("Using default stops for route {RouteId} (parsing failed): {Stops}", 
                route.RouteId, string.Join(", ", defaultStops));
            return defaultStops;
        }

        [RelayCommand]
        private async Task Add()
        {
            if (SelectedRoute == null)
            {
                ErrorMessage = "Please select a route first";
                HasError = true;
                return;
            }

            try
            {
                var dialog = new Window
                {
                    Title = "Добавить расписание",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    Margin = new Thickness(15)
                };

                // Left column controls
                var departureTimePicker = new TimePicker { Margin = new Thickness(0, 0, 10, 5) };
                var arrivalTimePicker = new TimePicker { Margin = new Thickness(0, 0, 10, 5) };
                var priceBox = new NumericUpDown { Minimum = 0, Value = 0.75M, Increment = 0.25M, Margin = new Thickness(0, 0, 10, 5) };
                var seatsBox = new NumericUpDown { Minimum = 0, Value = 42, Increment = 1, Margin = new Thickness(0, 0, 10, 5) };
                var isActiveCheckBox = new CheckBox { Content = "Активно", IsChecked = true, Margin = new Thickness(0, 0, 10, 5) };
                var isRecurringCheckBox = new CheckBox { Content = "Повторяющееся", IsChecked = true, Margin = new Thickness(0, 0, 10, 5) };

                // Right column - Route Stops
                var routeStopsPanel = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*"),
                    Margin = new Thickness(10, 0, 0, 0)
                };

                var routeStopsLabel = new TextBlock
                {
                    Text = "Остановки маршрута",
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                // Get route stops with failsafe fallback
                var routeStops = GetRouteStopsWithFailsafe(SelectedRoute);

                var routeStopsListBox = new ListBox
                {
                    ItemsSource = routeStops,
                    SelectionMode = SelectionMode.Multiple,
                    SelectedItems = new ObservableCollection<string>(routeStops), // Pre-select all stops
                    Margin = new Thickness(0, 0, 0, 10)
                };

                Grid.SetRow(routeStopsLabel, 0);
                Grid.SetRow(routeStopsListBox, 1);
                routeStopsPanel.Children.Add(routeStopsLabel);
                routeStopsPanel.Children.Add(routeStopsListBox);

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                var addButton = new Button { Content = "Добавить", Margin = new Thickness(0, 0, 10, 0) };
                var cancelButton = new Button { Content = "Отмена" };

                // Add left column controls
                var departureLabel = new TextBlock { Text = "Время отправления:", Margin = new Thickness(0, 0, 0, 5) };
                Grid.SetRow(departureLabel, 0);
                Grid.SetColumn(departureLabel, 0);
                grid.Children.Add(departureLabel);

                Grid.SetRow(departureTimePicker, 1);
                Grid.SetColumn(departureTimePicker, 0);
                grid.Children.Add(departureTimePicker);

                var arrivalLabel = new TextBlock { Text = "Время прибытия:", Margin = new Thickness(0, 10, 0, 5) };
                Grid.SetRow(arrivalLabel, 2);
                Grid.SetColumn(arrivalLabel, 0);
                grid.Children.Add(arrivalLabel);

                Grid.SetRow(arrivalTimePicker, 3);
                Grid.SetColumn(arrivalTimePicker, 0);
                grid.Children.Add(arrivalTimePicker);

                var priceLabel = new TextBlock { Text = "Цена:", Margin = new Thickness(0, 10, 0, 5) };
                Grid.SetRow(priceLabel, 4);
                Grid.SetColumn(priceLabel, 0);
                grid.Children.Add(priceLabel);

                Grid.SetRow(priceBox, 5);
                Grid.SetColumn(priceBox, 0);
                grid.Children.Add(priceBox);

                var seatsLabel = new TextBlock { Text = "Количество мест:", Margin = new Thickness(0, 10, 0, 5) };
                Grid.SetRow(seatsLabel, 6);
                Grid.SetColumn(seatsLabel, 0);
                grid.Children.Add(seatsLabel);

                Grid.SetRow(seatsBox, 7);
                Grid.SetColumn(seatsBox, 0);
                grid.Children.Add(seatsBox);
                Grid.SetRow(grid.Children[grid.Children.Count - 1], 7);

                Grid.SetRow(isActiveCheckBox, 8);
                Grid.SetColumn(isActiveCheckBox, 0);
                grid.Children.Add(isActiveCheckBox);

                Grid.SetRow(isRecurringCheckBox, 9);
                Grid.SetColumn(isRecurringCheckBox, 0);
                grid.Children.Add(isRecurringCheckBox);

                // Add right column - Route Stops
                Grid.SetColumn(routeStopsPanel, 1);
                Grid.SetRowSpan(routeStopsPanel, 8);
                grid.Children.Add(routeStopsPanel);

                // Add buttons
                Grid.SetRow(buttonsPanel, 9);
                Grid.SetColumnSpan(buttonsPanel, 2);
                buttonsPanel.Children.Add(addButton);
                buttonsPanel.Children.Add(cancelButton);
                grid.Children.Add(buttonsPanel);

                dialog.Content = grid;

                cancelButton.Click += (s, e) => dialog.Close();
                addButton.Click += async (s, e) =>
                {
                    var selectedStops = routeStopsListBox.SelectedItems?.Cast<string>().ToArray() ?? Array.Empty<string>();
                    if (selectedStops.Length < 2)
                    {
                        ErrorMessage = "Выберите как минимум две остановки";
                        HasError = true;
                        return;
                    }

                    // Calculate estimated times and distances based on API structure if needed
                    var estimatedTimes = new string[selectedStops.Length];
                    var stopDistances = new double[selectedStops.Length];
                    var departureTime = SelectedDate.Date.Add(departureTimePicker.SelectedTime ?? TimeSpan.Zero);
                    var arrivalTime = SelectedDate.Date.Add(arrivalTimePicker.SelectedTime ?? TimeSpan.Zero);
                    var totalMinutes = (arrivalTime - departureTime).TotalMinutes;
                    var minutesPerStop = totalMinutes / (selectedStops.Length - 1);

                    for (int i = 0; i < selectedStops.Length; i++)
                    {
                        estimatedTimes[i] = departureTime.AddMinutes(i * minutesPerStop).ToString("HH:mm");
                        stopDistances[i] = Math.Round(i * (6.0 / (selectedStops.Length - 1)), 2); // Assuming average route length of 6km
                    }

                    var departureDateTime = SelectedDate.Date.Add(departureTimePicker.SelectedTime ?? TimeSpan.Zero);
                    var arrivalDateTime = SelectedDate.Date.Add(arrivalTimePicker.SelectedTime ?? TimeSpan.Zero);
                    var departureOffset = new DateTimeOffset(departureDateTime);
                    var arrivalOffset = new DateTimeOffset(arrivalDateTime);

                    var schedule = new
                    {
                        RouteId = SelectedRoute.RouteId,
                        StartPoint = selectedStops.First(),
                        EndPoint = selectedStops.Last(),
                        RouteStops = selectedStops,
                        DepartureTime = (ulong)departureOffset.ToUnixTimeMilliseconds(),
                        ArrivalTime = (ulong)arrivalOffset.ToUnixTimeMilliseconds(),
                        Price = (double)priceBox.Value,
                        AvailableSeats = (uint)seatsBox.Value,
                        IsActive = isActiveCheckBox.IsChecked ?? true,
                        IsRecurring = isRecurringCheckBox.IsChecked ?? true,
                        DaysOfWeek = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" },
                        BusTypes = new[] { "МАЗ-103", "МАЗ-107" },
                        StopDurationMinutes = (uint)5,
                        EstimatedStopTimes = estimatedTimes.Length > 0 ? estimatedTimes : new[] { "08:00", "12:00" },
                        StopDistances = stopDistances.Length > 0 ? stopDistances : new[] { 0.0, 6.0 },
                        Notes = $"Маршрут {selectedStops.First()} - {selectedStops.Last()}",
                    };

                    try
                    {
                        var json = JsonSerializer.Serialize(schedule, _jsonOptions);
                        Log.Information("Sending route schedule data to API: {Json}", json);
                        
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PostAsync($"{_baseUrl}/RouteSchedules", content);
                        if (response.IsSuccessStatusCode)
                        {
                            Log.Information("Successfully created route schedule with {Stops} stops from {Start} to {End}", 
                                selectedStops.Length, schedule.StartPoint, schedule.EndPoint);
                            await LoadSchedules();
                            dialog.Close();
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            ErrorMessage = $"Failed to add schedule: {error}";
                            HasError = true;
                            Log.Error("Failed to add schedule: {Error}", error);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Error adding schedule: {ex.Message}";
                        HasError = true;
                        Log.Error(ex, "Error adding schedule");
                    }
                };

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error showing add dialog: {ex.Message}";
                HasError = true;
                Log.Error(ex, "Error showing add dialog");
            }
        }

        [RelayCommand]
        private async Task Edit(RouteScheduleDisplayModel? scheduleDisplay)
        {
            if (scheduleDisplay == null || SelectedRoute == null) return;
            
            var schedule = scheduleDisplay.Schedule;

            try
            {
                var dialog = new Window
                {
                    Title = "Редактировать расписание",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    Margin = new Thickness(15)
                };

                // Left column controls
                var departureTimePicker = new TimePicker
                {
                    SelectedTime = DateTimeOffset.FromUnixTimeMilliseconds((long)schedule.DepartureTime).TimeOfDay,
                    Margin = new Thickness(0, 0, 10, 5)
                };
                var arrivalTimePicker = new TimePicker
                {
                    SelectedTime = DateTimeOffset.FromUnixTimeMilliseconds((long)schedule.ArrivalTime).TimeOfDay,
                    Margin = new Thickness(0, 0, 10, 5)
                };
                var priceBox = new NumericUpDown
                {
                    Minimum = 0,
                    Value = (decimal)schedule.Price,
                    Increment = 0.25M,
                    Margin = new Thickness(0, 0, 10, 5)
                };
                var seatsBox = new NumericUpDown
                {
                    Minimum = 0,
                    Value = schedule.AvailableSeats,
                    Increment = 1,
                    Margin = new Thickness(0, 0, 10, 5)
                };
                var isActiveCheckBox = new CheckBox
                {
                    Content = "Активно",
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 10, 5)
                };
                var isRecurringCheckBox = new CheckBox
                {
                    Content = "Повторяющееся",
                    IsChecked = schedule.IsRecurring,
                    Margin = new Thickness(0, 0, 10, 5)
                };

                // Right column - Route Stops
                var routeStopsPanel = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*"),
                    Margin = new Thickness(10, 0, 0, 0)
                };

                var routeStopsLabel = new TextBlock
                {
                    Text = "Остановки маршрута",
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                // Get route stops with failsafe fallback
                var allPossibleStops = GetRouteStopsWithFailsafe(SelectedRoute);

                var routeStopsListBox = new ListBox
                {
                    ItemsSource = allPossibleStops,
                    SelectionMode = SelectionMode.Multiple,
                    SelectedItems = new ObservableCollection<string>(schedule.RouteStops ?? new List<string>()),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                Grid.SetRow(routeStopsLabel, 0);
                Grid.SetRow(routeStopsListBox, 1);
                routeStopsPanel.Children.Add(routeStopsLabel);
                routeStopsPanel.Children.Add(routeStopsListBox);

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                var updateButton = new Button { Content = "Обновить", Margin = new Thickness(0, 0, 10, 0) };
                var cancelButton = new Button { Content = "Отмена" };

                // Add left column controls
                var departureLabel = new TextBlock { Text = "Время отправления:", Margin = new Thickness(0, 0, 0, 5) };
                Grid.SetRow(departureLabel, 0);
                Grid.SetColumn(departureLabel, 0);
                grid.Children.Add(departureLabel);

                Grid.SetRow(departureTimePicker, 1);
                Grid.SetColumn(departureTimePicker, 0);
                grid.Children.Add(departureTimePicker);

                var arrivalLabel = new TextBlock { Text = "Время прибытия:", Margin = new Thickness(0, 10, 0, 5) };
                Grid.SetRow(arrivalLabel, 2);
                Grid.SetColumn(arrivalLabel, 0);
                grid.Children.Add(arrivalLabel);

                Grid.SetRow(arrivalTimePicker, 3);
                Grid.SetColumn(arrivalTimePicker, 0);
                grid.Children.Add(arrivalTimePicker);

                var priceLabel = new TextBlock { Text = "Цена:", Margin = new Thickness(0, 10, 0, 5) };
                Grid.SetRow(priceLabel, 4);
                Grid.SetColumn(priceLabel, 0);
                grid.Children.Add(priceLabel);

                Grid.SetRow(priceBox, 5);
                Grid.SetColumn(priceBox, 0);
                grid.Children.Add(priceBox);

                var seatsLabel = new TextBlock { Text = "Количество мест:", Margin = new Thickness(0, 10, 0, 5) };
                Grid.SetRow(seatsLabel, 6);
                Grid.SetColumn(seatsLabel, 0);
                grid.Children.Add(seatsLabel);

                Grid.SetRow(seatsBox, 7);
                Grid.SetColumn(seatsBox, 0);
                grid.Children.Add(seatsBox);
                Grid.SetRow(grid.Children[grid.Children.Count - 1], 7);

                Grid.SetRow(isActiveCheckBox, 8);
                Grid.SetColumn(isActiveCheckBox, 0);
                grid.Children.Add(isActiveCheckBox);

                Grid.SetRow(isRecurringCheckBox, 9);
                Grid.SetColumn(isRecurringCheckBox, 0);
                grid.Children.Add(isRecurringCheckBox);

                // Add right column - Route Stops
                Grid.SetColumn(routeStopsPanel, 1);
                Grid.SetRowSpan(routeStopsPanel, 8);
                grid.Children.Add(routeStopsPanel);

                // Add buttons
                Grid.SetRow(buttonsPanel, 9);
                Grid.SetColumnSpan(buttonsPanel, 2);
                buttonsPanel.Children.Add(updateButton);
                buttonsPanel.Children.Add(cancelButton);
                grid.Children.Add(buttonsPanel);

                dialog.Content = grid;

                cancelButton.Click += (s, e) => dialog.Close();
                updateButton.Click += async (s, e) =>
                {
                    var selectedStops = routeStopsListBox.SelectedItems?.Cast<string>().ToArray() ?? Array.Empty<string>();
                    if (selectedStops.Length < 2)
                    {
                        ErrorMessage = "Выберите как минимум две остановки";
                        HasError = true;
                        return;
                    }

                    var departureDateTime = SelectedDate.Date.Add(departureTimePicker.SelectedTime ?? TimeSpan.Zero);
                    var arrivalDateTime = SelectedDate.Date.Add(arrivalTimePicker.SelectedTime ?? TimeSpan.Zero);
                    var departureOffset = new DateTimeOffset(departureDateTime);
                    var arrivalOffset = new DateTimeOffset(arrivalDateTime);
                    
                    // Convert DisplayModel to RouteSchedule and update fields
                    var updatedSchedule = scheduleDisplay.ToRouteSchedule();
                    updatedSchedule.RouteId = SelectedRoute.RouteId;
                    updatedSchedule.StartPoint = selectedStops.First();
                    updatedSchedule.EndPoint = selectedStops.Last();
                    updatedSchedule.RouteStops = selectedStops.ToList();
                    updatedSchedule.DepartureTime = (ulong)departureOffset.ToUnixTimeMilliseconds();
                    updatedSchedule.ArrivalTime = (ulong)arrivalOffset.ToUnixTimeMilliseconds();
                    updatedSchedule.Price = (double)priceBox.Value;
                    updatedSchedule.AvailableSeats = (uint)seatsBox.Value;
                    updatedSchedule.IsActive = isActiveCheckBox.IsChecked ?? true;
                    updatedSchedule.IsRecurring = isRecurringCheckBox.IsChecked ?? true;

                    try
                    {
                        var json = JsonSerializer.Serialize(updatedSchedule, _jsonOptions);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        Log.Information("Updating schedule {ScheduleId} with data: {Json}", schedule.ScheduleId, json);
                        var response = await _httpClient.PutAsync(
                            $"{_baseUrl}/RouteSchedules/{schedule.ScheduleId}", content);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Log.Information("Successfully updated schedule {ScheduleId}", schedule.ScheduleId);
                            await LoadSchedules();
                            dialog.Close();
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            ErrorMessage = $"Failed to update schedule: {error}";
                            HasError = true;
                            Log.Error("Failed to update schedule {ScheduleId}. Status: {StatusCode}, Error: {Error}", 
                                schedule.ScheduleId, response.StatusCode, error);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Error updating schedule: {ex.Message}";
                        HasError = true;
                        Log.Error(ex, "Error updating schedule {ScheduleId}", schedule.ScheduleId);
                    }
                };

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error showing edit dialog: {ex.Message}";
                HasError = true;
                Log.Error(ex, "Error showing edit dialog");
            }
        }

        [RelayCommand]
        private async Task Delete(RouteScheduleDisplayModel? scheduleDisplay)
        {
            if (scheduleDisplay == null) return;
            
            var schedule = scheduleDisplay.Schedule;

            try
            {
                var dialog = new Window
                {
                    Title = "Подтверждение удаления",
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto"),
                    Margin = new Thickness(20)
                };

                var messageText = new TextBlock
                {
                    Text = $"Вы уверены, что хотите удалить расписание маршрута {schedule.StartPoint} - {schedule.EndPoint}?",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 20)
                };

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var deleteButton = new Button 
                { 
                    Content = "Удалить",
                    Background = new SolidColorBrush(Colors.Red),
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                var cancelButton = new Button { Content = "Отмена" };

                Grid.SetRow(messageText, 0);
                Grid.SetRow(buttonsPanel, 1);

                buttonsPanel.Children.Add(deleteButton);
                buttonsPanel.Children.Add(cancelButton);

                grid.Children.Add(messageText);
                grid.Children.Add(buttonsPanel);

                dialog.Content = grid;

                cancelButton.Click += (s, e) => dialog.Close();
                deleteButton.Click += async (s, e) =>
                {
                    try
                    {
                        var response = await _httpClient.DeleteAsync($"{_baseUrl}/RouteSchedules/{schedule.ScheduleId}");
                        if (response.IsSuccessStatusCode)
                        {
                            await LoadSchedules();
                            dialog.Close();
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            ErrorMessage = $"Failed to delete schedule: {error}";
                            HasError = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Error deleting schedule: {ex.Message}";
                        HasError = true;
                        Log.Error(ex, "Error deleting schedule");
                    }
                };

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error showing delete dialog: {ex.Message}";
                HasError = true;
                Log.Error(ex, "Error showing delete dialog");
            }
        }

        [RelayCommand]
        private Task Refresh()
        {
            return LoadSchedules();
        }
    }
} 