using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Allow all authenticated users to read
    public class TicketsController : ControllerBase
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<TicketsController> _logger;
        private readonly ITicketService _ticketService;

        public TicketsController(ISpacetimeDBService spacetimeService, ILogger<TicketsController> logger, ITicketService ticketService)
        {
            _spacetimeService = spacetimeService;
            _logger = logger;
            _ticketService = ticketService;
        }

        private bool IsAdmin()
        {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return false;

            var token = authHeader.Substring("Bearer ".Length);
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(token);
            var roleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "role");
            return roleClaim?.Value == "1";
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Ticket>>> GetTickets()
        {
            _logger.LogInformation("Fetching all tickets with route information");
            try
            {
                var conn = _spacetimeService.GetConnection();
                var tickets = conn.Db.Ticket.Iter().ToList();
                _logger.LogDebug("Retrieved {Count} tickets", tickets.Count);
                return tickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tickets");
                return StatusCode(500, "An error occurred while retrieving tickets");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Ticket>> GetTicket(uint id)
        {
            _logger.LogInformation("Fetching ticket {TicketId}", id);
            try
            {
                var conn = _spacetimeService.GetConnection();
                var ticket = conn.Db.Ticket.TicketId.Find(id);

                if (ticket == null)
                {
                    _logger.LogWarning("Ticket {TicketId} not found", id);
                    return NotFound();
                }

                _logger.LogDebug("Successfully retrieved ticket {TicketId}", id);
                return ticket;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ticket {TicketId}", id);
                return StatusCode(500, "An error occurred while retrieving the ticket");
            }
        }

        [HttpPost]
        public async Task<ActionResult<Ticket>> CreateTicket([FromBody] CreateTicketModel model)
        {
            if (!IsAdmin()) return Forbid();

            try
            {
                var conn = _spacetimeService.GetConnection();
                
                var route = conn.Db.Route.RouteId.Find(model.RouteId);
                if (route == null)
                {
                    return BadRequest("Invalid route ID");
                }

                // Check if the seat is already taken
                var existingTicket = conn.Db.Ticket.Iter()
                    .FirstOrDefault(t => t.RouteId == model.RouteId && t.SeatNumber == model.SeatNumber);
                if (existingTicket != null)
                {
                    return BadRequest("Seat is already taken");
                }

                // Call the CreateTicket reducer
                conn.Reducers.CreateTicket(model.RouteId, model.TicketPrice, model.SeatNumber, model.PaymentMethod, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                
                // Wait a moment for the reducer to complete and the subscription to update
                await Task.Delay(100);
                
                // Find the newly created ticket
                var ticket = conn.Db.Ticket.Iter()
                    .OrderByDescending(t => t.TicketId)
                    .FirstOrDefault();

                if (ticket == null)
                {
                    return StatusCode(500, "Failed to create ticket");
                }

                return CreatedAtAction(nameof(GetTicket), new { id = ticket.TicketId }, ticket);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ticket");
                return StatusCode(500, "An error occurred while creating the ticket");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTicket(uint id, [FromBody] UpdateTicketModel model)
        {
            if (!IsAdmin()) return Forbid();

            try
            {
                var conn = _spacetimeService.GetConnection();
                
                var ticket = conn.Db.Ticket.TicketId.Find(id);
                if (ticket == null)
                {
                    return NotFound();
                }

                if (model.RouteId.HasValue)
                {
                    var route = conn.Db.Route.RouteId.Find(model.RouteId.Value);
                    if (route == null)
                    {
                        return BadRequest("Invalid route ID");
                    }
                }
                
                // Check if the new seat number is already taken, if provided
                if (model.SeatNumber.HasValue && model.SeatNumber.Value != ticket.SeatNumber)
                {
                    var existingTicket = conn.Db.Ticket.Iter()
                        .FirstOrDefault(t => t.RouteId == (model.RouteId ?? ticket.RouteId) && t.SeatNumber == model.SeatNumber.Value);
                    if (existingTicket != null)
                    {
                        return BadRequest("Seat is already taken");
                    }
                }

                // Call the UpdateTicket reducer
                conn.Reducers.UpdateTicket(id, model.RouteId ?? ticket.RouteId, model.SeatNumber ?? ticket.SeatNumber, model.TicketPrice ?? ticket.TicketPrice, model.PaymentMethod ?? ticket.PaymentMethod, model.IsActive ?? ticket.IsActive);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating ticket {TicketId}", id);
                return StatusCode(500, "An error occurred while updating the ticket");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTicket(uint id)
        {
            if (!IsAdmin()) return Forbid();

            try
            {
                var conn = _spacetimeService.GetConnection();
                
                var ticket = conn.Db.Ticket.TicketId.Find(id);
                if (ticket == null)
                {
                    return NotFound();
                }

                // Check if ticket is sold
                var sale = conn.Db.Sale.Iter().FirstOrDefault(s => s.TicketId == id);
                if (sale != null)
                {
                    return BadRequest("Cannot delete a sold ticket");
                }

                // Call the DeleteTicket reducer
                conn.Reducers.DeleteTicket(id);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting ticket {TicketId}", id);
                return StatusCode(500, "An error occurred while deleting the ticket");
            }
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<Ticket>>> SearchTickets(
            [FromQuery] uint? routeId = null,
            [FromQuery] double? minPrice = null,
            [FromQuery] double? maxPrice = null,
            [FromQuery] bool? isSold = null)
        {
            _logger.LogInformation("Searching tickets with routeId: {RouteId}, minPrice: {MinPrice}, maxPrice: {MaxPrice}, isSold: {IsSold}",
                routeId?.ToString() ?? "any", minPrice?.ToString() ?? "any", maxPrice?.ToString() ?? "any", isSold?.ToString() ?? "any");

            try
            {
                var conn = _spacetimeService.GetConnection();
                
                var query = conn.Db.Ticket.Iter().AsEnumerable();

                if (routeId.HasValue)
                    query = query.Where(t => t.RouteId == routeId.Value);

                if (minPrice.HasValue)
                    query = query.Where(t => t.TicketPrice >= minPrice.Value);

                if (maxPrice.HasValue)
                    query = query.Where(t => t.TicketPrice <= maxPrice.Value);

                if (isSold.HasValue)
                {
                    var soldTicketIds = conn.Db.Sale.Iter().Select(s => s.TicketId).ToHashSet();
                    query = isSold.Value
                        ? query.Where(t => soldTicketIds.Contains(t.TicketId))
                        : query.Where(t => !soldTicketIds.Contains(t.TicketId));
                }

                var results = query.ToList();
                _logger.LogDebug("Found {Count} tickets matching search criteria", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching tickets");
                return StatusCode(500, "An error occurred while searching tickets");
            }
        }

        [HttpGet("route/{routeId}")]
        public async Task<ActionResult<IEnumerable<Ticket>>> GetTicketsByRoute(uint routeId)
        {
            _logger.LogInformation("Fetching tickets for route {RouteId}", routeId);

            try
            {
                var conn = _spacetimeService.GetConnection();
                
                // First verify route exists
                var route = conn.Db.Route.RouteId.Find(routeId);
                if (route == null)
                {
                    _logger.LogWarning("Route {RouteId} not found", routeId);
                    return NotFound($"Route {routeId} not found");
                }

                var tickets = conn.Db.Ticket.Iter()
                    .Where(t => t.RouteId == routeId)
                    .ToList();

                _logger.LogDebug("Found {Count} tickets for route {RouteId}", tickets.Count, routeId);
                return tickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tickets for route {RouteId}", routeId);
                return StatusCode(500, "An error occurred while retrieving tickets");
            }
        }

        [HttpGet("available")]
        public async Task<ActionResult<IEnumerable<Ticket>>> GetAvailableTickets()
        {
            _logger.LogInformation("Fetching available tickets");

            try
            {
                var conn = _spacetimeService.GetConnection();
                
                var soldTicketIds = conn.Db.Sale.Iter().Select(s => s.TicketId).ToHashSet();
                var tickets = conn.Db.Ticket.Iter()
                    .Where(t => !soldTicketIds.Contains(t.TicketId))
                    .ToList();

                _logger.LogDebug("Found {Count} available tickets", tickets.Count);
                return tickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving available tickets");
                return StatusCode(500, "An error occurred while retrieving available tickets");
            }
        }
    }

    public class CreateTicketModel
    {
        public required uint RouteId { get; set; }
        public required double TicketPrice { get; set; }
        public required uint SeatNumber { get; set; }
        public string PaymentMethod { get; set; } = "Cash"; // Default payment method
    }

    public class UpdateTicketModel
    {
        public uint? RouteId { get; set; }
        public double? TicketPrice { get; set; }
        public uint? SeatNumber { get; set; }
        public string? PaymentMethod { get; set; }
        public bool? IsActive { get; set; }
    }
} 