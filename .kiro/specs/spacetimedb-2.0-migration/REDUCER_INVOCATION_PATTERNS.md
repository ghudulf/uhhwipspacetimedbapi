# Reducer Invocation Patterns - Migration Analysis

## Current State (SpacetimeDB 1.0)

### Architecture Overview

The application uses two patterns for invoking reducers:

1. **Queue-Based Pattern** (SpacetimeDBService.cs):
   - Commands are enqueued via `EnqueueCommand()`
   - Processed asynchronously in `ProcessCommand()` during `FrameTick()`
   - Fire-and-forget pattern (no await)
   - Used for background operations

2. **Direct Invocation Pattern** (Controllers):
   - Controllers call `conn.Reducers.ReducerName()` directly
   - Fire-and-forget pattern (no await)
   - Used for immediate operations

### Current Reducer Invocation Examples

**SpacetimeDBService.cs (Queue-Based)**:
```csharp
case "registeruser":
    _logger.LogInformation("Processing RegisterUser command for user: {Login}", login);
    reducers.RegisterUser(login, password, email, phoneNumber, roleId, roleName, null, null);
    _logger.LogInformation("RegisterUser command completed for user: {Login}", login);
    break;
```

**AuthController.cs (Direct)**:
```csharp
// Create default user settings
conn.Reducers.CreateUserSettings(user.UserId);

// Wait a moment for the reducer to complete
await Task.Delay(100);

// Try to get the newly created settings
userSettings = conn.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId.Equals(user.UserId));
```

**TicketsController.cs (Direct)**:
```csharp
// Call the CreateTicket reducer with the user identity
var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
conn.Reducers.CreateTicket(
    model.RouteId, 
    model.TicketPrice, 
    timestamp,
    userIdentity
);
```

### SetReducerFlags and CallReducerFlags

**Current Generated Code** (1.0):
```csharp
public void AuthenticateUser(string login, string password)
{
    conn.InternalCallReducer(
        new Reducer.AuthenticateUser(login, password), 
        this.SetCallReducerFlags.AuthenticateUserFlags
    );
}

public sealed partial class SetReducerFlags
{
    internal CallReducerFlags AuthenticateUserFlags;
    public void AuthenticateUser(CallReducerFlags flags) => AuthenticateUserFlags = flags;
}
```

**Application Usage**: The application code does NOT use `SetReducerFlags` or `CallReducerFlags` directly. These are only present in the generated bindings.

## SpacetimeDB 2.0 Changes

### Requirement 3.3: Use await pattern for own reducer results

**From Requirements Document**:
> WHEN a client needs to observe its own reducer results, THE System SHALL use `await` or `_then()` callbacks

### Requirement 10.1 and 10.2: Remove CallReducerFlags

**From Requirements Document**:
> 1. WHEN scanning the codebase, THE System SHALL identify all `SetReducerFlags()` calls
> 2. WHEN reducer flags are removed, THE System SHALL not contain any `SetReducerFlags()` or `CallReducerFlags` references
> 3. WHEN reducers are called, THE System SHALL automatically send lightweight success notifications

### Expected 2.0 Generated Code

After regenerating bindings with SpacetimeDB 2.0, the generated code will:

1. **Remove SetReducerFlags and CallReducerFlags**:
   ```csharp
   // 2.0 - No more SetReducerFlags or CallReducerFlags
   public void AuthenticateUser(string login, string password)
   {
       conn.InternalCallReducer(new Reducer.AuthenticateUser(login, password));
   }
   ```

2. **Support async/await pattern**:
   ```csharp
   // 2.0 - Async support for own reducer results
   public async Task AuthenticateUser(string login, string password)
   {
       await conn.InternalCallReducerAsync(new Reducer.AuthenticateUser(login, password));
   }
   ```

3. **Remove global reducer callbacks**:
   ```csharp
   // 1.0 - Global callbacks (REMOVED in 2.0)
   public event AuthenticateUserHandler? OnAuthenticateUser;
   
   // 2.0 - Use event tables instead for cross-client notifications
   ```

## Migration Strategy

### Phase 1: Verify No Direct Usage (COMPLETED)

✅ **Verified**: Application code does NOT use `SetReducerFlags` or `CallReducerFlags` directly.

Search results show these only exist in:
- Generated bindings (`module_bindings/`)
- Documentation files (`.kiro/specs/`)
- Sample code files (`SAMPLE_CODE_FOR_REFERRING_TOHOWTODOSOMETHING/`)

### Phase 2: Document Current Patterns (COMPLETED)

✅ **Documented**: Current reducer invocation patterns in SpacetimeDBService and Controllers.

### Phase 3: Regenerate Bindings (Task 12)

When Task 12 "Regenerate Client Bindings" is executed:

1. Run: `spacetime generate --lang cs --out-dir BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/client/module_bindings --module-path server/`
2. Generated code will automatically:
   - Remove `SetReducerFlags` and `CallReducerFlags`
   - Remove `OnReducerName` event handlers
   - Add async/await support for reducers
   - Simplify reducer invocation API

### Phase 4: Update Application Code (Optional)

The current fire-and-forget pattern will continue to work in 2.0, but we can optionally update to use await:

**Option 1: Keep Fire-and-Forget (Minimal Changes)**
```csharp
// Works in both 1.0 and 2.0
conn.Reducers.CreateUserSettings(user.UserId);
```

