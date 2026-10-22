using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Table(Public = true)]
    public partial class BusRevision
    {
        [PrimaryKey] public uint BusId;
        public ulong Revision;
    }

    private static ulong AdvanceBusRevision(ReducerContext ctx, uint busId)
    {
        var revision = ctx.Db.BusRevision.BusId.Find(busId);
        if (revision == null) { ctx.Db.BusRevision.Insert(new BusRevision { BusId = busId, Revision = 1 }); return 1; }
        revision.Revision = checked(revision.Revision + 1);
        ctx.Db.BusRevision.BusId.Update(revision);
        return revision.Revision;
    }

    private static void ValidateBusFields(string? model, string? registrationNumber)
    {
        if (model != null && (string.IsNullOrWhiteSpace(model) || model.Length > 200)) throw new Exception("Invalid bus model.");
        if (registrationNumber?.Length > 100) throw new Exception("Invalid registration number.");
    }

    [SpacetimeDB.Reducer]
    public static void CreateBusV2(ReducerContext ctx, string operationId, Identity actingUser, string model, string? registrationNumber, uint? capacity, string? busType, uint? year, string? vin, string? licensePlate, string? currentStatus, bool? active)
    {
        var actor = ResolveEffectiveUser(ctx, actingUser);
        if (!HasPermission(ctx, actor, "buses.create")) throw new Exception("Missing buses.create permission.");
        var key = ReceiptKey(actor, operationId);
        var hash = MutationHash(model, registrationNumber, capacity?.ToString(), busType, year?.ToString(), vin, licensePlate, currentStatus, active?.ToString());
        if (AlreadyCommitted(ctx, key, "buses.create", hash)) return;
        var id = CreateBusCore(ctx, model, registrationNumber, actor);
        ApplyBusDetails(ctx, id, capacity, busType, year, vin, licensePlate, currentStatus, active, actor);
        SaveReceipt(ctx, key, actor, "buses.create", hash, id, ctx.Db.BusRevision.BusId.Find(id)!.Revision);
    }

    [SpacetimeDB.Reducer]
    public static void UpdateBusV2(ReducerContext ctx, string operationId, Identity actingUser, uint busId, ulong? expectedRevision,
        string? model, string? registrationNumber, uint? capacity, string? busType, uint? year, string? vin, string? licensePlate, string? currentStatus, bool? active)
    {
        var actor = ResolveEffectiveUser(ctx, actingUser);
        if (!HasPermission(ctx, actor, "buses.edit")) throw new Exception("Missing buses.edit permission.");
        var key = ReceiptKey(actor, operationId);
        var hash = MutationHash(busId.ToString(), expectedRevision?.ToString(), model, registrationNumber, capacity?.ToString(), busType, year?.ToString(), vin, licensePlate, currentStatus, active?.ToString());
        if (AlreadyCommitted(ctx, key, "buses.update", hash)) return;
        CheckBusRevision(ctx, busId, expectedRevision);
        UpdateBus(ctx, busId, model, registrationNumber, actor);
        ApplyBusDetails(ctx, busId, capacity, busType, year, vin, licensePlate, currentStatus, active, actor);
        SaveReceipt(ctx, key, actor, "buses.update", hash, busId, ctx.Db.BusRevision.BusId.Find(busId)!.Revision);
    }

    [SpacetimeDB.Reducer]
    public static void DeleteBusV2(ReducerContext ctx, string operationId, Identity actingUser, uint busId, ulong? expectedRevision)
    {
        var actor = ResolveEffectiveUser(ctx, actingUser);
        if (!HasPermission(ctx, actor, "buses.delete")) throw new Exception("Missing buses.delete permission.");
        var key = ReceiptKey(actor, operationId);
        var hash = MutationHash(busId.ToString(), expectedRevision?.ToString());
        if (AlreadyCommitted(ctx, key, "buses.delete", hash)) return;
        CheckBusRevision(ctx, busId, expectedRevision);
        DeleteBus(ctx, busId, actor);
        SaveReceipt(ctx, key, actor, "buses.delete", hash, busId, ctx.Db.BusRevision.BusId.Find(busId)!.Revision);
    }

    [SpacetimeDB.Reducer]
    public static void SetBusActiveV2(ReducerContext ctx, string operationId, Identity actingUser, uint busId, ulong? expectedRevision, bool active)
    {
        var actor = ResolveEffectiveUser(ctx, actingUser);
        if (!HasPermission(ctx, actor, "buses.edit")) throw new Exception("Missing buses.edit permission.");
        var key = ReceiptKey(actor, operationId);
        var hash = MutationHash(busId.ToString(), expectedRevision?.ToString(), active ? "1" : "0");
        if (AlreadyCommitted(ctx, key, "buses.set-active", hash)) return;
        CheckBusRevision(ctx, busId, expectedRevision);
        if (active) ActivateBus(ctx, busId, actor); else DeactivateBus(ctx, busId, actor);
        SaveReceipt(ctx, key, actor, "buses.set-active", hash, busId, ctx.Db.BusRevision.BusId.Find(busId)?.Revision ?? 0);
    }

    private static void ApplyBusDetails(ReducerContext ctx, uint id, uint? capacity, string? busType, uint? year,
        string? vin, string? licensePlate, string? currentStatus, bool? active, Identity actor)
    {
        // These fields already exist in the page and storage. Apply them in the same transaction as the receipt.
        if (capacity > 1000 || (year.HasValue && year != 0 && (year < 1886 || year > 2200))
            || busType?.Length > 100 || vin?.Length > 100 || licensePlate?.Length > 100 || currentStatus?.Length > 100)
            throw new Exception("Invalid bus details.");
        var bus = ctx.Db.Bus.BusId.Find(id)!;
        if (capacity.HasValue) bus.Capacity = capacity.Value;
        if (busType != null) bus.BusType = busType;
        if (year.HasValue) bus.Year = year.Value;
        if (vin != null) bus.VIN = vin;
        if (licensePlate != null) bus.LicensePlate = licensePlate;
        if (currentStatus != null) bus.CurrentStatus = currentStatus;
        ctx.Db.Bus.BusId.Update(bus);
        if (active.HasValue && bus.IsActive != active.Value)
        {
            // Creation is allowed with buses.create alone; updates already require buses.edit.
            if (!active.Value && ctx.Db.Route.Iter().Any(r => r.BusId == id && r.IsActive))
                throw new Exception("Cannot deactivate a bus with active routes.");
            var previous = bus.IsActive;
            bus.IsActive = active.Value;
            ctx.Db.Bus.BusId.Update(bus);
            ctx.Db.BusStatusEvent.Insert(new BusStatusEvent { BusId = id, PreviousStatus = previous ? "Active" : "Inactive",
                NewStatus = active.Value ? "Active" : "Inactive", ChangedBy = actor,
                Timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, Reason = "Bus details updated" });
        }
    }

    private static void CheckBusRevision(ReducerContext ctx, uint busId, ulong? expected)
    {
        if (expected.HasValue && expected.Value != (ctx.Db.BusRevision.BusId.Find(busId)?.Revision ?? 0))
            throw new Exception("Bus revision conflict.");
    }
}
