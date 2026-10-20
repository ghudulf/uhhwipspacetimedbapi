using System;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

public static class SalesReportClient
{
    public static Task<SalesReportSnapshot> GetAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
        ApplicationQueryClient.ReadAsync<SalesReportRequest, SalesReportSnapshot>("reports.sales", new(start, end), cancellationToken);
}
