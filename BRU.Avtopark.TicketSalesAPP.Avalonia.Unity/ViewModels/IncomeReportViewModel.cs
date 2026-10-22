using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization;
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
using System.Threading;
using Avalonia.Threading;
using BRU.Avtopark.Contracts;
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
        public decimal? AverageTicketPrice { get; set; }
    }

    public class RouteIncome
    {
        public string RouteName { get; set; } = string.Empty;
        public decimal TotalIncome { get; set; }
        public int TicketsSold { get; set; }
        public decimal? AverageTicketPrice { get; set; }
    }

    public partial class IncomeReportViewModel : ReactiveObject
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

        private int _unknownAmountCount;
        public int UnknownAmountCount
        {
            get => _unknownAmountCount;
            private set { this.RaiseAndSetIfChanged(ref _unknownAmountCount, value); this.RaisePropertyChanged(nameof(AmountSummary)); }
        }
        public string AmountSummary => UnknownAmountCount == 0 ? "Доход по суммам, записанным при продаже"
            : $"Доход и средняя цена рассчитаны только по известным суммам. Продажи без исторической суммы: {UnknownAmountCount}";

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

        private decimal? _averageTicketPrice;
        public decimal? AverageTicketPrice
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

        public IncomeReportViewModel(Func<DateOnly, DateOnly, CancellationToken, Task<SalesReportSnapshot>>? loadReport = null)
        {
            _loadReport = loadReport ?? SalesReportClient.GetAsync;
            InitializeCharts();
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
            _allMonthlyIncomes = report.Months.Select(m => new MonthlyIncome
            {
                Year = m.Year, Month = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m.Month),
                TicketsSold = m.Amounts.SaleCount, TotalIncome = m.Amounts.KnownTotal, AverageTicketPrice = m.Amounts.AverageKnownAmount
            }).ToList();
            _allRouteIncomes = report.Routes.OrderByDescending(r => r.Amounts.KnownTotal).Select(r => new RouteIncome
            {
                RouteName = r.RouteName, TicketsSold = r.Amounts.SaleCount, TotalIncome = r.Amounts.KnownTotal,
                AverageTicketPrice = r.Amounts.AverageKnownAmount
            }).ToList();
            MonthlyIncomes = new(_allMonthlyIncomes);
            RouteIncomes = new(_allRouteIncomes);
            TotalTicketsSold = report.Total.SaleCount;
            TotalIncome = report.Total.KnownTotal;
            AverageTicketPrice = report.Total.AverageKnownAmount;
            UpdateChartsWithData(_allMonthlyIncomes, _allRouteIncomes);
        }

        private void ClearReport()
        {
            _allMonthlyIncomes.Clear(); _allRouteIncomes.Clear();
            MonthlyIncomes.Clear(); RouteIncomes.Clear();
            TotalTicketsSold = 0; TotalIncome = 0; AverageTicketPrice = null;
            UnknownAmountCount = 0;
            InitializeCharts();
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
