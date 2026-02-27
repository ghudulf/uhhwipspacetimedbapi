# Testing and Debugging Guide for JSON Parsing

## Overview
All JSON parsing has been centralized into helper methods in `BusParsingExtensions.cs`. This makes debugging and testing much easier since all complexity is in ONE place.

## Architecture

```
API Response (with $id/$ref/$values)
    ↓
JsonReferenceHelper.ParseArrayWithReferences()
    ↓ (resolves references, extracts $values)
JsonArray of JsonObjects
    ↓
obj.ParseEntity() extension methods
    ↓ (handles all field parsing, type conversions, null checks)
Strongly-typed Entity objects
```

## Debugging Workflow

### 1. Set Breakpoints in Helper Methods

**Location**: `BRU.Avtopark.TicketSalesAPP.Avalonia.Unity/Helpers/BusParsingExtensions.cs`

**Methods to debug**:
- `ParseBus()` - for Bus parsing issues
- `ParseRoute()` - for Route parsing issues
- `ParseMaintenance()` - for Maintenance parsing issues
- `ParseEmployee()` - for Employee parsing issues
- `ParseTicket()` - for Ticket parsing issues
- `ParseSale()` - for Sale parsing issues
- `ParseRouteSchedule()` - for RouteSchedule parsing issues
- `ParseJob()` - for Job parsing issues
- `ParseUserProfile()` - for UserProfile parsing issues
- `ParseRole()` - for Role parsing issues
- `ParsePermission()` - for Permission parsing issues

### 2. Inspect Raw JSON

When breakpoint hits:
1. Hover over `JsonObject` parameter to see structure
2. Use `.ToJsonString()` to see raw JSON
3. Check for $id, $ref, $values metadata
4. Verify field names match (case-insensitive)

### 3. Step Through Parsing

Watch for:
- Null checks passing/failing
- Type conversions succeeding/failing
- DateTime to ulong conversions
- Array parsing with GetStringArray()
- Nested object parsing

### 4. Check Logs

All parsing operations log extensively:
- **Verbose**: Individual field parsing
- **Warning**: Missing/invalid fields
- **Error**: Critical parsing failures

**Log locations**:
- Console output
- Serilog sinks (if configured)

## Common Issues and Solutions

### Issue 1: Field Not Parsing

**Symptom**: Field is null when it shouldn't be

**Debug**:
1. Set breakpoint in Parse* method
2. Check if field exists in JSON: `obj.TryGetPropertyValue("fieldName", out var node)`
3. Check field name casing (should be case-insensitive)
4. Check if value is wrapped in $ref

**Solution**:
- Verify API is sending the field
- Check GetValue<T>() or GetStringValue() is using correct type
- Ensure JsonReferenceHelper resolved $ref correctly

### Issue 2: DateTime Conversion Fails

**Symptom**: Timestamp is 0 or ArgumentOutOfRangeException

**Debug**:
1. Set breakpoint in Parse* method where DateTime is parsed
2. Check raw value: `obj["fieldName"]?.ToJsonString()`
3. Verify it's a valid DateTime string or number

**Solution**:
- API should send ISO 8601 format: "2024-01-15T10:30:00Z"
- Or Unix timestamp in milliseconds
- Helper handles both formats

### Issue 3: Array Not Parsing

**Symptom**: Array field is null or empty

**Debug**:
1. Check if array is wrapped in $values: `obj["arrayField"]?["$values"]`
2. Use GetStringArray() helper which handles $values automatically
3. Check individual array items for $ref

**Solution**:
- Use `obj.GetStringArray("fieldName")` from JsonReferenceHelper
- It handles $values wrappers and $ref resolution

### Issue 4: Circular References

**Symptom**: Stack overflow or infinite loop

**Debug**:
1. Check if JsonReferenceHelper.ParseArrayWithReferences() was called
2. Verify $id and $ref are being resolved
3. Check BuildReferenceMap() is finding all $id values

**Solution**:
- Always use JsonReferenceHelper.ParseArrayWithReferences() first
- It resolves all $ref before parsing
- Never manually parse JSON with $ref

### Issue 5: Type Mismatch

**Symptom**: InvalidOperationException or FormatException

**Debug**:
1. Check expected type vs actual type in JSON
2. Use GetValue<T>() with correct type parameter
3. Check if string needs parsing (e.g., "123" to int)

**Solution**:
- Use appropriate GetValue<T>() type
- For numbers that might be strings, use TryParse
- Helpers handle most common conversions

## Testing Scenarios

### Test 1: Normal Response
```json
{
  "$id": "1",
  "$values": [
    {
      "$id": "2",
      "busId": 1,
      "model": "MAZ-103",
      "registrationNumber": "AB1234"
    }
  ]
}
```
**Expected**: Parses successfully, logs show all fields

### Test 2: Response with $ref
```json
{
  "$id": "1",
  "$values": [
    {
      "$id": "2",
      "maintenanceId": 1,
      "bus": { "$ref": "3" }
    },
    {
      "$id": "3",
      "busId": 1,
      "model": "MAZ-103"
    }
  ]
}
```
**Expected**: $ref resolved to actual bus object

