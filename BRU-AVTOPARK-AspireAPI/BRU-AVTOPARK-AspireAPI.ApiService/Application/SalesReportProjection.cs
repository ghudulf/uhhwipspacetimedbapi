using BRU.Avtopark.Contracts;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Application;

public sealed record SaleFact(DateTimeOffset OccurredAt, uint? RouteId, string RouteName, decimal? Amount);

public static class SalesReportProjection
{
    public static Task<SalesReportSnapshot> ReadAsync(ISpacetimeDBService database, SalesReportRequest request, TimeZoneInfo zone, CancellationToken ct)
    {
        Validate(request);
        return database.ReadAsync(c =>
        {
            var facts = c.Db.Sale.Iter().Select(s =>
            {
                var routeId = c.Db.Ticket.TicketId.Find(s.TicketId)?.RouteId;
                var route = routeId.HasValue ? c.Db.Route.RouteId.Find(routeId.Value) : null;
                return new SaleFact(DateTimeOffset.FromUnixTimeMilliseconds(checked((long)s.SaleDate)), routeId,
                    route == null ? routeId.HasValue ? $"Маршрут {routeId} (данные недоступны)" : "Маршрут неизвестен"
                    : $"{route.RouteNumber} ({route.StartPoint}–{route.EndPoint})", SalePresentation.Amount(c, s));
            });
            return Build(facts, request, zone);
        }, ct);
    }

    public static SalesReportSnapshot Build(IEnumerable<SaleFact> sales, SalesReportRequest request, TimeZoneInfo zone)
    {
        Validate(request);
        // Convert each actual instant before filtering by inclusive business dates. This handles DST gaps,
        // repeated midnights and offset changes without guessing a UTC boundary or relabeling local time.
        var dated = sales.Select(s => (Sale: s, Date: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.OccurredAt, zone).DateTime)))
            .Where(s => s.Date >= request.StartDate && s.Date <= request.EndDate).ToArray();
        var routes = dated.GroupBy(s => s.Sale.RouteId).Select(g => new RouteRevenue(g.Key, g.First().Sale.RouteName, Summarize(g.Select(s => s.Sale))))
            .OrderByDescending(r => r.Amounts.SaleCount).ThenBy(r => r.RouteId).ToArray();
        // Charts need a bounded projection. The grouped remainder retains all amounts and sale counts.
        if (routes.Length > 200)
        {
            var remainder = routes.Skip(199).ToArray();
            routes = routes.Take(199).Append(new RouteRevenue(null, "Другие маршруты", new RevenueSummary(
                remainder.Sum(r => r.Amounts.KnownTotal), remainder.Sum(r => r.Amounts.SaleCount), remainder.Sum(r => r.Amounts.UnknownAmountCount)))).ToArray();
        }
        var days = dated.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => Summarize(g.Select(s => s.Sale)));
        var months = dated.GroupBy(s => (s.Date.Year, s.Date.Month)).ToDictionary(g => g.Key, g => Summarize(g.Select(s => s.Sale)));
        var daily = Enumerable.Range(0, request.EndDate.DayNumber - request.StartDate.DayNumber + 1)
            .Select(offset => request.StartDate.AddDays(offset))
            .Select(date => new DailyRevenue(date, days.GetValueOrDefault(date) ?? new(0, 0, 0))).ToArray();
        var monthly = new List<MonthlyRevenue>();
        for (var date = new DateOnly(request.StartDate.Year, request.StartDate.Month, 1); date <= request.EndDate; date = date.AddMonths(1))
            monthly.Add(new(date.Year, date.Month, months.GetValueOrDefault((date.Year, date.Month)) ?? new(0, 0, 0)));
        return new(zone.Id, DateTimeOffset.UtcNow, Summarize(dated.Select(s => s.Sale)), daily, monthly.ToArray(), routes);
    }
    private static RevenueSummary Summarize(IEnumerable<SaleFact> sales)
    {
        decimal known = 0;
        var count = 0;
        var unknown = 0;
        foreach (var sale in sales) { count++; if (sale.Amount is { } amount) known = checked(known + amount); else unknown++; }
        return new(known, count, unknown);
    }
    private static void Validate(SalesReportRequest request)
    {
        if (request.StartDate.Year < 1970 || request.EndDate.Year > 9998 || request.EndDate < request.StartDate || request.EndDate.DayNumber - request.StartDate.DayNumber > 3660)
            throw new OperationException("validation_failed");
    }
}
