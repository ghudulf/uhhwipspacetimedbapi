using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Serilog;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Allow all authenticated users to read
    public class RoutesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public RoutesController(AppDbContext context)
        {
            _context = context;
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
        public async Task<ActionResult<IEnumerable<Marshut>>> GetRoutes()
        {
            Log.Information("Fetching all routes with their related data");
            var routes = await _context.Marshuti
                .Include(r => r.Avtobus)
                .Include(r => r.Employee)
                .Include(r => r.Tickets)
                .ToListAsync();
            
            Log.Debug("Retrieved {RouteCount} routes", routes.Count);
            return routes;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Marshut>> GetRoute(long id)
        {
            Log.Information("Fetching route with ID {RouteId}", id);
            var route = await _context.Marshuti
                .Include(r => r.Avtobus)
                .Include(r => r.Employee)
                .Include(r => r.Tickets)
                .FirstOrDefaultAsync(r => r.RouteId == id);

            if (route == null)
            {
                Log.Warning("Route with ID {RouteId} not found", id);
                return NotFound();
            }

            Log.Debug("Successfully retrieved route with ID {RouteId}", id);
            return route;
        }

        [HttpPost]
        public async Task<ActionResult<Marshut>> CreateRoute([FromBody] CreateRouteModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to create route by non-admin user");
                return Forbid();
            }

            Log.Information("Creating new route from {StartPoint} to {EndPoint}", model.StartPoint, model.EndPoint);

            var bus = await _context.Avtobusy.FindAsync(model.BusId);
            if (bus == null)
            {
                Log.Warning("Invalid bus ID {BusId} provided for route creation", model.BusId);
                return BadRequest("Invalid bus ID");
            }

            var driver = await _context.Employees.FindAsync(model.DriverId);
            if (driver == null)
            {
                Log.Warning("Invalid driver ID {DriverId} provided for route creation", model.DriverId);
                return BadRequest("Invalid driver ID");
            }

            var route = new Marshut
            {
                StartPoint = model.StartPoint,
                EndPoint = model.EndPoint,
                BusId = model.BusId,
                DriverId = model.DriverId,
                TravelTime = model.TravelTime
            };

            _context.Marshuti.Add(route);
            await _context.SaveChangesAsync();

            // Reload with navigation properties
            route = await _context.Marshuti
                .Include(r => r.Avtobus)
                .Include(r => r.Employee)
                .Include(r => r.Tickets)
                .FirstAsync(r => r.RouteId == route.RouteId);

            Log.Information("Successfully created route with ID {RouteId}", route.RouteId);
            return CreatedAtAction(nameof(GetRoute), new { id = route.RouteId }, route);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRoute(long id, [FromBody] UpdateRouteModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to update route by non-admin user");
                return Forbid();
            }

            Log.Information("Updating route with ID {RouteId}", id);
            var route = await _context.Marshuti.FindAsync(id);
            if (route == null)
            {
                Log.Warning("Route with ID {RouteId} not found for update", id);
                return NotFound();
            }

            if (model.BusId.HasValue)
            {
                var bus = await _context.Avtobusy.FindAsync(model.BusId.Value);
                if (bus == null)
                {
                    Log.Warning("Invalid bus ID {BusId} provided for route update", model.BusId.Value);
                    return BadRequest("Invalid bus ID");
                }
                route.BusId = model.BusId.Value;
            }

            if (model.DriverId.HasValue)
            {
                var driver = await _context.Employees.FindAsync(model.DriverId.Value);
                if (driver == null)
                {
                    Log.Warning("Invalid driver ID {DriverId} provided for route update", model.DriverId.Value);
                    return BadRequest("Invalid driver ID");
                }
                route.DriverId = model.DriverId.Value;
            }

            if (model.StartPoint != null)
                route.StartPoint = model.StartPoint;

            if (model.EndPoint != null)
                route.EndPoint = model.EndPoint;

            if (model.TravelTime != null)
                route.TravelTime = model.TravelTime;

            try
            {
                await _context.SaveChangesAsync();
                Log.Information("Successfully updated route with ID {RouteId}", id);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                Log.Error(ex, "Concurrency error updating route with ID {RouteId}", id);
                if (!await RouteExists(id))
                {
                    return NotFound();
                }
                throw;
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRoute(long id)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to delete route by non-admin user");
                return Forbid();
            }

            Log.Information("Deleting route with ID {RouteId}", id);
            var route = await _context.Marshuti.FindAsync(id);
            if (route == null)
            {
                Log.Warning("Route with ID {RouteId} not found for deletion", id);
                return NotFound();
            }

            _context.Marshuti.Remove(route);
            await _context.SaveChangesAsync();

            Log.Information("Successfully deleted route with ID {RouteId}", id);
            return NoContent();
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<Marshut>>> SearchRoutes(
            [FromQuery] string? startPoint = null,
            [FromQuery] string? endPoint = null,
            [FromQuery] string? busModel = null,
            [FromQuery] string? driverName = null)
        {
            Log.Information("Searching routes with start point: {StartPoint}, end point: {EndPoint}, bus model: {BusModel}, driver name: {DriverName}",
                startPoint ?? "any", endPoint ?? "any", busModel ?? "any", driverName ?? "any");

            var query = _context.Marshuti
                .Include(r => r.Avtobus)
                .Include(r => r.Employee)
                .Include(r => r.Tickets)
                .AsQueryable();

            if (!string.IsNullOrEmpty(startPoint))
                query = query.Where(r => r.StartPoint.Contains(startPoint));

            if (!string.IsNullOrEmpty(endPoint))
                query = query.Where(r => r.EndPoint.Contains(endPoint));

            if (!string.IsNullOrEmpty(busModel))
                query = query.Where(r => r.Avtobus.Model.Contains(busModel));

            if (!string.IsNullOrEmpty(driverName))
                query = query.Where(r => r.Employee.Name.Contains(driverName) || 
                                       r.Employee.Surname.Contains(driverName));

            var results = await query.ToListAsync();
            Log.Debug("Found {RouteCount} routes matching search criteria", results.Count);
            return results;
        }

        private async Task<bool> RouteExists(long id)
        {
            return await _context.Marshuti.AnyAsync(e => e.RouteId == id);
        }
    }

    public class CreateRouteModel
    {
        public required string StartPoint { get; set; }
        public required string EndPoint { get; set; }
        public required long BusId { get; set; }
        public required long DriverId { get; set; }
        public required string TravelTime { get; set; }
    }

    public class UpdateRouteModel
    {
        public string? StartPoint { get; set; }
        public string? EndPoint { get; set; }
        public long? BusId { get; set; }
        public long? DriverId { get; set; }
        public string? TravelTime { get; set; }
    }
}
