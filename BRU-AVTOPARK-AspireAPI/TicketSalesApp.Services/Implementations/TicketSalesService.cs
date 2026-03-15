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

        public async Task<List<Sale>> GetAllSalesAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all sales");
                var conn = _spacetimeService.GetConnection();
                return conn.Db.Sale.Iter().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all sales");
                throw;
            }
        }

        public async Task<(List<Sale> items, int totalCount)> GetPagedSalesAsync(int page, int pageSize)
        {
            try
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 1000);
                _logger.LogInformation("Retrieving sales page {Page} (size {PageSize})", page, pageSize);
                var conn = _spacetimeService.GetConnection();
                var all = conn.Db.Sale.Iter().OrderBy(s => s.SaleDate).ToList();
                var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                return (items, all.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving paged sales");
                throw;
            }
        }

        public async Task<Sale?> GetSaleByIdAsync(uint saleId)
        {
            try
            {
                _logger.LogInformation("Retrieving sale by ID: {SaleId}", saleId);
                var conn = _spacetimeService.GetConnection();
                return conn.Db.Sale.SaleId.Find(saleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sale by ID: {SaleId}", saleId);
                throw;
            }
        }

        public async Task<List<Sale>> GetSalesByTicketIdAsync(uint ticketId)
        {
            try
            {
                _logger.LogInformation("Retrieving sales for ticket: {TicketId}", ticketId);
                var conn = _spacetimeService.GetConnection();
                return conn.Db.Sale.Iter()
                    .Where(s => s.TicketId == ticketId)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sales for ticket: {TicketId}", ticketId);
                throw;
            }
        }

        public async Task<List<Sale>> GetSalesByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                // Validate date range
                if (endDate < startDate)
                {
                    throw new ArgumentException($"End date ({endDate}) must be greater than or equal to start date ({startDate})", nameof(endDate));
                }

                _logger.LogInformation("Retrieving sales between {StartDate} and {EndDate}", startDate, endDate);
                var conn = _spacetimeService.GetConnection();

                // Convert to UTC before creating DateTimeOffset to avoid relabeling local time as UTC.
                ulong startTimestamp = (ulong)new DateTimeOffset(startDate.ToUniversalTime()).ToUnixTimeMilliseconds();
                ulong endTimestamp = (ulong)new DateTimeOffset(endDate.AddDays(1).AddTicks(-1).ToUniversalTime()).ToUnixTimeMilliseconds();
                
                return conn.Db.Sale.Iter()
                    .Where(s => s.SaleDate >= startTimestamp && s.SaleDate <= endTimestamp)
                    .OrderBy(s => s.SaleDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sales between {StartDate} and {EndDate}", startDate, endDate);
                throw;
            }
        }

        public async Task<uint?> CreateSaleAsync(uint ticketId, string buyerName, string buyerPhone, string? saleLocation = null, string? saleNotes = null)
        {
            try
            {
                _logger.LogInformation("Creating sale for ticket: {TicketId}", ticketId);
                var conn = _spacetimeService.GetConnection();

                // Verify ticket exists
                var ticket = conn.Db.Ticket.TicketId.Find(ticketId);
                if (ticket == null)
                {
                    _logger.LogWarning("Ticket not found: {TicketId}", ticketId);
                    return null;
                }

                // Store pre-call count for out-of-band correlation
                var preCallCount = conn.Db.Sale.Iter().Count(s => s.TicketId == ticketId);
                var preCallTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // Call the CreateSale reducer with actual parameters (no correlation in notes)
                conn.Reducers.CreateSale(
                    ticketId,
                    buyerName,
                    buyerPhone,
                    saleLocation,
                    saleNotes
                );

                // Flush pending reducer responses
                conn.FrameTick();

                // WORKAROUND: SpacetimeDB reducers don't return created IDs, forcing timestamp-based correlation.
                // This is racy - concurrent creates for the same ticket can return wrong sale.
                // TODO: Add correlation token to Sale table or use SpacetimeDB transaction support when available.
                // Find the newly created sale using timestamp correlation
                var newSale = conn.Db.Sale.Iter()
                    .Where(s => s.TicketId == ticketId && s.SaleDate >= (ulong)preCallTimestamp)
                    .OrderByDescending(s => s.SaleDate)
                    .FirstOrDefault();

                // Verify we got a new sale (count increased)
                var postCallCount = conn.Db.Sale.Iter().Count(s => s.TicketId == ticketId);
                if (newSale != null && postCallCount > preCallCount)
                {
                    _logger.LogInformation("Successfully created sale with ID: {SaleId}", newSale.SaleId);
                }
                else
                {
                    _logger.LogWarning("Could not reliably identify created sale for ticket: {TicketId}", ticketId);
                    return null;
                }

                return newSale?.SaleId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating sale for ticket: {TicketId}", ticketId);
                throw;
            }
        }

        public async Task<bool> UpdateSaleAsync(uint saleId, string? buyerName = null, string? buyerPhone = null, string? saleLocation = null, string? saleNotes = null)
        {
            try
            {
                _logger.LogInformation("Updating sale: {SaleId}", saleId);
                var conn = _spacetimeService.GetConnection();

                var sale = conn.Db.Sale.SaleId.Find(saleId);
                if (sale == null)
                {
                    _logger.LogWarning("Sale not found: {SaleId}", saleId);
                    return false;
                }

                // UpdateSale reducer is not yet implemented in SpacetimeDB schema
                // Use create+delete workaround pattern (create first to avoid data loss)
                _logger.LogInformation("Using create+delete workaround for sale update: {SaleId}", saleId);

                // Clean notes (no correlation tokens)
                var cleanedNotes = saleNotes ?? sale.SaleNotes;
                if (!string.IsNullOrEmpty(cleanedNotes) && cleanedNotes.Contains("[CORRELATION:"))
                {
                    var startIdx = cleanedNotes.IndexOf("[CORRELATION:");
                    var endIdx = cleanedNotes.IndexOf(']', startIdx);
                    if (endIdx > startIdx)
                    {
                        cleanedNotes = cleanedNotes.Remove(startIdx, endIdx - startIdx + 1).Trim();
                        _logger.LogWarning("Stripped existing correlation marker from sale notes");
                    }
                }

                // Store pre-call timestamp for correlation
                var preCallTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // Create new sale FIRST with updated values (no correlation in notes)
                conn.Reducers.CreateSale(
                    sale.TicketId,
                    buyerName ?? sale.TicketSoldToUser,
                    buyerPhone ?? sale.TicketSoldToUserPhone,
                    saleLocation ?? sale.SaleLocation,
                    cleanedNotes
                );
                conn.FrameTick();

                // Find the newly created sale using timestamp
                var newSale = conn.Db.Sale.Iter()
                    .Where(s => s.TicketId == sale.TicketId &&
                               s.SaleDate >= (ulong)preCallTimestamp &&
                               s.SaleId != saleId)
                    .OrderByDescending(s => s.SaleDate)
                    .FirstOrDefault();

                if (newSale == null)
                {
                    _logger.LogError("Failed to create new sale during update for original SaleId: {SaleId}", saleId);
                    return false;
                }

                _logger.LogInformation("Created new sale {NewSaleId}, now deleting old sale {OldSaleId}", newSale.SaleId, saleId);

                // Now delete the old sale (data is safe in new sale)
                conn.Reducers.DeleteSale(saleId, null);
                conn.FrameTick();

                // Verify deletion
                var oldSaleStillExists = conn.Db.Sale.SaleId.Find(saleId);
                if (oldSaleStillExists != null)
                {
                    _logger.LogError("Old sale {SaleId} still exists after delete attempt - update failed", saleId);
                    return false;
                }

                _logger.LogInformation("Sale updated via create+delete: old SaleId={OldSaleId}, new SaleId={NewSaleId}", saleId, newSale.SaleId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating sale: {SaleId}", saleId);
                throw;
            }
        }

        public async Task<bool> DeleteSaleAsync(uint saleId)
        {
            try
            {
                _logger.LogInformation("Deleting sale: {SaleId}", saleId);
                var conn = _spacetimeService.GetConnection();

                var sale = conn.Db.Sale.SaleId.Find(saleId);
                if (sale == null)
                {
                    _logger.LogWarning("Sale not found: {SaleId}", saleId);
                    return false;
                }

                // Call the DeleteSale reducer
                conn.Reducers.DeleteSale(saleId, null);

                // Wait for confirmation using FrameTick
                conn.FrameTick();

                // Verify deletion
                var deletedSale = conn.Db.Sale.SaleId.Find(saleId);
                if (deletedSale != null)
                {
                    _logger.LogWarning("Sale {SaleId} still exists after delete attempt", saleId);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting sale: {SaleId}", saleId);
                throw;
            }
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