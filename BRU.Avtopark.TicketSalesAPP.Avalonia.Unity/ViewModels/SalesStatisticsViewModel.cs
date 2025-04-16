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

        private ObservableCollection<RouteStatistic> _routeStatistics = new();
        public ObservableCollection<RouteStatistic> RouteStatistics
        {
            get => _routeStatistics;
            set => this.RaiseAndSetIfChanged(ref _routeStatistics, value);
        }

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
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };

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

                        // Calculate route statistics using fetched data
                        var routeStats = sales
                            .GroupBy(s => 
                            {
                                var ticket = allTickets.FirstOrDefault(t => t.TicketId == s.TicketId);
                                var route = allRoutes.FirstOrDefault(r => r.RouteId == ticket?.RouteId);
                                return route != null ? $"{route.StartPoint} - {route.EndPoint}" : "Unknown Route";
                            })
                            .Select(g => new RouteStatistic
                            {
                                RouteName = g.Key,
                                TotalSales = g.Count(),
                                TotalRevenue = (decimal)g.Sum(s => allTickets.FirstOrDefault(t => t.TicketId == s.TicketId)?.TicketPrice ?? 0),
                                SalesPercentage = (double)g.Count() / sales.Count * 100
                            })
                            .OrderByDescending(r => r.TotalSales)
                            .ToList();

                        // Calculate daily statistics
                        var dailyStats = sales
                            .GroupBy(s => DateTimeOffset.FromUnixTimeMilliseconds((long)s.SaleDate).Date)
                            .Select(g => new DailyStatistic
                            {
                                Date = g.Key,
                                TotalSales = g.Count(),
                                TotalRevenue = (decimal)g.Sum(s => allTickets.FirstOrDefault(t => t.TicketId == s.TicketId)?.TicketPrice ?? 0)
                            })
                            .OrderBy(d => d.Date)
                            .ToList();

                        // Calculate growth rates
                        for (int i = 1; i < dailyStats.Count; i++)
                        {
                            var previousSales = dailyStats[i - 1].TotalSales;
                            var currentSales = dailyStats[i].TotalSales;
                            dailyStats[i].GrowthRate = previousSales > 0 
                                ? ((double)(currentSales - previousSales) / previousSales) * 100 
                                : 0;
                        }

                        // Update collections
                        RouteStatistics = new ObservableCollection<RouteStatistic>(routeStats);
                        DailyStatistics = new ObservableCollection<DailyStatistic>(dailyStats);

                        // Update summary statistics
                        TotalSales = sales.Count;
                        TotalRevenue = (decimal)sales.Sum(s => allTickets.FirstOrDefault(t => t.TicketId == s.TicketId)?.TicketPrice ?? 0);
                        AverageGrowthRate = dailyStats.Count > 1 ? dailyStats.Skip(1).Average(d => d.GrowthRate) : 0;

                        // Update charts
                        UpdateChartsWithData(dailyStats, routeStats);
                    }
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Error loading data: {ex.Message}";
                Log.Error(ex, "Error loading sales statistics data");
                InitializeCharts();
            }
            finally
            {
                IsBusy = false;
            }
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