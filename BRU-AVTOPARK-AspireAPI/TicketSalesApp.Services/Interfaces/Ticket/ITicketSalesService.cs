// Services/Interfaces/ITicketSalesService.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TicketSalesApp.Services.Interfaces
{
    public class SalesReport
    {
        public DateTime Period { get; set; }
        public decimal TotalIncome { get; set; }
        public int TotalTicketsSold { get; set; }
        public decimal AverageTicketPrice { get; set; }
        public List<RoutePerformance> TopRoutes { get; set; }
        public List<TransportUtilization> TransportStats { get; set; }
    }

    public class RoutePerformance
    {
        public string RouteName { get; set; }
        public string StartPoint { get; set; }
        public string EndPoint { get; set; }
        public int TicketsSold { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal OccupancyRate { get; set; }
    }

    public class TransportUtilization
    {
        public string TransportModel { get; set; }
        public int TotalRoutes { get; set; }
        public int TicketsSold { get; set; }
        public decimal TotalIncome { get; set; }
        public double UtilizationRate { get; set; }
    }

    public class TransportStatistic
    {
        public string TransportModel { get; set; }
        public int TicketsSold { get; set; }
    }

    public interface ITicketSalesService
    {
        Task<decimal> GetTotalIncomeAsync(int year, int month);
        Task<List<TransportStatistic>> GetTopTransportsAsync(int year, int month);
        Task<SalesReport> GetMonthlyReportAsync(int year, int month);
        Task<List<SalesReport>> GetYearlyReportAsync(int year);
        Task<List<RoutePerformance>> GetRoutePerformanceAsync(DateTime startDate, DateTime endDate);
        Task<List<TransportUtilization>> GetTransportUtilizationAsync(DateTime startDate, DateTime endDate);
        Task<byte[]> ExportToExcelAsync(DateTime startDate, DateTime endDate);
        Task<byte[]> ExportToPdfAsync(DateTime startDate, DateTime endDate);
        Task<byte[]> ExportToCsvAsync(DateTime startDate, DateTime endDate);
    }
}