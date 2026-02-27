using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Text;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class TicketSalesService : ITicketSalesService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<TicketSalesService> _logger;
        private readonly IExportService? _exportService; // Make it optional

        public TicketSalesService(
            ISpacetimeDBService spacetimeService,
            ILogger<TicketSalesService> logger,
            IExportService? exportService = null) // Make it optional with default null
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _exportService = exportService; // Can be null
        }

        public async Task<decimal> GetTotalIncomeAsync(int year, int month)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                
                // Calculate start and end timestamps for the month
                var startDate = new DateTime(year, month, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);
                
                ulong startTimestamp = (ulong)new DateTimeOffset(startDate).ToUnixTimeMilliseconds();
                ulong endTimestamp = (ulong)new DateTimeOffset(endDate.AddDays(1).AddTicks(-1)).ToUnixTimeMilliseconds();
                
                // Get all sales for the month
                var sales = conn.Db.Sale.Iter()
                    .Where(s => s.SaleDate >= startTimestamp && s.SaleDate <= endTimestamp)
                    .ToList();
                
                // Calculate total income
                decimal totalIncome = 0;
                foreach (var sale in sales)
                {
                    var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                    if (ticket != null)
                    {
                        totalIncome += (decimal)ticket.TicketPrice;
                    }
                }
                
                return totalIncome;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total income for {Year}-{Month}", year, month);
                throw;
            }
        }

        public async Task<List<TransportStatistic>> GetTopTransportsAsync(int year, int month)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                
                // Calculate start and end timestamps for the month
                var startDate = new DateTime(year, month, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);
                
                ulong startTimestamp = (ulong)new DateTimeOffset(startDate).ToUnixTimeMilliseconds();
                ulong endTimestamp = (ulong)new DateTimeOffset(endDate.AddDays(1).AddTicks(-1)).ToUnixTimeMilliseconds();
                
                // Get all sales for the month
                var sales = conn.Db.Sale.Iter()
                    .Where(s => s.SaleDate >= startTimestamp && s.SaleDate <= endTimestamp)
                    .ToList();
                
                // Group by bus model
                var transportStats = new Dictionary<string, int>();
                
                foreach (var sale in sales)
                {
                    var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                    if (ticket != null)
                    {
                        var route = conn.Db.Route.RouteId.Find(ticket.RouteId);
                        if (route != null)
                        {
                            var bus = conn.Db.Bus.BusId.Find(route.BusId);
                            if (bus != null)
                            {
                                if (transportStats.ContainsKey(bus.Model))
                                {
                                    transportStats[bus.Model]++;
                                }
                                else
                                {
                                    transportStats[bus.Model] = 1;
                                }
                            }
                        }
                    }
                }
                
                // Convert to TransportStatistic objects
                var result = transportStats
                    .Select(kvp => new TransportStatistic
                    {
                        TransportModel = kvp.Key,
                        TicketsSold = kvp.Value
                    })
                    .OrderByDescending(ts => ts.TicketsSold)
                    .Take(38)
                    .ToList();
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting top transports for {Year}-{Month}", year, month);
                throw;
            }
        }

        public async Task<SalesReport> GetMonthlyReportAsync(int year, int month)
        {
            try
            {
                var startDate = new DateTime(year, month, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                var totalIncome = await GetTotalIncomeAsync(year, month);
                var routePerformance = await GetRoutePerformanceAsync(startDate, endDate);
                var transportStats = await GetTransportUtilizationAsync(startDate, endDate);
                
                var conn = _spacetimeService.GetConnection();
                
                // Calculate start and end timestamps for the month
                ulong startTimestamp = (ulong)new DateTimeOffset(startDate).ToUnixTimeMilliseconds();
                ulong endTimestamp = (ulong)new DateTimeOffset(endDate.AddDays(1).AddTicks(-1)).ToUnixTimeMilliseconds();
                
                // Get total tickets sold
                var totalTicketsSold = conn.Db.Sale.Iter()
                    .Count(s => s.SaleDate >= startTimestamp && s.SaleDate <= endTimestamp);
                
                // Calculate average ticket price
                decimal averageTicketPrice = totalTicketsSold > 0 ? totalIncome / totalTicketsSold : 0;

                var report = new SalesReport
                {
                    Period = startDate,
                    TotalIncome = totalIncome,
                    TotalTicketsSold = totalTicketsSold,
                    AverageTicketPrice = averageTicketPrice,
                    TopRoutes = routePerformance,
                    TransportStats = transportStats
                };

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating monthly report for {Year}-{Month}", year, month);
                throw;
            }
        }

        public async Task<List<SalesReport>> GetYearlyReportAsync(int year)
        {
            var reports = new List<SalesReport>();
            for (int month = 1; month <= 12; month++)
            {
                reports.Add(await GetMonthlyReportAsync(year, month));
            }
            return reports;
        }

        public async Task<List<RoutePerformance>> GetRoutePerformanceAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                
                // Calculate start and end timestamps
                ulong startTimestamp = (ulong)new DateTimeOffset(startDate).ToUnixTimeMilliseconds();
                ulong endTimestamp = (ulong)new DateTimeOffset(endDate.AddDays(1).AddTicks(-1)).ToUnixTimeMilliseconds();
                
                // Get all sales for the period
                var sales = conn.Db.Sale.Iter()
                    .Where(s => s.SaleDate >= startTimestamp && s.SaleDate <= endTimestamp)
                    .ToList();
                
                // Group by route
                var routePerformance = new Dictionary<uint, RoutePerformanceData>();
                
                foreach (var sale in sales)
                {
                    var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                    if (ticket != null)
                    {
                        var routeId = ticket.RouteId;
                        
                        if (!routePerformance.ContainsKey(routeId))
                        {
                            var route = conn.Db.Route.RouteId.Find(routeId);
                            if (route != null)
                            {
                                routePerformance[routeId] = new RoutePerformanceData
                                {
                                    RouteId = routeId,
                                    StartPoint = route.StartPoint,
                                    EndPoint = route.EndPoint,
                                    TicketsSold = 1,
                                    TotalIncome = (decimal)ticket.TicketPrice
                                };
                            }
                        }
                        else
                        {
                            routePerformance[routeId].TicketsSold++;
                            routePerformance[routeId].TotalIncome += (decimal)ticket.TicketPrice;
                        }
                    }
                }
                
                // Calculate occupancy rates
                foreach (var rp in routePerformance.Values)
                {
                    // Get all schedules for this route
                    var schedules = conn.Db.RouteSchedule.Iter()
                        .Where(rs => rs.RouteId == rp.RouteId)
                        .ToList();
                    
                    // Calculate total available seats
                    int totalSeats = schedules.Sum(rs => (int)rs.AvailableSeats);
                    
                    // Calculate occupancy rate
                    rp.OccupancyRate = totalSeats > 0 ? (decimal)rp.TicketsSold / totalSeats * 100 : 0;
                }
                
                // Convert to RoutePerformance objects
                var result = routePerformance.Values
                    .Select(rp => new RoutePerformance
                    {
                        RouteName = $"{rp.StartPoint} - {rp.EndPoint}",
                        StartPoint = rp.StartPoint,
                        EndPoint = rp.EndPoint,
                        TicketsSold = rp.TicketsSold,
                        TotalIncome = rp.TotalIncome,
                        OccupancyRate = rp.OccupancyRate
                    })
                    .OrderByDescending(rp => rp.TicketsSold)
                    .ToList();
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting route performance");
                throw;
            }
        }

        public async Task<List<TransportUtilization>> GetTransportUtilizationAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                
                // Calculate start and end timestamps
                ulong startTimestamp = (ulong)new DateTimeOffset(startDate).ToUnixTimeMilliseconds();
                ulong endTimestamp = (ulong)new DateTimeOffset(endDate.AddDays(1).AddTicks(-1)).ToUnixTimeMilliseconds();
                
                // Get all sales for the period
                var sales = conn.Db.Sale.Iter()
                    .Where(s => s.SaleDate >= startTimestamp && s.SaleDate <= endTimestamp)
                    .ToList();
                
                // Group by bus model
                var transportUtilization = new Dictionary<string, TransportUtilizationData>();
                
                foreach (var sale in sales)
                {
                    var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                    if (ticket != null)
                    {
                        var route = conn.Db.Route.RouteId.Find(ticket.RouteId);
                        if (route != null)
                        {
                            var bus = conn.Db.Bus.BusId.Find(route.BusId);
                            if (bus != null)
                            {
                                if (!transportUtilization.ContainsKey(bus.Model))
                                {
                                    // Count routes for this bus model
                                    int totalRoutes = conn.Db.Route.Iter()
                                        .Count(r => r.BusId == bus.BusId);
                                    
                                    transportUtilization[bus.Model] = new TransportUtilizationData
                                    {
                                        Model = bus.Model,
                                        TotalRoutes = totalRoutes,
                                        TicketsSold = 1,
                                        TotalIncome = (decimal)ticket.TicketPrice,
                                        BusIds = new HashSet<uint> { bus.BusId }
                                    };
                                }
                                else
                                {
                                    transportUtilization[bus.Model].TicketsSold++;
                                    transportUtilization[bus.Model].TotalIncome += (decimal)ticket.TicketPrice;
                                    transportUtilization[bus.Model].BusIds.Add(bus.BusId);
                                }
                            }
                        }
                    }
                }
                
                // Calculate utilization rates
                foreach (var tu in transportUtilization.Values)
                {
                    // Get all tickets for routes with this bus model
                    int totalTickets = 0;
                    foreach (var busId in tu.BusIds)
                    {
                        var routes = conn.Db.Route.Iter()
                            .Where(r => r.BusId == busId)
                            .ToList();
                        
                        foreach (var route in routes)
                        {
                            var schedules = conn.Db.RouteSchedule.Iter()
                                .Where(rs => rs.RouteId == route.RouteId)
                                .ToList();
                            
                            totalTickets += schedules.Sum(rs => (int)rs.AvailableSeats);
                        }
                    }
                    
                    // Calculate utilization rate
                    tu.UtilizationRate = totalTickets > 0 ? (double)tu.TicketsSold / totalTickets * 100 : 0;
                }
                
                // Convert to TransportUtilization objects
                var result = transportUtilization.Values
                    .Select(tu => new TransportUtilization
                    {
                        TransportModel = tu.Model,
                        TotalRoutes = tu.TotalRoutes,
                        TicketsSold = tu.TicketsSold,
                        TotalIncome = tu.TotalIncome,
                        UtilizationRate = tu.UtilizationRate
                    })
                    .OrderByDescending(tu => tu.UtilizationRate)
                    .ToList();
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting transport utilization");
                throw;
            }
        }

        public async Task<byte[]> ExportToCsvAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var report = await GetMonthlyReportAsync(startDate.Year, startDate.Month);
                var sb = new StringBuilder();

                // Add headers
                sb.AppendLine("Period,TotalIncome,TotalTicketsSold,AverageTicketPrice");

                // Add main report data
                sb.AppendLine($"{report.Period:yyyy-MM-dd},{report.TotalIncome},{report.TotalTicketsSold},{report.AverageTicketPrice}");

                // Add route performance
                sb.AppendLine("\nRoute Performance");
                sb.AppendLine("RouteName,StartPoint,EndPoint,TicketsSold,TotalIncome,OccupancyRate");
                foreach (var route in report.TopRoutes)
                {
                    sb.AppendLine($"{route.RouteName},{route.StartPoint},{route.EndPoint},{route.TicketsSold},{route.TotalIncome},{route.OccupancyRate}");
                }

                // Add transport stats
                sb.AppendLine("\nTransport Statistics");
                sb.AppendLine("TransportModel,TotalRoutes,TicketsSold,TotalIncome,UtilizationRate");
                foreach (var transport in report.TransportStats)
                {
                    sb.AppendLine($"{transport.TransportModel},{transport.TotalRoutes},{transport.TicketsSold},{transport.TotalIncome},{transport.UtilizationRate}");
                }

                return Encoding.UTF8.GetBytes(sb.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting to CSV");
                throw;
            }
        }

        public async Task<byte[]> ExportToExcelAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                // For Excel export, we'll return the same CSV format since it can be opened directly in Excel
                return await ExportToCsvAsync(startDate, endDate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting to Excel");
                throw;
            }
        }

        public async Task<byte[]> ExportToPdfAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var report = await GetMonthlyReportAsync(startDate.Year, startDate.Month);
                var sb = new StringBuilder();

                // Create a simple HTML document that can be converted to PDF
                sb.AppendLine("<!DOCTYPE html>");
                sb.AppendLine("<html>");
                sb.AppendLine("<head>");
                sb.AppendLine("<style>");
                sb.AppendLine("body { font-family: Arial, sans-serif; }");
                sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-bottom: 20px; }");
                sb.AppendLine("th, td { border: 1px solid black; padding: 8px; text-align: left; }");
                sb.AppendLine("h2 { color: #333; }");
                sb.AppendLine("</style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");

                // Main report section
                sb.AppendLine($"<h1>Sales Report for {report.Period:MMMM yyyy}</h1>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Total Income</th><th>Total Tickets Sold</th><th>Average Ticket Price</th></tr>");
                sb.AppendLine($"<tr><td>{report.TotalIncome:C}</td><td>{report.TotalTicketsSold}</td><td>{report.AverageTicketPrice:C}</td></tr>");
                sb.AppendLine("</table>");

                // Route performance section
                sb.AppendLine("<h2>Route Performance</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Route</th><th>Tickets Sold</th><th>Total Income</th><th>Occupancy Rate</th></tr>");
                foreach (var route in report.TopRoutes)
                {
                    sb.AppendLine($"<tr><td>{route.RouteName}</td><td>{route.TicketsSold}</td><td>{route.TotalIncome:C}</td><td>{route.OccupancyRate:F1}%</td></tr>");
                }
                sb.AppendLine("</table>");

                // Transport statistics section
                sb.AppendLine("<h2>Transport Statistics</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Transport Model</th><th>Total Routes</th><th>Tickets Sold</th><th>Utilization Rate</th></tr>");
                foreach (var transport in report.TransportStats)
                {
                    sb.AppendLine($"<tr><td>{transport.TransportModel}</td><td>{transport.TotalRoutes}</td><td>{transport.TicketsSold}</td><td>{transport.UtilizationRate:F1}%</td></tr>");
                }
                sb.AppendLine("</table>");

                sb.AppendLine("</body>");
                sb.AppendLine("</html>");

                return Encoding.UTF8.GetBytes(sb.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting to PDF format");
                throw;
            }
        }
        
        // Helper classes for data processing
        private class RoutePerformanceData
        {
            public uint RouteId { get; set; }
            public string StartPoint { get; set; }
            public string EndPoint { get; set; }
            public int TicketsSold { get; set; }
            public decimal TotalIncome { get; set; }
            public decimal OccupancyRate { get; set; }
        }
        
        private class TransportUtilizationData
        {
            public string Model { get; set; }
            public int TotalRoutes { get; set; }
            public int TicketsSold { get; set; }
            public decimal TotalIncome { get; set; }
            public double UtilizationRate { get; set; }
            public HashSet<uint> BusIds { get; set; }
        }
    }
}