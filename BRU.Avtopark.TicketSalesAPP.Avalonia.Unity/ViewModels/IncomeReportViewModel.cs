using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
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
using System.Globalization;
using System.Text.Json.Nodes;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels
{
    public class MonthlyIncome
    {
        public string Month { get; set; } = string.Empty;
        public int Year { get; set; }
        public decimal TotalIncome { get; set; }
        public int TicketsSold { get; set; }
        public decimal AverageTicketPrice { get; set; }
    }

    public class RouteIncome
    {
        public string RouteName { get; set; } = string.Empty;
        public decimal TotalIncome { get; set; }
        public int TicketsSold { get; set; }
        public decimal AverageTicketPrice { get; set; }
    }

    public partial class IncomeReportViewModel : ReactiveObject
    {
        private HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private List<MonthlyIncome> _allMonthlyIncomes = new();
        private ObservableCollection<MonthlyIncome> _monthlyIncomes = new();
        public ObservableCollection<MonthlyIncome> MonthlyIncomes
        {
            get => _monthlyIncomes;
            set => this.RaiseAndSetIfChanged(ref _monthlyIncomes, value);
        }

        private List<RouteIncome> _allRouteIncomes = new();
        private ObservableCollection<RouteIncome> _routeIncomes = new();
        public ObservableCollection<RouteIncome> RouteIncomes
        {
            get => _routeIncomes;
            set => this.RaiseAndSetIfChanged(ref _routeIncomes, value);
        }

        private DateTimeOffset _startDate = DateTimeOffset.Now.AddMonths(-12);
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

        private int _totalTicketsSold;
        public int TotalTicketsSold
        {
            get => _totalTicketsSold;
            set => this.RaiseAndSetIfChanged(ref _totalTicketsSold, value);
        }

        private decimal _averageTicketPrice;
        public decimal AverageTicketPrice
        {
            get => _averageTicketPrice;
            set => this.RaiseAndSetIfChanged(ref _averageTicketPrice, value);
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

        private ISeries[] _monthlySalesChart;
        public ISeries[] MonthlySalesChart
        {
            get => _monthlySalesChart;
            set => this.RaiseAndSetIfChanged(ref _monthlySalesChart, value);
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

        public IncomeReportViewModel()
        {
            _httpClient = ApiClientService.Instance.CreateClient();
            _baseUrl = ApiClientService.Instance.CurrentBaseUrl?.TrimEnd('/') ?? "http://localhost:5000/api";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                // ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };

            // Initialize charts with default values
            InitializeCharts();

            ApiClientService.Instance.OnAuthTokenChanged += (sender, token) =>
            {
                Log.Information("Auth token changed in IncomeReportViewModel. Recreating HttpClient and reloading data.");
                _httpClient.Dispose();
                _httpClient = ApiClientService.Instance.CreateClient();
                LoadData().ConfigureAwait(false);
            };

            // Only load data if token is already set, otherwise wait for OnAuthTokenChanged event
            if (!string.IsNullOrEmpty(ApiClientService.Instance.AuthToken))
            {
                Log.Information("Token already set in IncomeReportViewModel constructor, loading data");
                LoadData().ConfigureAwait(false);
            }
            else
            {
                Log.Warning("Token not set in IncomeReportViewModel constructor, waiting for OnAuthTokenChanged event");
            }
        }

        private void InitializeCharts()
        {
            // Initialize line chart with default values
            MonthlySalesChart = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = new double[] { 0 },
                    Name = "Доход",
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
                    Name = "Доход (₽)",
                    Labeler = value => value.ToString("C0"),
                    TextSize = 12
                }
            };

            // Initialize pie chart with default values
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
            Log.Information("Starting LoadData for IncomeReportViewModel");
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;
                // Reset calculated values
                TotalIncome = 0M;
                TotalTicketsSold = 0;
                AverageTicketPrice = 0M;

                // --- Fetch Sales, Tickets, and Routes concurrently ---
                Log.Debug("Initiating API calls for Sales, Tickets, and Routes for income report");
                Task<HttpResponseMessage> salesTask = _httpClient.GetAsync(
                    $"{_baseUrl}/TicketSales/search?startDate={StartDate.Date:yyyy-MM-dd}&endDate={EndDate.Date:yyyy-MM-dd}");
                Task<HttpResponseMessage> ticketsTask = _httpClient.GetAsync($"{_baseUrl}/Tickets"); // Fetch all tickets for price/route info
                Task<HttpResponseMessage> routesTask = _httpClient.GetAsync($"{_baseUrl}/Routes"); // Fetch all routes for names
                // No separate /statistics/income endpoint assumed, calculating from Sales data.

                await Task.WhenAll(salesTask, ticketsTask, routesTask);
                Log.Debug("All API calls completed for IncomeReportViewModel.");

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
                    catch (Exception ex) { Log.Error(ex, "Error parsing routes for income report."); }
                }
                else { Log.Warning("Failed to load routes for income report. Route names may be missing."); }

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
                    catch (Exception ex) { Log.Error(ex, "Error parsing tickets for income report."); }
                }
                else { Log.Warning("Failed to load tickets for income report. Price/Route info may be missing."); }

                // --- 3. Process Sales Response ---
                var salesResponse = await salesTask;
                Log.Information("Processing Sales response. Status: {StatusCode}", salesResponse.StatusCode);
                var salesJsonString = await salesResponse.Content.ReadAsStringAsync();
                Log.Verbose("Raw Sales response received: {RawResponse}", salesJsonString);
                List<Sale> salesList = new(); // Store parsed sales with necessary info
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
                                    DateTime? parsedSaleDate = null;
                                    if (saleObj["saleDate"] != null) {
                                        try { parsedSaleDate = saleObj["saleDate"]?.GetValue<DateTime>(); }
                                        catch (Exception ex) { Log.Warning(ex, "Failed to parse SaleDate for SaleId {SaleId}", saleId); }
                                    }
                                    ulong saleDateUnix = 0;
                                    DateTime saleDateTime = DateTime.MinValue;
                                    if (parsedSaleDate.HasValue) {
                                         saleDateTime = parsedSaleDate.Value;
                                        try {
                                            TimeZoneInfo localZone = TimeZoneInfo.Local;
                                            DateTimeOffset dto = new DateTimeOffset(saleDateTime, localZone.GetUtcOffset(saleDateTime));
                                            saleDateUnix = (ulong)dto.ToUnixTimeMilliseconds();
                                        } catch (Exception ex) { Log.Error(ex, "Failed to convert SaleDate DateTimeOffset for SaleId {SaleId}", saleId); }
                                    }

                                     salesList.Add(new Sale
                                     {
                                         SaleId = saleId,
                                         TicketId = ticketId,
                                         SaleDate = saleDateUnix // Store Unix timestamp
                                         // Store DateTime separately if needed for grouping
                                         // SaleDateTime = saleDateTime
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
                    Log.Error("Failed to load sales for income report: {Error}", error);
                    InitializeCharts(); // Reset charts
                    return; // Stop processing
                }

                // --- 4. Calculate Income Statistics ---
                 Log.Information("Calculating income statistics based on {SalesCount} sales...", salesList.Count);
                if (salesList.Any())
                {
                    // Combine sales with ticket/route info
                    var salesDetails = salesList
                        .Select(s => new {
                            Sale = s,
                            Ticket = ticketsDict.GetValueOrDefault(s.TicketId),
                             SaleDateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)s.SaleDate).DateTime // Convert Unix ms back to DateTime
                        })
                        .Where(st => st.Ticket != null) // Ensure ticket exists
                        .Select(st => new {
                            st.Sale,
                            st.Ticket,
                             st.SaleDateTime,
                            Route = routesDict.GetValueOrDefault(st.Ticket!.RouteId)
                        })
                        .ToList(); // Materialize the list with combined details

                     Log.Debug("Created sales details list with {Count} entries.", salesDetails.Count);

                    // Calculate Monthly Income
                    var monthlyData = salesDetails
                        .GroupBy(sd => new { sd.SaleDateTime.Year, sd.SaleDateTime.Month })
                        .Select(g => {
                            int ticketsSold = g.Count();
                            decimal totalIncome = (decimal)g.Sum(sd => sd.Ticket!.TicketPrice);
                                return new MonthlyIncome
                                {
                                    Year = g.Key.Year,
                                Month = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(g.Key.Month),
                                TicketsSold = ticketsSold,
                                TotalIncome = totalIncome,
                                AverageTicketPrice = ticketsSold > 0 ? totalIncome / ticketsSold : 0
                                };
                            })
                        .OrderBy(m => m.Year).ThenBy(m => CultureInfo.CurrentCulture.DateTimeFormat.MonthNames.ToList().IndexOf(m.Month))
                            .ToList();

                     Log.Information("Calculated {Count} monthly income points.", monthlyData.Count);
                    _allMonthlyIncomes = monthlyData;
                    MonthlyIncomes = new ObservableCollection<MonthlyIncome>(_allMonthlyIncomes);

                    // Calculate Route Income
                    var routeData = salesDetails
                        .Where(sd => sd.Route != null) // Ensure route info exists
                        .GroupBy(sd => sd.Route!.RouteId)
                        .Select(g => {
                            var routeInfo = routesDict.GetValueOrDefault(g.Key);
                             var routeName = routeInfo != null ? $"{routeInfo.RouteNumber} ({routeInfo.StartPoint}-{routeInfo.EndPoint})" : $"Unknown Route (ID: {g.Key})";
                            int ticketsSold = g.Count();
                            decimal totalIncome = (decimal)g.Sum(sd => sd.Ticket!.TicketPrice);
                                return new RouteIncome
                                {
                                RouteName = routeName,
                                TicketsSold = ticketsSold,
                                TotalIncome = totalIncome,
                                AverageTicketPrice = ticketsSold > 0 ? totalIncome / ticketsSold : 0
                                };
                            })
                            .OrderByDescending(r => r.TotalIncome)
                            .ToList();

                     Log.Information("Calculated {Count} route income points.", routeData.Count);
                    _allRouteIncomes = routeData;
                    RouteIncomes = new ObservableCollection<RouteIncome>(_allRouteIncomes);

                    // Calculate Overall Totals
                    TotalTicketsSold = salesDetails.Count;
                    TotalIncome = _allMonthlyIncomes.Sum(m => m.TotalIncome);
                    AverageTicketPrice = TotalTicketsSold > 0 ? TotalIncome / TotalTicketsSold : 0;
                     Log.Information("Overall Income Stats: TotalSold={TotalTicketsSold}, TotalIncome={TotalIncome}, AvgPrice={AverageTicketPrice}", TotalTicketsSold, TotalIncome, AverageTicketPrice);

                    // Update Charts
                    UpdateChartsWithData(_allMonthlyIncomes, _allRouteIncomes);
                     Log.Information("Charts updated with new income data.");
                }
                else
                {
                     Log.Information("No sales data found for the selected period. Resetting income report.");
                    // Reset if no sales
                    _allMonthlyIncomes = new List<MonthlyIncome>();
                    MonthlyIncomes = new ObservableCollection<MonthlyIncome>();
                    _allRouteIncomes = new List<RouteIncome>();
                    RouteIncomes = new ObservableCollection<RouteIncome>();
                    InitializeCharts(); // Reset charts to default state
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Критическая ошибка загрузки и обработки отчета о доходах: {ex.Message}";
                Log.Fatal(ex, "Fatal error loading data in IncomeReportViewModel");
                // Clear data and reset charts
                 _allMonthlyIncomes = new List<MonthlyIncome>();
                 MonthlyIncomes = new ObservableCollection<MonthlyIncome>();
                 _allRouteIncomes = new List<RouteIncome>();
                 RouteIncomes = new ObservableCollection<RouteIncome>();
                 TotalIncome = 0M;
                 TotalTicketsSold = 0;
                 AverageTicketPrice = 0M;
                InitializeCharts();
            }
            finally
            {
                IsBusy = false;
                 Log.Information("LoadData finished for IncomeReportViewModel.");
            }
        }

        private void UpdateChartsWithData(List<MonthlyIncome> monthlyData, List<RouteIncome> routeData)
        {
            try
            {
                var monthlySalesValues = monthlyData
                    .OrderBy(m => m.Year)
                    .ThenBy(m => DateTime.ParseExact(m.Month, "MMMM", null).Month)
                    .Select(m => (double)m.TotalIncome)
                    .ToArray();

                var monthlyLabels = monthlyData
                    .OrderBy(m => m.Year)
                    .ThenBy(m => DateTime.ParseExact(m.Month, "MMMM", null).Month)
                    .Select(m => $"{m.Month} {m.Year}")
                    .ToArray();

                MonthlySalesChart = new ISeries[]
                {
                    new LineSeries<double>
                    {
                        Values = monthlySalesValues,
                        Name = "Доход",
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
                        Labels = monthlyLabels,
                        LabelsRotation = 45,
                        TextSize = 12
                    }
                };

                YAxes = new Axis[]
                {
                    new Axis
                    {
                        Name = "Доход (₽)",
                        Labeler = value => value.ToString("C0"),
                        TextSize = 12
                    }
                };

                var topRoutes = routeData.Take(5).ToList();
                var routeValues = topRoutes.Select(r => (double)r.TotalIncome).ToArray();
                var routeNames = topRoutes.Select(r => r.RouteName).ToArray();

                RouteDistributionChart = new ISeries[]
                {
                    new PieSeries<double>
                    {
                        Values = routeValues,
                        Name = "Маршруты",
                        DataLabelsFormatter = point => routeNames.Length > 0
                            ? $"{routeNames[Math.Clamp(point.Index, 0, routeNames.Length - 1)]}\n{point.Coordinate.PrimaryValue:C0}"
                            : $"{point.Coordinate.PrimaryValue:C0}",
                        DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Outer,
                        DataLabelsSize = 10,
                        InnerRadius = 40,
                        MaxRadialColumnWidth = 15,
                        DataLabelsRotation = 0,

                    }
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating charts");
                InitializeCharts(); // Reset charts to default state on error
            }
        }
    }
} 