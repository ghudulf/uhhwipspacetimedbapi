# WebSocket Debug Tool

## Overview

The WebSocket Debug Tool is a comprehensive testing utility for validating WebSocket connections to the BRU Avtopark API. It was created to test the WebSocket functionality introduced in this pull request.

## Features

### 🔌 Connection Management

- Connect/disconnect to WebSocket endpoints
- Support for Bearer token authentication
- Real-time connection status indicator
- Manual reconnection via Connect button

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

1. Enter the server URL (default: `http://localhost:5000` for local development; use `https://` for production environments)
   - Note: The client automatically maps `http://` to `ws://` and `https://` to `wss://` for WebSocket connections
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

## WebSocket Protocol

The tool uses the `bru.events.v1` subprotocol and communicates with:

### Endpoint

```text
ws://[server]/api/realtime/stream
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
  "timestamp": "2024-01-01T00:00:00Z",
  "metadata": {
    "userId": "user-123",
    "correlationId": "abc-123"
  }
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

This tool was created to test WebSocket functionality added in the realtime event bus implementation.

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

- [ ] Save/load connection profiles
- [ ] Export test results
- [ ] Performance metrics
- [ ] Stress testing capabilities
- [ ] Custom message templates
- [ ] WebSocket message history
- [ ] Automated test suites
- [ ] Integration with CI/CD

## Support

For issues or questions:

1. Check the event log for detailed error messages
2. Review server logs for backend errors
3. Verify WebSocket implementation in the realtime event bus
4. Contact the development team

## License

Part of the BRU Avtopark system.
