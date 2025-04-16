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
        [AllowAnonymous] // Allow all authenticated users to read
        public class TicketSalesController : BaseController
        {
            private readonly ISpacetimeDBService _spacetimeService;
            private readonly ITicketSalesService _ticketSalesService;
            private readonly IConfiguration _configuration;

            public TicketSalesController(ISpacetimeDBService spacetimeService, ITicketSalesService ticketSalesService, IConfiguration configuration)
            {
                _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
                _ticketSalesService = ticketSalesService ?? throw new ArgumentNullException(nameof(ticketSalesService));
                _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            }

            

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
                    
                    // Find user by login
                    var seller = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == usernameClaim.Value);
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