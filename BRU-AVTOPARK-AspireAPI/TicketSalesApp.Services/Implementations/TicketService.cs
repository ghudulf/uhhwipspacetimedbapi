using Microsoft.Extensions.Logging;
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
                return connection.Db.Ticket.Iter()
                    .FirstOrDefault(t => t.TicketId == ticketId);
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
                return connection.Db.Ticket.Iter()
                    .Where(t => t.RouteId == routeId)
                    .ToList();
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
                return connection.Db.Ticket.Iter()
                    //.Where(t => t.PassengerId == passengerId) // This line will be removed
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tickets for passenger: {PassengerId}", passengerId);
                throw;
            }
        }

        public async Task<bool> CreateTicketAsync(uint routeId, uint seatNumber, double ticketPrice, string paymentMethod, ulong purchaseTime)
        {
            try
            {
                _logger.LogInformation("Creating ticket for route {RouteId}", routeId); // Removed passengerId
                var connection = _spacetimeDBService.GetConnection();

                var route = connection.Db.Route.Iter()
                    .FirstOrDefault(r => r.RouteId == routeId);
                if (route == null)
                {
                    _logger.LogWarning("Route not found: {RouteId}", routeId);
                    return false;
                }

                // Removed passengerId related code
                // Check if seat is available
                var existingTicket = connection.Db.Ticket.Iter()
                    .FirstOrDefault(t => t.RouteId == routeId && t.SeatNumber == seatNumber);
                if (existingTicket != null)
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
                    (ulong?)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() // PurchaseTime



                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ticket for route {RouteId}", routeId); // Removed passengerId
                throw;
            }
        }

        public async Task<bool> UpdateTicketAsync(uint ticketId, uint? routeId = null, double? ticketPrice = null, uint? seatNumber = null, string? paymentMethod = null, bool? isActive = null, ulong? updatedAt = null, string? updatedBy = null)
        {
            try
            {
                _logger.LogInformation("Updating ticket: {TicketId}", ticketId);
                var connection = _spacetimeDBService.GetConnection();
                
                var ticket = connection.Db.Ticket.Iter()
                    .FirstOrDefault(t => t.TicketId == ticketId);
                if (ticket == null)
                {
                    _logger.LogWarning("Ticket not found: {TicketId}", ticketId);
                    return false;
                }

                if (seatNumber.HasValue)
                {
                    // Check if new seat is available
                    var existingTicket = connection.Db.Ticket.Iter()
                        .FirstOrDefault(t => t.RouteId == ticket.RouteId && 
                                           t.SeatNumber == seatNumber.Value &&
                                           t.TicketId != ticketId);
                    if (existingTicket != null)
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
                    isActive // Pass the isActive parameter directly
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating ticket: {TicketId}", ticketId);
                throw;
            }
        }

        public async Task<bool> DeleteTicketAsync(uint ticketId)
        {
            try
            {
                _logger.LogInformation("Deleting ticket: {TicketId}", ticketId);
                var connection = _spacetimeDBService.GetConnection();
                
                var ticket = connection.Db.Ticket.Iter()
                    .FirstOrDefault(t => t.TicketId == ticketId);
                if (ticket == null)
                {
                    _logger.LogWarning("Ticket not found: {TicketId}", ticketId);
                    return false;
                }

                // Call the DeleteTicket reducer
                connection.Reducers.DeleteTicket(ticketId);

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
                return connection.Db.Ticket.Iter()
                    .Where(t => t.IsActive)
                    .ToList();
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
                return connection.Db.Ticket.Iter()
                    .Where(t => t.PurchaseTime >= startDate && t.PurchaseTime <= endDate)
                    .OrderBy(t => t.PurchaseTime)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tickets between {StartDate} and {EndDate}",
                    DateTimeOffset.FromUnixTimeMilliseconds((long)startDate).ToString(),
                    DateTimeOffset.FromUnixTimeMilliseconds((long)endDate).ToString());
                throw;
            }
        }

        public async Task<bool> CancelTicketAsync(uint ticketId)
        {
            try
            {
                _logger.LogInformation("Cancelling ticket: {TicketId}", ticketId);
                var connection = _spacetimeDBService.GetConnection();

                var ticket = connection.Db.Ticket.Iter()
                    .FirstOrDefault(t => t.TicketId == ticketId);
                if (ticket == null)
                {
                    _logger.LogWarning("Ticket not found: {TicketId}", ticketId);
                    return false;
                }

                // Call the CancelTicket reducer
                connection.Reducers.CancelTicket(ticketId);

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

                var ticket = connection.Db.Ticket.Iter()
                    .FirstOrDefault(t => t.TicketId == ticketId);
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

                var sale = connection.Db.Sale.Iter()
                    .FirstOrDefault(s => s.SaleId == saleId);
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

                var sale = connection.Db.Sale.Iter()
                    .FirstOrDefault(s => s.SaleId == saleId);
                if (sale == null)
                {
                    _logger.LogWarning("Sale not found: {SaleId}", saleId);
                    return false;
                }

                // Call the DeleteSale reducer
                connection.Reducers.DeleteSale(saleId);

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