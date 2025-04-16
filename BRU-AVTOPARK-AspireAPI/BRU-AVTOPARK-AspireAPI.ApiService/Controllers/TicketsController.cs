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
using System.Security.Claims;
using System.Text.Json;
using Route = SpacetimeDB.Types.Route; // Add alias for Route

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allow all authenticated users to read
    public class TicketsController : BaseController
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<TicketsController> _logger;
        private readonly ITicketService _ticketService;
        private readonly IAuthenticationService _authService;

        public TicketsController(
            ISpacetimeDBService spacetimeService, 
            ILogger<TicketsController> logger, 
            ITicketService ticketService,
            IAuthenticationService authService)
        {
            _spacetimeService = spacetimeService;
            _logger = logger;
            _ticketService = ticketService;
            _authService = authService;
            _logger.LogInformation("TicketsController initialized with services: {@Services}", 
                new { SpacetimeDBService = spacetimeService != null, TicketService = ticketService != null, AuthService = authService != null });
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetTickets()
        {
            _logger.LogInformation("GET /api/Tickets - Fetching all tickets with route information");
            try
            {
                var conn = _spacetimeService.GetConnection();
                _logger.LogDebug("Database connection established successfully");
                
                var tickets = conn.Db.Ticket.Iter().ToList();
                
                // Map to anonymous type with Route details
                var result = tickets.Select(t => {
                    var route = conn.Db.Route.RouteId.Find(t.RouteId);
                    return new {
                        t.TicketId,
                        t.RouteId,
                        Route = route != null ? new {
                            route.RouteId,
                            route.StartPoint,
                            route.EndPoint,
                            route.TravelTime,
                            route.IsActive
                            // Potentially add Bus/Driver info here if needed
                        } : null,
                        t.SeatNumber,
                        t.TicketPrice,
                        t.PaymentMethod,
                        PurchaseTime = DateTimeOffset.FromUnixTimeMilliseconds((long)t.PurchaseTime).DateTime,
                        t.IsActive
                    };
                }).ToList();

                _logger.LogInformation("Retrieved {Count} tickets", result.Count);
                _logger.LogInformation("FULL TICKET DATA: {TicketsData}", JsonSerializer.Serialize(result)); // Added JSON logging
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tickets: {ErrorMessage}", ex.Message);
                return StatusCode(500, "An error occurred while retrieving tickets");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetTicket(uint id)
        {
            _logger.LogInformation("GET /api/Tickets/{TicketId} - Fetching ticket with ID: {TicketId}", id);
            try
            {
                var conn = _spacetimeService.GetConnection();
                _logger.LogDebug("Database connection established successfully for ticket ID: {TicketId}", id);
                
                var ticket = conn.Db.Ticket.TicketId.Find(id);
                _logger.LogInformation("Ticket lookup result: {@Ticket}", ticket);

                if (ticket == null)
                {
                    _logger.LogWarning("Ticket {TicketId} not found in database", id);
                    return NotFound();
                }

                var route = conn.Db.Route.RouteId.Find(ticket.RouteId);

                // Map to anonymous type with Route details
                var result = new {
                    ticket.TicketId,
                    ticket.RouteId,
                    Route = route != null ? new {
                        route.RouteId,
                        route.StartPoint,
                        route.EndPoint,
                        route.TravelTime,
                        route.IsActive
                        // Potentially add Bus/Driver info here if needed
                    } : null,
                    ticket.SeatNumber,
                    ticket.TicketPrice,
                    ticket.PaymentMethod,
                    PurchaseTime = DateTimeOffset.FromUnixTimeMilliseconds((long)ticket.PurchaseTime).DateTime,
                    ticket.IsActive
                };

                _logger.LogInformation("Successfully retrieved ticket: {@Ticket}", result);
                _logger.LogInformation("FULL TICKET DATA: {TicketData}", JsonSerializer.Serialize(result)); // Added JSON logging
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ticket {TicketId}: {ErrorMessage}", id, ex.Message);
                return StatusCode(500, "An error occurred while retrieving the ticket");
            }
        }

        [HttpPost]
        public async Task<ActionResult<Ticket>> CreateTicket([FromBody] CreateTicketModel model)
        {
            _logger.LogInformation("POST /api/Tickets - Create ticket request received with data: {@TicketModel}", model);
            
            if (!IsAdmin())
            {
                _logger.LogWarning("Unauthorized attempt to create ticket by non-admin user");
                return Forbid();
            }

            try
            {
                var conn = _spacetimeService.GetConnection();
                _logger.LogDebug("Database connection established successfully for ticket creation");
                
                var route = conn.Db.Route.RouteId.Find(model.RouteId);
                _logger.LogInformation("Route lookup result for ID {RouteId}: {@Route}", model.RouteId, route);
                
                if (route == null)
                {
                    _logger.LogWarning("Invalid route ID {RouteId} provided for ticket creation", model.RouteId);
                    return BadRequest("Invalid route ID");
                }

                // Check if the seat is already taken
                var existingTicket = conn.Db.Ticket.Iter()
                    .FirstOrDefault(t => t.RouteId == model.RouteId && t.SeatNumber == model.SeatNumber);
                _logger.LogInformation("Existing ticket check for route {RouteId} and seat {SeatNumber}: {@ExistingTicket}", 
                    model.RouteId, model.SeatNumber, existingTicket);
                
                if (existingTicket != null)
                {
                    _logger.LogWarning("Seat {SeatNumber} on route {RouteId} is already taken by ticket {TicketId}", 
                        model.SeatNumber, model.RouteId, existingTicket.TicketId);
                    return BadRequest("Seat is already taken");
                }
                
                // Get user login from JWT token claims
                _logger.LogDebug("JWT claims: {@Claims}", User.Claims.Select(c => new { Type = c.Type, Value = c.Value }));
                var userLogin = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userLogin))
                {
                    // Try alternative claim types if the standard one isn't found
                    userLogin = User.Claims.FirstOrDefault(c => c.Type == "login" || 
                                                               c.Type == "preferred_username" || 
                                                               c.Type == "sub")?.Value;
                }
                _logger.LogInformation("User login extracted from token: {UserLogin}", userLogin);
                
                if (string.IsNullOrEmpty(userLogin))
                {
                    _logger.LogWarning("User login not found in JWT token. All claims: {@Claims}", 
                        User.Claims.Select(c => new { Type = c.Type, Value = c.Value }));
                    return Unauthorized("User identity could not be determined");
                }
                
                // Get user identity from login
                var userIdentity = await _authService.GetUserIdentityByLoginAsync(userLogin);
                _logger.LogInformation("User identity lookup result for login {UserLogin}: {UserIdentity}", userLogin, userIdentity);
                
                if (userIdentity == null)
                {
                    _logger.LogWarning("User identity not found for login: {Login}", userLogin);
                    return Unauthorized("User identity could not be determined");
                }
                
                _logger.LogInformation("Creating ticket with parameters: RouteId={RouteId}, Price={Price}, Seat={Seat}, Payment={Payment}, UserIdentity={UserIdentity}", 
                    model.RouteId, model.TicketPrice, model.SeatNumber, model.PaymentMethod, userIdentity);

                // Call the CreateTicket reducer with the user identity
                var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                conn.Reducers.CreateTicket(
                    model.RouteId, 
                    model.TicketPrice, 
                    model.SeatNumber, 
                    model.PaymentMethod, 
                    timestamp, 
                    userIdentity);
                
                _logger.LogInformation("CreateTicket reducer called with timestamp: {Timestamp}", timestamp);
                
                // Wait a moment for the reducer to complete and the subscription to update
                await Task.Delay(100);
                _logger.LogDebug("Waited 100ms for reducer to complete");
                
                // Find the newly created ticket
                var ticket = conn.Db.Ticket.Iter()
                    .OrderByDescending(t => t.TicketId)
                    .FirstOrDefault();
                _logger.LogInformation("Newly created ticket lookup result: {@Ticket}", ticket);

                if (ticket == null)
                {
                    _logger.LogError("Failed to find newly created ticket after reducer call");
                    return StatusCode(500, "Failed to create ticket");
                }

                _logger.LogInformation("Successfully created ticket: {@Ticket}", ticket);
                return CreatedAtAction(nameof(GetTicket), new { id = ticket.TicketId }, ticket);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ticket: {ErrorMessage}, Stack trace: {StackTrace}", ex.Message, ex.StackTrace);
                return StatusCode(500, "An error occurred while creating the ticket");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTicket(uint id, [FromBody] UpdateTicketModel model)
        {
            _logger.LogInformation("PUT /api/Tickets/{TicketId} - Update ticket request received for ID {TicketId} with data: {@UpdateModel}", 
                id, model);
            
            if (!IsAdmin())
            {
                _logger.LogWarning("Unauthorized attempt to update ticket {TicketId} by non-admin user", id);
                return Forbid();
            }

            try
            {
                var conn = _spacetimeService.GetConnection();
                _logger.LogDebug("Database connection established successfully for updating ticket {TicketId}", id);
                
                var ticket = conn.Db.Ticket.TicketId.Find(id);
                _logger.LogInformation("Existing ticket lookup result for ID {TicketId}: {@Ticket}", id, ticket);
                
                if (ticket == null)
                {
                    _logger.LogWarning("Ticket {TicketId} not found for update operation", id);
                    return NotFound();
                }

                if (model.RouteId.HasValue)
                {
                    var route = conn.Db.Route.RouteId.Find(model.RouteId.Value);
                    _logger.LogInformation("Route lookup result for ID {RouteId}: {@Route}", model.RouteId.Value, route);
                    
                    if (route == null)
                    {
                        _logger.LogWarning("Invalid route ID {RouteId} provided for ticket update", model.RouteId.Value);
                        return BadRequest("Invalid route ID");
                    }
                }
                
                // Check if the new seat number is already taken, if provided
                if (model.SeatNumber.HasValue && model.SeatNumber.Value != ticket.SeatNumber)
                {
                    var existingTicket = conn.Db.Ticket.Iter()
                        .FirstOrDefault(t => t.RouteId == (model.RouteId ?? ticket.RouteId) && t.SeatNumber == model.SeatNumber.Value);
                    _logger.LogInformation("Seat availability check for route {RouteId} and seat {SeatNumber}: {@ExistingTicket}", 
                        model.RouteId ?? ticket.RouteId, model.SeatNumber.Value, existingTicket);
                    
                    if (existingTicket != null)
                    {
                        _logger.LogWarning("Seat {SeatNumber} on route {RouteId} is already taken by ticket {TicketId}", 
                            model.SeatNumber.Value, model.RouteId ?? ticket.RouteId, existingTicket.TicketId);
                        return BadRequest("Seat is already taken");
                    }
                }

                // Get user login from JWT token
                _logger.LogDebug("JWT identity name: {IdentityName}", User.Identity?.Name);
                var userLogin = User.Identity?.Name;
                if (string.IsNullOrEmpty(userLogin))
                {
                    _logger.LogWarning("User login not found in JWT token. Identity: {@Identity}", 
                        new { IsAuthenticated = User.Identity?.IsAuthenticated, AuthType = User.Identity?.AuthenticationType });
                    return Unauthorized("User identity could not be determined");
                }
                
                // Get user identity from login
                var userIdentity = await _authService.GetUserIdentityByLoginAsync(userLogin);
                _logger.LogInformation("User identity lookup result for login {UserLogin}: {UserIdentity}", userLogin, userIdentity);
                
                if (userIdentity == null)
                {
                    _logger.LogWarning("User identity not found for login: {Login}", userLogin);
                    return Unauthorized("User identity could not be determined");
                }
                
                _logger.LogInformation("Updating ticket {TicketId} with parameters: RouteId={RouteId}, Seat={Seat}, Price={Price}, Payment={Payment}, IsActive={IsActive}, UserIdentity={UserIdentity}", 
                    id, model.RouteId ?? ticket.RouteId, model.SeatNumber ?? ticket.SeatNumber, 
                    model.TicketPrice ?? ticket.TicketPrice, model.PaymentMethod ?? ticket.PaymentMethod, 
                    model.IsActive ?? ticket.IsActive, userIdentity);

                // Call the UpdateTicket reducer with the user identity
                conn.Reducers.UpdateTicket(
                    id, 
                    model.RouteId ?? ticket.RouteId, 
                    model.SeatNumber ?? ticket.SeatNumber, 
                    model.TicketPrice ?? ticket.TicketPrice, 
                    model.PaymentMethod ?? ticket.PaymentMethod, 
                    model.IsActive ?? ticket.IsActive,
                    userIdentity);

                _logger.LogInformation("Successfully called UpdateTicket reducer for ticket {TicketId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating ticket {TicketId}: {ErrorMessage}, Stack trace: {StackTrace}", 
                    id, ex.Message, ex.StackTrace);
                return StatusCode(500, "An error occurred while updating the ticket");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTicket(uint id)
        {
            _logger.LogInformation("DELETE /api/Tickets/{TicketId} - Delete ticket request received for ID: {TicketId}", id);
            
            if (!IsAdmin())
            {
                _logger.LogWarning("Unauthorized attempt to delete ticket {TicketId} by non-admin user", id);
                return Forbid();
            }

            try
            {
                var conn = _spacetimeService.GetConnection();
                _logger.LogDebug("Database connection established successfully for deleting ticket {TicketId}", id);
                
                var ticket = conn.Db.Ticket.TicketId.Find(id);
                _logger.LogInformation("Ticket lookup result for ID {TicketId}: {@Ticket}", id, ticket);
                
                if (ticket == null)
                {
                    _logger.LogWarning("Ticket {TicketId} not found for delete operation", id);
                    return NotFound();
                }

                // Check if ticket is sold
                var sale = conn.Db.Sale.Iter().FirstOrDefault(s => s.TicketId == id);
                _logger.LogInformation("Sale lookup result for ticket {TicketId}: {@Sale}", id, sale);
                
                if (sale != null)
                {
                    _logger.LogWarning("Cannot delete ticket {TicketId} as it is already sold. Sale details: {@Sale}", id, sale);
                    return BadRequest("Cannot delete a sold ticket");
                }

                // Get user login from JWT token
                _logger.LogDebug("JWT identity name: {IdentityName}", User.Identity?.Name);
                var userLogin = User.Identity?.Name;
                if (string.IsNullOrEmpty(userLogin))
                {
                    _logger.LogWarning("User login not found in JWT token. Identity: {@Identity}", 
                        new { IsAuthenticated = User.Identity?.IsAuthenticated, AuthType = User.Identity?.AuthenticationType });
                    return Unauthorized("User identity could not be determined");
                }
                
                // Get user identity from login
                var userIdentity = await _authService.GetUserIdentityByLoginAsync(userLogin);
                _logger.LogInformation("User identity lookup result for login {UserLogin}: {UserIdentity}", userLogin, userIdentity);
                
                if (userIdentity == null)
                {
                    _logger.LogWarning("User identity not found for login: {Login}", userLogin);
                    return Unauthorized("User identity could not be determined");
                }
                
                _logger.LogInformation("Deleting ticket {TicketId} with user identity: {UserIdentity}", id, userIdentity);

                // Call the DeleteTicket reducer with the user identity
                conn.Reducers.DeleteTicket(id, userIdentity);

                _logger.LogInformation("Successfully called DeleteTicket reducer for ticket {TicketId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting ticket {TicketId}: {ErrorMessage}, Stack trace: {StackTrace}", 
                    id, ex.Message, ex.StackTrace);
                return StatusCode(500, "An error occurred while deleting the ticket");
            }
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<dynamic>>> SearchTickets(
            [FromQuery] uint? routeId = null,
            [FromQuery] double? minPrice = null,
            [FromQuery] double? maxPrice = null,
            [FromQuery] bool? isSold = null)
        {
            _logger.LogInformation("GET /api/Tickets/search - Searching tickets with parameters: RouteId={RouteId}, MinPrice={MinPrice}, MaxPrice={MaxPrice}, IsSold={IsSold}",
                routeId, minPrice, maxPrice, isSold);

            try
            {
                var conn = _spacetimeService.GetConnection();
                _logger.LogDebug("Database connection established successfully for ticket search");
                
                var query = conn.Db.Ticket.Iter().AsEnumerable();
                _logger.LogDebug("Initial ticket count before filtering: {Count}", conn.Db.Ticket.Iter().Count());

                if (routeId.HasValue)
                {
                    query = query.Where(t => t.RouteId == routeId.Value);
                    _logger.LogDebug("After RouteId filter, ticket count: {Count}", query.Count());
                }

                if (minPrice.HasValue)
                {
                    query = query.Where(t => t.TicketPrice >= minPrice.Value);
                    _logger.LogDebug("After MinPrice filter, ticket count: {Count}", query.Count());
                }

                if (maxPrice.HasValue)
                {
                    query = query.Where(t => t.TicketPrice <= maxPrice.Value);
                    _logger.LogDebug("After MaxPrice filter, ticket count: {Count}", query.Count());
                }

                HashSet<uint>? soldTicketIds = null;
                if (isSold.HasValue)
                {
                    soldTicketIds = conn.Db.Sale.Iter().Select(s => s.TicketId).ToHashSet();
                    _logger.LogDebug("Sold ticket IDs: {@SoldTicketIds}", soldTicketIds);
                    
                    query = isSold.Value
                        ? query.Where(t => soldTicketIds.Contains(t.TicketId))
                        : query.Where(t => !soldTicketIds.Contains(t.TicketId));
                    _logger.LogDebug("After IsSold filter, ticket count: {Count}", query.Count());
                }

                // Map results to anonymous type with Route details
                var result = query.Select(t => {
                    var route = conn.Db.Route.RouteId.Find(t.RouteId);
                    return new {
                        t.TicketId,
                        t.RouteId,
                        Route = route != null ? new {
                            route.RouteId,
                            route.StartPoint,
                            route.EndPoint,
                            route.TravelTime,
                            route.IsActive
                        } : null,
                        t.SeatNumber,
                        t.TicketPrice,
                        t.PaymentMethod,
                        PurchaseTime = DateTimeOffset.FromUnixTimeMilliseconds((long)t.PurchaseTime).DateTime,
                        t.IsActive,
                        IsSold = soldTicketIds?.Contains(t.TicketId) // Add IsSold status if filter was applied
                    };
                }).ToList();

                _logger.LogInformation("Search results count: {Count}", result.Count);
                _logger.LogInformation("FULL SEARCH RESULTS DATA: {TicketsData}", JsonSerializer.Serialize(result)); // Added JSON logging
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching tickets: {ErrorMessage}, Stack trace: {StackTrace}", 
                    ex.Message, ex.StackTrace);
                return StatusCode(500, "An error occurred while searching tickets");
            }
        }

        [HttpGet("route/{routeId}")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetTicketsByRoute(uint routeId)
        {
            _logger.LogInformation("GET /api/Tickets/route/{RouteId} - Fetching tickets for route ID: {RouteId}", routeId);

            try
            {
                var conn = _spacetimeService.GetConnection();
                _logger.LogDebug("Database connection established successfully for route {RouteId}", routeId);
                
                // First verify route exists
                var route = conn.Db.Route.RouteId.Find(routeId);
                _logger.LogInformation("Route lookup result for ID {RouteId}: {@Route}", routeId, route);
                
                if (route == null)
                {
                    _logger.LogWarning("Route {RouteId} not found", routeId);
                    return NotFound($"Route {routeId} not found");
                }

                var tickets = conn.Db.Ticket.Iter()
                    .Where(t => t.RouteId == routeId)
                    .ToList();

                // Map to anonymous type
                var result = tickets.Select(t => new {
                    t.TicketId,
                    t.RouteId,
                    // Including Route details fetched above
                    Route = new {
                        route.RouteId,
                        route.StartPoint,
                        route.EndPoint,
                        route.TravelTime,
                        route.IsActive
                    },
                    t.SeatNumber,
                    t.TicketPrice,
                    t.PaymentMethod,
                    PurchaseTime = DateTimeOffset.FromUnixTimeMilliseconds((long)t.PurchaseTime).DateTime,
                    t.IsActive
                }).ToList();

                _logger.LogInformation("Tickets for route {RouteId}: {@Tickets}", routeId, result);
                _logger.LogInformation("FULL ROUTE TICKETS DATA: {TicketsData}", JsonSerializer.Serialize(result)); // Added JSON logging
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tickets for route {RouteId}: {ErrorMessage}, Stack trace: {StackTrace}", 
                    routeId, ex.Message, ex.StackTrace);
                return StatusCode(500, "An error occurred while retrieving tickets");
            }
        }

        [HttpGet("available")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetAvailableTickets()
        {
            _logger.LogInformation("GET /api/Tickets/available - Fetching available (unsold) tickets");

            try
            {
                var conn = _spacetimeService.GetConnection();
                _logger.LogDebug("Database connection established successfully for available tickets query");
                
                var soldTicketIds = conn.Db.Sale.Iter().Select(s => s.TicketId).ToHashSet();
                _logger.LogInformation("Sold ticket IDs: {@SoldTicketIds}", soldTicketIds);
                
                var availableTickets = conn.Db.Ticket.Iter()
                    .Where(t => !soldTicketIds.Contains(t.TicketId))
                    .ToList();

                // Map to anonymous type with Route details
                var result = availableTickets.Select(t => {
                    var route = conn.Db.Route.RouteId.Find(t.RouteId);
                    return new {
                        t.TicketId,
                        t.RouteId,
                        Route = route != null ? new {
                            route.RouteId,
                            route.StartPoint,
                            route.EndPoint,
                            route.TravelTime,
                            route.IsActive
                        } : null,
                        t.SeatNumber,
                        t.TicketPrice,
                        t.PaymentMethod,
                        PurchaseTime = DateTimeOffset.FromUnixTimeMilliseconds((long)t.PurchaseTime).DateTime,
                        t.IsActive
                    };
                }).ToList();

                _logger.LogInformation("Available tickets count: {Count}", result.Count);
                _logger.LogInformation("FULL AVAILABLE TICKET DATA: {TicketsData}", JsonSerializer.Serialize(result)); // Added JSON logging
                return Ok(result); // Return mapped result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving available tickets: {ErrorMessage}, Stack trace: {StackTrace}", 
                    ex.Message, ex.StackTrace);
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