using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Serilog;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;
using Route = SpacetimeDB.Types.Route;
using Log = Serilog.Log;
using SpacetimeDB;
using System.Text.Json;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allows both custom JWT (manual parsing) and ASP.NET Core auth (OpenIddict)
    public class RoutesController : BaseController
    {
        private readonly IRouteService _routeService;
        private readonly ILogger<RoutesController> _logger;
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IRealtimeEventBus _realtimeEventBus;

        /// <summary>
        /// Initializes a new instance of <see cref="RoutesController"/> with its required services.
        /// </summary>
        /// <param name="routeService">Service for managing route data and operations.</param>
        /// <param name="logger">Logger for controller diagnostics.</param>
        /// <param name="spacetimeService">Service providing access to related Bus and Driver data.</param>
        /// <param name="realtimeEventBus">Event bus used to subscribe to and publish realtime route events.</param>
        /// <exception cref="ArgumentNullException">Thrown when any constructor argument is null.</exception>
        public RoutesController(IRouteService routeService, ILogger<RoutesController> logger, ISpacetimeDBService spacetimeService, IRealtimeEventBus realtimeEventBus)
        {
            _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
        }

      

        /// <summary>
        /// Opens a WebSocket endpoint that streams real-time CRUD events for routes to the caller.
        /// </summary>
        /// <param name="cancellationToken">Token that cancels and terminates the streaming session.</param>
        /// <remarks>Responds with 401 Unauthorized if the caller is not authenticated.</remarks>
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
                _realtimeEventBus.SubscribeAsync("routes", cancellationToken),
                HandleRealtimeCrudAsync,
                _logger,
                cancellationToken);
        }

        /// <summary>
        /// Handle a real-time CRUD command and produce a command-specific result object.
        /// </summary>
        /// <param name="request">The incoming realtime CRUD request containing `Command`, optional `Id`, and optional `Payload`.</param>
        /// <param name="cancellationToken">Token to observe while awaiting asynchronous operations.</param>
        /// <returns>
        /// An object whose shape depends on `request.Command`:
        /// - "read_all": { routes = IEnumerable&lt;Route&gt; }
        /// - "read": { route = Route }
        /// - "create": operation result and snapshot (e.g. { operation = "create", success, snapshot })
        /// - "update": operation result and snapshot (e.g. { operation = "update", success, entity, snapshot })
        /// - "delete": operation result and snapshot (e.g. { operation = "delete", success, deletedId, snapshot })
        /// </returns>
        /// <exception cref="InvalidOperationException">Thrown when a required `Id` is missing for "read" or when `Command` is unsupported.</exception>
        private async Task<object> HandleRealtimeCrudAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
        {
            var command = (request.Command ?? string.Empty).Trim().ToLowerInvariant();

            return command switch
            {
                "read_all" => new { routes = await _routeService.GetAllRoutesAsync() },
                "read" => new { route = await _routeService.GetRouteByIdAsync(request.Id ?? throw new InvalidOperationException("id is required for read")) },
                "create" => await HandleCreateCommandAsync(request),
                "update" => await HandleUpdateCommandAsync(request),
                "delete" => await HandleDeleteCommandAsync(request),
                _ => throw new InvalidOperationException($"Unsupported command '{request.Command}'")
            };
        }

        /// <summary>
        /// Handle an incoming realtime "create" CRUD command by creating a new route and returning the operation result plus a full snapshot of routes.
        /// </summary>
        /// <param name="request">The realtime CRUD request whose Payload must contain a CreateRouteModel JSON object.</param>
        /// <returns>
        /// An object with the following properties:
        /// - `operation`: the string "create".
        /// - `success`: a boolean indicating whether creation succeeded.
        /// - `snapshot`: the current collection of all routes after the operation.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an administrator.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request payload is null or cannot be deserialized into a CreateRouteModel.</exception>
        private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin()) throw new UnauthorizedAccessException("Admin role required");

            var model = request.Payload?.Deserialize<CreateRouteModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for create");

            var success = await _routeService.CreateRouteAsync(model.StartPoint, model.EndPoint, model.DriverId, model.BusId, model.TravelTime, true);
            var snapshot = await _routeService.GetAllRoutesAsync();
            return new { operation = "create", success, snapshot };
        }

        /// <summary>
        /// Handle an incoming realtime "update" command to modify an existing route and produce an operation result snapshot.
        /// </summary>
        /// <param name="request">The realtime CRUD request containing the target Id and a JSON payload deserializable to <see cref="UpdateRouteModel"/>.</param>
        /// <returns>
        /// An object with properties:
        /// - `operation`: the string "update",
        /// - `success`: a boolean indicating whether the update succeeded,
        /// - `entity`: the updated route entity (or null if not found),
        /// - `snapshot`: the full collection of routes after the operation.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an administrator.</exception>
        /// <exception cref="InvalidOperationException">Thrown when `request.Id` is null or when the request payload is missing or invalid.</exception>
        private async Task<object> HandleUpdateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin()) throw new UnauthorizedAccessException("Admin role required");

            var id = request.Id ?? throw new InvalidOperationException("id is required for update");
            var model = request.Payload?.Deserialize<UpdateRouteModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for update");

            var success = await _routeService.UpdateRouteAsync(id, model.StartPoint, model.EndPoint, model.DriverId, model.BusId, model.TravelTime, null);
            var entity = await _routeService.GetRouteByIdAsync(id);
            var snapshot = await _routeService.GetAllRoutesAsync();
            return new { operation = "update", success, entity, snapshot };
        }

        /// <summary>
        /// Handle a realtime "delete" CRUD command for routes, performing authorization and returning an operation result with a post-operation snapshot.
        /// </summary>
        /// <param name="request">Realtime CRUD request that must contain the Id of the route to delete.</param>
        /// <returns>
        /// An object with the following properties:
        /// - operation: the string "delete".
        /// - success: `true` if the delete succeeded, `false` otherwise.
        /// - deletedId: the id of the deleted route.
        /// - snapshot: the current list of all routes after the operation.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an administrator.</exception>
        /// <exception cref="InvalidOperationException">Thrown when `request.Id` is null.</exception>
        private async Task<object> HandleDeleteCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin()) throw new UnauthorizedAccessException("Admin role required");

            var id = request.Id ?? throw new InvalidOperationException("id is required for delete");
            var success = await _routeService.DeleteRouteAsync(id);
            var snapshot = await _routeService.GetAllRoutesAsync();
            return new { operation = "delete", success, deletedId = id, snapshot };
        }

        /// <summary>
        /// Retrieves all routes with their associated bus and driver information.
        /// </summary>
        /// <returns>An ActionResult containing a collection of route objects; each item includes RouteId, StartPoint, EndPoint, DriverId with a nested Driver object when available (EmployeeId, Name, Surname), BusId with a nested Bus object when available (BusId, Model, RegistrationNumber), TravelTime, and IsActive.</returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetRoutes()
        {
            Log.Information("Fetching all routes with their related data");
            var routes = await _routeService.GetAllRoutesAsync();
            var conn = _spacetimeService.GetConnection();

            // Map to anonymous type including Bus and Driver
            var result = routes.Select(r => {
                var bus = conn.Db.Bus.BusId.Find(r.BusId);
                var driver = conn.Db.Employee.EmployeeId.Find(r.DriverId);
                return new {
                    r.RouteId,
                    r.StartPoint,
                    r.EndPoint,
                    r.DriverId,
                    Driver = driver != null ? new { driver.EmployeeId, driver.Name, driver.Surname } : null,
                    r.BusId,
                    Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                    r.TravelTime,
                    r.IsActive
                };
            }).ToList();

            Log.Debug("Retrieved {RouteCount} routes", result.Count);
            _logger.LogInformation("FULL ROUTES DATA: {RoutesData}", JsonSerializer.Serialize(result));
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetRoute(uint id)
        {
            Log.Information("Fetching route with ID {RouteId}", id);
            var route = await _routeService.GetRouteByIdAsync(id);

            if (route == null)
            {
                Log.Warning("Route with ID {RouteId} not found", id);
                return NotFound();
            }

            var conn = _spacetimeService.GetConnection();
            var bus = conn.Db.Bus.BusId.Find(route.BusId);
            var driver = conn.Db.Employee.EmployeeId.Find(route.DriverId);

            // Map to anonymous type including Bus and Driver
            var result = new {
                route.RouteId,
                route.StartPoint,
                route.EndPoint,
                route.DriverId,
                Driver = driver != null ? new { driver.EmployeeId, driver.Name, driver.Surname } : null,
                route.BusId,
                Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                route.TravelTime,
                route.IsActive
            };

            Log.Debug("Successfully retrieved route with ID {RouteId}", id);
            _logger.LogInformation("FULL ROUTE DATA: {RouteData}", JsonSerializer.Serialize(result));
            return Ok(result);
        }

        [HttpPost]
        public async Task<ActionResult<Route>> CreateRoute([FromBody] CreateRouteModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to create route by non-admin user");
                return Forbid();
            }

            Log.Information("Creating new route from {StartPoint} to {EndPoint}", model.StartPoint, model.EndPoint);

            var success = await _routeService.CreateRouteAsync(
                model.StartPoint,
                model.EndPoint,
                model.DriverId,
                model.BusId,
                model.TravelTime,
                true // isActive
            );

            if (!success)
            {
                Log.Warning("Failed to create route");
                return BadRequest("Failed to create route");
            }

            // Get the newly created route
            var routes = await _routeService.GetAllRoutesAsync();
            var route = routes.LastOrDefault();

            if (route == null)
            {
                Log.Error("Route was created but could not be retrieved");
                return StatusCode(500, "Route was created but could not be retrieved");
            }

            Log.Information("Successfully created route with ID {RouteId}", route.RouteId);
            return CreatedAtAction(nameof(GetRoute), new { id = route.RouteId }, route);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRoute(uint id, [FromBody] UpdateRouteModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to update route by non-admin user");
                return Forbid();
            }

            Log.Information("Updating route with ID {RouteId}", id);

            var success = await _routeService.UpdateRouteAsync(
                id,
                model.StartPoint,
                model.EndPoint,
                model.DriverId,
                model.BusId,
                model.TravelTime
            );

            if (!success)
            {
                Log.Warning("Route with ID {RouteId} not found for update", id);
                return NotFound();
            }

            Log.Information("Successfully updated route with ID {RouteId}", id);
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRoute(uint id)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to delete route by non-admin user");
                return Forbid();
            }

            Log.Information("Deleting route with ID {RouteId}", id);

            var success = await _routeService.DeleteRouteAsync(id);
            if (!success)
            {
                Log.Warning("Route with ID {RouteId} not found for deletion", id);
                return NotFound();
            }

            Log.Information("Successfully deleted route with ID {RouteId}", id);
            return NoContent();
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<dynamic>>> SearchRoutes(
            [FromQuery] string? startPoint = null,
            [FromQuery] string? endPoint = null,
            [FromQuery] string? busModel = null,
            [FromQuery] string? driverName = null)
        {
            Log.Information("Searching routes with start point: {StartPoint}, end point: {EndPoint}, bus model: {BusModel}, driver name: {DriverName}",
                startPoint ?? "any", endPoint ?? "any", busModel ?? "any", driverName ?? "any");

            var routes = await _routeService.GetAllRoutesAsync();
            var conn = _spacetimeService.GetConnection();
            var query = routes.AsEnumerable();

            // Apply filters
            if (!string.IsNullOrEmpty(startPoint))
                query = query.Where(r => r.StartPoint.Contains(startPoint, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(endPoint))
                query = query.Where(r => r.EndPoint.Contains(endPoint, StringComparison.OrdinalIgnoreCase));

            // Filter by bus model
            if (!string.IsNullOrEmpty(busModel))
            {
                query = query.Where(r => {
                    var bus = conn.Db.Bus.BusId.Find(r.BusId);
                    return bus != null && bus.Model.Contains(busModel, StringComparison.OrdinalIgnoreCase);
                });
            }

            // Filter by driver name
            if (!string.IsNullOrEmpty(driverName))
            {
                query = query.Where(r => {
                    var driver = conn.Db.Employee.EmployeeId.Find(r.DriverId);
                    return driver != null && 
                           (driver.Name.Contains(driverName, StringComparison.OrdinalIgnoreCase) || 
                            driver.Surname.Contains(driverName, StringComparison.OrdinalIgnoreCase));
                });
            }

            // Map to anonymous type including Bus and Driver
            var result = query.Select(r => {
                var bus = conn.Db.Bus.BusId.Find(r.BusId);
                var driver = conn.Db.Employee.EmployeeId.Find(r.DriverId);
                return new {
                    r.RouteId,
                    r.StartPoint,
                    r.EndPoint,
                    r.DriverId,
                    Driver = driver != null ? new { driver.EmployeeId, driver.Name, driver.Surname } : null,
                    r.BusId,
                    Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                    r.TravelTime,
                    r.IsActive
                };
            }).ToList();

            Log.Debug("Found {RouteCount} routes matching search criteria", result.Count);
            _logger.LogInformation("FULL SEARCH RESULTS DATA: {RoutesData}", JsonSerializer.Serialize(result));
            return Ok(result);
        }
    }

    public class CreateRouteModel
    {
        public required string StartPoint { get; set; }
        public required string EndPoint { get; set; }
        public required uint BusId { get; set; }
        public required uint DriverId { get; set; }
        public required string TravelTime { get; set; }
    }

    public class UpdateRouteModel
    {
        public string? StartPoint { get; set; }
        public string? EndPoint { get; set; }
        public uint? BusId { get; set; }
        public uint? DriverId { get; set; }
        public string? TravelTime { get; set; }
    }
}
