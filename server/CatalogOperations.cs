using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void CreateRouteV2(ReducerContext ctx, string operationId, Identity actingUser, string startPoint, string endPoint, uint driverId, uint busId, string? travelTime, bool isActive, string routeNumber, uint stopCount, double routeLength, string? routeDescription, string? routeType)
        => CommitCatalogMutation(ctx, operationId, actingUser, "routes", "create", null, null,
            MutationHash(startPoint, endPoint, driverId.ToString(), busId.ToString(), travelTime, isActive.ToString(), routeNumber, stopCount.ToString(), routeLength.ToString("R", System.Globalization.CultureInfo.InvariantCulture), routeDescription, routeType), actor => CreateRouteCore(ctx, startPoint, endPoint, driverId, busId, travelTime, isActive, actor, routeNumber, stopCount, routeLength, routeDescription, routeType));

    [SpacetimeDB.Reducer]
    public static void UpdateRouteV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, string? startPoint, string? endPoint, uint? driverId, uint? busId, string? travelTime, bool? isActive, string? routeNumber, uint? stopCount, double? routeLength, string? routeDescription, string? routeType)
        => CommitCatalogMutation(ctx, operationId, actingUser, "routes", "update", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), startPoint, endPoint, driverId?.ToString(), busId?.ToString(), travelTime, isActive?.ToString(), routeNumber, stopCount?.ToString(), routeLength?.ToString("R", System.Globalization.CultureInfo.InvariantCulture), routeDescription, routeType), actor => { UpdateRouteCore(ctx, entityId, startPoint, endPoint, driverId, busId, travelTime, isActive, actor, routeNumber, stopCount, routeLength, routeDescription, routeType); return entityId; });

    [SpacetimeDB.Reducer]
    public static void DeleteRouteV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision)
        => CommitCatalogMutation(ctx, operationId, actingUser, "routes", "delete", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString()), actor => { DeleteRoute(ctx, entityId, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void CreateEmployeeV2(ReducerContext ctx, string operationId, Identity actingUser, string employeeName, string employeeSurname, string employeePatronym, uint jobId)
        => CommitCatalogMutation(ctx, operationId, actingUser, "employees", "create", null, null,
            MutationHash(employeeName, employeeSurname, employeePatronym, jobId.ToString()), actor => CreateEmployeeCore(ctx, employeeName, employeeSurname, employeePatronym, jobId, actor));

    [SpacetimeDB.Reducer]
    public static void UpdateEmployeeV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, string? employeeName, string? employeeSurname, string? employeePatronym, uint? jobId)
        => CommitCatalogMutation(ctx, operationId, actingUser, "employees", "update", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), employeeName, employeeSurname, employeePatronym, jobId?.ToString()), actor => { UpdateEmployee(ctx, entityId, employeeName, employeeSurname, employeePatronym, jobId, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void DeleteEmployeeV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision)
        => CommitCatalogMutation(ctx, operationId, actingUser, "employees", "delete", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString()), actor => { DeleteEmployee(ctx, entityId, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void CreateJobV2(ReducerContext ctx, string operationId, Identity actingUser, string jobTitle, string jobInternship)
        => CommitCatalogMutation(ctx, operationId, actingUser, "jobs", "create", null, null,
            MutationHash(jobTitle, jobInternship), actor => CreateJobCore(ctx, jobTitle, jobInternship, actor));

    [SpacetimeDB.Reducer]
    public static void UpdateJobV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, string? jobTitle, string? internship)
        => CommitCatalogMutation(ctx, operationId, actingUser, "jobs", "update", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), jobTitle, internship), actor => { UpdateJob(ctx, entityId, jobTitle, internship, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void DeleteJobV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision)
        => CommitCatalogMutation(ctx, operationId, actingUser, "jobs", "delete", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString()), actor => { DeleteJob(ctx, entityId, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void CreateMaintenanceV2(ReducerContext ctx, string operationId, Identity actingUser, uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string maintenanceType)
        => CommitCatalogMutation(ctx, operationId, actingUser, "maintenance", "create", null, null,
            MutationHash(busId.ToString(), lastServiceDate.ToString(), serviceEngineer, foundIssues, nextServiceDate.ToString(), roadworthiness, maintenanceType), actor => CreateMaintenanceCore(ctx, busId, lastServiceDate, serviceEngineer, foundIssues, nextServiceDate, roadworthiness, maintenanceType, actor));

    [SpacetimeDB.Reducer]
    public static void UpdateMaintenanceV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, uint? busId, ulong? lastServiceDate, string? serviceEngineer, string? foundIssues, ulong? nextServiceDate, string? roadworthiness, string? maintenanceType, string? mileage)
        => CommitCatalogMutation(ctx, operationId, actingUser, "maintenance", "update", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), busId?.ToString(), lastServiceDate?.ToString(), serviceEngineer, foundIssues, nextServiceDate?.ToString(), roadworthiness, maintenanceType, mileage), actor => { UpdateMaintenance(ctx, entityId, busId, lastServiceDate, serviceEngineer, foundIssues, nextServiceDate, roadworthiness, maintenanceType, mileage, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void DeleteMaintenanceV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision)
        => CommitCatalogMutation(ctx, operationId, actingUser, "maintenance", "delete", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString()), actor => { DeleteMaintenance(ctx, entityId, actor); return entityId; });

    [SpacetimeDB.Reducer]
    public static void SetRouteActiveV2(ReducerContext ctx, string operationId, Identity actingUser, uint entityId, ulong? expectedRevision, bool active)
        => CommitCatalogMutation(ctx, operationId, actingUser, "routes", "set-active", entityId, expectedRevision,
            MutationHash(entityId.ToString(), expectedRevision?.ToString(), active.ToString()), actor => { if (active) ActivateRoute(ctx, entityId, actor); else DeactivateRoute(ctx, entityId, actor); return entityId; });
}
