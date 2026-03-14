// Services/Interfaces/ITicketSalesService.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

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
        // Read operations
        Task<List<Sale>> GetAllSalesAsync();
        Task<Sale?> GetSaleByIdAsync(uint saleId);
        Task<List<Sale>> GetSalesByTicketIdAsync(uint ticketId);
        Task<List<Sale>> GetSalesByDateRangeAsync(DateTime startDate, DateTime endDate);

        
        // Create/Update/Delete operations
        Task<uint?> CreateSaleAsync(uint ticketId, string buyerName, string buyerPhone, string? saleLocation = null, string? saleNotes = null);
        Task<bool> UpdateSaleAsync(uint saleId, string? buyerName = null, string? buyerPhone = null, string? saleLocation = null, string? saleNotes = null);
        Task<bool> DeleteSaleAsync(uint saleId);
        
        // Reporting operations
        Task<decimal> GetTotalIncomeAsync(int year, int month);
        Task<List<TransportStatistic>> GetTopTransportsAsync(int year, int month);
        Task<SalesReport> GetMonthlyReportAsync(int year, int month);
        Task<List<SalesReport>> GetYearlyReportAsync(int year);
        Task<List<RoutePerformance>> GetRoutePerformanceAsync(DateTime startDate, DateTime endDate);
        Task<List<TransportUtilization>> GetTransportUtilizationAsync(DateTime startDate, DateTime endDate);
        
        // Export operations
        Task<byte[]> ExportToExcelAsync(DateTime startDate, DateTime endDate);
        Task<byte[]> ExportToPdfAsync(DateTime startDate, DateTime endDate);
        Task<byte[]> ExportToCsvAsync(DateTime startDate, DateTime endDate);
    }
}
