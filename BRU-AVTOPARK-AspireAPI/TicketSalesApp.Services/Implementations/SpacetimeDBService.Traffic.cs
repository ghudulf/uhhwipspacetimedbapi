using System.Collections;
using BRU.Avtopark.Contracts;
using Microsoft.Extensions.Logging;
using TicketSalesApp.Services.Diagnostics;

namespace TicketSalesApp.Services.Implementations;

public partial class SpacetimeDBService
{
    private void LogTableSnapshots()
    {
        var options = TrafficDiagnostics.ServerOptions(_configuration);
        if (options.Level == TrafficLevel.Off || _connection == null || !_logger.IsEnabled(LogLevel.Debug)) return;
        foreach (var field in _connection.Db.GetType().GetFields())
        {
            var table = field.GetValue(_connection.Db);
            if (table?.GetType().GetMethod("Iter", Type.EmptyTypes)?.Invoke(table, null) is not IEnumerable rows) continue;
            _logger.LogDebug("Subscription applied Table={Table} Snapshot={Snapshot}", field.Name, TrafficDiagnostics.Snapshot(rows, options));
        }
    }
}
