# WebSocket Debug Tool

## Overview

The WebSocket Debug Tool is a comprehensive testing utility for validating WebSocket connections to the BRU Avtopark API. It was created to test the WebSocket functionality added in commit `2c7cec6`.

## Features

### 🔌 Connection Management

- Connect/disconnect to WebSocket endpoints
- Support for Bearer token authentication
- Real-time connection status indicator
- Automatic reconnection handling

### 🧪 Controller Testing

- Test all 11 API controllers:
  - Buses
  - Employees
  - Jobs
  - Maintenance
  - Permissions
  - Roles
  - Routes
  - Route Schedules
  - Tickets
  - Ticket Sales
  - Users

### 📡 Event Monitoring

- Real-time event log with timestamps
- Color-coded status indicators:
  - 🟢 Green: Passed tests
  - 🔴 Red: Failed tests
  - 🔵 Blue: Tests in progress
  - ⚫ Gray: Not tested yet

### 🎯 Test Features

- Test all controllers at once
- Test individual controllers
- Send broadcast test events
- View detailed test results
- Reset tests
- Clear event log

## Usage

### 1. Launch the Tool

From the main application:

```csharp
var debugWindow = new WebSocketDebugWindow();
debugWindow.Show();
```

### 2. Configure Connection

1. Enter the server URL (default: `ws://localhost:5000`)
2. Optionally enter an access token for authentication
3. Click "🔌 Connect"

### 3. Run Tests

#### Test All Controllers

Click "🧪 Test All Controllers" to run tests on all API endpoints sequentially.

#### Test Individual Controller

Click the "Test" button next to any controller in the list.

#### Send Broadcast Test

Click "📡 Send Broadcast Test" to trigger a system broadcast event.

### 4. Monitor Results

- Watch the event log for real-time updates
- Check controller status indicators
- View last tested timestamps
- Review error messages

## WebSocket Endpoint Types

The BRU Avtopark API provides **two types of WebSocket endpoints**:

### 1. Controller-Specific CRUD Endpoints (11 endpoints)

Each resource controller provides its own dedicated WebSocket endpoint:

- **Buses**: `/api/buses/realtime/ws`
- **Employees**: `/api/employees/realtime/ws`
- **Jobs**: `/api/jobs/realtime/ws`
- **Maintenance**: `/api/maintenance/realtime/ws`
- **Permissions**: `/api/permissions/realtime/ws`
- **Roles**: `/api/roles/realtime/ws`
- **Routes**: `/api/routes/realtime/ws`
- **Route Schedules**: `/api/routeschedules/realtime/ws`
- **Tickets**: `/api/tickets/realtime/ws`
- **Ticket Sales**: `/api/ticketsales/realtime/ws`
- **Users**: `/api/users/realtime/ws`

**Characteristics**:
- Each endpoint is resource-specific and isolated
- Supports standard CRUD commands: `read_all`, `read`, `create`, `update`, `delete`
- Automatically broadcasts domain events for that resource
- Requires authentication with resource-specific authorization

### 2. Universal Stream Endpoint (1 endpoint)

A single unified endpoint that supports dynamic routing across all resources:

- **Universal Stream**: `/api/realtime/stream`

**Characteristics**:
- Supports dynamic resource routing via `resource` parameter in messages
- Supports CRUD commands for all resources through a single connection
- Supports event subscriptions across multiple resources simultaneously
- Supports pagination commands: `read_all`, `next_page`, `prev_page`, `first_page`, `last_page`, `goto_page`
- Includes ping/pong functionality for connection health checks
- Can subscribe/unsubscribe to multiple resource streams in real-time
- Uses a resource routing map to resolve services dynamically

**When to Use Which**:
- **Controller-Specific**: Use when working with a single resource and want a dedicated connection
- **Universal Stream**: Use when working with multiple resources, need pagination, or want a single connection for everything

## WebSocket Protocol

The tool uses the `bru.events.v1` subprotocol and communicates with:

### Endpoint

The debug tool primarily tests the **Universal Stream Endpoint**:

```text
ws://[server]/api/realtime/stream
```

But can also test individual **Controller-Specific Endpoints**:

```text
ws://[server]/api/{resource}/realtime/ws
```

### Message Format

#### Request (Client → Server)

```json
{
  "command": "read",
  "requestId": "unique-guid",
  "resource": "buses"
}
```

#### Response (Server → Client)

```json
{
  "type": "result",
  "requestId": "unique-guid",
  "ok": true,
  "data": { ... }
}
```

#### Event (Server → Client)

```json
{
  "type": "event",
  "eventName": "bus.created",
  "resource": "buses",
  "data": { ... }
}
```

## Architecture

### Components

1. **WebSocketDebugViewModel**
   - Manages WebSocket connection
   - Handles test execution
   - Maintains event log
   - Tracks test results

2. **WebSocketDebugWindow**
   - Main UI window
   - Two-panel layout:
     - Left: Connection settings and test controls
     - Right: Event log

3. **ControllerTestResult**
   - Represents test status for each controller
   - Tracks last test time
   - Stores error messages

