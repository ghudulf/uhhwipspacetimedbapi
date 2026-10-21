using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Table(Public = true)]
    public partial class EntityRevision
    {
        [PrimaryKey] public string Key = "";
        public ulong Revision;
    }
    private static ulong EntityVersion(ReducerContext ctx, string resource, uint id) => ctx.Db.EntityRevision.Key.Find(resource + ":" + id)?.Revision ?? 0;
    private static ulong AdvanceEntityRevision(ReducerContext ctx, string resource, uint id)
    {
        var key = resource + ":" + id;
        var row = ctx.Db.EntityRevision.Key.Find(key);
        if (row == null) { ctx.Db.EntityRevision.Insert(new EntityRevision { Key = key, Revision = 1 }); return 1; }
        row.Revision = checked(row.Revision + 1);
        ctx.Db.EntityRevision.Key.Update(row);
        return row.Revision;
    }
    private static void CommitCatalogMutation(ReducerContext ctx, string operationId, Identity actingUser, string resource,
        string action, uint? entityId, ulong? expectedRevision, string hash, Func<Identity, uint> apply, string? permissionOverride = null)
    {
        var actor = ResolveEffectiveUser(ctx, actingUser);
        var permission = permissionOverride ?? resource + "." + (action is "update" or "set-active" ? "edit" : action);
        if (!HasPermission(ctx, actor, permission)) throw new Exception("Missing " + permission + " permission.");
        var operation = resource + "." + action;
        var key = ReceiptKey(actor, operationId);
        if (AlreadyCommitted(ctx, key, operation, hash)) return;
        if (entityId.HasValue && expectedRevision.HasValue && EntityVersion(ctx, resource, entityId.Value) != expectedRevision)
            throw new Exception("Entity revision conflict.");
        var id = apply(actor);
        SaveReceipt(ctx, key, actor, operation, hash, id, EntityVersion(ctx, resource, id));
    }
    private static void ValidateText(string? value, int max, bool nonempty = false)
    {
        if (value != null && (value.Length > max || nonempty && string.IsNullOrWhiteSpace(value))) throw new Exception("Invalid field length or value.");
    }
    private static void ValidateRouteFields(string? start, string? end, string? duration)
    {
        ValidateText(start, 200, true); ValidateText(end, 200, true); ValidateText(duration, 100);
    }
    private static void ValidateEmployeeFields(string? name, string? surname, string? patronym)
    {
        ValidateText(name, 200, true); ValidateText(surname, 200, true); ValidateText(patronym, 200);
    }
    private static void ValidateJobFields(ReducerContext ctx, uint? id, string? title, string? internship)
    {
        ValidateText(title, 200, true); ValidateText(internship, 2000);
        if (title != null && ctx.Db.Job.Iter().Any(j => j.JobId != id && j.JobTitle == title)) throw new Exception("Job title already exists.");
    }
    private static void ValidateMaintenanceFields(ulong last, ulong next, string? engineer, string? issues, string? roadworthiness, string? type, string? mileage)
    {
        if (last > 253402300799999 || next > 253402300799999 || next < last) throw new Exception("Invalid maintenance dates.");
        ValidateText(engineer, 200, true); ValidateText(issues, 4000); ValidateText(roadworthiness, 200); ValidateText(type, 200); ValidateText(mileage, 100);
    }
    private static void EnsureRouteUnreferenced(ReducerContext ctx, uint id)
    {
        if (ctx.Db.RouteSchedule.Iter().Any(r => r.RouteId == id) || ctx.Db.Ticket.Iter().Any(r => r.RouteId == id)
            || ctx.Db.EmployeeShift.Iter().Any(r => r.RouteId == id) || ctx.Db.CashierDay.Iter().Any(r => r.RouteId == id)
            || ctx.Db.RouteConductor.Iter().Any(r => r.RouteId == id) || ctx.Db.PassengerCount.Iter().Any(r => r.RouteId == id)
            || ctx.Db.BusLocation.Iter().Any(r => r.RouteId == id) || ctx.Db.Incident.Iter().Any(r => r.RouteId == id))
            throw new Exception("Route is referenced by existing records.");
    }
    private static void EnsureEmployeeUnreferenced(ReducerContext ctx, uint id)
    {
        if (ctx.Db.Route.Iter().Any(r => r.DriverId == id) || ctx.Db.EmployeeShift.Iter().Any(r => r.EmployeeId == id)
            || ctx.Db.CashierDay.Iter().Any(r => r.EmployeeId == id) || ctx.Db.RouteConductor.Iter().Any(r => r.EmployeeId == id)
            || ctx.Db.ConductorStatistics.Iter().Any(r => r.EmployeeId == id) || ctx.Db.PassengerCount.Iter().Any(r => r.EmployeeId == id)
            || ctx.Db.Maintenance.Iter().Any(r => r.ScheduledByEmployeeId == id || r.CompletedByEmployeeId == id)
            || ctx.Db.Ticket.Iter().Any(r => r.ValidatedByEmployeeId == id) || ctx.Db.FuelRecord.Iter().Any(r => r.EmployeeId == id)
            || ctx.Db.Incident.Iter().Any(r => r.EmployeeId == id))
            throw new Exception("Employee is referenced by existing records.");
    }
}
