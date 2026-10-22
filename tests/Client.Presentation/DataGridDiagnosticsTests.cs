using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

internal static class DataGridDiagnosticsTests
{
    public static void Run()
    {
        var previous = Environment.GetEnvironmentVariable("BRU_DEBUG_TRAFFIC");
        try
        {
            Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", "redacted");
            var rows = Enumerable.Range(0, 25).Select(i => (object)new Row(i, "Alice", null, 0, "sentinel")).ToArray();
            var json = DataGridDiagnostics.DumpRows("TestGrid", rows, page: 2, total: 100, maxRows: 20);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var first = document.RootElement[0];
            if (document.RootElement.GetArrayLength() != 20 || first.GetProperty("Nullable").ValueKind != System.Text.Json.JsonValueKind.Null || first.GetProperty("Zero").GetInt32() != 0 || !json.Contains("Alice") || json.Contains("sentinel"))
                throw new Exception("Bound-row diagnostics lost null/default/business fields, cap, or credential policy");
            Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", "off");
            if (DataGridDiagnostics.DumpRows("TestGrid", rows).Length != 0) throw new Exception("Off row dump emitted data");
        }
        finally { Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", previous); }
        Console.WriteLine("PASS DataGridDiagnostics bound-row JSON retains null/default properties and business names, masks credentials and caps rows; off mode empty");
    }
    private sealed record Row(int Id, string Name, string? Nullable, int Zero, string AccessToken);
}
