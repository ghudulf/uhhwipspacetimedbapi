using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization;

/// <summary>Compatibility boundary for plain arrays and legacy reference-preserving HTTP responses.</summary>
public static class CollectionPayload
{
    public static JsonArray ReadArray(JsonNode? root)
    {
        var ids = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        void Index(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (obj["$id"] is JsonValue id && !ids.TryAdd(id.GetValue<string>(), obj)) throw new JsonException("Duplicate reference ID");
                foreach (var item in obj) Index(item.Value);
            }
            else if (node is JsonArray array) foreach (var item in array) Index(item);
        }
        Index(root);
        var remainingNodes = 100_000;
        JsonNode? Resolve(JsonNode? node, HashSet<string> path, int depth)
        {
            if (--remainingNodes < 0) throw new JsonException("Expanded collection limit exceeded");
            if (depth > 64) throw new JsonException("Reference nesting limit exceeded");
            if (node is JsonObject obj)
            {
                if (obj["$ref"] is JsonValue reference)
                {
                    var id = reference.GetValue<string>();
                    if (!path.Add(id) || !ids.TryGetValue(id, out var target)) throw new JsonException("Cyclic or missing reference");
                    var resolved = Resolve(target, path, depth + 1);
                    path.Remove(id);
                    return resolved;
                }
                if (obj["$values"] is JsonArray values) return Resolve(values, path, depth + 1);
                var result = new JsonObject();
                foreach (var item in obj) if (item.Key != "$id") result[item.Key] = Resolve(item.Value, path, depth + 1);
                return result;
            }
            if (node is JsonArray array)
            {
                var result = new JsonArray();
                foreach (var item in array) result.Add(Resolve(item, path, depth + 1));
                return result;
            }
            return node?.DeepClone();
        }
        return Resolve(root, new HashSet<string>(StringComparer.Ordinal), 0) as JsonArray
            ?? throw new JsonException("Expected a collection response");
    }

    public static ulong UnixMilliseconds(JsonNode? value)
    {
        if (value is JsonValue scalar && scalar.TryGetValue<ulong>(out var timestamp)) return timestamp;
        if (value is JsonValue text && text.TryGetValue<string>(out var date)
            && DateTimeOffset.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) && parsed.ToUnixTimeMilliseconds() >= 0)
            return checked((ulong)parsed.ToUnixTimeMilliseconds());
        throw new JsonException("Expected a non-negative timestamp or ISO timestamp");
    }
}
