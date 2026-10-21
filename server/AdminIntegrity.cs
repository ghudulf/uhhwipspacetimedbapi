using SpacetimeDB;

public static partial class Module
{
    private static bool IsAdministrator(ReducerContext ctx, Identity userId) => ctx.Db.UserProfile.UserId.Find(userId)?.IsActive == true &&
        ctx.Db.UserRole.UserId.Filter(userId).Any(r => ctx.Db.Role.RoleId.Find(r.RoleId) is { IsActive: true, Name: "Administrator" });

    private static void EnsureAdministratorRemains(ReducerContext ctx, Identity userId)
    {
        if (!IsAdministrator(ctx, userId)) return;
        if (!ctx.Db.UserProfile.Iter().Any(u => u.UserId != userId && IsAdministrator(ctx, u.UserId)))
            throw new Exception("Cannot remove the last active Administrator.");
    }

    private static void EnsureUserMayBeRemoved(ReducerContext ctx, Identity userId, Identity actor)
    {
        if (userId == actor) throw new Exception("Cannot deactivate or delete your own account.");
        EnsureAdministratorRemains(ctx, userId);
    }

    private static void AdvanceUserRevision(ReducerContext ctx, Identity userId)
    {
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user != null) AdvanceEntityRevision(ctx, "users", user.LegacyUserId);
    }

    private static Identity UserIdentity(ReducerContext ctx, uint id) =>
        ctx.Db.UserProfile.LegacyUserId.Find(id)?.UserId ?? throw new Exception("User not found.");

    private static void ValidateTicketFields(double? price, uint? seat, string? payment, ulong? purchase)
    {
        if (price.HasValue && (!double.IsFinite(price.Value) || price < 0 || price > 1e12) || seat == 0 || purchase > 253402300799999)
            throw new Exception("Invalid ticket fields.");
        ValidateText(payment, 100, true);
    }
    private static void ValidateTicketSeat(ReducerContext ctx, uint routeId, uint seat)
    {
        var route = ctx.Db.Route.RouteId.Find(routeId) ?? throw new Exception("Route not found.");
        var bus = ctx.Db.Bus.BusId.Find(route.BusId);
        var capacity = bus?.SeatedCapacity ?? bus?.Capacity ?? 0;
        if (seat == 0 || capacity > 0 && seat > capacity) throw new Exception("Seat does not exist on the route's bus.");
    }

    private static void ValidateScheduleFields(ulong departure, ulong arrival, double price, string? start, string? end,
        string[]? stops, string[]? days, string[]? times, double[]? distances, string? notes)
    {
        if (departure > 253402300799999 || arrival > 253402300799999 || arrival != 0 && arrival < departure ||
            !double.IsFinite(price) || price < 0 || price > 1e12) throw new Exception("Invalid schedule time or price.");
        ValidateText(start, 200); ValidateText(end, 200); ValidateText(notes, 4000);
        foreach (var array in new[] { stops, days, times })
        {
            if (array?.Length > 200) throw new Exception("Too many schedule values.");
            if (array != null) foreach (var value in array) ValidateText(value, 200, true);
        }
        if (days?.Length > 7)
            throw new Exception("Invalid schedule days.");
        if (distances?.Length > 200 || distances?.Any(d => !double.IsFinite(d) || d < 0 || d > 1e9) == true)
            throw new Exception("Invalid stop distances.");
        if (stops != null && (times is { Length: > 0 } && times.Length != stops.Length || distances is { Length: > 0 } && distances.Length != stops.Length))
            throw new Exception("Schedule stop metadata lengths differ.");
    }
}
