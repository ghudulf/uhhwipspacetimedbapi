namespace BRU.Avtopark.Contracts;

public sealed record SalesReportRequest(DateOnly StartDate, DateOnly EndDate);
public sealed record DailyRevenue(DateOnly Date, RevenueSummary Amounts);
public sealed record MonthlyRevenue(int Year, int Month, RevenueSummary Amounts);
public sealed record RouteRevenue(uint? RouteId, string RouteName, RevenueSummary Amounts);
public sealed record SalesReportSnapshot(string BusinessTimeZone, DateTimeOffset GeneratedAt, RevenueSummary Total,
    DailyRevenue[] Days, MonthlyRevenue[] Months, RouteRevenue[] Routes);
