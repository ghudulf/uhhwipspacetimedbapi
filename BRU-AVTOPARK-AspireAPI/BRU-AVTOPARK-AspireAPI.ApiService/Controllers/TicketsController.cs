using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using TicketSalesApp.Services.Implementations;
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

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController, ApplicationErrorFilter]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allows both custom JWT (manual parsing) and ASP.NET Core auth (OpenIddict)
    public class TicketsController : BaseController
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<TicketsController> _logger;
        private readonly ITicketService _ticketService;
        private readonly IAuthenticationService _authService;
        private readonly IRealtimeEventBus _realtimeEventBus;

        /// <summary>
        /// Initializes a new instance of the <see cref="TicketsController"/> with the required services and logger.
        /// </summary>
        /// <param name="spacetimeService">Service providing database connections and reducers for spacetime data operations.</param>
        /// <param name="logger">Logger for controller diagnostics and operational messages.</param>
        /// <param name="ticketService">Domain service for ticket-related operations and queries.</param>
        /// <param name="authService">Service for resolving and validating user identities.</param>
        /// <param name="realtimeEventBus">Event bus used to subscribe and publish realtime ticket events.</param>
        public TicketsController(
            ISpacetimeDBService spacetimeService, 
            ILogger<TicketsController> logger, 
            ITicketService ticketService,
            IAuthenticationService authService,
            IRealtimeEventBus realtimeEventBus)
        {
            _spacetimeService = spacetimeService;
            _logger = logger;
            _ticketService = ticketService;
            _authService = authService;
            _realtimeEventBus = realtimeEventBus;
            _logger.LogInformation("TicketsController initialized with services: {@Services}", 
                new { SpacetimeDBService = spacetimeService != null, TicketService = ticketService != null, AuthService = authService != null });
        }

        /// <summary>
        /// Opens a WebSocket endpoint that streams realtime ticket CRUD events to the caller.
        /// The request must be authenticated; unauthorized requests receive a 401 response.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the realtime stream and associated operations.</param>
        [HttpGet("realtime/ws")]
        public async Task StreamRealtimeEvents(CancellationToken cancellationToken)
        {
            if (!await IsAuthenticatedAsync())
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var events = await HasPermissionAsync("tickets.view")
                ? AuthorizedEventsAsync(_realtimeEventBus.SubscribeAsync("tickets", cancellationToken), "tickets.view", cancellationToken)
                : AdminEmptyEventsAsync();
            await WebSocketEventStreamWriter.StreamCrudSessionAsync(
                HttpContext,
                events,
                HandleRealtimeCrudAsync,
                _logger,
                cancellationToken);
        }

        /// <summary>
        /// Dispatches a realtime CRUD request for tickets and returns a command-specific result object.
        /// </summary>
        /// <param name="request">The realtime CRUD request containing a command, optional id, and payload.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>
        /// An object whose shape depends on the command:
        /// - "read_all": { tickets = IEnumerable&lt;Ticket&gt; }
        /// - "read": { ticket = Ticket }
        /// - "create": operation result object including snapshot
        /// - "update": operation result object including entity and snapshot
        /// - "delete": operation result object including deletedId and snapshot
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a required id is missing for a "read" command, or when an unsupported command is provided.
        /// </exception>
        private async Task<object> HandleRealtimeCrudAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
        {
            await RequireRealtimeCommandAsync("tickets", request.Command ?? "");
            var command = (request.Command ?? string.Empty).Trim().ToLowerInvariant();

            return command switch
            {
                "read_all" => await HandleReadAllCommandAsync(),
                "read" => await HandleReadCommandAsync(request),
                "create" => await HandleCreateCommandAsync(request),
                "update" => await HandleUpdateCommandAsync(request),
                "delete" => await HandleDeleteCommandAsync(request),
                _ => throw new InvalidOperationException($"Unsupported command '{request.Command}'")
            };
        }

        /// <summary>
        /// Handle the realtime "read_all" command and return projected tickets with route details and PurchaseTime conversion matching the REST shape.
        /// </summary>
        /// <returns>An object with a `tickets` property containing all projected tickets.</returns>
        private async Task<object> HandleReadAllCommandAsync()
        {
            var conn = await new AdminLookupService(_spacetimeService).ReadAsync(true, HttpContext.RequestAborted);
            var tickets = conn.Tickets.Values.ToList();

            // Map to anonymous type matching REST endpoint projection
            var result = tickets.Select(t => {
                var route = conn.Routes.GetValueOrDefault(t.RouteId);
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

            return new { tickets = result };
        }

        /// <summary>
        /// Handle a realtime "read" command and return a single projected ticket with route details and PurchaseTime conversion matching the REST shape.
        /// </summary>
        /// <param name="request">The realtime request; its Id must be provided to identify the ticket.</param>
        /// <returns>An object with a `ticket` property containing the projected ticket, or null if not found.</returns>
        /// <exception cref="InvalidOperationException">Thrown when request.Id is not provided.</exception>
        private async Task<object> HandleReadCommandAsync(RealtimeCrudRequest request)
        {
            var id = request.Id ?? throw new InvalidOperationException("id is required for read");
            var conn = await new AdminLookupService(_spacetimeService).ReadAsync(true, HttpContext.RequestAborted);
            var ticket = conn.Tickets.GetValueOrDefault(id);

            if (ticket == null)
            {
                return new { ticket = (object?)null };
            }

            var route = conn.Routes.GetValueOrDefault(ticket.RouteId);

            // Map to anonymous type matching REST endpoint projection
            var result = new {
                ticket.TicketId,
                ticket.RouteId,
                Route = route != null ? new {
                    route.RouteId,
                    route.StartPoint,
                    route.EndPoint,
                    route.TravelTime,
                    route.IsActive
                } : null,
                ticket.SeatNumber,
                ticket.TicketPrice,
                ticket.PaymentMethod,
                PurchaseTime = DateTimeOffset.FromUnixTimeMilliseconds((long)ticket.PurchaseTime).DateTime,
                ticket.IsActive
            };

            return new { ticket = result };
        }

        /// <summary>
        /// Process a realtime "create" CRUD request to create a ticket and return the operation result with a full ticket snapshot.
        /// </summary>
        /// <param name="request">The incoming realtime CRUD request whose payload must deserialize to <see cref="CreateTicketModel"/>.</param>
        /// <returns>An object with properties: `operation` (string "create"), `success` (boolean indicating if creation succeeded), and `snapshot` (the current list of all tickets).</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin, when the user identity cannot be determined from claims, or when the user identity cannot be resolved.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request payload is missing or cannot be deserialized into a <see cref="CreateTicketModel"/>.</exception>
        private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
        {
            if (!await HasPermissionAsync("tickets.create"))
            {
                throw new UnauthorizedAccessException("Admin role required");
            }

            var model = request.Payload?.Deserialize<CreateTicketModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for create");

            var success = await AdminBooleanAsync("tickets.create", new CreateTicketCommand(model.RouteId, model.TicketPrice, model.SeatNumber, model.PaymentMethod), request);
            if (!await HasPermissionAsync("tickets.view")) return new { operation = "create", success };

            // Get projected snapshot matching REST endpoint shape
            var conn = await new AdminLookupService(_spacetimeService).ReadAsync(true, HttpContext.RequestAborted);
            var tickets = conn.Tickets.Values.ToList();
            var snapshot = tickets.Select(t => {
                var route = conn.Routes.GetValueOrDefault(t.RouteId);
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

            return new { operation = "create", success, snapshot };
        }

        /// <summary>
        /// Processes an "update" realtime CRUD request, applies ticket updates, and returns the updated entity plus a full snapshot of tickets.
        /// </summary>
        /// <param name="request">Realtime CRUD request containing the target ticket id and a payload deserializable to <see cref="UpdateTicketModel"/>.</param>
        /// <returns>
        /// An object with the following properties:
        /// - operation: the operation name ("update"),
        /// - success: a boolean indicating whether the update succeeded,
        /// - entity: the updated ticket entity (or null if not found),
        /// - snapshot: the current list of all tickets.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller does not have the admin role.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request is missing the required id or payload for the update.</exception>
        private async Task<object> HandleUpdateCommandAsync(RealtimeCrudRequest request)
        {
            if (!await HasPermissionAsync("tickets.edit"))
            {
                throw new UnauthorizedAccessException("Admin role required");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for update");
            var model = request.Payload?.Deserialize<UpdateTicketModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for update");

            var success = await AdminBooleanAsync("tickets.update", new UpdateTicketCommand(id, model.RouteId, model.TicketPrice, model.SeatNumber, model.PaymentMethod, model.IsActive, model.ExpectedRevision ?? AdminExpectedRevision(request)), request);
            if (!await HasPermissionAsync("tickets.view")) return new { operation = "update", success, entity = (object?)null };

            var conn = await new AdminLookupService(_spacetimeService).ReadAsync(true, HttpContext.RequestAborted);
            var ticket = conn.Tickets.GetValueOrDefault(id);

            // Project entity to match REST endpoint shape
            object? projectedEntity = null;
            if (ticket != null)
            {
                var route = conn.Routes.GetValueOrDefault(ticket.RouteId);
                projectedEntity = new {
                    ticket.TicketId,
                    ticket.RouteId,
                    Route = route != null ? new {
                        route.RouteId,
                        route.StartPoint,
                        route.EndPoint,
                        route.TravelTime,
                        route.IsActive
                    } : null,
                    ticket.SeatNumber,
                    ticket.TicketPrice,
                    ticket.PaymentMethod,
                    PurchaseTime = DateTimeOffset.FromUnixTimeMilliseconds((long)ticket.PurchaseTime).DateTime,
                    ticket.IsActive
                };
            }

            // Get projected snapshot matching REST endpoint shape
            var tickets = conn.Tickets.Values.ToList();
            var snapshot = tickets.Select(t => {
                var route = conn.Routes.GetValueOrDefault(t.RouteId);
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

            return new { operation = "update", success, entity = projectedEntity, snapshot };
        }

        /// <summary>
        /// Handle a realtime "delete" CRUD request by deleting the specified ticket and returning an updated snapshot.
        /// </summary>
        /// <param name="request">The realtime CRUD request containing the ticket Id to delete.</param>
        /// <returns>An object containing the operation name ("delete"), a boolean success flag, the deletedId, and a snapshot of all tickets.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an administrator.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request does not specify an Id for deletion.</exception>
        private async Task<object> HandleDeleteCommandAsync(RealtimeCrudRequest request)
        {
            if (!await HasPermissionAsync("tickets.delete"))
            {
                throw new UnauthorizedAccessException("Admin role required");
            }

            var id = request.Id ?? throw new InvalidOperationException("id is required for delete");
            var success = await AdminBooleanAsync("tickets.delete", new DeleteTicketCommand(id, AdminExpectedRevision(request)), request);
            if (!await HasPermissionAsync("tickets.view")) return new { operation = "delete", success, deletedId = id };

            // Get projected snapshot matching REST endpoint shape
            var conn = await new AdminLookupService(_spacetimeService).ReadAsync(true, HttpContext.RequestAborted);
            var tickets = conn.Tickets.Values.ToList();
            var snapshot = tickets.Select(t => {
                var route = conn.Routes.GetValueOrDefault(t.RouteId);
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

            return new { operation = "delete", success, deletedId = id, snapshot };
        }

        /// <summary>
        /// Retrieve all tickets with their associated route details and mapped presentation fields.
        /// </summary>
        /// <returns>
        /// A collection of ticket objects where each item contains:
        /// - TicketId
        /// - RouteId
        /// - Route: an object with RouteId, StartPoint, EndPoint, TravelTime, and IsActive, or null if the route is missing
        /// - SeatNumber
        /// - TicketPrice
        /// - PaymentMethod
        /// - PurchaseTime: the ticket purchase time converted from Unix milliseconds to a DateTime
        /// - IsActive
        /// </returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetTickets()
        {
            _logger.LogInformation("GET /api/Tickets - Fetching all tickets with route information");
            try
            {
                var conn = await new AdminLookupService(_spacetimeService).ReadAsync(true, HttpContext.RequestAborted);
                _logger.LogDebug("Database connection established successfully");
                
                var tickets = conn.Tickets.Values.ToList();
                
                // Map to anonymous type with Route details
                var result = tickets.Select(t => {
                    var route = conn.Routes.GetValueOrDefault(t.RouteId);
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
            catch (Exception ex) when (!IsApplicationFailure(ex))
            {
                _logger.LogError(ex, "Error retrieving tickets: {ErrorMessage}", ex.Message);
                return StatusCode(500, "An error occurred while retrieving tickets");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetTicket(uint id)
        {
            _logger.LogInformation("GET /api/Tickets/{TicketId} - Fetching ticket", id);
            try
            {
                var conn = await new AdminLookupService(_spacetimeService).ReadAsync(true, HttpContext.RequestAborted);
                _logger.LogDebug("Database connection established successfully for ticket ID: {TicketId}", id);
                
                var ticket = conn.Tickets.GetValueOrDefault(id);
                _logger.LogInformation("Ticket lookup result: {@Ticket}", ticket);

                if (ticket == null)
                {
                    _logger.LogWarning("Ticket {TicketId} not found in database", id);
                    return NotFound();
                }

                var route = conn.Routes.GetValueOrDefault(ticket.RouteId);

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
            catch (Exception ex) when (!IsApplicationFailure(ex))
            {
                _logger.LogError(ex, "Error retrieving ticket {TicketId}: {ErrorMessage}", id, ex.Message);
                return StatusCode(500, "An error occurred while retrieving the ticket");
            }
        }

        [HttpPost]
        public async Task<ActionResult<Ticket>> CreateTicket([FromBody] CreateTicketModel model)
        {
            var result = await AdminMutationAsync("tickets.create", new CreateTicketCommand(model.RouteId, model.TicketPrice, model.SeatNumber, model.PaymentMethod));
            var ticket = await _spacetimeService.ReadAsync(c => c.Db.Ticket.TicketId.Find(result.EntityId), HttpContext.RequestAborted);
            return CreatedAtAction(nameof(GetTicket), new { id = result.EntityId }, ticket);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTicket(uint id, [FromBody] UpdateTicketModel model)
        {
            await AdminMutationAsync("tickets.update", new UpdateTicketCommand(id, model.RouteId, model.TicketPrice, model.SeatNumber, model.PaymentMethod, model.IsActive, model.ExpectedRevision ?? AdminExpectedRevision()));
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTicket(uint id)
        {
            await AdminMutationAsync("tickets.delete", new DeleteTicketCommand(id, AdminExpectedRevision()));
            return NoContent();
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
                var conn = await new AdminLookupService(_spacetimeService).ReadAsync(true, HttpContext.RequestAborted);
                _logger.LogDebug("Database connection established successfully for ticket search");
                
                var query = conn.Tickets.Values.AsEnumerable();
                _logger.LogDebug("Initial ticket count before filtering: {Count}", conn.Tickets.Values.Count());

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
                    soldTicketIds = conn.Sales.Select(s => s.TicketId).ToHashSet();
                    _logger.LogDebug("Sold ticket IDs: {@SoldTicketIds}", soldTicketIds);
                    
                    query = isSold.Value
                        ? query.Where(t => soldTicketIds.Contains(t.TicketId))
                        : query.Where(t => !soldTicketIds.Contains(t.TicketId));
                    _logger.LogDebug("After IsSold filter, ticket count: {Count}", query.Count());
                }

                // Map results to anonymous type with Route details
                var result = query.Select(t => {
                    var route = conn.Routes.GetValueOrDefault(t.RouteId);
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
            catch (Exception ex) when (!IsApplicationFailure(ex))
            {
                _logger.LogError(ex, "Error searching tickets: {ErrorMessage}, Stack trace: {StackTrace}", 
                    ex.Message, ex.StackTrace);
                return StatusCode(500, "An error occurred while searching tickets");
            }
        }

        [HttpGet("route/{routeId}")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetTicketsByRoute(uint routeId)
        {
            _logger.LogInformation("GET /api/Tickets/route/{RouteId} - Fetching tickets for route", routeId);

            try
            {
                var conn = await new AdminLookupService(_spacetimeService).ReadAsync(true, HttpContext.RequestAborted);
                _logger.LogDebug("Database connection established successfully for route {RouteId}", routeId);
                
                // First verify route exists
                var route = conn.Routes.GetValueOrDefault(routeId);
                _logger.LogInformation("Route lookup result for ID {RouteId}: {@Route}", routeId, route);
                
                if (route == null)
                {
                    _logger.LogWarning("Route {RouteId} not found", routeId);
                    return NotFound($"Route {routeId} not found");
                }

                var tickets = conn.Tickets.Values
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
            catch (Exception ex) when (!IsApplicationFailure(ex))
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
                var conn = await new AdminLookupService(_spacetimeService).ReadAsync(true, HttpContext.RequestAborted);
                _logger.LogDebug("Database connection established successfully for available tickets query");
                
                var soldTicketIds = conn.Sales.Select(s => s.TicketId).ToHashSet();
                _logger.LogInformation("Sold ticket IDs: {@SoldTicketIds}", soldTicketIds);
                
                var availableTickets = conn.Tickets.Values
                    .Where(t => !soldTicketIds.Contains(t.TicketId))
                    .ToList();

                // Map to anonymous type with Route details
                var result = availableTickets.Select(t => {
                    var route = conn.Routes.GetValueOrDefault(t.RouteId);
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
            catch (Exception ex) when (!IsApplicationFailure(ex))
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
        public ulong? ExpectedRevision { get; set; }
        public uint? RouteId { get; set; }
        public double? TicketPrice { get; set; }
        public uint? SeatNumber { get; set; }
        public string? PaymentMethod { get; set; }
        public bool? IsActive { get; set; }
    }
}
