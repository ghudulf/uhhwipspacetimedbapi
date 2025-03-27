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

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Allow all authenticated users to read
    public class RoutesController : ControllerBase
    {
        private readonly IRouteService _routeService;
        private readonly ILogger<RoutesController> _logger;

        public RoutesController(IRouteService routeService, ILogger<RoutesController> logger)
        {
            _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        public async Task<ActionResult<IEnumerable<Route>>> GetRoutes()
        {
            Log.Information("Fetching all routes with their related data");
            var routes = await _routeService.GetAllRoutesAsync();
            Log.Debug("Retrieved {RouteCount} routes", routes.Count);
            return Ok(routes);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Route>> GetRoute(uint id)
        {
            Log.Information("Fetching route with ID {RouteId}", id);
            var route = await _routeService.GetRouteByIdAsync(id);

            if (route == null)
            {
                Log.Warning("Route with ID {RouteId} not found", id);
                return NotFound();
            }

            Log.Debug("Successfully retrieved route with ID {RouteId}", id);
            return Ok(route);
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
        public async Task<ActionResult<IEnumerable<Route>>> SearchRoutes(
            [FromQuery] string? startPoint = null,
            [FromQuery] string? endPoint = null,
            [FromQuery] string? busModel = null,
            [FromQuery] string? driverName = null)
        {
            Log.Information("Searching routes with start point: {StartPoint}, end point: {EndPoint}, bus model: {BusModel}, driver name: {DriverName}",
                startPoint ?? "any", endPoint ?? "any", busModel ?? "any", driverName ?? "any");

            var routes = await _routeService.GetAllRoutesAsync();

            // Apply filters
            if (!string.IsNullOrEmpty(startPoint))
                routes = routes.Where(r => r.StartPoint.Contains(startPoint, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrEmpty(endPoint))
                routes = routes.Where(r => r.EndPoint.Contains(endPoint, StringComparison.OrdinalIgnoreCase)).ToList();

            Log.Debug("Found {RouteCount} routes matching search criteria", routes.Count);
            return Ok(routes);
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
