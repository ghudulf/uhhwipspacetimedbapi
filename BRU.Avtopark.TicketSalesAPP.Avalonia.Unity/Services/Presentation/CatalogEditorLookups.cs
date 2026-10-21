using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

internal static class CatalogEditorLookups
{
    internal static async Task<List<T>> CatalogAsync<T>(string operation, CancellationToken ct)
    {
        var rows = new List<T>();
        for (var page = 1; ; page++)
        {
            var result = await ApplicationQueryClient.ReadAsync<PageRequest, CatalogPageResult<T>>(operation, new(page, 100), ct);
            rows.AddRange(result.Items);
            if (result.Items.Length == 0 || (long)page * 100 >= result.TotalCount) return rows;
        }
    }
    internal static async Task<List<BusRow>> BusesAsync(CancellationToken ct)
    {
        var rows = new List<BusRow>();
        for (var page = 1; ; page++)
        {
            var result = await ApplicationQueryClient.ReadAsync<BusQuery, BusPageResult>("buses.list", new(page, 100), ct);
            rows.AddRange(result.Items);
            if (result.Items.Length == 0 || (long)page * 100 >= result.TotalCount) return rows;
        }
    }
}
