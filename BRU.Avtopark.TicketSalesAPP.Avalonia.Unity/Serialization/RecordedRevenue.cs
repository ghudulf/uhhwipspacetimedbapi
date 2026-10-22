using System.Collections.Generic;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization;

/// <summary>Unknown historical amounts must never count as zero-priced sales.</summary>
public static class RecordedRevenue
{
    public static decimal? Average(IEnumerable<decimal?> amounts)
    {
        decimal total = 0;
        long count = 0;
        foreach (var amount in amounts)
        {
            if (!amount.HasValue) continue;
            total = checked(total + amount.Value);
            count++;
        }
        return count == 0 ? null : total / count;
    }
}
