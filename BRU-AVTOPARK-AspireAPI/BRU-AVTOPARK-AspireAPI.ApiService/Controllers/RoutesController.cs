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

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allow all authenticated users to read
    public class RoutesController : BaseController
    {
        private readonly IRouteService _routeService;
        private readonly ILogger<RoutesController> _logger;
        private readonly ISpacetimeDBService _spacetimeService;

        public RoutesController(IRouteService routeService, ILogger<RoutesController> logger, ISpacetimeDBService spacetimeService)
        {
            _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        }

      

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
