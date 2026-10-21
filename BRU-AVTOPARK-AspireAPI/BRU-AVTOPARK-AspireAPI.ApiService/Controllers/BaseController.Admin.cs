using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.AdminServer.Controllers;

public abstract partial class BaseController
{
    protected async Task<MutationResult> AdminMutationAsync(string operation, object command, RealtimeCrudRequest? request = null)
    {
        var actor = await RequireApplicationActorAsync();
        var id = request == null ? ApplicationOperationId() : ApplicationOperationId(request);
        return (MutationResult)(await HttpContext.RequestServices.GetRequiredService<ApplicationOperations>()
            .ExecuteAsync(operation, actor, JsonSerializer.SerializeToElement(command, ApiProtocol.Json), id, HttpContext.RequestAborted))!;
    }
    protected ulong? AdminExpectedRevision(RealtimeCrudRequest? request = null)
    {
        if (request?.Payload is { ValueKind: JsonValueKind.Object } payload)
            foreach (var field in payload.EnumerateObject())
                if (field.Name.Equals("expectedRevision", StringComparison.OrdinalIgnoreCase) && field.Value.ValueKind != JsonValueKind.Null)
                    return field.Value.TryGetUInt64(out var revision) ? revision : throw new OperationException("validation_failed");
        if (!Request.Headers.TryGetValue("If-Match", out var header)) return null;
        return ulong.TryParse(header.ToString().Trim('"'), out var value) ? value : throw new OperationException("validation_failed");
    }
    protected static bool IsApplicationFailure(Exception exception) => exception is OperationException or ReducerRejectedException or
        DatabaseUnavailableException or CommitOutcomeUnknownException or TimeoutException or OperationCanceledException;
    private ISpacetimeDBService AdminDatabase => HttpContext.RequestServices.GetRequiredService<ISpacetimeDBService>();
    protected static T AdminPayload<T>(JsonElement payload) => payload.Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new OperationException("validation_failed");
    protected static RealtimeCrudRequest AdminRequest(string command, string id, uint? entityId, JsonElement? payload) => new(command, id, entityId, payload) { OperationId = id };
    protected static bool IsAdministrativeCommand(string resource, string? command) => (resource.ToLowerInvariant().Replace("_", "").Replace("-", ""), command?.ToLowerInvariant().Replace('_', '-')) is
        ("users", "set-active" or "assign-role" or "remove-role") or ("roles", "grant-permission" or "revoke-permission") or ("schedules" or "routeschedules", "set-active");
    protected async Task<object> AdminSpecialRealtimeAsync(string resource, RealtimeCrudRequest request)
    {
        var id = request.Id ?? throw new OperationException("validation_failed");
        var command = request.Command!.ToLowerInvariant().Replace('_', '-');
        var revision = AdminExpectedRevision(request);
        var payload = request.Payload ?? throw new OperationException("validation_failed");
        JsonElement Field(string name)
        {
            if (payload.ValueKind == JsonValueKind.Object)
                foreach (var field in payload.EnumerateObject()) if (field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return field.Value;
            throw new OperationException("validation_failed");
        }
        var canonical = resource.ToLowerInvariant().Replace("_", "").Replace("-", "") is "routeschedules" or "schedules" ? "schedules" : resource.ToLowerInvariant();
        object input = (canonical, command) switch
        {
            ("users", "set-active") => new SetUserActiveCommand(id, Field("active").GetBoolean(), revision),
            ("schedules", "set-active") => new SetScheduleActiveCommand(id, Field("active").GetBoolean(), revision),
            ("users", "assign-role") => new AssignUserRoleCommand(id, Field("roleId").GetUInt32(), revision),
            ("users", "remove-role") => new RemoveUserRoleCommand(id, Field("roleId").GetUInt32(), revision),
            ("roles", "grant-permission") => new GrantRolePermissionCommand(id, Field("permissionId").GetUInt32(), revision),
            ("roles", "revoke-permission") => new RevokeRolePermissionCommand(id, Field("permissionId").GetUInt32(), revision),
            _ => throw new OperationException("unsupported_operation")
        };
        var receipt = await AdminMutationAsync(canonical + "." + command, input, request);
        return new { operation = command, success = true, entityId = receipt.EntityId, revision = receipt.Revision };
    }
    protected async Task<Ticket?> AdminCreateTicketEntityAsync(CreateTicketModel model, RealtimeCrudRequest? request = null)
    {
        var result = await AdminMutationAsync("tickets.create", new CreateTicketCommand(model.RouteId, model.TicketPrice, model.SeatNumber, model.PaymentMethod), request);
        return await AdminDatabase.ReadAsync(c => c.Db.Ticket.TicketId.Find(result.EntityId), HttpContext.RequestAborted);
    }
    protected Task<bool> AdminUpdateTicketAsync(uint id, UpdateTicketModel model, RealtimeCrudRequest? request = null) =>
        AdminBooleanAsync("tickets.update", new UpdateTicketCommand(id, model.RouteId, model.TicketPrice, model.SeatNumber, model.PaymentMethod, model.IsActive, model.ExpectedRevision ?? AdminExpectedRevision(request)), request);
    protected async Task<bool> AdminBooleanAsync(string operation, object command, RealtimeCrudRequest? request = null)
    { await AdminMutationAsync(operation, command, request); return true; }
    protected async Task<UserProfile?> AdminCreateUserAsync(CreateUserModel model, RealtimeCrudRequest? request = null)
    {
        uint? role = null;
        if (model.Role != 0) role = await AdminDatabase.ReadAsync(c => c.Db.Role.Iter().FirstOrDefault(r => r.LegacyRoleId == model.Role)?.RoleId, HttpContext.RequestAborted)
            ?? throw new OperationException("validation_failed");
        var receipt = await AdminMutationAsync("users.create", new CreateUserCommand(model.Login, model.Password, model.Email, model.PhoneNumber, role), request);
        return await AdminDatabase.ReadAsync(c => c.Db.UserProfile.LegacyUserId.Find(receipt.EntityId), HttpContext.RequestAborted);
    }
    protected async Task<bool> AdminUpdateUserAsync(uint id, UpdateUserModel model, RealtimeCrudRequest? request = null)
    {
        // Credential reset/login/role changes are not profile mutations. Never silently accept them.
        if (model.Login != null || model.Password != null || model.Role != null || model.LastLoginAt != null ||
            model.IsActive.HasValue && (model.Email != null || model.PhoneNumber != null)) throw new OperationException("validation_failed");
        return model.IsActive.HasValue
            ? await AdminBooleanAsync("users.set-active", new SetUserActiveCommand(id, model.IsActive.Value, model.ExpectedRevision ?? AdminExpectedRevision(request)), request)
            : await AdminBooleanAsync("users.update", new UpdateUserCommand(id, model.Email, model.PhoneNumber, model.ExpectedRevision ?? AdminExpectedRevision(request)), request);
    }
    protected async Task<Role?> AdminCreateRoleAsync(CreateRoleModel model, RealtimeCrudRequest? request = null)
    {
        var receipt = await AdminMutationAsync("roles.create", new CreateRoleCommand(model.Name, model.Description, model.LegacyRoleId, false, model.Priority, model.PermissionIds?.ToArray()), request);
        return await AdminDatabase.ReadAsync(c => c.Db.Role.RoleId.Find(receipt.EntityId), HttpContext.RequestAborted);
    }
    protected Task<bool> AdminUpdateRoleAsync(uint id, UpdateRoleModel model, RealtimeCrudRequest? request = null) =>
        AdminBooleanAsync("roles.update", new UpdateRoleCommand(id, model.Name, model.Description, null, model.Priority, model.ExpectedRevision ?? AdminExpectedRevision(request), model.PermissionIds?.ToArray()), request);
    protected async Task<Permission?> AdminCreatePermissionAsync(CreatePermissionModel model, RealtimeCrudRequest? request = null)
    {
        var receipt = await AdminMutationAsync("permissions.create", new CreatePermissionCommand(model.Name, model.Description, model.Category), request);
        return await AdminDatabase.ReadAsync(c => c.Db.Permission.PermissionId.Find(receipt.EntityId), HttpContext.RequestAborted);
    }
    protected Task<bool> AdminUpdatePermissionAsync(uint id, UpdatePermissionModel model, RealtimeCrudRequest? request = null) =>
        AdminBooleanAsync("permissions.update", new UpdatePermissionCommand(id, model.Name, model.Description, model.Category, model.IsActive, model.ExpectedRevision ?? AdminExpectedRevision(request)), request);
    protected async Task<uint?> AdminCreateScheduleAsync(CreateRouteScheduleModel model, RealtimeCrudRequest? request = null)
    {
        var receipt = await AdminMutationAsync("schedules.create", new CreateScheduleCommand(model.RouteId, AdminTime(model.DepartureTime)!.Value, model.Price, model.AvailableSeats,
            model.DaysOfWeek, model.StartPoint, model.EndPoint, model.RouteStops, AdminTime(model.ArrivalTime), model.StopDurationMinutes, model.IsRecurring, model.EstimatedStopTimes, model.StopDistances, model.Notes, model.BusTypes), request);
        return receipt.EntityId;
    }
    protected Task<bool> AdminUpdateScheduleAsync(uint id, UpdateRouteScheduleModel model, RealtimeCrudRequest? request = null)
    {
        if (model.ValidUntil.HasValue || model.IsActive.HasValue && (model.RouteId.HasValue || model.StartPoint != null || model.EndPoint != null || model.RouteStops != null || model.DepartureTime.HasValue ||
            model.ArrivalTime.HasValue || model.Price.HasValue || model.AvailableSeats.HasValue || model.DaysOfWeek != null || model.BusTypes != null || model.StopDurationMinutes.HasValue || model.IsRecurring.HasValue || model.EstimatedStopTimes != null || model.StopDistances != null || model.Notes != null))
            throw new OperationException("validation_failed");
        return model.IsActive.HasValue
            ? AdminBooleanAsync("schedules.set-active", new SetScheduleActiveCommand(id, model.IsActive.Value, model.ExpectedRevision ?? AdminExpectedRevision(request)), request)
            : AdminBooleanAsync("schedules.update", new UpdateScheduleCommand(id, model.RouteId, AdminTime(model.DepartureTime), model.Price, model.AvailableSeats, model.DaysOfWeek,
                model.StartPoint, model.EndPoint, model.RouteStops, AdminTime(model.ArrivalTime), model.StopDurationMinutes, model.IsRecurring, model.EstimatedStopTimes, model.StopDistances,
                model.Notes, model.BusTypes, model.ExpectedRevision ?? AdminExpectedRevision(request)), request);
    }
    private static ulong? AdminTime(DateTime? value)
    {
        if (!value.HasValue) return null;
        var utc = value.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value.Value.ToUniversalTime();
        var milliseconds = new DateTimeOffset(utc).ToUnixTimeMilliseconds();
        return milliseconds < 0 ? throw new OperationException("validation_failed") : (ulong)milliseconds;
    }
}
