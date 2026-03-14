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
    using SpacetimeDB.Types;
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
                // Use hybrid authentication - check if already authenticated or validate token
                if (!await IsAuthenticatedAsync())
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

                switch (command)
                {
                    case "read_all":
                    case "next_page":
                    case "prev_page":
                    case "first_page":
                    case "last_page":
                    case "goto_page":
                        return await HandlePageNavigationAsync(command, request.Page, request.PageSize);

                    case "read":
                        return new { sale = BuildSaleById(_spacetimeService.GetConnection(), request.Id ?? throw new InvalidOperationException("id is required for read")) };

                    case "create":
                        return await HandleCreateCommandAsync(request);

                    case "update":
                        return new { operation = "update", success = false, message = "Update operation is not implemented in SpacetimeDB module" };

                    case "delete":
                        return new { operation = "delete", success = false, message = "Delete operation is not implemented in SpacetimeDB module" };

                    default:
                        throw new InvalidOperationException($"Unsupported command '{request.Command}'");
                }
            }

            private async Task<object> HandlePageNavigationAsync(string command, int? requestedPage, int? requestedPageSize)
            {
                var currentPageSize = Math.Max(1, requestedPageSize ?? 100);
                if (currentPageSize > 500) currentPageSize = 500;

                var initialPage = Math.Max(1, requestedPage ?? 1);

                var (initialItems, totalCount) = await _ticketSalesService.GetSalesPageAsync(initialPage, currentPageSize);
                var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)currentPageSize));

                var currentPage = Math.Max(1, Math.Min(initialPage, totalPages));

                switch (command)
                {
                    case "next_page":
                        currentPage = Math.Min(currentPage + 1, totalPages);
                        break;
                    case "prev_page":
                        currentPage = Math.Max(currentPage - 1, 1);
                        break;
                    case "first_page":
                        currentPage = 1;
                        break;
                    case "last_page":
                        currentPage = totalPages;
                        break;
                }

                _logger.LogInformation("TicketSales WebSocket {Command} - Page: {Page}/{TotalPages}, PageSize: {PageSize}, Total: {TotalCount}",
                    command, currentPage, totalPages, currentPageSize, totalCount);

                var items = (currentPage == initialPage)
                    ? initialItems
                    : (await _ticketSalesService.GetSalesPageAsync(currentPage, currentPageSize)).items;

                var conn = _spacetimeService.GetConnection();
                var sales = items.Select(s => BuildSaleView(conn, s)).ToList<object>();

                return new
                {
                    sales,
                    pagination = new
                    {
                        page = currentPage,
                        pageSize = currentPageSize,
                        totalCount,
                        totalPages,
                        hasNextPage = currentPage < totalPages,
                        hasPreviousPage = currentPage > 1
                    }
                };
            }

            /// <summary>
            /// Handles a realtime "create" CRUD command by creating a ticket sale and returning the result.
            /// </summary>
            /// <param name="request">Realtime CRUD request whose Payload must deserialize to <see cref="CreateTicketSaleModel"/> (case-insensitive).</param>
            /// <returns>
            /// An object containing:
            /// - `operation`: the string "create",
            /// - `success`: `true` if creation succeeded, `false` otherwise,
            /// - `entity`: the created sale view or `null`.
            /// </returns>
            /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an administrator.</exception>
            /// <exception cref="InvalidOperationException">Thrown when the request payload is missing or cannot be deserialized to <see cref="CreateTicketSaleModel"/>.</exception>
            private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
            {
                if (!await IsAdminAsync()) throw new UnauthorizedAccessException("Admin role required");
                var model = request.Payload?.Deserialize<CreateTicketSaleModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("payload is required for create");

                var created = ExecuteCreateSale(model);
                var result = new { operation = "create", success = created is not null, entity = created };

                if (created is not null)
                {
                    try
                    {
                        await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                            EventName: "ticket-sale.created",
                            Resource: "ticket-sales",
                            HttpMethod: "POST",
                            StatusCode: 201,
                            OccurredAt: DateTimeOffset.UtcNow,
                            CorrelationId: Guid.NewGuid().ToString(),
                            UserId: User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                            UserName: User?.Identity?.Name,
                            Tenant: null,
                            SourceIp: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                            Metadata: new Dictionary<string, string>
                            {
                                ["operation"] = "create",
                                ["success"] = "true",
                                ["saleId"] = created.GetType().GetProperty("SaleId")?.GetValue(created)?.ToString() ?? "unknown"
                            }
                        ));
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
            /// <param name="saleId">The sale ID to retrieve.</param>
            /// <returns>An anonymous object containing SaleId, SaleDate (DateTime), TicketId, TicketSoldToUser, TicketSoldToUserPhone, SellerId, and a nested Ticket object with TicketId, RouteId, TicketPrice and optional Route (RouteId, StartPoint, EndPoint); or null if the sale does not exist.</returns>
            private object? BuildSaleById(DbConnection conn, uint saleId)
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
            /// Creates a sale for the specified ticket after validation and returns the created sale representation.
            /// </summary>
            /// <param name="model">Model containing sale data (TicketId, SaleDate, TicketSoldToUser, TicketSoldToUserPhone).</param>
            /// <returns>The created sale view object produced by BuildSaleById, or null if the newly created sale could not be retrieved.</returns>
            /// <exception cref="InvalidOperationException">Thrown if the ticket does not exist, the ticket is already sold, or the seller profile cannot be found.</exception>
            /// <exception cref="UnauthorizedAccessException">Thrown if the caller's identity claim is missing.</exception>
            private object? ExecuteCreateSale(CreateTicketSaleModel model)
            {
                var conn = _spacetimeService.GetConnection();

                // Validate TicketId is within valid uint range
                if (model.TicketId < 0 || model.TicketId > uint.MaxValue)
                {
                    throw new ArgumentException($"TicketId must be between 0 and {uint.MaxValue}", nameof(model.TicketId));
                }

                var ticketId = (uint)model.TicketId;
                var ticket = conn.Db.Ticket.TicketId.Find(ticketId);
                if (ticket == null)
                {
                    throw new InvalidOperationException($"Ticket {ticketId} does not exist");
                }

                var existingSales = conn.Db.Sale.Iter().Where(s => s.TicketId == ticketId).ToList();
                if (existingSales.Any())
                {
                    throw new InvalidOperationException($"Ticket {ticketId} already sold");
                }

                // Extract identity from already-validated User claims (no raw token parsing)
                string? identityClaim = null;
                if (User?.Identity?.IsAuthenticated == true)
                {
                    identityClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                 ?? User.FindFirst("sub")?.Value
                                 ?? User.FindFirst(ClaimTypes.Name)?.Value
                                 ?? User.FindFirst("name")?.Value
                                 ?? User.FindFirst("login")?.Value;
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

                conn.Reducers.CreateSale(ticketId, model.TicketSoldToUser ?? "ФИЗ.ПРОДАЖА", model.TicketSoldToUserPhone ?? string.Empty, "POS", null);

                var newSale = conn.Db.Sale.Iter().Where(s => s.TicketId == ticketId).OrderByDescending(s => s.SaleId).FirstOrDefault();
                return newSale == null ? null : BuildSaleById(conn, newSale.SaleId);
            }

            /// <summary>
            /// Retrieves a paginated list of ticket sales with their related ticket and route details.
            /// </summary>
            /// <param name="page">Page number (1-based, default 1).</param>
            /// <param name="pageSize">Number of items per page (default 100, max 500).</param>
            /// <returns>
            /// An OK response containing a paged list of sales. Pagination metadata is included in
            /// response headers: X-Total-Count, X-Page, X-Page-Size, X-Total-Pages.
            /// Returns a 500 status with an error message if an exception occurs.
            /// </returns>
            [HttpGet]
            public async Task<ActionResult<IEnumerable<dynamic>>> GetTicketSales(
                [FromQuery] int page = 1,
                [FromQuery] int pageSize = 100)
            {
                try
                {
                    const int MaxPageSize = 500;
                    if (page < 1) page = 1;
                    if (pageSize < 1) pageSize = 1;
                    if (pageSize > MaxPageSize) pageSize = MaxPageSize;

                    Log.Information("Fetching ticket sales - Page: {Page}, PageSize: {PageSize}", page, pageSize);

                    var (sales, totalCount) = await _ticketSalesService.GetSalesPageAsync(page, pageSize);
                    Log.Information("Retrieved {Count} sales (page {Page}, total {TotalCount})", sales.Count, page, totalCount);

                    var conn = _spacetimeService.GetConnection();
                    var result = sales.Select(s => BuildSaleView(conn, s)).ToList();

                    var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));

                    Response.Headers["X-Total-Count"] = totalCount.ToString();
                    Response.Headers["X-Page"] = page.ToString();
                    Response.Headers["X-Page-Size"] = pageSize.ToString();
                    Response.Headers["X-Total-Pages"] = totalPages.ToString();

                    Log.Debug("Returning {Count} ticket sales (page {Page}/{TotalPages})", result.Count, page, totalPages);
                    return Ok(result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving ticket sales: {ErrorMessage}", ex.Message);
                    return StatusCode(500, new { message = "An error occurred while retrieving ticket sales" });
                }
            }

            private object BuildSaleView(DbConnection conn, Sale s)
            {
                var ticket = conn.Db.Ticket.TicketId.Find(s.TicketId);
                var route = ticket != null ? conn.Db.Route.RouteId.Find(ticket.RouteId) : null;
                return new
                {
                    SaleId = s.SaleId,
                    SaleDate = DateTimeOffset.FromUnixTimeMilliseconds((long)s.SaleDate).DateTime,
                    TicketId = s.TicketId,
                    TicketSoldToUser = s.TicketSoldToUser,
                    TicketSoldToUserPhone = s.TicketSoldToUserPhone,
                    SellerId = s.SellerId?.ToString(),
                    Ticket = ticket != null ? new
                    {
                        TicketId = ticket.TicketId,
                        RouteId = ticket.RouteId,
                        TicketPrice = ticket.TicketPrice,
                        Route = route != null ? new { route.RouteId, route.StartPoint, route.EndPoint } : null
                    } : null
                };
            }

            [HttpGet("{id}")]
            public ActionResult<dynamic> GetTicketSale(long id)
            {
                try
                {
                    // Validate id is within valid uint range
                    if (id < 0 || id > uint.MaxValue)
                    {
                        Log.Warning("Invalid sale ID {SaleId} - must be between 0 and {MaxValue}", id, uint.MaxValue);
                        return BadRequest(new { message = $"Sale ID must be between 0 and {uint.MaxValue}" });
                    }
                    
                    Log.Information("Fetching ticket sale with ID {SaleId}", id);
                    
                    var conn = _spacetimeService.GetConnection();
                    Log.Debug("Database connection established successfully for fetching sale {SaleId}", id);
                    
                    // Find sale by ID
                    var sale = conn.Db.Sale.SaleId.Find((uint)id);
                    // Log without PII - don't log full sale object
                    Log.Information("Retrieved sale data for ID {SaleId}", id);
                    
                    if (sale == null)
                    {
                        Log.Warning("Ticket sale with ID {SaleId} not found", id);
                        return NotFound();
                    }
                    
                    // Get related ticket and route
                    var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                    Log.Information("Retrieved ticket data for sale {SaleId}", id);
                    
                    var route = ticket != null ? conn.Db.Route.RouteId.Find(ticket.RouteId) : null;
                    Log.Information("Retrieved route data for ticket {TicketId}", ticket?.TicketId);
                    
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
                    
                    Log.Information("Returning ticket sale response for ID {SaleId}", id);
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
                    
                    // Validate TicketId is within valid uint range
                    if (model.TicketId < 0 || model.TicketId > uint.MaxValue)
                    {
                        Log.Warning("Invalid ticket ID {TicketId} - must be between 0 and {MaxValue}", model.TicketId, uint.MaxValue);
                        return BadRequest(new { message = $"Ticket ID must be between 0 and {uint.MaxValue}" });
                    }
                    
                    // Check if ticket exists
                    var ticket = conn.Db.Ticket.TicketId.Find((uint)model.TicketId);
                    Log.Information("Ticket lookup result for ID {TicketId}", model.TicketId);
                    
                    if (ticket == null)
                    {
                        Log.Warning("Invalid ticket ID {TicketId} provided for sale creation", model.TicketId);
                        return BadRequest("Invalid ticket ID");
                    }
                    
                    // Check if ticket is already sold
                    var existingSales = conn.Db.Sale.Iter().Where(s => s.TicketId == (uint)model.TicketId).ToList();
                    Log.Information("Existing sales count for ticket {TicketId}: {Count}", model.TicketId, existingSales.Count);
                    
                    if (existingSales.Any())
                    {
                        Log.Warning("Ticket with ID {TicketId} is already sold", model.TicketId);
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
                    Log.Information("Seller lookup result for username {Username}: {Found}", usernameClaim.Value, seller != null);
                    
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
                    
                    Log.Information("Newly created sale ID: {SaleId}", newSale?.SaleId);
                    
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
                    
                    // Log without PII - mask phone number
                    var maskedPhone = string.IsNullOrEmpty(newSale.TicketSoldToUserPhone) 
                        ? "none" 
                        : newSale.TicketSoldToUserPhone.Length > 4 
                            ? "***" + newSale.TicketSoldToUserPhone.Substring(newSale.TicketSoldToUserPhone.Length - 4) 
                            : "***";
                    Log.Information("Successfully created ticket sale with ID {SaleId} for user {User} with phone {MaskedPhone}", 
                        newSale.SaleId, newSale.TicketSoldToUser, maskedPhone);
                    
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
                // Validate id is within valid uint range
                if (id < 0 || id > uint.MaxValue)
                {
                    Log.Warning("Invalid sale ID {SaleId} - must be between 0 and {MaxValue}", id, uint.MaxValue);
                    return BadRequest(new { message = $"Sale ID must be between 0 and {uint.MaxValue}" });
                }
                
                Log.Information("Update ticket sale request received for ID {SaleId}", id);
                
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
                    Log.Information("Existing sale data for ID {SaleId}: {Found}", id, sale != null);
                    
                    if (sale == null)
                    {
                        Log.Warning("Ticket sale with ID {SaleId} not found for update", id);
                        return NotFound();
                    }
                    
                    // Note: SpacetimeDB doesn't have an UpdateSale reducer yet
                    // This would need to be implemented in the SpacetimeDB module
                    
                    Log.Warning("UpdateTicketSale is not implemented in the SpacetimeDB module. Sale ID: {SaleId}", id);
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
                // Validate id is within valid uint range
                if (id < 0 || id > uint.MaxValue)
                {
                    Log.Warning("Invalid sale ID {SaleId} - must be between 0 and {MaxValue}", id, uint.MaxValue);
                    return BadRequest(new { message = $"Sale ID must be between 0 and {uint.MaxValue}" });
                }
                
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
                    Log.Information("Sale to be deleted with ID {SaleId}: {Found}", id, sale != null);
                    
                    if (sale == null)
                    {
                        Log.Warning("Ticket sale with ID {SaleId} not found for deletion", id);
                        return NotFound();
                    }
                    
                    // Note: SpacetimeDB doesn't have a DeleteSale reducer yet
                    // This would need to be implemented in the SpacetimeDB module
                    
                    Log.Warning("DeleteTicketSale is not implemented in the SpacetimeDB module. Sale ID: {SaleId}", id);
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
                    Log.Debug("All sales retrieved from database: {Count}", allSales.Count);
                    
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
                    Log.Information("Sales after date and user filtering: {Count}", filteredSales.Count);
                    
                    var result = new List<dynamic>();
                    
                    foreach (var sale in filteredSales)
                    {
                        var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                        Log.Debug("Ticket for sale {SaleId}", sale.SaleId);
                        
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
                        Log.Debug("Route for ticket {TicketId}", ticket.TicketId);
                        
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
                    
                    Log.Information("Search results count: {Count}", result.Count);
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