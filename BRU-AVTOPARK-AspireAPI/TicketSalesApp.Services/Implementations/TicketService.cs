using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class TicketService : ITicketService
    {
        private readonly ISpacetimeDBService _spacetimeDBService;
        private readonly ILogger<TicketService> _logger;

        public TicketService(ISpacetimeDBService spacetimeDBService, ILogger<TicketService> logger)
        {
            _spacetimeDBService = spacetimeDBService ?? throw new ArgumentNullException(nameof(spacetimeDBService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<Ticket>> GetAllTicketsAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all tickets");
                var connection = _spacetimeDBService.GetConnection();
                return connection.Db.Ticket.Iter().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all tickets");
                throw;
            }
        }

        public async Task<Ticket?> GetTicketByIdAsync(uint ticketId)
        {
            try
            {
                _logger.LogInformation("Retrieving ticket by ID: {TicketId}", ticketId);
                var connection = _spacetimeDBService.GetConnection();
                
                var allTickets = connection.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total tickets from database", allTickets.Count);
                
                Ticket? matchingTicket = null;
                foreach (var ticket in allTickets)
                {
                    if (ticket.TicketId == ticketId)
                    {
                        matchingTicket = ticket;
                        break;
                    }
                }
                
                return matchingTicket;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ticket by ID: {TicketId}", ticketId);
                throw;
            }
        }

        public async Task<List<Ticket>> GetTicketsByRouteIdAsync(uint routeId)
        {
            try
            {
                _logger.LogInformation("Retrieving tickets for route: {RouteId}", routeId);
                var connection = _spacetimeDBService.GetConnection();
                
                var allTickets = connection.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total tickets from database", allTickets.Count);
                
                List<Ticket> matchingTickets = new List<Ticket>();
                foreach (var ticket in allTickets)
                {
                    if (ticket.RouteId == routeId)
                    {
                        matchingTickets.Add(ticket);
                    }
                }
                
                return matchingTickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tickets for route: {RouteId}", routeId);
                throw;
            }
        }

        public async Task<List<Ticket>> GetTicketsByPassengerIdAsync(uint passengerId)
        {
            try
            {
                _logger.LogInformation("Retrieving tickets for passenger: {PassengerId}", passengerId);
                var connection = _spacetimeDBService.GetConnection();
                
                var allTickets = connection.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total tickets from database", allTickets.Count);
                
                // This is just returning all tickets since PassengerId field was removed
                return allTickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tickets for passenger: {PassengerId}", passengerId);
                throw;
            }
        }

        public async Task<bool> CreateTicketAsync(uint routeId, uint seatNumber, double ticketPrice, string paymentMethod, ulong purchaseTime,Identity loggedinuser)
        {
            try
            {
                _logger.LogInformation("Creating ticket for route {RouteId}", routeId); // Removed passengerId
                var connection = _spacetimeDBService.GetConnection();

                var allRoutes = connection.Db.Route.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total routes from database", allRoutes.Count);
                
                Route? route = null;
                foreach (var r in allRoutes)
                {
                    if (r.RouteId == routeId)
                    {
                        route = r;
                        break;
                    }
                }
                
                if (route == null)
                {
                    _logger.LogWarning("Route not found: {RouteId}", routeId);
                    return false;
                }

                // Check if seat is available
                var allTickets = connection.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total tickets from database", allTickets.Count);
                
                bool seatTaken = false;
                foreach (var ticket in allTickets)
                {
                    if (ticket.RouteId == routeId && ticket.SeatNumber == seatNumber)
                    {
                        seatTaken = true;
                        break;
                    }
                }
                
                if (seatTaken)
                {
                    _logger.LogWarning("Seat {SeatNumber} is already taken on route {RouteId}", seatNumber, routeId);
                    return false;
                }

                // Call the CreateTicket reducer
                connection.Reducers.CreateTicket(
                    routeId,
                    (double)ticketPrice, // Corrected variable name
                    seatNumber, // Removed passengerId
                    
                    paymentMethod,
                    (ulong?)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), // PurchaseTime
                    loggedinuser// logged in identity



                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ticket for route {RouteId}", routeId); // Removed passengerId
                throw;
            }
        }

        public async Task<bool> UpdateTicketAsync(uint ticketId, uint? routeId = null, double? ticketPrice = null, uint? seatNumber = null, string? paymentMethod = null, bool? isActive = null, ulong? updatedAt = null, string? updatedBy = null, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Updating ticket: {TicketId}", ticketId);
                var connection = _spacetimeDBService.GetConnection();
                
                var allTickets = connection.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total tickets from database", allTickets.Count);
                
                Ticket? ticket = null;
                foreach (var t in allTickets)
                {
                    if (t.TicketId == ticketId)
                    {
                        ticket = t;
                        break;
                    }
                }
                
                if (ticket == null)
                {
                    _logger.LogWarning("Ticket not found: {TicketId}", ticketId);
                    return false;
                }

                if (seatNumber.HasValue)
                {
                    // Check if new seat is available
                    bool seatTaken = false;
                    foreach (var t in allTickets)
                    {
                        if (t.RouteId == ticket.RouteId && 
                            t.SeatNumber == seatNumber.Value &&
                            t.TicketId != ticketId)
                        {
                            seatTaken = true;
                            break;
                        }
                    }
                    
                    if (seatTaken)
                    {
                        _logger.LogWarning("Seat {SeatNumber} is already taken on route {RouteId}", seatNumber.Value, ticket.RouteId);
                        return false;
                    }
                }

                // Call the UpdateTicket reducer
                connection.Reducers.UpdateTicket(
                    ticketId,
                    routeId ?? ticket.RouteId,
                    seatNumber ?? ticket.SeatNumber,
                    ticketPrice ?? ticket.TicketPrice,
                    paymentMethod ?? ticket.PaymentMethod,
                    isActive, // Pass the isActive parameter directly
                    actingUser
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating ticket: {TicketId}", ticketId);
                throw;
            }
        }

        public async Task<bool> DeleteTicketAsync(uint ticketId, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Deleting ticket: {TicketId}", ticketId);
                var connection = _spacetimeDBService.GetConnection();
                
                var allTickets = connection.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total tickets from database", allTickets.Count);
                
                Ticket? ticket = null;
                foreach (var t in allTickets)
                {
                    if (t.TicketId == ticketId)
                    {
                        ticket = t;
                        break;
                    }
                }
                
                if (ticket == null)
                {
                    _logger.LogWarning("Ticket not found: {TicketId}", ticketId);
                    return false;
                }

                // Call the DeleteTicket reducer
                connection.Reducers.DeleteTicket(ticketId, actingUser);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting ticket: {TicketId}", ticketId);
                throw;
            }
        }

        public async Task<List<Ticket>> GetActiveTicketsAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving active tickets");
                var connection = _spacetimeDBService.GetConnection();
                
                var allTickets = connection.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total tickets from database", allTickets.Count);
                
                List<Ticket> activeTickets = new List<Ticket>();
                foreach (var ticket in allTickets)
                {
                    if (ticket.IsActive)
                    {
                        activeTickets.Add(ticket);
                    }
                }
                
                return activeTickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving active tickets");
                throw;
            }
        }

        public async Task<List<Ticket>> GetTicketsByDateRangeAsync(ulong startDate, ulong endDate)
        {
            try
            {
                _logger.LogInformation("Retrieving tickets between {StartDate} and {EndDate}",
                    DateTimeOffset.FromUnixTimeMilliseconds((long)startDate).ToString(),
                    DateTimeOffset.FromUnixTimeMilliseconds((long)endDate).ToString());
                
                var connection = _spacetimeDBService.GetConnection();
                
                var allTickets = connection.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total tickets from database", allTickets.Count);
                
                List<Ticket> matchingTickets = new List<Ticket>();
                foreach (var ticket in allTickets)
                {
                    if (ticket.PurchaseTime >= startDate && ticket.PurchaseTime <= endDate)
                    {
                        matchingTickets.Add(ticket);
                    }
                }
                
                // Sort by purchase time
                matchingTickets.Sort((a, b) => a.PurchaseTime.CompareTo(b.PurchaseTime));
                
                return matchingTickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tickets between {StartDate} and {EndDate}",
                    DateTimeOffset.FromUnixTimeMilliseconds((long)startDate).ToString(),
                    DateTimeOffset.FromUnixTimeMilliseconds((long)endDate).ToString());
                throw;
            }
        }

        public async Task<bool> CancelTicketAsync(uint ticketId, Identity? actingUser = null)
        {
            try
            {
                _logger.LogInformation("Cancelling ticket: {TicketId}", ticketId);
                var connection = _spacetimeDBService.GetConnection();

                var allTickets = connection.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total tickets from database", allTickets.Count);
                
                Ticket? ticket = null;
                foreach (var t in allTickets)
                {
                    if (t.TicketId == ticketId)
                    {
                        ticket = t;
                        break;
                    }
                }
                
                if (ticket == null)
                {
                    _logger.LogWarning("Ticket not found: {TicketId}", ticketId);
                    return false;
                }

                // Call the CancelTicket reducer
                connection.Reducers.CancelTicket(ticketId, actingUser);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling ticket: {TicketId}", ticketId);
                throw;
            }   
        }   

        public async Task<bool> CreateSaleAsync(uint ticketId, string paymentMethod, string paymentStatus, string? paymentReference = null, string? notes = null)
        {
            try
            {
                _logger.LogInformation("Creating sale for ticket: {TicketId}", ticketId);
                var connection = _spacetimeDBService.GetConnection();

                var allTickets = connection.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total tickets from database", allTickets.Count);
                
                Ticket? ticket = null;
                foreach (var t in allTickets)
                {
                    if (t.TicketId == ticketId)
                    {
                        ticket = t;
                        break;
                    }
                }
                
                if (ticket == null)
                {   
                    _logger.LogWarning("Ticket not found: {TicketId}", ticketId);
                    return false;
                }

                // Call the CreateSale reducer
                connection.Reducers.CreateSale(
                    ticketId,
                    paymentMethod,
                    paymentStatus,
                    paymentReference,
                    notes
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating sale for ticket: {TicketId}", ticketId);
                throw;
            }
        }

        public async Task<bool> UpdateSaleAsync(uint saleId, string? paymentMethod = null, string? paymentStatus = null, string? paymentReference = null, string? notes = null)
        {
            try
            {
                _logger.LogInformation("Updating sale: {SaleId}", saleId);
                var connection = _spacetimeDBService.GetConnection();

                var allSales = connection.Db.Sale.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total sales from database", allSales.Count);
                
                Sale? sale = null;
                foreach (var s in allSales)
                {
                    if (s.SaleId == saleId)
                    {
                        sale = s;
                        break;
                    }
                }
                
                if (sale == null)
                {
                    _logger.LogWarning("Sale not found: {SaleId}", saleId);
                    return false;
                }

                // Call the UpdateSale reducer
                //connection.Reducers.UpdateSale(
                //    saleId,
                //    paymentMethod ?? sale.PaymentMethod, // Removed PaymentMethod related code
                //    paymentStatus ?? sale.PaymentStatus, // Removed PaymentStatus related code
                //    paymentReference ?? sale.PaymentReference, // Removed PaymentReference related code
                //    notes ?? sale.Notes // Removed Notes related code
                //);

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
                var connection = _spacetimeDBService.GetConnection();

                var allSales = connection.Db.Sale.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} total sales from database", allSales.Count);
                
                Sale? sale = null;
                foreach (var s in allSales)
                {
                    if (s.SaleId == saleId)
                    {
                        sale = s;
                        break;
                    }
                }
                
                if (sale == null)
                {
                    _logger.LogWarning("Sale not found: {SaleId}", saleId);
                    return false;
                }

                // Call the DeleteSale reducer
                connection.Reducers.DeleteSale(saleId,null);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting sale: {SaleId}", saleId);
                throw;
            }
        }   
    }
} 