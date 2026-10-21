using BRU.Avtopark.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;

internal static class ReportTests
{
    public static void Run()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var report = SalesReportProjection.Build([
            new(new DateTimeOffset(2026, 3, 8, 4, 59, 0, TimeSpan.Zero), 1, "Outside business date", 999),
            new(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), 1, "Known route", 30),
            new(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), 1, "Known route", null),
            new(new DateTimeOffset(2026, 3, 9, 3, 59, 0, TimeSpan.Zero), null, "Missing ticket", 0),
            new(new DateTimeOffset(2026, 3, 9, 4, 0, 0, TimeSpan.Zero), 2, "Outside end date", 999)
        ], new(new(2026, 3, 8), new(2026, 3, 8)), zone);
        Assert(report.Total == new RevenueSummary(30, 3, 1) && report.Total.AverageKnownAmount == 15, "known revenue/average or unknown count incorrect");
        Assert(report.Routes.Sum(r => r.Amounts.SaleCount) == 3 && report.Days.Single().Amounts.SaleCount == 3, "missing related records lost sales");
        Assert(report.Months.Single().Amounts == report.Total, "monthly and overall projections disagree");
        var many = Enumerable.Range(1, 250).Select(id => new SaleFact(DateTimeOffset.Parse("2026-03-08T12:00:00Z"), (uint)id, $"Route {id}", 2));
        var bounded = SalesReportProjection.Build(many, new(new(2026, 3, 8), new(2026, 3, 8)), zone);
        Assert(bounded.Routes.Length == 200 && bounded.Routes.Sum(r => r.Amounts.KnownTotal) == 500 && bounded.Routes.Sum(r => r.Amounts.SaleCount) == 250, "bounded route remainder lost amounts");
        var unknown = SalesReportProjection.Build([new(DateTimeOffset.Parse("2026-03-08T12:00:00Z"), null, "Missing route", null)], new(new(2026, 3, 8), new(2026, 3, 8)), zone);
        Assert(unknown.Total.AverageKnownAmount == null && !unknown.Total.IsComplete, "unknown historical amounts presented as zero average");
        var quiet = SalesReportProjection.Build([], new(new(2026, 1, 31), new(2026, 3, 1)), zone);
        Assert(quiet.Days.Length == 30 && quiet.Days.Select(d => d.Date).SequenceEqual(Enumerable.Range(0, 30).Select(i => new DateOnly(2026, 1, 31).AddDays(i)))
            && quiet.Days.All(d => d.Amounts == new RevenueSummary(0, 0, 0)), "quiet dates missing or out of order");
        Assert(quiet.Months.Select(m => m.Month).SequenceEqual(new[] { 1, 2, 3 }) && quiet.Months.All(m => m.Amounts.SaleCount == 0), "quiet months missing");
        Console.WriteLine("PASS report DST/inclusive business dates, missing relationships, unknown/free amounts and bounded projection totals");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
