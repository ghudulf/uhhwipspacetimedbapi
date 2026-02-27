using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using ReactiveUI;
using System.Linq;
using Serilog;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using System.Collections.Generic;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using SpacetimeDB.Types;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Globalization;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public class RouteStatistic
    {
        public string RouteName { get; set; } = string.Empty;
        public int TotalSales { get; set; }
        public decimal TotalRevenue { get; set; }
        public double SalesPercentage { get; set; }
    }

    public class DailyStatistic
    {
        public DateTime Date { get; set; }
        public int TotalSales { get; set; }
        public decimal TotalRevenue { get; set; }
        public double GrowthRate { get; set; }
    }

    public partial class SalesStatisticsViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private List<RouteStatistic> _allRouteStatistics = new();
        private ObservableCollection<RouteStatistic> _routeStatistics = new();
        public ObservableCollection<RouteStatistic> RouteStatistics
        {
            get => _routeStatistics;
            set => this.RaiseAndSetIfChanged(ref _routeStatistics, value);
        }

        private List<DailyStatistic> _allDailyStatistics = new();
        private ObservableCollection<DailyStatistic> _dailyStatistics = new();
        public ObservableCollection<DailyStatistic> DailyStatistics
        {
            get => _dailyStatistics;
            set => this.RaiseAndSetIfChanged(ref _dailyStatistics, value);
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

        private int _totalSales;
        public int TotalSales
        {
            get => _totalSales;
            set => this.RaiseAndSetIfChanged(ref _totalSales, value);
        }

        private decimal _totalRevenue;
        public decimal TotalRevenue
        {
            get => _totalRevenue;
            set => this.RaiseAndSetIfChanged(ref _totalRevenue, value);
        }

        private double _averageGrowthRate;
        public double AverageGrowthRate
        {
            get => _averageGrowthRate;
            set => this.RaiseAndSetIfChanged(ref _averageGrowthRate, value);
        }

        private ISeries[] _salesTrendChart;
        public ISeries[] SalesTrendChart
        {
            get => _salesTrendChart;
            set => this.RaiseAndSetIfChanged(ref _salesTrendChart, value);
        }

        private ISeries[] _routeDistributionChart;
        public ISeries[] RouteDistributionChart
        {
            get => _routeDistributionChart;
            set => this.RaiseAndSetIfChanged(ref _routeDistributionChart, value);
        }

        private Axis[] _xAxes;
        public Axis[] XAxes
        {
            get => _xAxes;
            set => this.RaiseAndSetIfChanged(ref _xAxes, value);
        }

        private Axis[] _yAxes;
        public Axis[] YAxes
        {
            get => _yAxes;
            set => this.RaiseAndSetIfChanged(ref _yAxes, value);
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

        public SalesStatisticsViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = "http://localhost:5000/api";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                // ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };

            InitializeCharts();

            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) => // Use sender and token
            {
                 Log.Information("Auth token changed in SalesStatisticsViewModel. Recreating HttpClient and reloading data.");
                 _httpClient.Dispose();
                _httpClient = ApiClientService.Instance.CreateClient();
                LoadData().ConfigureAwait(false);
            };

            // Only load data if token is already set, otherwise wait for OnAuthTokenChanged event
            if (!string.IsNullOrEmpty(ApiClientService.Instance.AuthToken))
            {
                Log.Information("Token already set in SalesStatisticsViewModel constructor, loading data");
                LoadData().ConfigureAwait(false);
            }
            else
            {
                Log.Warning("Token not set in SalesStatisticsViewModel constructor, waiting for OnAuthTokenChanged event");
            }
        }

        private void InitializeCharts()
        {
            SalesTrendChart = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = new double[] { 0 },
                    Name = "Продажи",
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                    Fill = new SolidColorPaint(SKColors.LightBlue.WithAlpha(100)),
                    GeometrySize = 8,
                    GeometryStroke = new SolidColorPaint(SKColors.DodgerBlue, 2)
                }
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Labels = new[] { "Нет данных" },
                    LabelsRotation = 45,
                    TextSize = 12
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Количество продаж",
                    TextSize = 12
                }
            };

            RouteDistributionChart = new ISeries[]
            {
                new PieSeries<double>
                {
                    Values = new double[] { 1 },
                    Name = "Маршруты",
                    DataLabelsFormatter = point => "Нет данных",
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                    DataLabelsSize = 14,
                    InnerRadius = 50,
                    MaxRadialColumnWidth = double.MaxValue
                }
            };
        }

        private async Task LoadData()
        {
            Log.Information("Starting LoadData for SalesStatisticsViewModel");
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;
                // Reset calculated values
                TotalSales = 0;
                TotalRevenue = 0M;
                AverageGrowthRate = 0.0;

                // --- Fetch Sales, Tickets, and Routes concurrently ---
                Log.Debug("Initiating API calls for Sales, Tickets, and Routes");
                // Fetch sales within the selected date range
                Task<HttpResponseMessage> salesTask = _httpClient.GetAsync(
                    $"{_baseUrl}/TicketSales/search?startDate={StartDate.Date:yyyy-MM-dd}&endDate={EndDate.Date:yyyy-MM-dd}");
                Task<HttpResponseMessage> ticketsTask = _httpClient.GetAsync($"{_baseUrl}/Tickets"); // Fetch all tickets for price/route info
                Task<HttpResponseMessage> routesTask = _httpClient.GetAsync($"{_baseUrl}/Routes"); // Fetch all routes for names
                // Optionally fetch top transports separately if API exists
                 // Task<HttpResponseMessage> topTransportsTask = _httpClient.GetAsync($"{_baseUrl}/statistics/top-transports?startDate={StartDate.Date:yyyy-MM-dd}&endDate={EndDate.Date:yyyy-MM-dd}");

                await Task.WhenAll(salesTask, ticketsTask, routesTask /*, topTransportsTask */);
                Log.Debug("All API calls completed for SalesStatisticsViewModel.");

                // --- 1. Process Routes Response (for names) ---
                var routesResponse = await routesTask;
                Log.Information("Processing Routes response. Status: {StatusCode}", routesResponse.StatusCode);
                var routesJsonString = await routesResponse.Content.ReadAsStringAsync();
                 Log.Verbose("Raw Routes response received: {RawResponse}", routesJsonString);
                Dictionary<uint, Route> routesDict = new();
                if (routesResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        JsonNode? routesNode = JsonNode.Parse(routesJsonString);
                        if (routesNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var routesValuesNode) && routesValuesNode is JsonArray routesArray)
                        {
                            Log.Information("Parsing {Count} route objects for lookup.", routesArray.Count);
                            foreach (var routeNode in routesArray)
                            {
                                if (routeNode is JsonObject routeObj)
                                {
                                    uint routeId = routeObj["routeId"]?.GetValue<uint>() ?? 0;
                                    if (routeId == 0) continue;
                                    string routeNumber = routeObj["routeNumber"]?.GetValue<string>() ?? string.Empty;
                                    string startPoint = routeObj["startPoint"]?.GetValue<string>() ?? string.Empty;
                                    string endPoint = routeObj["endPoint"]?.GetValue<string>() ?? string.Empty;
                                    var route = new Route { RouteId = routeId, RouteNumber = routeNumber, StartPoint = startPoint, EndPoint = endPoint };
                                    if (!routesDict.TryAdd(routeId, route))
                                    { Log.Warning("Duplicate RouteId {RouteId} found.", routeId); }
                                }
                            }
                             Log.Information("Parsed {Count} routes into dictionary.", routesDict.Count);
                        }
                         else { Log.Error("Routes JSON root was not an object with a '$values' array. Raw: {RawJson}", routesJsonString); }
                    }
                    catch (Exception ex) { Log.Error(ex, "Error parsing routes for statistics."); }
                }
                else { Log.Warning("Failed to load routes for statistics. Route names may be missing."); }

                // --- 2. Process Tickets Response (for price/route info) ---
                var ticketsResponse = await ticketsTask;
                Log.Information("Processing Tickets response. Status: {StatusCode}", ticketsResponse.StatusCode);
                var ticketsJsonString = await ticketsResponse.Content.ReadAsStringAsync();
                 Log.Verbose("Raw Tickets response received: {RawResponse}", ticketsJsonString);
                Dictionary<uint, Ticket> ticketsDict = new();
                if (ticketsResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        JsonNode? ticketsNode = JsonNode.Parse(ticketsJsonString);
                        if (ticketsNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var ticketsValuesNode) && ticketsValuesNode is JsonArray ticketsArray)
                        {
                            Log.Information("Parsing {Count} ticket objects for lookup.", ticketsArray.Count);
                            foreach (var ticketNode in ticketsArray)
                            {
                                if (ticketNode is JsonObject ticketObj)
                                {
                                    uint ticketId = ticketObj["ticketId"]?.GetValue<uint>() ?? 0;
                                    if (ticketId == 0) continue;
                                    double ticketPrice = ticketObj["ticketPrice"]?.GetValue<double>() ?? 0.0;
                                    uint routeId = ticketObj["routeId"]?.GetValue<uint>() ?? 0;
                                    var ticket = new Ticket { TicketId = ticketId, RouteId = routeId, TicketPrice = ticketPrice };
                                    if (!ticketsDict.TryAdd(ticketId, ticket))
                                    { Log.Warning("Duplicate TicketId {TicketId} found.", ticketId); }
                                }
                            }
                             Log.Information("Parsed {Count} tickets into dictionary.", ticketsDict.Count);
                        }
                         else { Log.Error("Tickets JSON root was not an object with a '$values' array. Raw: {RawJson}", ticketsJsonString); }
                    }
                    catch (Exception ex) { Log.Error(ex, "Error parsing tickets for statistics."); }
                }
                else { Log.Warning("Failed to load tickets for statistics. Price/Route info may be missing."); }

                // --- 3. Process Sales Response ---
                var salesResponse = await salesTask;
                Log.Information("Processing Sales response. Status: {StatusCode}", salesResponse.StatusCode);
                var salesJsonString = await salesResponse.Content.ReadAsStringAsync();
                 Log.Verbose("Raw Sales response received: {RawResponse}", salesJsonString);
                List<Sale> salesList = new(); // Store parsed sales
                if (salesResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        JsonNode? salesNode = JsonNode.Parse(salesJsonString);
                        if (salesNode is JsonObject rootObject && rootObject.TryGetPropertyValue("$values", out var salesValuesNode) && salesValuesNode is JsonArray salesArray)
                        {
                             Log.Information("Parsing {Count} sale objects.", salesArray.Count);
                            foreach (var saleNode in salesArray)
                    {
                                if (saleNode is JsonObject saleObj)
                                {
                                    uint saleId = saleObj["saleId"]?.GetValue<uint>() ?? 0;
                                     if (saleId == 0) continue;

                                    uint ticketId = saleObj["ticketId"]?.GetValue<uint>() ?? 0;
                                    // Parse SaleDate (DateTime to ulong)
                                    DateTime? parsedSaleDate = null;
                                    if (saleObj["saleDate"] != null) {
                                        try { parsedSaleDate = saleObj["saleDate"]?.GetValue<DateTime>(); }
                                        catch (Exception ex) { Log.Warning(ex, "Failed to parse SaleDate for SaleId {SaleId}", saleId); }
                                    }
                                    ulong saleDateUnix = 0;
                                    DateTime saleDate = DateTime.MinValue;
                                    if (parsedSaleDate.HasValue) {
                                        saleDate = parsedSaleDate.Value;
                                        try {
                                            TimeZoneInfo localZone = TimeZoneInfo.Local;
                                            DateTimeOffset dto = new DateTimeOffset(saleDate, localZone.GetUtcOffset(saleDate));
                                            saleDateUnix = (ulong)dto.ToUnixTimeMilliseconds();
                                        } catch (Exception ex) { Log.Error(ex, "Failed to convert SaleDate DateTimeOffset for SaleId {SaleId}", saleId); }
                                    }
                                     // Get SellerLogin/UserId if available
                                     string? sellerLogin = saleObj["sellerLogin"]?.GetValue<string>();

                                     salesList.Add(new Sale
                                     {
                                         SaleId = saleId,
                                         TicketId = ticketId,
                                         SaleDate = saleDateUnix // Store Unix timestamp
                                         // Note: We added SellerLogin to Sale struct for temp storage here
                                     });
                                }
                            }
                             Log.Information("Parsed {Count} sales.", salesList.Count);
                        }
                         else { Log.Error("Sales JSON root was not an object with a '$values' array. Raw: {RawJson}", salesJsonString); }
                    }
                    catch (JsonException jsonEx) { Log.Error(jsonEx, "Failed to parse Sales JSON: {RawJson}", salesJsonString); }
                    catch (Exception ex) { Log.Error(ex, "Unexpected error during manual sales parsing."); }
                }
                else
                {
                    var error = await salesResponse.Content.ReadAsStringAsync();
                    ErrorMessage = $"Ошибка загрузки данных о продажах: {error}";
                    HasError = true;
                    Log.Error("Failed to load sales: {Error}", error);
                    // Stop processing if sales data failed to load
                    InitializeCharts(); // Reset charts to empty state
                    return;
                }

                // --- 4. Calculate Statistics ---
                if (salesList.Any())
                {
                     Log.Information("Calculating statistics based on {SalesCount} sales...", salesList.Count);
                    // Calculate Daily Statistics
                    var dailyGrouped = salesList
                        .Select(s => new { Sale = s, Ticket = ticketsDict.GetValueOrDefault(s.TicketId) })
                        .Where(st => st.Ticket != null) // Only consider sales with valid tickets
                        .GroupBy(st => DateTimeOffset.FromUnixTimeMilliseconds((long)st.Sale.SaleDate).Date) // Group by Date part only
                            .Select(g => new DailyStatistic
                            {
                                Date = g.Key,
                                TotalSales = g.Count(),
                            TotalRevenue = (decimal)g.Sum(st => st.Ticket!.TicketPrice)
                            })
                            .OrderBy(d => d.Date)
                            .ToList();

                     Log.Information("Calculated {Count} daily statistics points.", dailyGrouped.Count);
                    _allDailyStatistics = CalculateGrowthRate(dailyGrouped);
                    DailyStatistics = new ObservableCollection<DailyStatistic>(_allDailyStatistics);

                    // Calculate Route Statistics
                    var totalSalesCount = salesList.Count;
                    var routeGrouped = salesList
                        .Select(s => new { Sale = s, Ticket = ticketsDict.GetValueOrDefault(s.TicketId) })
                        .Where(st => st.Ticket != null) // Ensure ticket exists
                        .Select(st => new { st.Sale, st.Ticket, Route = routesDict.GetValueOrDefault(st.Ticket!.RouteId) })
                        .Where(str => str.Route != null) // Ensure route exists
                        .GroupBy(str => str.Route!.RouteId)
                        .Select(g =>
                        {
                            var routeInfo = routesDict.GetValueOrDefault(g.Key);
                             var routeName = routeInfo != null ? $"{routeInfo.RouteNumber} ({routeInfo.StartPoint}-{routeInfo.EndPoint})" : $"Unknown Route (ID: {g.Key})";
                            var routeSales = g.Count();
                            var routeRevenue = (decimal)g.Sum(str => str.Ticket!.TicketPrice);
                            return new RouteStatistic
                            {
                                RouteName = routeName,
                                TotalSales = routeSales,
                                TotalRevenue = routeRevenue,
                                SalesPercentage = totalSalesCount > 0 ? (double)routeSales / totalSalesCount * 100 : 0
                            };
                        })
                        .OrderByDescending(r => r.TotalSales)
                        .ToList();

                     Log.Information("Calculated {Count} route statistics points.", routeGrouped.Count);
                    _allRouteStatistics = routeGrouped;
                    RouteStatistics = new ObservableCollection<RouteStatistic>(_allRouteStatistics);

                    // Calculate overall totals
                    TotalSales = totalSalesCount;
                    TotalRevenue = routeGrouped.Sum(r => r.TotalRevenue);
                    AverageGrowthRate = _allDailyStatistics.Any() ? _allDailyStatistics.Average(d => d.GrowthRate) : 0;
                     Log.Information("Overall Stats: TotalSales={TotalSales}, TotalRevenue={TotalRevenue}, AvgGrowth={AvgGrowthRate}", TotalSales, TotalRevenue, AverageGrowthRate);

                    // Update Charts
                    UpdateChartsWithData(_allDailyStatistics, _allRouteStatistics);
                     Log.Information("Charts updated with new data.");
                }
                else
                {
                    Log.Information("No sales data found for the selected period. Resetting charts.");
                    // Reset collections and charts if no sales
                    _allDailyStatistics = new List<DailyStatistic>();
                    DailyStatistics = new ObservableCollection<DailyStatistic>();
                    _allRouteStatistics = new List<RouteStatistic>();
                    RouteStatistics = new ObservableCollection<RouteStatistic>();
                    InitializeCharts(); // Reset charts to default/empty state
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Критическая ошибка загрузки и обработки статистики: {ex.Message}";
                Log.Fatal(ex, "Fatal error loading data in SalesStatisticsViewModel");
                // Clear data and reset charts on fatal error
                 _allDailyStatistics = new List<DailyStatistic>();
                 DailyStatistics = new ObservableCollection<DailyStatistic>();
                 _allRouteStatistics = new List<RouteStatistic>();
                 RouteStatistics = new ObservableCollection<RouteStatistic>();
                 TotalSales = 0;
                 TotalRevenue = 0M;
                 AverageGrowthRate = 0.0;
                InitializeCharts();
            }
            finally
            {
                IsBusy = false;
                Log.Information("LoadData finished for SalesStatisticsViewModel.");
            }
        }

         // Helper function to calculate growth rate (can be kept)
         private List<DailyStatistic> CalculateGrowthRate(List<DailyStatistic> dailyStats)
         {
             if (dailyStats == null || dailyStats.Count <= 1)
             {
                 // Set growth rate to 0 if no data or only one data point
                 if (dailyStats != null)
                 {
                     foreach (var stat in dailyStats) { stat.GrowthRate = 0; }
                 }
                 return dailyStats ?? new List<DailyStatistic>();
             }

             // Set first day growth rate to 0
             dailyStats[0].GrowthRate = 0;

             for (int i = 1; i < dailyStats.Count; i++)
             {
                 var previousDaySales = dailyStats[i - 1].TotalSales;
                 var currentDaySales = dailyStats[i].TotalSales;

                 if (previousDaySales > 0)
                 {
                     dailyStats[i].GrowthRate = ((double)(currentDaySales - previousDaySales) / previousDaySales) * 100;
                 }
                 else
                 {
                     // Handle division by zero if previous day sales were 0
                     // If current sales are > 0, could represent infinite growth, or just set to 100% or 0
                     dailyStats[i].GrowthRate = currentDaySales > 0 ? 100.0 : 0.0; // Or handle as appropriate
                 }
             }
             return dailyStats;
        }

        private void UpdateChartsWithData(List<DailyStatistic> dailyStats, List<RouteStatistic> routeStats)
        {
            try
            {
                var salesValues = dailyStats.Select(d => (double)d.TotalSales).ToArray();
                var dateLabels = dailyStats.Select(d => d.Date.ToString("dd.MM.yyyy")).ToArray();

                SalesTrendChart = new ISeries[]
                {
                    new LineSeries<double>
                    {
                        Values = salesValues,
                        Name = "Продажи",
                        Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                        Fill = new SolidColorPaint(SKColors.LightBlue.WithAlpha(100)),
                        GeometrySize = 8,
                        GeometryStroke = new SolidColorPaint(SKColors.DodgerBlue, 2)
                    }
                };

                XAxes = new Axis[]
                {
                    new Axis
                    {
                        Labels = dateLabels,
                        LabelsRotation = 45,
                        TextSize = 12
                    }
                };

                YAxes = new Axis[]
                {
                    new Axis
                    {
                        Name = "Количество продаж",
                        TextSize = 12
                    }
                };

                var topRoutes = routeStats.Take(5).ToList();
                var routeValues = topRoutes.Select(r => (double)r.TotalSales).ToArray();
                var routeNames = topRoutes.Select(r => r.RouteName).ToArray();

                RouteDistributionChart = new ISeries[]
                {
                    new PieSeries<double>
                    {
                        Values = routeValues,
                        Name = "Маршруты",
                        DataLabelsFormatter = point => $"{routeNames[point.Index]}\n{point.PrimaryValue:N0} продаж",
                        DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Outer,
                        DataLabelsSize = 10,
                        InnerRadius = 40,
                        MaxRadialColumnWidth = 15
                    }
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating charts");
                InitializeCharts();
            }
        }
    }
} 