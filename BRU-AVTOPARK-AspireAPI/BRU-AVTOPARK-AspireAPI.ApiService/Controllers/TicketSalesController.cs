using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
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
    using TicketSalesApp.Services.Implementations;

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

                if (!await IsAdminAsync() && !await HasPermissionAsync("sales.view"))
                {
                    Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                await WebSocketEventStreamWriter.StreamCrudSessionAsync(
                    HttpContext,
                    AuthorizedEventsAsync(_realtimeEventBus.SubscribeAsync("ticket-sales", cancellationToken), "sales.view", cancellationToken),
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
            /// - For "update" / "delete": the committed operation result.
            /// </returns>
            /// <exception cref="InvalidOperationException">
            /// Thrown when the command is unsupported or when "read" is requested without an <c>Id</c>.
            /// </exception>
            private async Task<object> HandleRealtimeCrudAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
            {
            await RequireRealtimeCommandAsync("sales", request.Command ?? "");
                var command = (request.Command ?? string.Empty).Trim().ToLowerInvariant();

                return command switch
                {
                    "read_all" => await ReadSalesPageAsync(request, cancellationToken),
                    "read" => new { sale = await _spacetimeService.ReadAsync(c => BuildSaleById(c, request.Id ?? throw new ArgumentException("id is required")), cancellationToken) },
                    "create" => await HandleCreateCommandAsync(request),
                    "update" => await UpdateSaleCommandAsync(request, cancellationToken),
                    "delete" => await DeleteSaleCommandAsync(request, cancellationToken),
                    _ => throw new InvalidOperationException($"Unsupported command '{request.Command}'")
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
                if (!await HasPermissionAsync("sales.create")) throw new UnauthorizedAccessException("Sales create permission required");
                var model = request.Payload?.Deserialize<CreateTicketSaleModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("payload is required for create");

                var created = await ExecuteCreateSale(model, ApplicationOperationId(request));
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
                return sale == null ? null : SalePresentation.Map(conn, sale);
            }

            /// <summary>
            /// Builds a snapshot list of all ticket sales with their detailed sale, ticket, and route information.
            /// </summary>
            /// <returns>A list of objects where each item represents a sale with related ticket and route details; sales that cannot be resolved are omitted.</returns>
            private async Task<object> ReadSalesPageAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
            {
                var page = request.Page ?? 1;
                var size = request.PageSize ?? 100;
                if (page < 1 || size < 1 || size > 1000 || (long)(page - 1) * size > int.MaxValue)
                    throw new ArgumentException("Invalid page or page size");
                return await _spacetimeService.ReadAsync(c =>
                {
                    var rows = c.Db.Sale.Iter().OrderBy(s => s.SaleDate).ThenBy(s => s.SaleId).ToArray();
                    return new { sales = rows.Skip((page - 1) * size).Take(size).Select(row => SalePresentation.Map(c, row)).ToArray(),
                        page, pageSize = size, totalCount = rows.Length };
                }, cancellationToken);
            }

            private async Task<object> UpdateSaleCommandAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
            {
                var id = request.Id ?? throw new ArgumentException("id is required");
                var model = request.Payload?.Deserialize<UpdateTicketSaleModel>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new ArgumentException("payload is required");
                var existing = await _ticketSalesService.GetSaleByIdAsync(id) ?? throw new KeyNotFoundException("Sale not found");
                if (model.SaleDate.HasValue) throw new ArgumentException("Sale date cannot be changed", "SaleDate");
                if (model.TicketId.HasValue && model.TicketId != existing.TicketId) throw new ArgumentException("Original ticket cannot be changed");
                await ExecuteSaleOperationAsync("sales.update", await ResolveActorAsync(),
                    new UpdateSaleCommand(id, request.ExpectedRevision ?? model.ExpectedRevision, model.TicketSoldToUser, model.TicketSoldToUserPhone),
                    ApplicationOperationId(request), cancellationToken);
                await PublishSaleMutationAsync("update", id);
                return new { operation = "update", success = true, entity = await _spacetimeService.ReadAsync(c => BuildSaleById(c, id), cancellationToken) };
            }

            private async Task<object> DeleteSaleCommandAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
            {
                var id = request.Id ?? throw new ArgumentException("id is required");
                await ExecuteSaleOperationAsync("sales.delete", await ResolveActorAsync(), new DeleteSaleCommand(id, request.ExpectedRevision),
                    ApplicationOperationId(request), cancellationToken);
                await PublishSaleMutationAsync("delete", id);
                return new { operation = "delete", success = true, id };
            }

            private async Task PublishSaleMutationAsync(string operation, uint id)
            {
                try
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent($"ticket-sale.{(operation == "update" ? "updated" : "deleted")}",
                        "ticket-sales", operation == "update" ? "PUT" : "DELETE", 200, DateTimeOffset.UtcNow,
                        HttpContext.TraceIdentifier, await GetUserIdAsync(), await GetUserNameAsync(), null, GetClientIp(),
                        new Dictionary<string, string> { ["operation"] = operation, ["saleId"] = id.ToString() }), deadline.Token);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to publish committed sale mutation"); }
            }

            /// <summary>
            /// Creates a sale for the specified ticket after validation and returns the created sale representation.
            /// </summary>
            /// <param name="model">Model containing sale data (TicketId, SaleDate, TicketSoldToUser, TicketSoldToUserPhone).</param>
            /// <returns>The created sale view object produced by BuildSaleById, or null if the newly created sale could not be retrieved.</returns>
            /// <exception cref="InvalidOperationException">Thrown if the ticket does not exist, the ticket is already sold, or the seller profile cannot be found.</exception>
            /// <exception cref="UnauthorizedAccessException">Thrown if the caller's identity claim is missing.</exception>
            private async Task<Identity> ResolveActorAsync()
            {
                var value = await GetSpacetimeIdentityAsync() ?? await GetUserIdAsync();
                if (string.IsNullOrWhiteSpace(value)) throw new UnauthorizedAccessException("Authenticated actor required.");
                return await _spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.IsActive && (u.UserId.ToString() == value || u.Login == value))?.UserId
                    ?? throw new UnauthorizedAccessException("Active application account required."), HttpContext.RequestAborted);
            }

            private async Task<object?> ExecuteCreateSale(CreateTicketSaleModel model, string? operationId = null)
            {
                if (model.TicketId <= 0 || model.TicketId > uint.MaxValue) throw new ArgumentException("Invalid ticket ID.");
                var actor = await ResolveActorAsync();
                var receipt = await ExecuteSaleOperationAsync("sales.create", actor,
                    new CreateSaleCommand((uint)model.TicketId, model.TicketSoldToUser ?? "ФИЗ.ПРОДАЖА", model.TicketSoldToUserPhone ?? "", "POS"),
                    operationId ?? ApplicationOperationId(), HttpContext.RequestAborted);
                return await _spacetimeService.ReadAsync(c => c.Db.Sale.SaleId.Find(receipt.EntityId) is { } row
                    ? SalePresentation.Map(c, row) : null, HttpContext.RequestAborted);
            }

            private async Task<MutationResult> ExecuteSaleOperationAsync<T>(string name, Identity actor, T payload, string? operationId, CancellationToken ct)
            {
                var operations = HttpContext.RequestServices.GetRequiredService<ApplicationOperations>();
                return (MutationResult)(await operations.ExecuteAsync(name, actor, JsonSerializer.SerializeToElement(payload, ApiProtocol.Json),
                    operationId ?? ApplicationOperationId(), ct))!;
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
            public async Task<IActionResult> GetTicketSales()
            {
                if (!await HasPermissionAsync("sales.view")) return Forbid();
                return Ok(await _spacetimeService.ReadAsync(c => c.Db.Sale.Iter()
                    .OrderBy(s => s.SaleDate).ThenBy(s => s.SaleId).Select(s => SalePresentation.Map(c, s)).ToArray(), HttpContext.RequestAborted));
            }

            [HttpGet("{id}")]
            public async Task<IActionResult> GetTicketSale(long id)
            {
                if (id <= 0 || id > uint.MaxValue) return BadRequest(new { message = "Invalid sale ID" });
                if (!await HasPermissionAsync("sales.view")) return Forbid();
                var sale = await _spacetimeService.ReadAsync(c => c.Db.Sale.SaleId.Find((uint)id) is { } row
                    ? SalePresentation.Map(c, row) : null, HttpContext.RequestAborted);
                return sale == null ? NotFound() : Ok(sale);
            }

            [HttpPost]
            public async Task<ActionResult<dynamic>> CreateTicketSale([FromBody] CreateTicketSaleModel model)
            {
                if (!await HasPermissionAsync("sales.create")) return Forbid();
                try
                {
                    var created = await ExecuteCreateSale(model);
                    return created == null ? StatusCode(503) : Ok(created);
                }
                catch (ArgumentException) { return BadRequest(new { message = "Invalid sale request" }); }
                catch (UnauthorizedAccessException) { return Unauthorized(); }
                catch (OperationException ex) { return StatusCode(ex.Code == "forbidden" ? 403 : 400, new { code = ex.Code }); }
                catch (ReducerRejectedException) { return Conflict(new { message = "Sale was rejected; refresh ticket availability and permissions" }); }
                catch (CommitOutcomeUnknownException) { return StatusCode(503, new { code = "commit_outcome_unknown", message = "Reconcile the operation before retrying" }); }
                catch (DatabaseUnavailableException) { return StatusCode(503, new { code = "database_unavailable" }); }
            }

            [HttpPut("{id}")]
            public async Task<IActionResult> UpdateTicketSale(long id, [FromBody] UpdateTicketSaleModel model)
            {
                if (id <= 0 || id > uint.MaxValue) return BadRequest(new { message = "Invalid sale ID" });
                if (!await HasPermissionAsync("sales.edit")) return Forbid();
                if (model.SaleDate.HasValue) return BadRequest(new { code = "validation_failed", message = "Sale date cannot be changed" });
                var sale = await _ticketSalesService.GetSaleByIdAsync((uint)id);
                if (sale == null) return NotFound();
                if (model.TicketId.HasValue && model.TicketId != sale.TicketId)
                    return BadRequest(new { message = "The original ticket cannot be changed" });
                try
                {
                    await ExecuteSaleOperationAsync("sales.update", await ResolveActorAsync(),
                        new UpdateSaleCommand((uint)id, model.ExpectedRevision, model.TicketSoldToUser, model.TicketSoldToUserPhone),
                        ApplicationOperationId(), HttpContext.RequestAborted);
                    return NoContent();
                }
                catch (OperationException ex) { return StatusCode(ex.Code == "forbidden" ? 403 : 400, new { code = ex.Code }); }
                catch (ReducerRejectedException) { return Conflict(new { code = "conflict", message = "Reload the sale and review your changes" }); }
                catch (CommitOutcomeUnknownException) { return StatusCode(503, new { code = "commit_outcome_unknown" }); }
                catch (DatabaseUnavailableException) { return StatusCode(503, new { code = "database_unavailable" }); }
            }

            [HttpDelete("{id}")]
            public async Task<IActionResult> DeleteTicketSale(long id)
            {
                if (id <= 0 || id > uint.MaxValue) return BadRequest(new { message = "Invalid sale ID" });
                if (!await HasPermissionAsync("sales.delete")) return Forbid();
                try
                {
                    await ExecuteSaleOperationAsync("sales.delete", await ResolveActorAsync(), new DeleteSaleCommand((uint)id),
                        ApplicationOperationId(), HttpContext.RequestAborted);
                    return NoContent();
                }
                catch (OperationException ex) { return StatusCode(ex.Code == "forbidden" ? 403 : 400, new { code = ex.Code }); }
                catch (ReducerRejectedException) { return Conflict(new { code = "conflict" }); }
                catch (CommitOutcomeUnknownException) { return StatusCode(503, new { code = "commit_outcome_unknown" }); }
                catch (DatabaseUnavailableException) { return StatusCode(503, new { code = "database_unavailable" }); }
            }

            [HttpGet("statistics/income-summary")]
            public async Task<IActionResult> GetIncomeSummary(int year, int month, CancellationToken cancellationToken)
            {
                if (!await HasPermissionAsync("sales.view")) return Forbid();
                if (year < 1970 || year > 9998 || month < 1 || month > 12) return BadRequest();
                return Ok(await _ticketSalesService.GetIncomeSummaryAsync(year, month, cancellationToken));
            }

            [HttpGet("statistics/income")]
            public async Task<ActionResult<decimal>> GetTotalIncome(int year, int month)
            {
                if (!await HasPermissionAsync("sales.view")) return Forbid();
                if (year < 1970 || year > 9998 || month < 1 || month > 12) return BadRequest();
                Log.Information("Fetching total income for {Year}-{Month}", year, month);
                
                try
                {
                    var income = await _ticketSalesService.GetTotalIncomeAsync(year, month);
                    Log.Information("Total income for {Year}-{Month}: {Income}", year, month, income);
                    return income;
                }
                catch (HistoricalAmountUnknownException)
                {
                    return Conflict(new { code = "historical_amount_unknown", message = "Use the income summary to view known totals and the unknown amount count" });
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
                if (!await HasPermissionAsync("sales.view")) return Forbid();
                if (year < 1970 || year > 9998 || month < 1 || month > 12) return BadRequest();
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
            public async Task<IActionResult> SearchSales([FromQuery] DateTime? startDate = null,
                [FromQuery] DateTime? endDate = null, [FromQuery] decimal? minPrice = null,
                [FromQuery] decimal? maxPrice = null, [FromQuery] string? soldToUser = null)
            {
                if (!await HasPermissionAsync("sales.view")) return Forbid();
                if (endDate < startDate || minPrice < 0 || maxPrice < minPrice || soldToUser?.Length > 200)
                    return BadRequest(new { message = "Invalid sale search filters" });
                var start = startDate?.ToUniversalTime();
                var end = endDate?.ToUniversalTime();
                var rows = await _spacetimeService.ReadAsync(c => c.Db.Sale.Iter()
                    .OrderBy(s => s.SaleDate).ThenBy(s => s.SaleId).Select(s => SalePresentation.Map(c, s))
                    .Where(s => (!start.HasValue || s.SaleDate >= start) && (!end.HasValue || s.SaleDate <= end))
                    .Where(s => string.IsNullOrEmpty(soldToUser) || s.TicketSoldToUser.Contains(soldToUser, StringComparison.OrdinalIgnoreCase))
                    .Where(s => SalePresentation.MatchesTicketPrice(s, minPrice, maxPrice))
                    .ToArray(), HttpContext.RequestAborted);
                return Ok(rows);
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
            public ulong? ExpectedRevision { get; set; }
            public long? TicketId { get; set; }
            public DateTimeOffset? SaleDate { get; set; }
            public string? TicketSoldToUser { get; set; } = "ФИЗ.ПРОДАЖА";
            public string? TicketSoldToUserPhone { get; set; }
        }
    }
