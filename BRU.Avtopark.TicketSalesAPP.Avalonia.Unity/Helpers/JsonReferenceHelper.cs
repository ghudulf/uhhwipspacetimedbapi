using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers
{
    /// <summary>
    /// Helper class to parse JSON responses with ReferenceHandler.Preserve metadata ($id, $values, $ref).
    /// This handles the reference preservation format used by the API to prevent stack overflow issues.
    /// </summary>
    public static class JsonReferenceHelper
    {
        /// <summary>
        /// Parses a JSON string that may contain $values array wrapper and $id/$ref metadata.
        /// Handles nested references and circular references properly.
        /// Returns the inner array or the original node if no wrapper exists.
        /// </summary>
        /// <param name="jsonString">The JSON string to parse</param>
        /// <param name="logContext">Optional context for logging (e.g., "Buses", "Routes")</param>
        /// <returns>JsonArray if successful, null otherwise</returns>
        public static JsonArray? ParseArrayWithReferences(string jsonString, string logContext = "Data")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    Log.Warning("{Context}: Empty JSON string provided", logContext);
                    return null;
                }

                JsonNode? rootNode = JsonNode.Parse(jsonString);
                
                if (rootNode == null)
                {
                    Log.Warning("{Context}: Failed to parse JSON - null root node", logContext);
                    return null;
                }

                // Check if this is a reference-preserved object with $values array
                if (rootNode is JsonObject rootObject)
                {
                    // Handle the case where we have both $id and $values (reference-preserved collection)
                    if (rootObject.TryGetPropertyValue("$values", out var valuesNode) && valuesNode is JsonArray valuesArray)
                    {
                        Log.Debug("{Context}: Found $values array with {Count} items (reference-preserved collection)", logContext, valuesArray.Count);
                        
                        // Process the array to resolve any $ref references
                        var resolvedArray = ResolveReferences(valuesArray, rootObject);
                        return resolvedArray;
                    }
                    
                    // Handle the case where the root object itself is the data (no $values wrapper)
                    // This shouldn't happen for arrays, but handle it gracefully
                    Log.Warning("{Context}: Root is JsonObject but no $values found. Keys: {Keys}", 
                        logContext, string.Join(", ", rootObject.Select(kvp => kvp.Key)));
                    return null;
                }
                
                // Check if it's already an array (no reference preservation)
                if (rootNode is JsonArray directArray)
                {
                    Log.Debug("{Context}: Direct array with {Count} items (no reference preservation)", logContext, directArray.Count);
                    return directArray;
                }

                Log.Warning("{Context}: JSON structure unexpected - not an array or object with $values. Type: {Type}", 
                    logContext, rootNode.GetType().Name);
                return null;
            }
            catch (JsonException jsonEx)
            {
                Log.Error(jsonEx, "{Context}: JSON parsing exception. Raw JSON: {Json}", logContext, jsonString);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "{Context}: Unexpected error parsing JSON", logContext);
                return null;
            }
        }

        /// <summary>
        /// Resolves $ref references in a JsonArray by looking up $id values in the reference map.
        /// This handles circular references created by ReferenceHandler.Preserve.
        /// </summary>
        private static JsonArray ResolveReferences(JsonArray array, JsonObject rootObject)
        {
            // Build a reference map from $id to actual objects
            var referenceMap = new Dictionary<string, JsonNode>();
            BuildReferenceMap(rootObject, referenceMap);

            var resolvedArray = new JsonArray();
            
            foreach (var item in array)
            {
                if (item is JsonObject itemObj && itemObj.TryGetPropertyValue("$ref", out var refNode))
                {
                    // This is a reference, resolve it
                    var refId = refNode?.GetValue<string>();
                    if (refId != null && referenceMap.TryGetValue(refId, out var referencedNode))
                    {
                        resolvedArray.Add(referencedNode.DeepClone());
                        Log.Verbose("Resolved $ref '{RefId}' to actual object", refId);
                    }
                    else
                    {
                        Log.Warning("Could not resolve $ref '{RefId}'", refId);
                        resolvedArray.Add(item.DeepClone());
                    }
                }
                else
                {
                    // Not a reference, add as-is
                    resolvedArray.Add(item?.DeepClone());
                }
            }

            return resolvedArray;
        }

        /// <summary>
        /// Recursively builds a map of $id to JsonNode for reference resolution.
        /// </summary>
        private static void BuildReferenceMap(JsonNode node, Dictionary<string, JsonNode> referenceMap)
        {
            if (node is JsonObject obj)
            {
                // Check if this object has an $id
                if (obj.TryGetPropertyValue("$id", out var idNode))
                {
                    var id = idNode?.GetValue<string>();
                    if (id != null && !referenceMap.ContainsKey(id))
                    {
                        referenceMap[id] = obj;
                    }
                }

                // Recursively process all properties
                foreach (var kvp in obj)
                {
                    if (kvp.Value != null)
                    {
                        BuildReferenceMap(kvp.Value, referenceMap);
                    }
                }
            }
            else if (node is JsonArray arr)
            {
                // Recursively process array items
                foreach (var item in arr)
                {
                    if (item != null)
                    {
                        BuildReferenceMap(item, referenceMap);
                    }
                }
            }
        }

        /// <summary>
        /// Safely gets a value from a JsonObject, handling null cases and type conversions.
        /// Handles both camelCase and PascalCase property names.
        /// </summary>
        public static T? GetValue<T>(this JsonObject obj, string propertyName, T? defaultValue = default)
        {
            try
            {
                // Try exact match first
                if (obj.TryGetPropertyValue(propertyName, out var node) && node != null)
                {
                    // Handle null JsonValue
                    if (node is JsonValue jsonValue && jsonValue.TryGetValue<T>(out var value))
                    {
                        return value;
                    }
                    return node.GetValue<T>();
                }

                // Try case-insensitive match
                var key = obj.FirstOrDefault(kvp => 
                    string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Key;
                
                if (key != null && obj.TryGetPropertyValue(key, out var caseInsensitiveNode) && caseInsensitiveNode != null)
                {
                    if (caseInsensitiveNode is JsonValue jsonValue && jsonValue.TryGetValue<T>(out var value))
                    {
                        return value;
                    }
                    return caseInsensitiveNode.GetValue<T>();
                }
            }
            catch (InvalidOperationException)
            {
                // Type conversion failed, return default
                Log.Verbose("Type conversion failed for property {Property} to type {Type}, using default", 
                    propertyName, typeof(T).Name);
            }
            catch (Exception ex)
            {
                Log.Verbose(ex, "Failed to get value for property {Property} as type {Type}", 
                    propertyName, typeof(T).Name);
            }
            
            return defaultValue;
        }

        /// <summary>
        /// Safely gets a nullable value from a JsonObject.
        /// Handles both camelCase and PascalCase property names.
        /// </summary>
        public static T? GetNullableValue<T>(this JsonObject obj, string propertyName) where T : struct
        {
            try
            {
                // Try exact match first
                if (obj.TryGetPropertyValue(propertyName, out var node) && node != null)
                {
                    // Check if it's explicitly null
                    if (node is JsonValue jsonValue)
                    {
                        if (jsonValue.TryGetValue<T>(out var value))
                        {
                            return value;
                        }
                        // Check for null
                        if (jsonValue.TryGetValue<object>(out var objValue) && objValue == null)
                        {
                            return null;
                        }
                    }
                    return node.GetValue<T>();
                }

                // Try case-insensitive match
                var key = obj.FirstOrDefault(kvp => 
                    string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Key;
                
                if (key != null && obj.TryGetPropertyValue(key, out var caseInsensitiveNode) && caseInsensitiveNode != null)
                {
                    if (caseInsensitiveNode is JsonValue jsonValue)
                    {
                        if (jsonValue.TryGetValue<T>(out var value))
                        {
                            return value;
                        }
                        if (jsonValue.TryGetValue<object>(out var objValue) && objValue == null)
                        {
                            return null;
                        }
                    }
                    return caseInsensitiveNode.GetValue<T>();
                }
            }
            catch (InvalidOperationException)
            {
                // Type conversion failed or value is null
                return null;
            }
            catch (Exception ex)
            {
                Log.Verbose(ex, "Failed to get nullable value for property {Property} as type {Type}", 
                    propertyName, typeof(T).Name);
            }
            
            return null;
        }

        /// <summary>
        /// Safely gets a string value from a JsonObject, handling null cases.
        /// Handles both camelCase and PascalCase property names.
        /// </summary>
        public static string? GetStringValue(this JsonObject obj, string propertyName, string? defaultValue = null)
        {
            try
            {
                // Try exact match first
                if (obj.TryGetPropertyValue(propertyName, out var node) && node != null)
                {
                    if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var strValue))
                    {
                        return strValue;
                    }
                    return node.GetValue<string>();
                }

                // Try case-insensitive match
                var key = obj.FirstOrDefault(kvp => 
                    string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Key;
                
                if (key != null && obj.TryGetPropertyValue(key, out var caseInsensitiveNode) && caseInsensitiveNode != null)
                {
                    if (caseInsensitiveNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var strValue))
                    {
                        return strValue;
                    }
                    return caseInsensitiveNode.GetValue<string>();
                }
            }
            catch (Exception ex)
            {
                Log.Verbose(ex, "Failed to get string value for property {Property}", propertyName);
            }
            
            return defaultValue;
        }

        /// <summary>
        /// Safely gets a string array from a JsonObject, handling $values wrappers and references.
        /// Handles both camelCase and PascalCase property names.
        /// </summary>
        public static string[]? GetStringArray(this JsonObject obj, string propertyName)
        {
            try
            {
                JsonNode? arrayNode = null;
                
                // Try exact match first
                if (obj.TryGetPropertyValue(propertyName, out var node))
                {
                    arrayNode = node;
                }
                else
                {
                    // Try case-insensitive match
                    var key = obj.FirstOrDefault(kvp => 
                        string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Key;
                    
                    if (key != null && obj.TryGetPropertyValue(key, out var caseInsensitiveNode))
                    {
                        arrayNode = caseInsensitiveNode;
                    }
                }

                if (arrayNode == null)
                {
                    return null;
                }

                // Check if it's wrapped in a reference-preserved object with $values
                if (arrayNode is JsonObject arrayObj && arrayObj.TryGetPropertyValue("$values", out var valuesNode))
                {
                    arrayNode = valuesNode;
                }

                if (arrayNode is JsonArray array)
                {
                    var result = new List<string>();
                    foreach (var item in array)
                    {
                        if (item != null)
                        {
                            // Handle $ref references
                            if (item is JsonObject itemObj && itemObj.TryGetPropertyValue("$ref", out _))
                            {
                                Log.Verbose("Skipping $ref in string array for property {Property}", propertyName);
                                continue;
                            }

                            if (item is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var strValue) && strValue != null)
                            {
                                result.Add(strValue);
                            }
                            else
                            {
                                var str = item.GetValue<string>();
                                if (str != null)
                                {
                                    result.Add(str);
                                }
                            }
                        }
                    }
                    return result.Count > 0 ? result.ToArray() : null;
                }
            }
            catch (Exception ex)
            {
                Log.Verbose(ex, "Failed to get string array for property {Property}", propertyName);
            }
            
            return null;
        }

        /// <summary>
        /// Creates a JsonSerializerOptions instance configured to handle reference preservation.
        /// </summary>
        public static JsonSerializerOptions CreateOptionsWithReferenceHandling()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <summary>
        /// Creates a JsonSerializerOptions instance without reference preservation (for clean serialization).
        /// </summary>
        public static JsonSerializerOptions CreateStandardOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }
    }
}