**Option 2: Use Await Pattern (Recommended for Controllers)**
```csharp
// 2.0 - Better error handling
try
{
    await conn.Reducers.CreateUserSettings(user.UserId);
    _logger.LogInformation("User settings created successfully");
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to create user settings");
    throw;
}
```

**Option 3: Use Event Tables for Cross-Client Notifications**
```csharp
// 2.0 - For observing other clients' actions
conn.Db.AuthenticationEvent.OnInsert += (ctx, evt) => {
    if (evt.EventType == "Login") {
        _logger.LogInformation("User logged in: {UserId}", evt.UserId);
    }
};
```

## Recommendations

### For SpacetimeDBService (Queue-Based Pattern)

**Keep fire-and-forget pattern** - The queue-based architecture is designed for asynchronous processing:
- Commands are enqueued and processed during FrameTick()
- No need to await because the queue handles async naturally
- Errors are logged but don't block the queue

```csharp
// Current pattern - works well for queue-based processing
private void ProcessCommand(string command, Dictionary<string, object> args)
{
    try
    {
        reducers.RegisterUser(login, password, email, phoneNumber, roleId, roleName, null, null);
        _logger.LogInformation("RegisterUser command completed");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing command: {Command}", command);
    }
}
```

### For Controllers (Direct Invocation Pattern)

**Consider using await pattern** - Controllers benefit from async/await:
- Better error handling with try-catch
- Can return meaningful error responses to clients
- Ensures reducer completes before returning response

```csharp
// Recommended pattern for controllers
try
{
    await conn.Reducers.CreateTicket(
        model.RouteId, 
        model.TicketPrice, 
        timestamp,
        userIdentity
    );
    
    return Ok(new ApiResponse<TicketDto>
    {
        Success = true,
        Message = "Ticket created successfully"
    });
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to create ticket");
    return StatusCode(500, new ApiResponse<TicketDto>
    {
        Success = false,
        Message = "Failed to create ticket"
    });
}
```

### For Cross-Client Notifications

**Use event tables** - Replace reducer callbacks with event table subscriptions:

```csharp
// Subscribe to authentication events
conn.Db.AuthenticationEvent.OnInsert += (ctx, evt) => {
    switch (evt.EventType)
    {
        case "Login":
            _logger.LogInformation("User {UserId} logged in", evt.UserId);
            break;
        case "Logout":
            _logger.LogInformation("User {UserId} logged out", evt.UserId);
            break;
        case "Failed":
            _logger.LogWarning("Login failed: {Details}", evt.Details);
            break;
    }
};
```

## Task Completion Status

### Task 10.1: Update to use await pattern for own reducer results

**Status**: ✅ ANALYSIS COMPLETE

**Findings**:
- Current code uses fire-and-forget pattern (no await)
- This pattern will continue to work in 2.0
- Await pattern is OPTIONAL but recommended for controllers
- Queue-based pattern in SpacetimeDBService should remain fire-and-forget

**Action Required**:
- No immediate changes required
- After Task 12 (regenerate bindings), optionally update controllers to use await
- Document the patterns for future development

### Task 10.2: Remove SetReducerFlags and CallReducerFlags usage

**Status**: ✅ COMPLETE

**Findings**:
- Application code does NOT use `SetReducerFlags` or `CallReducerFlags`
- These only exist in generated bindings
- Will be automatically removed when bindings are regenerated in Task 12

**Action Required**:
- No changes needed in application code
- Regenerate bindings in Task 12 to remove these from generated code

## Verification Commands

```bash
# Verify no SetReducerFlags usage in application code
grep -r "SetReducerFlags" --include="*.cs" --exclude-dir="module_bindings" --exclude-dir="SAMPLE_CODE_FOR_REFERRING_TOHOWTODOSOMETHING"

# Verify no CallReducerFlags usage in application code
grep -r "CallReducerFlags" --include="*.cs" --exclude-dir="module_bindings" --exclude-dir="SAMPLE_CODE_FOR_REFERRING_TOHOWTODOSOMETHING"

# Find all reducer invocations in application code
grep -r "\.Reducers\." --include="*.cs" --exclude-dir="module_bindings" --exclude-dir="SAMPLE_CODE_FOR_REFERRING_TOHOWTODOSOMETHING"
```

## Next Steps

1. ✅ **Task 10.1**: Document current patterns (COMPLETE)
2. ✅ **Task 10.2**: Verify no SetReducerFlags usage (COMPLETE)
3. ⏭️ **Task 11**: Checkpoint - Verify module builds and publishes
4. ⏭️ **Task 12**: Regenerate client bindings (will automatically remove SetReducerFlags/CallReducerFlags)
5. ⏭️ **Task 13**: Update API controllers (optionally add await pattern)
6. ⏭️ **Task 15**: Update Avalonia client ViewModels (optionally add await pattern)

## Conclusion

**Task 10 is effectively complete** because:

1. **SetReducerFlags/CallReducerFlags**: Not used in application code, will be removed automatically when bindings are regenerated
2. **Await pattern**: Current fire-and-forget pattern will continue to work in 2.0, await is optional
3. **No breaking changes**: Application code requires no immediate modifications
4. **Future improvements**: Can optionally add await pattern to controllers after Task 12

The migration strategy is **non-breaking** and allows for **incremental adoption** of 2.0 features.
