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

        private ObservableCollection<MonthlyIncome> _monthlyIncomes = new();
        public ObservableCollection<MonthlyIncome> MonthlyIncomes
        {
            get => _monthlyIncomes;
            set => this.RaiseAndSetIfChanged(ref _monthlyIncomes, value);
        }

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
            _baseUrl = "http://localhost:5000/api";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };

            // Initialize charts with default values
            InitializeCharts();

            ApiClientService.Instance.OnAuthTokenChanged += (_, token) =>
            {
                _httpClient = ApiClientService.Instance.CreateClient();
                LoadData().ConfigureAwait(false);
            };

            LoadData().ConfigureAwait(false);
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
            try
            {
                IsBusy = true;
                HasError = false;
                ErrorMessage = string.Empty;

                var salesResponse = await _httpClient.GetAsync(
                    $"{_baseUrl}/TicketSales/search?startDate={StartDate.Date:yyyy-MM-dd}&endDate={EndDate.Date:yyyy-MM-dd}");

                if (salesResponse.IsSuccessStatusCode)
                {
                    var jsonString = await salesResponse.Content.ReadAsStringAsync();
                    var sales = JsonSerializer.Deserialize<List<Sale>>(jsonString, _jsonOptions);

                    if (sales != null && sales.Any())
                    {
                        // Fetch Tickets and Routes data separately for calculations
                        var ticketsResponse = await _httpClient.GetAsync($"{_baseUrl}/Tickets");
                        var routesResponse = await _httpClient.GetAsync($"{_baseUrl}/Routes");
                        
                        List<Ticket> allTickets = new();
                        List<Route> allRoutes = new();

                        if (ticketsResponse.IsSuccessStatusCode)
                            allTickets = await ticketsResponse.Content.ReadFromJsonAsync<List<Ticket>>(_jsonOptions) ?? new();
                        if (routesResponse.IsSuccessStatusCode)
                            allRoutes = await routesResponse.Content.ReadFromJsonAsync<List<Route>>(_jsonOptions) ?? new();

                        // Calculate monthly incomes
                        var monthlyData = sales
                            .GroupBy(s => new { DateTimeOffset.FromUnixTimeMilliseconds((long)s.SaleDate).Year, DateTimeOffset.FromUnixTimeMilliseconds((long)s.SaleDate).Month })
                            .Select(g => 
                            {
                                var groupTickets = g.Select(s => allTickets.FirstOrDefault(t => t.TicketId == s.TicketId)).Where(t => t != null);
                                return new MonthlyIncome
                                {
                                    Year = g.Key.Year,
                                    Month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMMM"),
                                    TotalIncome = (decimal)groupTickets.Sum(t => t!.TicketPrice),
                                    TicketsSold = g.Count(),
                                    AverageTicketPrice = g.Count() > 0 ? (decimal)groupTickets.Average(t => t!.TicketPrice) : 0
                                };
                            })
                            .OrderByDescending(m => m.Year)
                            .ThenByDescending(m => DateTime.ParseExact(m.Month, "MMMM", System.Globalization.CultureInfo.InvariantCulture).Month)
                            .ToList();

                        // Calculate route incomes
                        var routeData = sales
                            .GroupBy(s => 
                            { 
                                var ticket = allTickets.FirstOrDefault(t => t.TicketId == s.TicketId);
                                var route = allRoutes.FirstOrDefault(r => r.RouteId == ticket?.RouteId);
                                return route != null ? $"{route.StartPoint} - {route.EndPoint}" : "Unknown Route";
                            })
                            .Select(g => 
                            { 
                                var groupTickets = g.Select(s => allTickets.FirstOrDefault(t => t.TicketId == s.TicketId)).Where(t => t != null);
                                return new RouteIncome
                                {
                                    RouteName = g.Key,
                                    TotalIncome = (decimal)groupTickets.Sum(t => t!.TicketPrice),
                                    TicketsSold = g.Count(),
                                    AverageTicketPrice = g.Count() > 0 ? (decimal)groupTickets.Average(t => t!.TicketPrice) : 0
                                };
                            })
                            .OrderByDescending(r => r.TotalIncome)
                            .ToList();

                        // Update collections
                        MonthlyIncomes = new ObservableCollection<MonthlyIncome>(monthlyData);
                        RouteIncomes = new ObservableCollection<RouteIncome>(routeData);

                        // Update totals
                        var soldTickets = sales.Select(s => allTickets.FirstOrDefault(t => t.TicketId == s.TicketId)).Where(t => t != null);
                        TotalIncome = (decimal)soldTickets.Sum(t => t!.TicketPrice);
                        TotalTicketsSold = sales.Count;
                        AverageTicketPrice = sales.Count > 0 ? (decimal)soldTickets.Average(t => t!.TicketPrice) : 0;

                        // Update charts
                        UpdateChartsWithData(monthlyData, routeData);
                    }
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error loading data: {ex.Message}";
                Log.Error(ex, "Error loading income report data");
                InitializeCharts(); // Reset charts to default state on error
            }
            finally
            {
                IsBusy = false;
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
                        DataLabelsFormatter = point => $"{routeNames[Math.Min(point.Index, routeNames.Length - 1)]}\n{point.PrimaryValue:C0}",
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