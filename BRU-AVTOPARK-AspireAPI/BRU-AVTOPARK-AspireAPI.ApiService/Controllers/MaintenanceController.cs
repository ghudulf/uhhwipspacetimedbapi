using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Serilog;
using System.Text.Json; // Added for serialization logging
using Log = Serilog.Log;
using SpacetimeDB.Types;
using SpacetimeDB; // Added for direct DB access
using TicketSalesApp.Services.Interfaces;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;

namespace TicketSalesApp.AdminServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // Allows both custom JWT (manual parsing) and ASP.NET Core auth (OpenIddict)
    public class MaintenanceController : BaseController
    {
        private readonly IMaintenanceService _maintenanceService;
        private readonly ILogger<MaintenanceController> _logger;
        private readonly ISpacetimeDBService _spacetimeService; // Added SpacetimeDBService

        private readonly IRealtimeEventBus _realtimeEventBus;

        /// <summary>
        /// Initializes a new instance of <see cref="MaintenanceController"/> with its required services.
        /// </summary>
        /// <param name="maintenanceService">Service that manages maintenance record operations.</param>
        /// <param name="logger">Logger for controller diagnostics and informational events.</param>
        /// <param name="spacetimeService">Database service used to query related Bus data.</param>
        /// <param name="realtimeEventBus">Real-time event bus used to publish and subscribe maintenance CRUD events.</param>
        /// <exception cref="ArgumentNullException">Thrown when any required dependency is <c>null</c>.</exception>
        public MaintenanceController(
            IMaintenanceService maintenanceService,
            ILogger<MaintenanceController> logger,
            ISpacetimeDBService spacetimeService, // Added SpacetimeDBService
            IRealtimeEventBus realtimeEventBus)
        {
            _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService)); // Added SpacetimeDBService
            _realtimeEventBus = realtimeEventBus ?? throw new ArgumentNullException(nameof(realtimeEventBus));
        }

        

        /// <summary>
        /// Streams maintenance CRUD events over a WebSocket to an authenticated client.
        /// If the caller is not authenticated, responds with HTTP 401 and does not start a stream.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token that cancels the WebSocket streaming session and related operations.</param>
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
                _realtimeEventBus.SubscribeAsync("maintenance", cancellationToken),
                HandleRealtimeCrudAsync,
                _logger,
                cancellationToken);
        }

        /// <summary>
        /// Dispatches a realtime CRUD request for maintenance records based on the request's Command and returns the corresponding result payload.
        /// </summary>
        /// <param name="request">The realtime CRUD request; its Command determines the action. Supported commands: "read_all" (returns all records), "read" (requires request.Id and returns a single record), "create" (creates a record), "update" (updates a record), and "delete" (deletes a record).</param>
        /// <param name="cancellationToken">Token to observe while processing the request.</param>
        /// <returns>
        /// An object whose shape depends on the command:
        /// - For "read_all": { records = IEnumerable&lt;MaintenanceRecord&gt; }
        /// - For "read": { record = MaintenanceRecord }
        /// - For "create"/"update"/"delete": a command-specific operation result (includes success status and related data/snapshot).
        /// </returns>
        /// <exception cref="InvalidOperationException">Thrown when "read" is requested without an Id or when the Command value is unsupported.</exception>
        private async Task<object> HandleRealtimeCrudAsync(RealtimeCrudRequest request, CancellationToken cancellationToken)
        {
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
        /// Handle the realtime "read_all" command and return projected maintenance records with timestamps converted and bus data enriched.
        /// </summary>
        /// <returns>An object with a `records` property containing all projected maintenance records matching the HTTP API shape.</returns>
        private async Task<object> HandleReadAllCommandAsync()
        {
            var records = await _maintenanceService.GetAllMaintenanceRecordsAsync();

            // Map to anonymous type matching REST endpoint projection
            var result = records.Select(m => new {
                m.MaintenanceId,
                m.BusId,
                m.LastServiceDate,
                m.MileageThreshold,
                m.MaintenanceType,
                m.ServiceEngineer,
                m.FoundIssues,
                m.NextServiceDate,
                m.Roadworthiness,
                m.MaintenanceCost,
                m.PartsReplaced,
                m.MaintenanceDuration,
                m.IsScheduled,
                m.MaintenanceLocation,
                m.ScheduledByEmployeeId,
                m.CompletedByEmployeeId,
                m.MaintenanceNotes,
                m.MaintenanceStatus,
                m.DiagnosticCodes,
                m.LaborCost,
                m.PartsCost
            }).ToList();

            return new { records = result };
        }

        /// <summary>
        /// Handle a realtime "read" command and return a single projected maintenance record with enriched bus data and timestamp conversion.
        /// </summary>
        /// <param name="request">The realtime request; its Id must be provided to identify the maintenance record.</param>
        /// <returns>An object with a `record` property containing the projected maintenance record matching the HTTP API shape, or null if not found.</returns>
        /// <exception cref="InvalidOperationException">Thrown when request.Id is not provided.</exception>
        private async Task<object> HandleReadCommandAsync(RealtimeCrudRequest request)
        {
            var id = request.Id ?? throw new InvalidOperationException("id is required for read");
            var maintenance = await _maintenanceService.GetMaintenanceByIdAsync(id);

            if (maintenance == null)
            {
                return new { record = (object?)null };
            }

            var conn = _spacetimeService.GetConnection();
            var bus = conn.Db.Bus.BusId.Find(maintenance.BusId);

            // Map to anonymous type matching REST endpoint projection
            var result = new {
                maintenance.MaintenanceId,
                maintenance.BusId,
                Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                LastServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)maintenance.LastServiceDate).DateTime,
                maintenance.ServiceEngineer,
                maintenance.FoundIssues,
                NextServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)maintenance.NextServiceDate).DateTime,
                maintenance.Roadworthiness,
                maintenance.MaintenanceType,
                maintenance.MileageThreshold
            };

            return new { record = result };
        }

        /// <summary>
        /// Handle a realtime "create" CRUD command and create a new maintenance record.
        /// </summary>
        /// <param name="request">Realtime CRUD request whose payload must contain a CreateMaintenanceModel.</param>
        /// <returns>An object with operation = "create", `success` set to `true` if the creation succeeded and `false` otherwise, and `snapshot` containing the full list of maintenance records.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin and lacks the "maintenance.create" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request payload is missing or cannot be deserialized into a CreateMaintenanceModel.</exception>
        private async Task<object> HandleCreateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin()) throw new UnauthorizedAccessException("Not authorized for maintenance.create");
            var model = request.Payload?.Deserialize<CreateMaintenanceModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for create");
            var createdId = await _maintenanceService.CreateMaintenanceAsync(model.BusId, (ulong)new DateTimeOffset(model.LastServiceDate).ToUnixTimeMilliseconds(), model.ServiceEngineer, model.FoundIssues, (ulong)new DateTimeOffset(model.NextServiceDate).ToUnixTimeMilliseconds(), model.Roadworthiness, "General");
            var record = createdId ? await _maintenanceService.GetMaintenanceByBusIdAsync(model.BusId).ContinueWith(t => t.Result.OrderByDescending(r => r.LastServiceDate).FirstOrDefault()) : null;
            var result = new { operation = "create", success = createdId, record };

            if (createdId)
            {
                try
                {
                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "maintenance.created",
                        Resource: "maintenance",
                        HttpMethod: "POST",
                        StatusCode: 201,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: null,
                        UserName: null,
                        Tenant: null,
                        SourceIp: "internal",
                        Metadata: new Dictionary<string, string> { ["operation"] = "create", ["success"] = "true" }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for maintenance.created (Resource: maintenance, EventName: maintenance.created, RecordId: {RecordId})", record?.MaintenanceId);
                }
            }

            return result;
        }

        /// <summary>
        /// Handle an incoming realtime "update" command for a maintenance record and return the operation result with an updated entity snapshot.
        /// </summary>
        /// <param name="request">Realtime CRUD request containing the target Id and a payload deserializable to <see cref="UpdateMaintenanceModel"/>.</param>
        /// <returns>
        /// An object with:
        /// - `operation`: the operation name ("update"),
        /// - `success`: `true` if the update succeeded, `false` otherwise,
        /// - `entity`: the updated maintenance entity (or null if not found),
        /// - `snapshot`: the current list of all maintenance records.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin and lacks the "maintenance.edit" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request is missing the required Id or payload for the update.</exception>
        private async Task<object> HandleUpdateCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin()) throw new UnauthorizedAccessException("Not authorized for maintenance.edit");
            var id = request.Id ?? throw new InvalidOperationException("id is required for update");
            var model = request.Payload?.Deserialize<UpdateMaintenanceModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("payload is required for update");
            var success = await _maintenanceService.UpdateMaintenanceAsync(id, model.BusId, model.LastServiceDate.HasValue ? (ulong)new DateTimeOffset(model.LastServiceDate.Value).ToUnixTimeMilliseconds() : null, model.ServiceEngineer, model.FoundIssues, model.NextServiceDate.HasValue ? (ulong)new DateTimeOffset(model.NextServiceDate.Value).ToUnixTimeMilliseconds() : null, model.Roadworthiness);
            var maintenance = await _maintenanceService.GetMaintenanceByIdAsync(id);

            // Project entity to match REST endpoint shape
            object? projectedEntity = null;
            if (maintenance != null)
            {
                var conn = _spacetimeService.GetConnection();
                var bus = conn.Db.Bus.BusId.Find(maintenance.BusId);

                projectedEntity = new {
                    maintenance.MaintenanceId,
                    maintenance.BusId,
                    Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                    LastServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)maintenance.LastServiceDate).DateTime,
                    maintenance.ServiceEngineer,
                    maintenance.FoundIssues,
                    NextServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)maintenance.NextServiceDate).DateTime,
                    maintenance.Roadworthiness,
                    maintenance.MaintenanceType,
                    maintenance.MileageThreshold
                };
            }

            var result = new { operation = "update", success, entity = projectedEntity, record = projectedEntity };

            if (success)
            {
                try
                {
                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "maintenance.updated",
                        Resource: "maintenance",
                        HttpMethod: "PUT",
                        StatusCode: 200,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: null,
                        UserName: null,
                        Tenant: null,
                        SourceIp: "internal",
                        Metadata: new Dictionary<string, string> { ["operation"] = "update", ["success"] = "true" }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for maintenance.updated (Resource: maintenance, EventName: maintenance.updated, MaintenanceId: {MaintenanceId})", id);
                }
            }

            return result;
        }

        /// <summary>
        /// Handle a realtime "delete" CRUD command by deleting the specified maintenance record and returning an operation snapshot.
        /// </summary>
        /// <param name="request">Realtime CRUD request containing the target record Id and optional payload metadata.</param>
        /// <returns>
        /// An object with properties:
        /// - operation: the string "delete",
        /// - success: `true` if the delete succeeded, `false` otherwise,
        /// - deletedId: the Id of the deleted record,
        /// - snapshot: the full list of maintenance records after the operation.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks admin role and the "maintenance.delete" permission.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request does not include an Id for the delete operation.</exception>
        private async Task<object> HandleDeleteCommandAsync(RealtimeCrudRequest request)
        {
            if (!IsAdmin()) throw new UnauthorizedAccessException("Not authorized for maintenance.delete");
            var id = request.Id ?? throw new InvalidOperationException("id is required for delete");
            var success = await _maintenanceService.DeleteMaintenanceAsync(id);
            var result = new { operation = "delete", success, deletedId = id, record = (object?)null };

            if (success)
            {
                try
                {
                    await _realtimeEventBus.PublishAsync(new ApiDomainEvent(
                        EventName: "maintenance.deleted",
                        Resource: "maintenance",
                        HttpMethod: "DELETE",
                        StatusCode: 200,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: Guid.NewGuid().ToString(),
                        UserId: null,
                        UserName: null,
                        Tenant: null,
                        SourceIp: "internal",
                        Metadata: new Dictionary<string, string> { ["operation"] = "delete", ["success"] = "true" }
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish realtime event for maintenance.deleted (Resource: maintenance, EventName: maintenance.deleted, DeletedId: {DeletedId})", id);
                }
            }

            return result;
        }

        /// <summary>
        /// Retrieves all maintenance records projected to an anonymous shape containing all fields required by clients.
        /// </summary>
        /// <returns>A list of maintenance record objects with the complete field set used by clients for deserialization.</returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetMaintenanceRecords()
        {
            Log.Information("Fetching all maintenance records");
            var records = await _maintenanceService.GetAllMaintenanceRecordsAsync();
            
            // Map to anonymous type with ALL fields - CRITICAL for client deserialization
            var result = records.Select(m => new {
                m.MaintenanceId,
                m.BusId,
                m.LastServiceDate,
                m.MileageThreshold,
                m.MaintenanceType,
                m.ServiceEngineer,
                m.FoundIssues,
                m.NextServiceDate,
                m.Roadworthiness,
                m.MaintenanceCost,
                m.PartsReplaced,
                m.MaintenanceDuration,
                m.IsScheduled,
                m.MaintenanceLocation,
                m.ScheduledByEmployeeId,
                m.CompletedByEmployeeId,
                m.MaintenanceNotes,
                m.MaintenanceStatus,
                m.DiagnosticCodes,
                m.LaborCost,
                m.PartsCost
            }).ToList();

            Log.Debug("Retrieved {RecordCount} maintenance records", result.Count);
            _logger.LogInformation("FULL MAINTENANCE DATA: {MaintenanceData}", JsonSerializer.Serialize(result));
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<dynamic>> GetMaintenanceRecord(uint id) // Changed return type
        {
            Log.Information("Fetching maintenance record with ID {MaintenanceId}", id);
            var maintenance = await _maintenanceService.GetMaintenanceByIdAsync(id);

            if (maintenance == null)
            {
                Log.Warning("Maintenance record with ID {MaintenanceId} not found", id);
                return NotFound();
            }

            var conn = _spacetimeService.GetConnection();
            var bus = conn.Db.Bus.BusId.Find(maintenance.BusId);

            // Map to anonymous type
            var result = new {
                maintenance.MaintenanceId,
                maintenance.BusId,
                Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                LastServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)maintenance.LastServiceDate).DateTime,
                maintenance.ServiceEngineer,
                maintenance.FoundIssues,
                NextServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)maintenance.NextServiceDate).DateTime,
                maintenance.Roadworthiness,
                maintenance.MaintenanceType,
                maintenance.MileageThreshold
            };

            Log.Debug("Successfully retrieved maintenance record with ID {MaintenanceId}", id);
            _logger.LogInformation("FULL MAINTENANCE DATA: {MaintenanceData}", JsonSerializer.Serialize(result)); // Added JSON logging
            return Ok(result); // Return mapped result
        }

        [HttpPost]
        public async Task<ActionResult<Maintenance>> CreateMaintenanceRecord([FromBody] CreateMaintenanceModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to create maintenance record by non-admin user");
                return Forbid();
            }

            Log.Information("Creating new maintenance record for bus ID {BusId}", model.BusId);

            var success = await _maintenanceService.CreateMaintenanceAsync(
                model.BusId,
                (ulong)new DateTimeOffset(model.LastServiceDate).ToUnixTimeMilliseconds(),
                model.ServiceEngineer,
                model.FoundIssues,
                (ulong)new DateTimeOffset(model.NextServiceDate).ToUnixTimeMilliseconds(),
                model.Roadworthiness,
                "Regular" // Default maintenance type
            );

            if (!success)
            {
                Log.Warning("Failed to create maintenance record");
                return BadRequest("Failed to create maintenance record");
            }

            // Get the newly created record
            var records = await _maintenanceService.GetMaintenanceByBusIdAsync(model.BusId);
            var record = records.OrderByDescending(r => r.LastServiceDate).FirstOrDefault();

            if (record == null)
            {
                Log.Error("Maintenance record was created but could not be retrieved");
                return StatusCode(500, "Maintenance record was created but could not be retrieved");
            }

            Log.Information("Successfully created maintenance record with ID {MaintenanceId}", record.MaintenanceId);
            return CreatedAtAction(nameof(GetMaintenanceRecord), new { id = record.MaintenanceId }, record);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMaintenanceRecord(uint id, [FromBody] UpdateMaintenanceModel model)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to update maintenance record by non-admin user");
                return Forbid();
            }

            Log.Information("Updating maintenance record with ID {MaintenanceId}", id);

            var success = await _maintenanceService.UpdateMaintenanceAsync(
                id,
                model.BusId,
                model.LastServiceDate.HasValue ? (ulong)new DateTimeOffset(model.LastServiceDate.Value).ToUnixTimeMilliseconds() : null,
                model.ServiceEngineer,
                model.FoundIssues,
                model.NextServiceDate.HasValue ? (ulong)new DateTimeOffset(model.NextServiceDate.Value).ToUnixTimeMilliseconds() : null,
                model.Roadworthiness
            );

            if (!success)
            {
                Log.Warning("Maintenance record with ID {MaintenanceId} not found for update", id);
                return NotFound();
            }

            Log.Information("Successfully updated maintenance record with ID {MaintenanceId}", id);
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaintenanceRecord(uint id)
        {
            if (!IsAdmin())
            {
                Log.Warning("Unauthorized attempt to delete maintenance record by non-admin user");
                return Forbid();
            }

            Log.Information("Deleting maintenance record with ID {MaintenanceId}", id);

            var success = await _maintenanceService.DeleteMaintenanceAsync(id);
            if (!success)
            {
                Log.Warning("Maintenance record with ID {MaintenanceId} not found for deletion", id);
                return NotFound();
            }

            Log.Information("Successfully deleted maintenance record with ID {MaintenanceId}", id);
            return NoContent();
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<dynamic>>> SearchMaintenanceRecords( // Changed return type
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string? busModel = null,
            [FromQuery] string? engineer = null,
            [FromQuery] string? roadworthiness = null)
        {
            Log.Information("Searching maintenance records with parameters: StartDate={StartDate}, EndDate={EndDate}, BusModel={BusModel}, Engineer={Engineer}, Roadworthiness={Roadworthiness}",
                startDate, endDate, busModel, engineer, roadworthiness);

            var records = await _maintenanceService.GetAllMaintenanceRecordsAsync();
            var conn = _spacetimeService.GetConnection();
            var query = records.AsEnumerable();

            if (startDate.HasValue)
            {
                var startTimestamp = (ulong)new DateTimeOffset(startDate.Value).ToUnixTimeMilliseconds();
                query = query.Where(m => m.LastServiceDate >= startTimestamp);
            }

            if (endDate.HasValue)
            {
                var endTimestamp = (ulong)new DateTimeOffset(endDate.Value).ToUnixTimeMilliseconds();
                query = query.Where(m => m.LastServiceDate <= endTimestamp);
            }

            if (!string.IsNullOrEmpty(engineer))
                query = query.Where(m => m.ServiceEngineer.Contains(engineer, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(roadworthiness))
                query = query.Where(m => m.Roadworthiness.Equals(roadworthiness, StringComparison.OrdinalIgnoreCase));

            // Filter by bus model (requires joining with Bus table)
            if (!string.IsNullOrEmpty(busModel))
            {
                query = query.Where(m => {
                    var bus = conn.Db.Bus.BusId.Find(m.BusId);
                    return bus != null && bus.Model.Contains(busModel, StringComparison.OrdinalIgnoreCase);
                });
            }

            // Map to anonymous type
            var result = query.Select(m => {
                var bus = conn.Db.Bus.BusId.Find(m.BusId);
                return new {
                    m.MaintenanceId,
                    m.BusId,
                    Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                    LastServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)m.LastServiceDate).DateTime,
                    m.ServiceEngineer,
                    m.FoundIssues,
                    NextServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)m.NextServiceDate).DateTime,
                    m.Roadworthiness,
                    m.MaintenanceType,
                    m.MileageThreshold
                };
            }).ToList();

            Log.Debug("Found {RecordCount} maintenance records matching search criteria", result.Count);
            _logger.LogInformation("FULL SEARCH RESULTS DATA: {MaintenanceData}", JsonSerializer.Serialize(result)); // Added JSON logging
            return Ok(result); // Return mapped result
        }

        [HttpGet("due-maintenance")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetDueMaintenanceRecords() // Changed return type
        {
            Log.Information("Fetching due maintenance records");
            var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var records = await _maintenanceService.GetAllMaintenanceRecordsAsync();
            var conn = _spacetimeService.GetConnection();
            
            var dueRecords = records.Where(m => m.NextServiceDate <= now)
                           .OrderBy(m => m.NextServiceDate)
                           .ToList();
            
            // Map to anonymous type
            var result = dueRecords.Select(m => {
                var bus = conn.Db.Bus.BusId.Find(m.BusId);
                return new {
                    m.MaintenanceId,
                    m.BusId,
                    Bus = bus != null ? new { bus.BusId, bus.Model, bus.RegistrationNumber } : null,
                    LastServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)m.LastServiceDate).DateTime,
                    m.ServiceEngineer,
                    m.FoundIssues,
                    NextServiceDate = DateTimeOffset.FromUnixTimeMilliseconds((long)m.NextServiceDate).DateTime,
                    m.Roadworthiness,
                    m.MaintenanceType,
                    m.MileageThreshold
                };
            }).ToList();
            
            Log.Debug("Found {RecordCount} due maintenance records", result.Count);
            _logger.LogInformation("FULL DUE MAINTENANCE DATA: {MaintenanceData}", JsonSerializer.Serialize(result)); // Added JSON logging
            return Ok(result); // Return mapped result
        }
    }

    public class CreateMaintenanceModel
    {
        public required uint BusId { get; set; }
        public required DateTime LastServiceDate { get; set; }
        public required string ServiceEngineer { get; set; }
        public required string FoundIssues { get; set; }
        public required DateTime NextServiceDate { get; set; }
        public required string Roadworthiness { get; set; }
        public string MaintenanceType { get; set; } = "Regular";
    }

    public class UpdateMaintenanceModel
    {
        public uint? BusId { get; set; }
        public DateTime? LastServiceDate { get; set; }
        public string? ServiceEngineer { get; set; }
        public string? FoundIssues { get; set; }
        public DateTime? NextServiceDate { get; set; }
        public string? Roadworthiness { get; set; }
    }
} 