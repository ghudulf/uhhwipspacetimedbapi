using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

public static class DataGridDiagnostics
{
    public static string DumpRows(string screen, IEnumerable<object> rows, int page = 1, int? total = null, int maxRows = 20)
    {
        var options = ClientTrafficOptions.Current;
        if (options.Level == TrafficLevel.Off) return "";
        var count = rows is ICollection collection ? collection.Count : rows.Count();
        var json = Format(rows.Take(Math.Max(0, maxRows)).ToArray(), options);
        Log.Debug("DataGrid Screen={Screen} Page={Page} Total={Total} RowCount={RowCount} BoundRows={BoundRows}", screen, page, total ?? count, count, json);
        return json;
    }
    public static void DumpCollections(object viewModel)
    {
        if (ClientTrafficOptions.Current.Level == TrafficLevel.Off) return;
        foreach (var property in viewModel.GetType().GetProperties())
            if (property.GetIndexParameters().Length == 0 && property.GetValue(viewModel) is ICollection items)
                DumpRows(viewModel.GetType().Name + "." + property.Name, items.Cast<object>());
    }
    public static T Parsed<T>(string screen, JsonObject raw, T parsed, params string[] requiredPaths)
    {
        var options = ClientTrafficOptions.Current;
        if (options.Level == TrafficLevel.Off) return parsed;
        var missing = requiredPaths.Where(path => raw[path] == null || raw[path]!.ToJsonString() is "0" or "null" or "\"\"").ToArray();
        if (missing.Length > 0)
            Log.Debug("Parse Screen={Screen} MissingOrDefaultPaths={Paths} RawNode={RawNode} ParsedModel={ParsedModel}", screen, string.Join(",", missing.Select(path => "$." + path)), DebugPayload.Json(raw.ToJsonString(), options.Level, options.MaxBodyBytes), Format(parsed, options));
        return parsed;
    }
    private static string Format(object? value, TrafficOptions options)
    {
        try { return DebugPayload.Json(JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true }), options.Level, options.MaxBodyBytes); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException) { return "[row serialization failed: " + ex.GetType().Name + "]"; }
    }
}