### Test 3: Missing Optional Fields
```json
{
  "$id": "1",
  "$values": [
    {
      "busId": 1,
      "model": "MAZ-103"
      // registrationNumber missing
    }
  ]
}
```
**Expected**: Parses successfully, optional fields are null

### Test 4: Invalid Data Type
```json
{
  "busId": "not a number",
  "model": "MAZ-103"
}
```
**Expected**: Logs warning, returns null, continues with next item

### Test 5: Empty Array
```json
{
  "$id": "1",
  "$values": []
}
```
**Expected**: Returns empty collection, no errors

## Performance Monitoring

### Metrics to Watch

1. **Parse Time**: How long does ParseArrayWithReferences() take?
2. **Reference Resolution**: How many $ref are resolved?
3. **Parse Success Rate**: How many items parse successfully vs fail?
4. **Memory Usage**: Are large JSON responses causing issues?

### Optimization Tips

1. **Lazy Loading**: Don't parse all fields if not needed
2. **Caching**: Cache parsed entities if used multiple times
3. **Streaming**: For very large responses, consider streaming parser
4. **Parallel Processing**: Parse independent items in parallel

## Unit Testing Helper Methods

### Example Test Structure

```csharp
[Fact]
public void ParseBus_WithValidJson_ReturnsValidBus()
{
    // Arrange
    var json = @"{
        ""busId"": 1,
        ""model"": ""MAZ-103"",
        ""registrationNumber"": ""AB1234"",
        ""isActive"": true
    }";
    var jsonObj = JsonNode.Parse(json).AsObject();
    
    // Act
    var bus = jsonObj.ParseBus();
    
    // Assert
    Assert.NotNull(bus);
    Assert.Equal(1u, bus.BusId);
    Assert.Equal("MAZ-103", bus.Model);
    Assert.Equal("AB1234", bus.RegistrationNumber);
    Assert.True(bus.IsActive);
}

[Fact]
public void ParseBus_WithMissingOptionalFields_ReturnsValidBus()
{
    // Arrange
    var json = @"{
        ""busId"": 1,
        ""model"": ""MAZ-103"",
        ""isActive"": true
    }";
    var jsonObj = JsonNode.Parse(json).AsObject();
    
    // Act
    var bus = jsonObj.ParseBus();
    
    // Assert
    Assert.NotNull(bus);
    Assert.Null(bus.RegistrationNumber);
}

[Fact]
public void ParseBus_WithInvalidBusId_ReturnsNull()
{
    // Arrange
    var json = @"{
        ""busId"": 0,
        ""model"": ""MAZ-103""
    }";
    var jsonObj = JsonNode.Parse(json).AsObject();
    
    // Act
    var bus = jsonObj.ParseBus();
    
    // Assert
    Assert.Null(bus);
}
```

## Integration Testing

### Test API Responses

1. **Mock API responses** with various $id/$ref/$values combinations
2. **Test ViewModels** with mocked HttpClient
3. **Verify collections** are populated correctly
4. **Check error handling** when API returns errors

### Example Integration Test

```csharp
[Fact]
public async Task LoadData_WithValidApiResponse_PopulatesCollection()
{
    // Arrange
    var mockHttp = new MockHttpMessageHandler();
    mockHttp.When("/api/Buses")
        .Respond("application/json", @"{
            ""$id"": ""1"",
            ""$values"": [
                {
                    ""busId"": 1,
                    ""model"": ""MAZ-103"",
                    ""isActive"": true
                }
            ]
        }");
    
    var viewModel = new BusManagementViewModel(mockHttp.ToHttpClient());
    
    // Act
    await viewModel.LoadDataCommand.ExecuteAsync(null);
    
    // Assert
    Assert.Single(viewModel.Buses);
    Assert.Equal("MAZ-103", viewModel.Buses[0].Model);
}
```

## Troubleshooting Checklist

- [ ] Is JsonReferenceHelper.ParseArrayWithReferences() being called?
- [ ] Are Parse* extension methods being used?
- [ ] Are logs showing parsing attempts?
- [ ] Is the JSON structure what we expect ($id/$values)?
- [ ] Are field names matching (case-insensitive)?
- [ ] Are DateTime fields in correct format?
- [ ] Are arrays wrapped in $values being handled?
- [ ] Are $ref being resolved correctly?
- [ ] Are null checks working for optional fields?
- [ ] Are type conversions succeeding?

## Support and Maintenance

### When to Update Helpers

Update Parse* methods when:
1. API adds new fields to entities
2. Field types change in API
3. New edge cases discovered
4. Performance optimizations needed

### When to Update ViewModels

Update ViewModels when:
1. New entities need to be parsed (add new Parse* method first)
2. Business logic changes (not parsing logic)
3. UI requirements change

### Documentation

Keep updated:
1. This guide when new patterns emerge
2. COMPLETION_STATUS.md when files change
3. Code comments in helpers for complex logic

## Conclusion

All parsing complexity is centralized in helper methods. This makes:
- **Debugging easier**: One place to set breakpoints
- **Testing simpler**: Test helpers independently
- **Maintenance better**: Changes in one place
- **Code cleaner**: ViewModels focus on business logic

For any parsing issues, start by debugging the appropriate Parse* method in BusParsingExtensions.cs.
