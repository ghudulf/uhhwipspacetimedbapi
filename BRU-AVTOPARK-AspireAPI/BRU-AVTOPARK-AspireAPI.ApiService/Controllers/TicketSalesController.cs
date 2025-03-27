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
        [Authorize] // Allow all authenticated users to read
        public class TicketSalesController : ControllerBase
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
            public ActionResult<IEnumerable<dynamic>> GetTicketSales()
            {
                try
                {
                    Serilog.Log.Information("Fetching all ticket sales");
                    
                    var conn = _spacetimeService.GetConnection();
                    
                    // Get all sales from SpacetimeDB
                    var sales = conn.Db.Sale.Iter().ToList();
                    
                    // Convert to a list of dynamic objects with necessary properties
                    var result = sales.Select(s => {
                        var ticket = conn.Db.Ticket.TicketId.Find(s.TicketId);
                        var route = ticket != null ? conn.Db.Route.RouteId.Find(ticket.RouteId) : null;
                        
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
                    
                    Log.Debug("Retrieved {SalesCount} ticket sales", result.Count);
                    return Ok(result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving ticket sales");
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
                    
                    // Find sale by ID
                    var sale = conn.Db.Sale.SaleId.Find((uint)id);
                    
                    if (sale == null)
                    {
                        Log.Warning("Ticket sale with ID {SaleId} not found", id);
                        return NotFound();
                    }
                    
                    // Get related ticket and route
                    var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                    var route = ticket != null ? conn.Db.Route.RouteId.Find(ticket.RouteId) : null;
                    
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
                    
                    Log.Debug("Successfully retrieved ticket sale with ID {SaleId}", id);
                    return Ok(result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving ticket sale with ID {SaleId}", id);
                    return StatusCode(500, new { message = $"An error occurred while retrieving ticket sale with ID {id}" });
                }
            }

            [HttpPost]
            public ActionResult<dynamic> CreateTicketSale([FromBody] CreateTicketSaleModel model)
            {
                if (!IsAdmin())
                {
                    Log.Warning("Unauthorized attempt to create ticket sale by non-admin user");
                    return Forbid();
                }

                try
                {
                    Log.Information("Creating new ticket sale for ticket ID {TicketId}", model.TicketId);
                    
                    var conn = _spacetimeService.GetConnection();
                    
                    // Check if ticket exists
                    var ticket = conn.Db.Ticket.TicketId.Find((uint)model.TicketId);
                    if (ticket == null)
                    {
                        Log.Warning("Invalid ticket ID {TicketId} provided for sale creation", model.TicketId);
                        return BadRequest("Invalid ticket ID");
                    }
                    
                    // Check if ticket is already sold
                    var existingSales = conn.Db.Sale.Iter().Where(s => s.TicketId == (uint)model.TicketId).ToList();
                    if (existingSales.Any())
                    {
                        Log.Warning("Ticket with ID {TicketId} is already sold", model.TicketId);
                        return BadRequest("Ticket is already sold");
                    }
                    
                    // Get seller identity from token
                    var authHeader = Request.Headers["Authorization"].ToString();
                    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                    {
                        Log.Warning("Missing or invalid Authorization header");
                        return Unauthorized(new { message = "Missing or invalid Authorization header" });
                    }
                    
                    var token = authHeader.Substring("Bearer ".Length);
                    var tokenHandler = new JwtSecurityTokenHandler();
                    
                    if (!tokenHandler.CanReadToken(token))
                    {
                        Log.Warning("Invalid JWT token format");
                        return Unauthorized(new { message = "Invalid token format" });
                    }
                    
                    var jwtToken = tokenHandler.ReadJwtToken(token);
                    var usernameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name" || c.Type == "name");
                    
                    if (usernameClaim == null)
                    {
                        Log.Warning("No username claim found in validated token");
                        return Unauthorized(new { message = "Invalid token: no username claim found" });
                    }
                    
                    // Find user by login
                    var seller = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == usernameClaim.Value);
                    if (seller == null)
                    {
                        Log.Warning("User from token not found in database: {Username}", usernameClaim.Value);
                        return NotFound(new { message = $"User '{usernameClaim.Value}' not found" });
                    }
                    
                    // Create sale using reducer
                    var buyerName = model.TicketSoldToUser ?? "ФИЗ.ПРОДАЖА";
                    var buyerPhone = model.TicketSoldToUserPhone ?? "";
                    
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
                    
                    if (newSale == null)
                    {
                        Log.Warning("Sale was not created properly");
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
                    
                    Log.Information("Successfully created ticket sale with ID {SaleId} for user {User} with phone {Phone}", 
                        newSale.SaleId, newSale.TicketSoldToUser, newSale.TicketSoldToUserPhone);
                    
                    return CreatedAtAction(nameof(GetTicketSale), new { id = newSale.SaleId }, result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error creating ticket sale");
                    return StatusCode(500, new { message = "An error occurred while creating the ticket sale" });
                }
            }

            [HttpPut("{id}")]
            public IActionResult UpdateTicketSale(long id, [FromBody] UpdateTicketSaleModel model)
            {
                if (!IsAdmin())
                {
                    Log.Warning("Unauthorized attempt to update ticket sale by non-admin user");
                    return Forbid();
                }

                try
                {
                    Log.Information("Updating ticket sale with ID {SaleId}", id);
                    
                    var conn = _spacetimeService.GetConnection();
                    
                    // Find sale by ID
                    var sale = conn.Db.Sale.SaleId.Find((uint)id);
                    if (sale == null)
                    {
                        Log.Warning("Ticket sale with ID {SaleId} not found for update", id);
                        return NotFound();
                    }
                    
                    // Note: SpacetimeDB doesn't have an UpdateSale reducer yet
                    // This would need to be implemented in the SpacetimeDB module
                    
                    Log.Warning("UpdateTicketSale is not implemented in the SpacetimeDB module");
                    return StatusCode(501, new { message = "Update operation is not implemented" });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error updating ticket sale with ID {SaleId}", id);
                    return StatusCode(500, new { message = $"An error occurred while updating ticket sale with ID {id}" });
                }
            }

            [HttpDelete("{id}")]
            public IActionResult DeleteTicketSale(long id)
            {
                if (!IsAdmin())
                {
                    Log.Warning("Unauthorized attempt to delete ticket sale by non-admin user");
                    return Forbid();
                }

                try
                {
                    Log.Information("Deleting ticket sale with ID {SaleId}", id);
                    
                    var conn = _spacetimeService.GetConnection();
                    
                    // Find sale by ID
                    var sale = conn.Db.Sale.SaleId.Find((uint)id);
                    if (sale == null)
                    {
                        Log.Warning("Ticket sale with ID {SaleId} not found for deletion", id);
                        return NotFound();
                    }
                    
                    // Note: SpacetimeDB doesn't have a DeleteSale reducer yet
                    // This would need to be implemented in the SpacetimeDB module
                    
                    Log.Warning("DeleteTicketSale is not implemented in the SpacetimeDB module");
                    return StatusCode(501, new { message = "Delete operation is not implemented" });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error deleting ticket sale with ID {SaleId}", id);
                    return StatusCode(500, new { message = $"An error occurred while deleting ticket sale with ID {id}" });
                }
            }

            [HttpGet("statistics/income")]
            public async Task<ActionResult<decimal>> GetTotalIncome(int year, int month)
            {
                try
                {
                    Log.Information("Fetching total income for {Year}-{Month}", year, month);
                    
                    var income = await _ticketSalesService.GetTotalIncomeAsync(year, month);
                    Log.Debug("Total income for {Year}-{Month}: {Income}", year, month, income);
                    return income;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving total income for {Year}-{Month}", year, month);
                    return StatusCode(500, new { message = $"An error occurred while retrieving total income for {year}-{month}" });
                }
            }

            [HttpGet("statistics/top-transports")]
            public async Task<ActionResult<List<TransportStatistic>>> GetTopTransports(int year, int month)
            {
                try
                {
                    Log.Information("Fetching top transports for {Year}-{Month}", year, month);
                    
                    var transports = await _ticketSalesService.GetTopTransportsAsync(year, month);
                    Log.Debug("Found {TransportCount} top transports for {Year}-{Month}", transports.Count, year, month);
                    return transports;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving top transports for {Year}-{Month}", year, month);
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
                    Log.Information("Searching sales with start date: {StartDate}, end date: {EndDate}, min price: {MinPrice}, max price: {MaxPrice}, user: {User}",
                        startDate?.ToString() ?? "any", endDate?.ToString() ?? "any", minPrice?.ToString() ?? "any", maxPrice?.ToString() ?? "any", soldToUser ?? "any");
                    
                    var conn = _spacetimeService.GetConnection();
                    
                    // Get all sales
                    var query = conn.Db.Sale.Iter().AsEnumerable();
                    
                    // Apply filters
                    if (startDate.HasValue)
                    {
                        var startTimestamp = startDate.Value.ToUnixTimeMilliseconds();
                        query = query.Where(s => s.SaleDate >= startTimestamp);
                    }
                    
                    if (endDate.HasValue)
                    {
                        var endTimestamp = endDate.Value.ToUnixTimeMilliseconds();
                        query = query.Where(s => s.SaleDate <= endTimestamp);
                    }
                    
                    if (!string.IsNullOrEmpty(soldToUser))
                    {
                        query = query.Where(s => s.TicketSoldToUser.Contains(soldToUser, StringComparison.OrdinalIgnoreCase));
                    }
                    
                    // Apply price filters (need to join with tickets)
                    var filteredSales = query.ToList();
                    var result = new List<dynamic>();
                    
                    foreach (var sale in filteredSales)
                    {
                        var ticket = conn.Db.Ticket.TicketId.Find(sale.TicketId);
                        if (ticket == null) continue;
                        
                        // Apply price filters
                        if (minPrice.HasValue && ticket.TicketPrice < (double)minPrice.Value) continue;
                        if (maxPrice.HasValue && ticket.TicketPrice > (double)maxPrice.Value) continue;
                        
                        var route = conn.Db.Route.RouteId.Find(ticket.RouteId);
                        
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
                    
                    Log.Debug("Found {SalesCount} sales matching search criteria", result.Count);
                    return Ok(result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error searching sales");
                    return StatusCode(500, new { message = "An error occurred while searching sales" });
                }
            }

            private bool TicketSaleExists(long id)
            {
                var conn = _spacetimeService.GetConnection();
                return conn.Db.Sale.SaleId.Find((uint)id) != null;
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