using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization;
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
using System.Threading;
using Avalonia.Threading;
using BRU.Avtopark.Contracts;
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
        private readonly Func<DateOnly, DateOnly, CancellationToken, Task<SalesReportSnapshot>> _loadReport;
        private CancellationTokenSource? _loadCancellation;
        private bool _active;
        private string? _lastSessionToken;
        private long _queryGeneration;

        public async Task ActivateAsync()
        {
            if (_active) return;
            if (_lastSessionToken != ApiClientService.Instance.AuthToken) ClearReport();
            _lastSessionToken = ApiClientService.Instance.AuthToken;
            _active = true;
            ApiClientService.Instance.OnAuthTokenChanged += OnSessionChanged;
            await LoadData();
        }

        public void Deactivate()
        {
            _active = false;
            IsBusy = false;
            _queryGeneration++;
            _loadCancellation?.Cancel();
            ApiClientService.Instance.OnAuthTokenChanged -= OnSessionChanged;
        }

        private void OnSessionChanged(object? sender, string? token)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _lastSessionToken = token;
                ClearReport();
                HasError = false;
                ErrorMessage = string.Empty;
                _ = LoadData();
            });
        }

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
                _ = LoadData();
            }
        }

        private DateTimeOffset _endDate = DateTimeOffset.Now;
        public DateTimeOffset EndDate
        {
            get => _endDate;
            set
            {
                this.RaiseAndSetIfChanged(ref _endDate, value);
                _ = LoadData();
            }
        }

        private int _totalSales;
        public int TotalSales
        {
            get => _totalSales;
            set => this.RaiseAndSetIfChanged(ref _totalSales, value);
        }

        private int _unknownAmountCount;
        public int UnknownAmountCount
        {
            get => _unknownAmountCount;
            private set { this.RaiseAndSetIfChanged(ref _unknownAmountCount, value); this.RaisePropertyChanged(nameof(AmountSummary)); }
        }
        public string AmountSummary => UnknownAmountCount == 0 ? "Доход по суммам, записанным при продаже"
            : $"Показана известная часть дохода. Продажи без исторической суммы: {UnknownAmountCount}";

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

        public SalesStatisticsViewModel(Func<DateOnly, DateOnly, CancellationToken, Task<SalesReportSnapshot>>? loadReport = null)
        {
            _loadReport = loadReport ?? SalesReportClient.GetAsync;
            InitializeCharts();
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

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private async Task RefreshAsync() => await LoadData();

        private async Task LoadData()
        {
            if (!_active) return;
            _loadCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _loadCancellation = cancellation;
            var generation = ++_queryGeneration;
            var start = DateOnly.FromDateTime(StartDate.Date);
            var end = DateOnly.FromDateTime(EndDate.Date);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => { IsBusy = true; HasError = false; ErrorMessage = string.Empty; });
                var report = await _loadReport(start, end, cancellation.Token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_active || generation != _queryGeneration || cancellation.IsCancellationRequested) return;
                    UnknownAmountCount = report.Total.UnknownAmountCount;
                    ApplyReport(report);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning("Report refresh failed; exception type {ExceptionType}", ex.GetType().Name);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_active || generation != _queryGeneration) return;
                    HasError = true;
                    ErrorMessage = "Не удалось обновить отчёт. Показаны ранее загруженные данные. Проверьте соединение и повторите.";
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => { if (generation == _queryGeneration) IsBusy = false; });
                if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
                cancellation.Dispose();
            }
        }

        private void ApplyReport(SalesReportSnapshot report)
        {
            _allDailyStatistics = CalculateGrowthRate(report.Days.Select(d => new DailyStatistic
            { Date = d.Date.ToDateTime(TimeOnly.MinValue), TotalSales = d.Amounts.SaleCount, TotalRevenue = d.Amounts.KnownTotal }).ToList());
            _allRouteStatistics = report.Routes.Select(r => new RouteStatistic
            { RouteName = r.RouteName, TotalSales = r.Amounts.SaleCount, TotalRevenue = r.Amounts.KnownTotal,
                SalesPercentage = report.Total.SaleCount == 0 ? 0 : 100d * r.Amounts.SaleCount / report.Total.SaleCount }).ToList();
            DailyStatistics = new(_allDailyStatistics);
            RouteStatistics = new(_allRouteStatistics);
            TotalSales = report.Total.SaleCount;
            TotalRevenue = report.Total.KnownTotal;
            AverageGrowthRate = _allDailyStatistics.Count == 0 ? 0 : _allDailyStatistics.Average(d => d.GrowthRate);
            UpdateChartsWithData(_allDailyStatistics, _allRouteStatistics);
        }

        private void ClearReport()
        {
            _allDailyStatistics.Clear(); _allRouteStatistics.Clear();
            DailyStatistics.Clear(); RouteStatistics.Clear();
            TotalSales = 0; TotalRevenue = 0; AverageGrowthRate = 0;
            UnknownAmountCount = 0;
            InitializeCharts();
        }

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
                        DataLabelsFormatter = point =>
                        {
                            if (point?.Index == null || point.Index < 0 || point.Index >= routeNames.Length)
                                return string.Empty;
                            return $"{routeNames[point.Index]}\n{point.Coordinate.PrimaryValue:N0} продаж";
                        },
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