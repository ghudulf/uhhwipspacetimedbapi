using SpacetimeDB;
using System.Globalization;

public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void CreateTicketV2(ReducerContext ctx, string operationId, Identity actingUser, uint routeId, double price, uint seatNumber, string paymentMethod, ulong? purchaseTime)
        => CommitCatalogMutation(ctx, operationId, actingUser, "tickets", "create", null, null,
            MutationHash(routeId.ToString(), price.ToString("R", CultureInfo.InvariantCulture), seatNumber.ToString(), paymentMethod, purchaseTime?.ToString()),
            actor => CreateTicketCore(ctx, routeId, price, seatNumber, paymentMethod, purchaseTime, actor));

    [SpacetimeDB.Reducer]
    public static void UpdateTicketV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, uint? routeId, uint? seatNumber, double? price, string? paymentMethod, bool? isActive)
        => CommitCatalogMutation(ctx, operationId, actingUser, "tickets", "update", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), routeId?.ToString(), seatNumber?.ToString(), price?.ToString("R", CultureInfo.InvariantCulture), paymentMethod, isActive?.ToString()),
            actor => { UpdateTicketCore(ctx, entityId, routeId, seatNumber, price, paymentMethod, isActive, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void DeleteTicketV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision)
        => CommitCatalogMutation(ctx, operationId, actingUser, "tickets", "delete", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString()), actor => { DeleteTicketCore(ctx, entityId, actor); return entityId; });

    private static string ScheduleMutationHash(uint? id, ulong? revision, uint? routeId, ulong? departure, double? price, uint? seats,
        string[]? days, string? start, string? end, string[]? stops, ulong? arrival, uint? duration, bool? recurring,
        string[]? times, double[]? distances, string? notes, string[]? busTypes) => MutationHash(id?.ToString(), revision?.ToString(), routeId?.ToString(),
            departure?.ToString(), price?.ToString("R", CultureInfo.InvariantCulture), seats?.ToString(),
            days == null ? null : MutationHash(days), start, end, stops == null ? null : MutationHash(stops), arrival?.ToString(), duration?.ToString(), recurring?.ToString(),
            times == null ? null : MutationHash(times), distances == null ? null : MutationHash(distances.Select(d => d.ToString("R", CultureInfo.InvariantCulture)).ToArray()), notes,
            busTypes == null ? null : MutationHash(busTypes));

    [SpacetimeDB.Reducer]
    public static void CreateScheduleV2(ReducerContext ctx, string operationId, Identity actingUser, uint routeId, ulong departureTime, double price, uint availableSeats,
        string[]? daysOfWeek, string? startPoint, string? endPoint, string[]? routeStops, ulong? arrivalTime, uint? stopDurationMinutes,
        bool? isRecurring, string[]? estimatedStopTimes, double[]? stopDistances, string? notes, string[]? busTypes)
        => CommitCatalogMutation(ctx, operationId, actingUser, "schedules", "create", null, null,
            ScheduleMutationHash(null, null, routeId, departureTime, price, availableSeats, daysOfWeek, startPoint, endPoint, routeStops, arrivalTime, stopDurationMinutes, isRecurring, estimatedStopTimes, stopDistances, notes, busTypes),
            actor =>
            {
                if (busTypes?.Length > 200) throw new Exception("Too many bus types.");
                if (busTypes != null) foreach (var type in busTypes) ValidateText(type, 200, true);
                var id = CreateRouteScheduleCore(ctx, routeId, departureTime, price, availableSeats, daysOfWeek, startPoint, endPoint, routeStops, arrivalTime, stopDurationMinutes, isRecurring, estimatedStopTimes, stopDistances, notes, actor);
                var schedule = ctx.Db.RouteSchedule.ScheduleId.Find(id)!; schedule.BusTypes = busTypes; ctx.Db.RouteSchedule.ScheduleId.Update(schedule);
                return id;
            });

    [SpacetimeDB.Reducer]
    public static void UpdateScheduleV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, uint? routeId,
        string? startPoint, string? endPoint, string[]? routeStops, ulong? departureTime, ulong? arrivalTime, double? price, uint? availableSeats,
        string[]? daysOfWeek, string[]? busTypes, uint? stopDurationMinutes, bool? isRecurring, string[]? estimatedStopTimes, double[]? stopDistances, string? notes)
        => CommitCatalogMutation(ctx, operationId, actingUser, "schedules", "update", entityId, expectedRevision,
            ScheduleMutationHash(entityId, expectedRevision, routeId, departureTime, price, availableSeats, daysOfWeek, startPoint, endPoint, routeStops, arrivalTime, stopDurationMinutes, isRecurring, estimatedStopTimes, stopDistances, notes, busTypes),
            actor => { UpdateRouteScheduleCore(ctx, entityId, routeId, startPoint, endPoint, routeStops, departureTime, arrivalTime, price, availableSeats, daysOfWeek, busTypes, stopDurationMinutes, isRecurring, estimatedStopTimes, stopDistances, notes, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void DeleteScheduleV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision)
        => CommitCatalogMutation(ctx, operationId, actingUser, "schedules", "delete", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString()), actor => { DeleteRouteScheduleCore(ctx, entityId, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void SetScheduleActiveV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, bool active)
        => CommitCatalogMutation(ctx, operationId, actingUser, "schedules", "set-active", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), active.ToString()), actor => { if (active) ActivateScheduleCore(ctx, entityId, actor); else DeactivateScheduleCore(ctx, entityId, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void CreateUserV2(ReducerContext ctx, string operationId, Identity actingUser, string login, string initialCredential, string email, string phoneNumber, uint? roleId)
        => CommitCatalogMutation(ctx, operationId, actingUser, "users", "create", null, null,
            MutationHash(login, MutationHash(initialCredential), email, phoneNumber, roleId?.ToString()),
            actor => RegisterUserCore(ctx, login, initialCredential, email, phoneNumber, roleId, null, actor, ctx.NewUuidV4().ToString().Replace("-", "") + ctx.NewUuidV4().ToString().Replace("-", "")));

    [SpacetimeDB.Reducer]
    public static void UpdateUserV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, string? email, string? phoneNumber)
        => CommitCatalogMutation(ctx, operationId, actingUser, "users", "update", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), email, phoneNumber), actor => { UpdateUserCore(ctx, UserIdentity(ctx, entityId), null, null, null, phoneNumber, email, null, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void SetUserActiveV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, bool active)
        => CommitCatalogMutation(ctx, operationId, actingUser, "users", "set-active", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), active.ToString()), actor => { if (active) ActivateUserCore(ctx, UserIdentity(ctx, entityId), actor); else DeactivateUserCore(ctx, UserIdentity(ctx, entityId), actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void DeleteUserV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision)
        => CommitCatalogMutation(ctx, operationId, actingUser, "users", "delete", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString()), actor => { DeleteUserCore(ctx, UserIdentity(ctx, entityId), actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void AssignUserRoleV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, uint roleId)
        => CommitCatalogMutation(ctx, operationId, actingUser, "users", "assign-role", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), roleId.ToString()), actor => { AssignRoleCore(ctx, UserIdentity(ctx, entityId), roleId, actor); return entityId; }, "assign_roles");

    [SpacetimeDB.Reducer]
    public static void RemoveUserRoleV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, uint roleId)
        => CommitCatalogMutation(ctx, operationId, actingUser, "users", "remove-role", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), roleId.ToString()), actor => { RemoveRoleCore(ctx, UserIdentity(ctx, entityId), roleId, actor); return entityId; }, "assign_roles");

    [SpacetimeDB.Reducer]
    public static void CreateRoleV2(ReducerContext ctx, string operationId, Identity actingUser, int legacyRoleId, string name, string description, bool isSystem, uint priority, uint[]? permissionIds)
        => CommitCatalogMutation(ctx, operationId, actingUser, "roles", "create", null, null, MutationHash(legacyRoleId.ToString(), name, description, isSystem.ToString(), priority.ToString(), permissionIds == null ? null : MutationHash(permissionIds.Select(p => p.ToString()).ToArray())),
            actor => { var id = CreateRoleCore(ctx, legacyRoleId, name, description, isSystem, priority, actor); ReplaceRolePermissions(ctx, id, permissionIds, actor); return id; });

    [SpacetimeDB.Reducer]
    public static void UpdateRoleV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, string? name, string? description, int? legacyRoleId, uint? priority, uint[]? permissionIds)
        => CommitCatalogMutation(ctx, operationId, actingUser, "roles", "update", entityId, expectedRevision, MutationHash(entityId.ToString(), expectedRevision?.ToString(), name, description, legacyRoleId?.ToString(), priority?.ToString(), permissionIds == null ? null : MutationHash(permissionIds.Select(p => p.ToString()).ToArray())),
            actor => { UpdateRoleCore(ctx, entityId, name, description, legacyRoleId, priority, actor); ReplaceRolePermissions(ctx, entityId, permissionIds, actor); return entityId; });

    private static void ReplaceRolePermissions(ReducerContext ctx, uint roleId, uint[]? permissionIds, Identity actor)
    {
        if (permissionIds == null) return;
        if (permissionIds.Length > 500 || permissionIds.Distinct().Count() != permissionIds.Length) throw new Exception("Invalid permission set.");
        // Each core checks grant_permissions and anti-escalation; the outer reducer is one transaction.
        var previous = ctx.Db.RolePermission.RoleId.Filter(roleId).Select(r => r.PermissionId).ToArray();
        foreach (var id in previous.Except(permissionIds)) RevokePermissionFromRoleCore(ctx, roleId, id, actor);
        foreach (var id in permissionIds.Except(previous)) GrantPermissionToRoleCore(ctx, roleId, id, actor);
    }

    [SpacetimeDB.Reducer]
    public static void DeleteRoleV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision)
        => CommitCatalogMutation(ctx, operationId, actingUser, "roles", "delete", entityId, expectedRevision, MutationHash(entityId.ToString(), expectedRevision?.ToString()),
            actor => { DeleteRoleCore(ctx, entityId, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void GrantRolePermissionV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, uint permissionId)
        => CommitCatalogMutation(ctx, operationId, actingUser, "roles", "grant-permission", entityId, expectedRevision, MutationHash(entityId.ToString(), expectedRevision?.ToString(), permissionId.ToString()),
            actor => { GrantPermissionToRoleCore(ctx, entityId, permissionId, actor); return entityId; }, "grant_permissions");

    [SpacetimeDB.Reducer]
    public static void RevokeRolePermissionV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, uint permissionId)
        => CommitCatalogMutation(ctx, operationId, actingUser, "roles", "revoke-permission", entityId, expectedRevision, MutationHash(entityId.ToString(), expectedRevision?.ToString(), permissionId.ToString()),
            actor => { RevokePermissionFromRoleCore(ctx, entityId, permissionId, actor); return entityId; }, "grant_permissions");

    [SpacetimeDB.Reducer]
    public static void CreatePermissionV2(ReducerContext ctx, string operationId, Identity actingUser, string name, string description, string category)
        => CommitCatalogMutation(ctx, operationId, actingUser, "permissions", "create", null, null, MutationHash(name, description, category), actor => CreatePermissionCore(ctx, name, description, category, actor));

    [SpacetimeDB.Reducer]
    public static void UpdatePermissionV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, string? name, string? description, string? category, bool? isActive)
        => CommitCatalogMutation(ctx, operationId, actingUser, "permissions", "update", entityId, expectedRevision, MutationHash(entityId.ToString(), expectedRevision?.ToString(), name, description, category, isActive?.ToString()),
            actor => { UpdatePermissionCore(ctx, entityId, name, description, category, isActive, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void DeletePermissionV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision)
        => CommitCatalogMutation(ctx, operationId, actingUser, "permissions", "delete", entityId, expectedRevision, MutationHash(entityId.ToString(), expectedRevision?.ToString()),
            actor => { DeletePermissionCore(ctx, entityId, actor); return entityId; });
}