### Value Converters

- `BoolToColorConverter`: Connection status indicator
- `BoolToConnectTextConverter`: Connect/disconnect button text
- `NullToVisibilityConverter`: Show/hide last tested time

## Testing the WebSocket Implementation

### Prerequisites

1. API server running on `http://localhost:5000`
2. Valid authentication token (if required)
3. WebSocket endpoint enabled at `/api/realtime/stream`

### Test Scenarios

#### 1. Basic Connectivity

1. Launch the tool
2. Click "Connect"
3. Verify connection status turns green
4. Check event log for connection confirmation

#### 2. Controller Validation

1. Connect to the server
2. Click "Test All Controllers"
3. Verify each controller shows "Passed" status
4. Check event log for request/response pairs

#### 3. Event Broadcasting

1. Connect to the server
2. Click "Send Broadcast Test"
3. Verify event appears in the log
4. Check for `system.broadcast-test` event

#### 4. Error Handling

1. Enter invalid server URL
2. Attempt to connect
3. Verify error message appears
4. Check connection status remains red

## Troubleshooting

### Connection Fails

- Verify API server is running
- Check server URL format (ws:// or wss://)
- Ensure firewall allows WebSocket connections
- Verify authentication token is valid

### Tests Fail

- Check API endpoint availability
- Verify controller names match API routes
- Review event log for error details
- Ensure proper authorization

### No Events Received

- Verify WebSocket subprotocol support
- Check server-side event publishing
- Review server logs for errors
- Ensure event bus is configured

## Implementation Details

### Commit Reference

This tool was created to test WebSocket functionality added in:

- **Commit**: `2c7cec6`
- **PR**: #4 - "Add realtime event bus (SignalR/WebSocket) with mutation event publishing"

### Files Modified/Added

- `Controllers/RealtimeController.cs` - WebSocket endpoint
- `Realtime/Infrastructure/WebSocketEventStreamWriter.cs` - WebSocket handler
- `Realtime/Contracts/ApiDomainEvent.cs` - Event model
- Multiple controller updates for event publishing

### Key Features Tested

- WebSocket connection establishment
- Subprotocol negotiation (`bru.events.v1`)
- CRUD request handling
- Event streaming
- Error handling
- Connection lifecycle management

## Future Enhancements

### Near-term Tool Improvements

- [ ] Save/load connection profiles
- [ ] Export test results
- [ ] Performance metrics
- [ ] Stress testing capabilities
- [ ] Custom message templates
- [ ] WebSocket message history
- [ ] Automated test suites
- [ ] Integration with CI/CD

### Long-term Architecture Considerations

#### Elysia JS as Authentication Gateway

The project is evaluating **Elysia JS** (TypeScript web framework on Bun runtime) as a potential future authentication gateway layer. This would be a significant architectural evolution:

**Potential Benefits**:
- **Simplified OAuth/OIDC**: Replace OpenIddict complexity with `@myazarc/elysia-oauth2-server` plugin
- **Native WebSocket support**: Built-in WebSocket handling for real-time authentication
- **Type-safe routing**: Compile-time type checking and modern TypeScript DX
- **Independent scaling**: Auth layer can scale separately from business logic
- **Better PKCE handling**: Native PKCE support for public clients (mobile apps, SPAs)

**Three Architecture Options Under Consideration**:

1. **Authentication Gateway**: Elysia handles all auth, proxies to C# backend for business logic
2. **OAuth Proxy**: Elysia handles only OAuth/OIDC flows, C# keeps other auth methods
3. **Hybrid Approach**: Gradual adoption - Elysia for new features, C# for existing features

**Timeline (Tentative)**:
- **Short-term** (current): Complete C# authentication refactoring first
- **Medium-term** (6-12 months): Evaluate Elysia for OAuth if pain points persist
- **Long-term** (12+ months): Consider full migration to Elysia as auth gateway

**Current Status**: The Elysia approach is documented in `.kiro/specs/auth-controller-refactoring/design.md` as a valid alternative architecture. However, it is **NOT a current priority**. The team will complete the C# refactoring first, then evaluate based on actual pain points.

**Impact on WebSocket Testing**: If Elysia is adopted, this debug tool would need updates to:
- Support Elysia-specific authentication flows
- Test OIDC-over-WebSocket scenarios
- Handle two-layer authentication (Elysia gateway + C# backend)
- Validate JWT tokens generated by Elysia

**Validation Status**: ✅ The Elysia JS vision is **well-architected and pragmatic**:
- Addresses real pain points (OpenIddict + SpacetimeDB complexity)
- Provides clear migration phases with proof-of-concept first
- Acknowledges trade-offs (two runtimes, deployment complexity, learning curve)
- Recommends risk-averse approach (complete current work first)
- For full OIDC compliance, pairs with industry-standard `oidc-provider` by panva

## Support

For issues or questions:

1. Check the event log for detailed error messages
2. Review server logs for backend errors
3. Verify WebSocket implementation in commit `2c7cec6`
4. Contact the development team

## License

Part of the BRU Avtopark system.