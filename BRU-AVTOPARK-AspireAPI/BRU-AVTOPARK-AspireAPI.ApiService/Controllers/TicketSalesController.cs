using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using TicketSalesApp.Services.Interfaces;
    using System.IdentityModel.Tokens.Jwt;
    using Serilog;
    using Microsoft.IdentityModel.Tokens;
    using System.Security.Claims;
    using System.Text;
    using Microsoft.Extensions.Configuration;
    using SpacetimeDB;
    using Log = Serilog.Log;
    using System.Text.Json;

    using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

    using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

    namespace TicketSalesApp.AdminServer.Controllers
    {
        public static class DateTimeExtensions
        {
            public static ulong ToUnixTimeMilliseconds(this DateTime dateTime)
            {
                return (ulong)((DateTimeOffset)dateTime.ToUniversalTime()).ToUnixTimeMilliseconds();
            }
        }

        [ApiController]
        [Route("api/[controller]")]
        [AllowAnonymous] // Allows both custom JWT (manual parsing) and ASP.NET Core auth (OpenIddict)
        public class TicketSalesController : BaseController
        {
            private readonly ISpacetimeDBService _spacetimeService;
            private readonly ITicketSalesService _ticketSalesService;
            private readonly IConfiguration _configuration;
            private readonly IRealtimeEventBus _realtimeEventBus;
            private readonly ILogger<TicketSalesController> _logger;

            /// <summary>
            /// Initializes a new instance of <see cref="TicketSalesController"/> with its required services and utilities.
            /// </summary>
            /// <param name="spacetimeService">Service for obtaining Spacetime DB connections and operations.</param>
            /// <param name="ticketSalesService">Service providing ticket sales business logic and statistics.</param>
            /// <param name="configuration">Application configuration settings.</param>
            /// <param name="realtimeEventBus">Event bus used to subscribe and publish realtime ticket-sales events.</param>
            /// <param name="logger">Logger for controller diagnostics and operational logging.</param>
            /// <exception cref="ArgumentNullException">Thrown when any required dependency is <c>null</c>.</exception>
            public TicketSalesController(ISpacetimeDBService spacetimeService, ITicketSalesService ticketSalesService, IConfiguration configuration, IRealtimeEventBus realtimeEventBus, ILogger<TicketSalesController> logger)
            {
                _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
                _ticketSalesService = ticketSalesService ?? throw new ArgumentNullException(nameof(ticketSalesService));
                _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
                _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            

            /// <summary>
            /// Opens a WebSocket session to stream realtime ticket-sales CRUD events to an authenticated client.
            /// </summary>
            /// <param name="cancellationToken">Token used to cancel the streaming session.</param>
            /// <returns>A task representing the lifetime of the WebSocket CRUD streaming session.</returns>
            [HttpGet("realtime/ws")]
            public async Task StreamRealtimeEvents(CancellationToken cancellationToken)
            {
                if (!IsAuthenticated())
                {
                    Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await WebSocketEventStreamWriter.StreamCrudSessionAsync(
                    HttpContext,
                    _realtimeEventBus.SubscribeAsync("ticket-sales", cancellationToken),
                    HandleRealtimeCrudAsync,
                    _logger,
                    cancellationToken);
            }

            /// <summary>
            /// Processes a realtime CRUD-style request and returns the corresponding result payload.
            /// </summary>
            /// <param name="request">The incoming realtime CRUD request; its <c>Command</c> determines the action and some commands require <c>Id</c> or a payload.</param>
            /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
            /// <returns>
            /// An anonymous result object:
            /// - For "read_all": { sales = List of sale snapshots }.
            /// - For "read": { sale = single sale view }.
            /// - For "create": result from the create handler (operation, success, entity, snapshot).
            /// - For "update" / "delete": an operation result indicating not implemented.
            /// </returns>
            /// <exception cref="InvalidOperationException">
            /// Thrown when the command is unsupported or when "read" is requested without an <c>Id</c>.
            /// </exception>
            private async Task<object> HandleRealtimeCrudAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
            {
                var command = (request.Command ?? string.Empty).Trim().ToLowerInvariant();

                return command switch
                {
                    "read_all" => new { sales = BuildSalesSnapshot() },
                    "read" => new { sale = BuildSaleById(_spacetimeService.GetConnection(), request.Id ?? throw new InvalidOperationException("id is required for read")) },
                    "create" => await HandleCreateCommandAsync(request),
                    "update" => new { operation = "update", success = false, message = "Update operation is not implemented in SpacetimeDB module" },
                    "delete" => new { operation = "delete", success = false, message = "Delete operation is not implemented in SpacetimeDB module" },
                    _ => throw new InvalidOperationException($"Unsupported command '{request.Command}'")
                };
            }

            /// <summary>
            /// Handles a realtime "create" CRUD command by creating a ticket sale and returning the result and updated snapshot.
            /// </summary>
            /// <param name="request">Realtime CRUD request whose Payload must deserialize to <see cref="CreateTicketSaleModel"/> (case-insensitive).</param>
            /// <returns>
            /// An object containing:
            /// - `operation`: the string "create",
            /// - `success`: `true` if creation succeeded, `false` otherwise,
            /// - `entity`: the created sale view or `null`,
            /// - `snapshot`: the current list of sales.
            /// </returns>
            /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an administrator.</exception>
            /// <exception cref="InvalidOperationException">Thrown when the request payload is missing or cannot be deserialized to <see cref="CreateTicketSaleModel"/>.</exception>
            private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
            {
                if (!IsAdmin()) throw new UnauthorizedAccessException("Admin role required");
                var model = request.Payload?.Deserialize<CreateTicketSaleModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("payload is required for create");

                var created = ExecuteCreateSale(model);
                var result = new { operation = "create", success = created is not null, entity = created, snapshot = BuildSalesSnapshot() };

                if (created is not null)
                {
                    try
                    {
                        await _realtimeEventBus.PublishAsync(new ApiDomainEvent
                        {
                            Resource = "ticket-sales",
                            EventName = "ticket-sale.created",
                            Timestamp = DateTimeOffset.UtcNow,
                            Payload = result
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to publish realtime event for ticket-sale.created (Resource: ticket-sales, EventName: ticket-sale.created)");
                    }
                }

                return result;
            }

            /// <summary>
            /// Builds a detailed view of the ticket sale identified by the provided saleId, including nested ticket and route information when available.
            /// </summary>
            /// <param name="conn">The SpacetimeDB connection to use for database queries.</param>
            /// <returns>An anonymous object containing SaleId, SaleDate (DateTime), TicketId, TicketSoldToUser, TicketSoldToUserPhone, SellerId, and a nested Ticket object with TicketId, RouteId, TicketPrice and optional Route (RouteId, StartPoint, EndPoint); or null if the sale does not exist.</returns>
            private object? BuildSaleById(SpacetimeDBClient conn, uint saleId)
            {
                var sale = conn.Db.Sale.SaleId.Find(saleId);
                if (sale == null) return null;

                var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                var route = ticket != null ? conn.Db.Route.RouteId.Find(ticket.RouteId) : null;

                return new
                {
                    SaleId = sale.SaleId,
                    SaleDate = DateTimeOffset.FromUnixTimeMilliseconds((long)sale.SaleDate).DateTime,
                    TicketId = sale.TicketId,
                    TicketSoldToUser = sale.TicketSoldToUser,
                    TicketSoldToUserPhone = sale.TicketSoldToUserPhone,
                    SellerId = sale.SellerId?.ToString(),
                    Ticket = ticket != null ? new
                    {
                        TicketId = ticket.TicketId,
                        RouteId = ticket.RouteId,
                        TicketPrice = ticket.TicketPrice,
                        Route = route != null ? new { route.RouteId, route.StartPoint, route.EndPoint } : null
                    } : null
                };
            }

            /// <summary>
            /// Builds a snapshot list of all ticket sales with their detailed sale, ticket, and route information.
            /// </summary>
            /// <returns>A list of objects where each item represents a sale with related ticket and route details; sales that cannot be resolved are omitted.</returns>
            private List<object> BuildSalesSnapshot()
            {
                var conn = _spacetimeService.GetConnection();
                return conn.Db.Sale.Iter()
                    .Select(s => BuildSaleById(conn, s.SaleId))
                    .Where(s => s != null)
                    .Cast<object>()
                    .ToList();
            }

            /// <summary>
            /// Creates a sale for the specified ticket after validation and returns the created sale representation.
            /// </summary>
            /// <param name="model">Model containing sale data (TicketId, SaleDate, TicketSoldToUser, TicketSoldToUserPhone).</param>
            /// <returns>The created sale view object produced by BuildSaleById, or null if the newly created sale could not be retrieved.</returns>
            /// <exception cref="InvalidOperationException">Thrown if the ticket does not exist, the ticket is already sold, or the seller profile cannot be found.</exception>
            /// <exception cref="UnauthorizedAccessException">Thrown if the caller's identity claim is missing.</exception>
            private object? ExecuteCreateSale(CreateTicketSaleModel model)
            {
                var conn = _spacetimeService.GetConnection();

                var ticket = conn.Db.Ticket.TicketId.Find((uint)model.TicketId);
                if (ticket == null)
                {
                    throw new InvalidOperationException($"Ticket {model.TicketId} does not exist");
                }

                var existingSales = conn.Db.Sale.Iter().Where(s => s.TicketId == (uint)model.TicketId).ToList();
                if (existingSales.Any())
                {
                    throw new InvalidOperationException($"Ticket {model.TicketId} already sold");
                }

                // Extract login from validated bearer/token data path (same as BaseController)
                string? identityClaim = null;
                if (User?.Identity?.IsAuthenticated == true)
                {
                    identityClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
                }

                if (string.IsNullOrWhiteSpace(identityClaim))
                {
                    // Fallback to parsing JWT from Authorization header
                    var authHeader = Request.Headers["Authorization"].ToString();
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                    {
                        var token = authHeader.Substring("Bearer ".Length);
                        var tokenHandler = new JwtSecurityTokenHandler();
                        if (tokenHandler.CanReadToken(token))
                        {
                            var jwtToken = tokenHandler.ReadJwtToken(token);
                            identityClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub" || c.Type == ClaimTypes.NameIdentifier)?.Value;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(identityClaim))
                {
                    throw new UnauthorizedAccessException("Identity claim missing");
                }

                // Validate authenticated user exists in UserProfile (used for validation only)
                var seller = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.UserId.ToString() == identityClaim || u.Login == identityClaim);
                if (seller == null)
                {
                    throw new InvalidOperationException("Seller not found");
                }

                conn.Reducers.CreateSale((uint)model.TicketId, model.TicketSoldToUser ?? "ФИЗ.ПРОДАЖА", model.TicketSoldToUserPhone ?? string.Empty, "POS", null);

                var newSale = conn.Db.Sale.Iter().Where(s => s.TicketId == (uint)model.TicketId).OrderByDescending(s => s.SaleId).FirstOrDefault();
                return newSale == null ? null : BuildSaleById(conn, newSale.SaleId);
            }

            /// <summary>
            /// Retrieves all ticket sales and their related ticket and route details.
            /// </summary>
            /// <returns>
            /// An OK response containing a list of sales where each item includes:
            /// SaleId, SaleDate, TicketId, TicketSoldToUser, TicketSoldToUserPhone, SellerId,
            /// and an optional nested Ticket object (TicketId, RouteId, TicketPrice) with an optional Route (RouteId, StartPoint, EndPoint).
            /// Returns a 500 status with an error message if an exception occurs.
            /// </returns>
            [HttpGet]
            public ActionResult<IEnumerable<dynamic>> GetTicketSales()
            {
                try
                {
                    Log.Information("Fetching all ticket sales");
                    
                    var conn = _spacetimeService.GetConnection();
                    Log.Debug("Database connection established successfully");
                    
                    // Get all sales from SpacetimeDB
                    var sales = conn.Db.Sale.Iter().ToList();
                    Log.Information("Raw sales data retrieved from database: {@Sales}", sales);
                    
                    // Convert to a list of dynamic objects with necessary properties
                    var result = sales.Select(s => {
                        var ticket = conn.Db.Ticket.TicketId.Find(s.TicketId);
                        Log.Debug("Found ticket for sale {SaleId}: {@Ticket}", s.SaleId, ticket);
                        
                        var route = ticket != null ? conn.Db.Route.RouteId.Find(ticket.RouteId) : null;
                        Log.Debug("Found route for ticket {TicketId}: {@Route}", ticket?.TicketId, route);
                        
                        return new {
                            SaleId = s.SaleId,
                            SaleDate = DateTimeOffset.FromUnixTimeMilliseconds((long)s.SaleDate).DateTime,
                            TicketId = s.TicketId,
                            TicketSoldToUser = s.TicketSoldToUser,
                            TicketSoldToUserPhone = s.TicketSoldToUserPhone,
                            SellerId = s.SellerId?.ToString(),
                            Ticket = ticket != null ? new {
                                TicketId = ticket.TicketId,
                                RouteId = ticket.RouteId,
                                TicketPrice = ticket.TicketPrice,
                                Route = route != null ? new {
                                    RouteId = route.RouteId,
                                    StartPoint = route.StartPoint,
                                    EndPoint = route.EndPoint
                                } : null
                            } : null
                        };
                    }).ToList();
                    
                    Log.Information("Processed ticket sales data: {@Result}", result);
                    Log.Debug("Retrieved {SalesCount} ticket sales with full details", result.Count);
                    return Ok(result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving ticket sales: {ErrorMessage}", ex.Message);
                    return StatusCode(500, new { message = "An error occurred while retrieving ticket sales" });
                }
            }

            [HttpGet("{id}")]
            public ActionResult<dynamic> GetTicketSale(long id)
            {
                try
                {
                    Log.Information("Fetching ticket sale with ID {SaleId}", id);
                    
                    var conn = _spacetimeService.GetConnection();
                    Log.Debug("Database connection established successfully for fetching sale {SaleId}", id);
                    
                    // Find sale by ID
                    var sale = conn.Db.Sale.SaleId.Find((uint)id);
                    Log.Information("Retrieved sale data for ID {SaleId}: {@Sale}", id, sale);
                    
                    if (sale == null)
                    {
                        Log.Warning("Ticket sale with ID {SaleId} not found", id);
                        return NotFound();
                    }
                    
                    // Get related ticket and route
                    var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                    Log.Information("Retrieved ticket data for sale {SaleId}: {@Ticket}", id, ticket);
                    
                    var route = ticket != null ? conn.Db.Route.RouteId.Find(ticket.RouteId) : null;
                    Log.Information("Retrieved route data for ticket {TicketId}: {@Route}", ticket?.TicketId, route);
                    
                    // Create response object
                    var result = new {
                        SaleId = sale.SaleId,
                        SaleDate = DateTimeOffset.FromUnixTimeMilliseconds((long)sale.SaleDate).DateTime,
                        TicketId = sale.TicketId,
                        TicketSoldToUser = sale.TicketSoldToUser,
                        TicketSoldToUserPhone = sale.TicketSoldToUserPhone,
                        SellerId = sale.SellerId?.ToString(),
                        Ticket = ticket != null ? new {
                            TicketId = ticket.TicketId,
                            RouteId = ticket.RouteId,
                            TicketPrice = ticket.TicketPrice,
                            Route = route != null ? new {
                                RouteId = route.RouteId,
                                StartPoint = route.StartPoint,
                                EndPoint = route.EndPoint
                            } : null
                        } : null
                    };
                    
                    Log.Information("Returning ticket sale response for ID {SaleId}: {@Result}", id, result);
                    Log.Debug("Successfully retrieved ticket sale with ID {SaleId}", id);
                    return Ok(result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving ticket sale with ID {SaleId}: {ErrorMessage}", id, ex.Message);
                    return StatusCode(500, new { message = $"An error occurred while retrieving ticket sale with ID {id}" });
                }
            }

            [HttpPost]
            public ActionResult<dynamic> CreateTicketSale([FromBody] CreateTicketSaleModel model)
            {
                Log.Information("Create ticket sale request received with data: {@Model}", model);
                
                if (!IsAdmin())
                {
                    Log.Warning("Unauthorized attempt to create ticket sale by non-admin user");
                    return Forbid();
                }

                try
                {
                    Log.Information("Creating new ticket sale for ticket ID {TicketId}", model.TicketId);
                    
                    var conn = _spacetimeService.GetConnection();
                    Log.Debug("Database connection established successfully for creating sale");
                    
                    // Check if ticket exists
                    var ticket = conn.Db.Ticket.TicketId.Find((uint)model.TicketId);
                    Log.Information("Ticket lookup result for ID {TicketId}: {@Ticket}", model.TicketId, ticket);
                    
                    if (ticket == null)
                    {
                        Log.Warning("Invalid ticket ID {TicketId} provided for sale creation", model.TicketId);
                        return BadRequest("Invalid ticket ID");
                    }
                    
                    // Check if ticket is already sold
                    var existingSales = conn.Db.Sale.Iter().Where(s => s.TicketId == (uint)model.TicketId).ToList();
                    Log.Information("Existing sales for ticket {TicketId}: {@ExistingSales}", model.TicketId, existingSales);
                    
                    if (existingSales.Any())
                    {
                        Log.Warning("Ticket with ID {TicketId} is already sold. Existing sales: {@ExistingSales}", model.TicketId, existingSales);
                        return BadRequest("Ticket is already sold");
                    }
                    
                    // Get seller identity from token
                    var authHeader = Request.Headers["Authorization"].ToString();
                    Log.Debug("Authorization header: {AuthHeader}", authHeader);
                    
                    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                    {
                        Log.Warning("Missing or invalid Authorization header: {AuthHeader}", authHeader);
                        return Unauthorized(new { message = "Missing or invalid Authorization header" });
                    }
                    
                    var token = authHeader.Substring("Bearer ".Length);
                    var tokenHandler = new JwtSecurityTokenHandler();
                    
                    if (!tokenHandler.CanReadToken(token))
                    {
                        Log.Warning("Invalid JWT token format: {Token}", token);
                        return Unauthorized(new { message = "Invalid token format" });
                    }
                    
                    var jwtToken = tokenHandler.ReadJwtToken(token);
                    Log.Debug("JWT token claims: {@Claims}", jwtToken.Claims.Select(c => new { Type = c.Type, Value = c.Value }));
                    
                    var usernameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name" || c.Type == "name");
                    
                    if (usernameClaim == null)
                    {
                        Log.Warning("No username claim found in validated token. All claims: {@Claims}", 
                            jwtToken.Claims.Select(c => new { Type = c.Type, Value = c.Value }));
                        return Unauthorized(new { message = "Invalid token: no username claim found" });
                    }

                    // Find user by login or UserId
                    var seller = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.UserId.ToString() == usernameClaim.Value || u.Login == usernameClaim.Value);
                    Log.Information("Seller lookup result for username {Username}: {@Seller}", usernameClaim.Value, seller);
                    
                    if (seller == null)
                    {
                        Log.Warning("User from token not found in database: {Username}", usernameClaim.Value);
                        return NotFound(new { message = $"User '{usernameClaim.Value}' not found" });
                    }
                    
                    // Create sale using reducer
                    var buyerName = model.TicketSoldToUser ?? "ФИЗ.ПРОДАЖА";
                    var buyerPhone = model.TicketSoldToUserPhone ?? "";
                    
                    Log.Information("Calling CreateSale reducer with parameters: TicketId={TicketId}, BuyerName={BuyerName}, BuyerPhone={BuyerPhone}, Location=POS", 
                        model.TicketId, buyerName, buyerPhone);
                    
                    // Call the CreateSale reducer
                    conn.Reducers.CreateSale(
                        (uint)model.TicketId, 
                        buyerName, 
                        buyerPhone,
                        "POS", // Default sale location
                        null // No notes
                    );
                    
                    // Find the newly created sale
                    var newSale = conn.Db.Sale.Iter()
                        .Where(s => s.TicketId == (uint)model.TicketId)
                        .OrderByDescending(s => s.SaleId)
                        .FirstOrDefault();
                    
                    Log.Information("Newly created sale: {@NewSale}", newSale);
                    
                    if (newSale == null)
                    {
                        Log.Warning("Sale was not created properly. No sale found for ticket {TicketId}", model.TicketId);
                        return StatusCode(500, new { message = "Failed to create sale" });
                    }
                    
                    // Create response object
                    var result = new {
                        SaleId = newSale.SaleId,
                        SaleDate = DateTimeOffset.FromUnixTimeMilliseconds((long)newSale.SaleDate).DateTime,
                        TicketId = newSale.TicketId,
                        TicketSoldToUser = newSale.TicketSoldToUser,
                        TicketSoldToUserPhone = newSale.TicketSoldToUserPhone,
                        SellerId = newSale.SellerId?.ToString()
                    };
                    
                    Log.Information("Successfully created ticket sale with ID {SaleId} for user {User} with phone {Phone}. Full result: {@Result}", 
                        newSale.SaleId, newSale.TicketSoldToUser, newSale.TicketSoldToUserPhone, result);
                    
                    return CreatedAtAction(nameof(GetTicketSale), new { id = newSale.SaleId }, result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error creating ticket sale: {ErrorMessage}", ex.Message);
                    return StatusCode(500, new { message = "An error occurred while creating the ticket sale" });
                }
            }

            [HttpPut("{id}")]
            public IActionResult UpdateTicketSale(long id, [FromBody] UpdateTicketSaleModel model)
            {
                Log.Information("Update ticket sale request received for ID {SaleId} with data: {@Model}", id, model);
                
                if (!IsAdmin())
                {
                    Log.Warning("Unauthorized attempt to update ticket sale by non-admin user");
                    return Forbid();
                }

                try
                {
                    Log.Information("Updating ticket sale with ID {SaleId}", id);
                    
                    var conn = _spacetimeService.GetConnection();
                    Log.Debug("Database connection established successfully for updating sale {SaleId}", id);
                    
                    // Find sale by ID
                    var sale = conn.Db.Sale.SaleId.Find((uint)id);
                    Log.Information("Existing sale data for ID {SaleId}: {@Sale}", id, sale);
                    
                    if (sale == null)
                    {
                        Log.Warning("Ticket sale with ID {SaleId} not found for update", id);
                        return NotFound();
                    }
                    
                    // Note: SpacetimeDB doesn't have an UpdateSale reducer yet
                    // This would need to be implemented in the SpacetimeDB module
                    
                    Log.Warning("UpdateTicketSale is not implemented in the SpacetimeDB module. Sale ID: {SaleId}, Requested changes: {@Model}", id, model);
                    return StatusCode(501, new { message = "Update operation is not implemented" });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error updating ticket sale with ID {SaleId}: {ErrorMessage}", id, ex.Message);
                    return StatusCode(500, new { message = $"An error occurred while updating ticket sale with ID {id}" });
                }
            }

            [HttpDelete("{id}")]
            public IActionResult DeleteTicketSale(long id)
            {
                Log.Information("Delete ticket sale request received for ID {SaleId}", id);
                
                if (!IsAdmin())
                {
                    Log.Warning("Unauthorized attempt to delete ticket sale by non-admin user");
                    return Forbid();
                }

                try
                {
                    Log.Information("Deleting ticket sale with ID {SaleId}", id);
                    
                    var conn = _spacetimeService.GetConnection();
                    Log.Debug("Database connection established successfully for deleting sale {SaleId}", id);
                    
                    // Find sale by ID
                    var sale = conn.Db.Sale.SaleId.Find((uint)id);
                    Log.Information("Sale to be deleted with ID {SaleId}: {@Sale}", id, sale);
                    
                    if (sale == null)
                    {
                        Log.Warning("Ticket sale with ID {SaleId} not found for deletion", id);
                        return NotFound();
                    }
                    
                    // Note: SpacetimeDB doesn't have a DeleteSale reducer yet
                    // This would need to be implemented in the SpacetimeDB module
                    
                    Log.Warning("DeleteTicketSale is not implemented in the SpacetimeDB module. Attempted to delete sale: {@Sale}", sale);
                    return StatusCode(501, new { message = "Delete operation is not implemented" });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error deleting ticket sale with ID {SaleId}: {ErrorMessage}", id, ex.Message);
                    return StatusCode(500, new { message = $"An error occurred while deleting ticket sale with ID {id}" });
                }
            }

            [HttpGet("statistics/income")]
            public async Task<ActionResult<decimal>> GetTotalIncome(int year, int month)
            {
                Log.Information("Fetching total income for {Year}-{Month}", year, month);
                
                try
                {
                    var income = await _ticketSalesService.GetTotalIncomeAsync(year, month);
                    Log.Information("Total income for {Year}-{Month}: {Income}", year, month, income);
                    return income;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving total income for {Year}-{Month}: {ErrorMessage}", year, month, ex.Message);
                    return StatusCode(500, new { message = $"An error occurred while retrieving total income for {year}-{month}" });
                }
            }

            [HttpGet("statistics/top-transports")]
            public async Task<ActionResult<List<TransportStatistic>>> GetTopTransports(int year, int month)
            {
                Log.Information("Fetching top transports for {Year}-{Month}", year, month);
                
                try
                {
                    var transports = await _ticketSalesService.GetTopTransportsAsync(year, month);
                    Log.Information("Top transports for {Year}-{Month}: {@Transports}", year, month, transports);
                    return transports;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving top transports for {Year}-{Month}: {ErrorMessage}", year, month, ex.Message);
                    return StatusCode(500, new { message = $"An error occurred while retrieving top transports for {year}-{month}" });
                }
            }

            [HttpGet("search")]
            public ActionResult<IEnumerable<dynamic>> SearchSales(
                [FromQuery] DateTime? startDate = null,
                [FromQuery] DateTime? endDate = null,
                [FromQuery] decimal? minPrice = null,
                [FromQuery] decimal? maxPrice = null,
                [FromQuery] string? soldToUser = null)
            {
                try
                {
                    Log.Information("Searching sales with parameters: StartDate={StartDate}, EndDate={EndDate}, MinPrice={MinPrice}, MaxPrice={MaxPrice}, SoldToUser={SoldToUser}",
                        startDate, endDate, minPrice, maxPrice, soldToUser);
                    
                    var conn = _spacetimeService.GetConnection();
                    Log.Debug("Database connection established successfully for searching sales");
                    
                    // Get all sales
                    var allSales = conn.Db.Sale.Iter().ToList();
                    Log.Debug("All sales retrieved from database: {@AllSales}", allSales);
                    
                    var query = allSales.AsEnumerable();
                    
                    // Apply filters
                    if (startDate.HasValue)
                    {
                        var startTimestamp = startDate.Value.ToUnixTimeMilliseconds();
                        Log.Debug("Filtering sales by start date: {StartDate} (timestamp: {StartTimestamp})", startDate, startTimestamp);
                        query = query.Where(s => s.SaleDate >= startTimestamp);
                    }
                    
                    if (endDate.HasValue)
                    {
                        var endTimestamp = endDate.Value.ToUnixTimeMilliseconds();
                        Log.Debug("Filtering sales by end date: {EndDate} (timestamp: {EndTimestamp})", endDate, endTimestamp);
                        query = query.Where(s => s.SaleDate <= endTimestamp);
                    }
                    
                    if (!string.IsNullOrEmpty(soldToUser))
                    {
                        Log.Debug("Filtering sales by sold to user: {SoldToUser}", soldToUser);
                        query = query.Where(s => s.TicketSoldToUser.Contains(soldToUser, StringComparison.OrdinalIgnoreCase));
                    }
                    
                    // Apply price filters (need to join with tickets)
                    var filteredSales = query.ToList();
                    Log.Information("Sales after date and user filtering: {@FilteredSales}", filteredSales);
                    
                    var result = new List<dynamic>();
                    
                    foreach (var sale in filteredSales)
                    {
                        var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                        Log.Debug("Ticket for sale {SaleId}: {@Ticket}", sale.SaleId, ticket);
                        
                        if (ticket == null) continue;
                        
                        // Apply price filters
                        if (minPrice.HasValue && ticket.TicketPrice < (double)minPrice.Value)
                        {
                            Log.Debug("Sale {SaleId} filtered out due to ticket price {TicketPrice} being less than minimum price {MinPrice}", 
                                sale.SaleId, ticket.TicketPrice, minPrice.Value);
                            continue;
                        }
                        
                        if (maxPrice.HasValue && ticket.TicketPrice > (double)maxPrice.Value)
                        {
                            Log.Debug("Sale {SaleId} filtered out due to ticket price {TicketPrice} being greater than maximum price {MaxPrice}", 
                                sale.SaleId, ticket.TicketPrice, maxPrice.Value);
                            continue;
                        }
                        
                        var route = conn.Db.Route.RouteId.Find(ticket.RouteId);
                        Log.Debug("Route for ticket {TicketId}: {@Route}", ticket.TicketId, route);
                        
                        result.Add(new {
                            SaleId = sale.SaleId,
                            SaleDate = DateTimeOffset.FromUnixTimeMilliseconds((long)sale.SaleDate).DateTime,
                            TicketId = sale.TicketId,
                            TicketSoldToUser = sale.TicketSoldToUser,
                            TicketSoldToUserPhone = sale.TicketSoldToUserPhone,
                            SellerId = sale.SellerId?.ToString(),
                            Ticket = new {
                                TicketId = ticket.TicketId,
                                RouteId = ticket.RouteId,
                                TicketPrice = ticket.TicketPrice,
                                Route = route != null ? new {
                                    RouteId = route.RouteId,
                                    StartPoint = route.StartPoint,
                                    EndPoint = route.EndPoint
                                } : null
                            }
                        });
                    }
                    
                    // Order by sale date descending
                    result = result.OrderByDescending(s => ((DateTime)s.SaleDate)).ToList();
                    
                    Log.Information("Search results: {@SearchResults}", result);
                    Log.Debug("Found {SalesCount} sales matching search criteria", result.Count);
                    return Ok(result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error searching sales: {ErrorMessage}", ex.Message);
                    return StatusCode(500, new { message = "An error occurred while searching sales" });
                }
            }

            private bool TicketSaleExists(long id)
            {
                var conn = _spacetimeService.GetConnection();
                var exists = conn.Db.Sale.SaleId.Find((uint)id) != null;
                Log.Debug("Checking if ticket sale {SaleId} exists: {Exists}", id, exists);
                return exists;
            }
        }

        public class CreateTicketSaleModel
        {
            public required long TicketId { get; set; }
            public required DateTimeOffset SaleDate { get; set; }
            public string? TicketSoldToUser { get; set; } = "ФИЗ.ПРОДАЖА";
            public string? TicketSoldToUserPhone { get; set; }
        }

        public class UpdateTicketSaleModel
        {
            public long? TicketId { get; set; }
            public DateTimeOffset? SaleDate { get; set; }
            public string? TicketSoldToUser { get; set; } = "ФИЗ.ПРОДАЖА";
            public string? TicketSoldToUserPhone { get; set; }
        }
    } 