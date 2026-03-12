# COMPLETE WEBSOCKET ENDPOINTS INVENTORY

## ALL WEBSOCKET ENDPOINTS WITH EXACT ROUTES

### 1. BUSES CONTROLLER
**Endpoint**: `GET /api/buses/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/BusesController.cs`
**Line**: 60
**Resource Channel**: `buses`
**Supported Commands**: `read_all`, `read`, `create`, `update`, `delete`

### 2. MAINTENANCE CONTROLLER
**Endpoint**: `GET /api/maintenance/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/MaintenanceController.cs`
**Line**: 56
**Resource Channel**: `maintenance`
**Supported Commands**: `read_all`, `read`, `create`, `update`, `delete`

### 3. EMPLOYEES CONTROLLER
**Endpoint**: `GET /api/employees/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/EmployeesController.cs`
**Line**: 54
**Resource Channel**: `employees`
**Supported Commands**: `read_all`, `read`, `create`, `update`, `delete`

### 4. PERMISSIONS CONTROLLER
**Endpoint**: `GET /api/permissions/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/PermissionsController.cs`
**Line**: 50
**Resource Channel**: `permissions`
**Supported Commands**: `read_all`, `read`, `create`, `update`, `delete`

### 5. JOBS CONTROLLER
**Endpoint**: `GET /api/jobs/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/JobsController.cs`
**Line**: 56
**Resource Channel**: `jobs`
**Supported Commands**: `read_all`, `read`, `create`, `update`, `delete`

### 6. ROLES CONTROLLER
**Endpoint**: `GET /api/roles/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/RolesController.cs`
**Line**: 60
**Resource Channel**: `roles`
**Supported Commands**: `read_all`, `read`, `create`, `update`, `delete`

### 7. USERS CONTROLLER
**Endpoint**: `GET /api/users/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/UsersController.cs`
**Line**: 58
**Resource Channel**: `users`
**Supported Commands**: `read_all`, `read`, `create`, `update`, `delete`

### 8. TICKET SALES CONTROLLER
**Endpoint**: `GET /api/ticketsales/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/TicketSalesController.cs`
**Line**: 73
**Resource Channel**: `ticket-sales`
**Supported Commands**: `read_all`, `read`, `create`, `update` (not implemented), `delete` (not implemented)

### 9. TICKETS CONTROLLER
**Endpoint**: `GET /api/tickets/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/TicketsController.cs`
**Line**: 60
**Resource Channel**: `tickets`
**Supported Commands**: `read_all`, `read`, `create`, `update`, `delete`

### 10. ROUTE SCHEDULES CONTROLLER
**Endpoint**: `GET /api/routeschedules/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/RouteSchedulesController.cs`
**Line**: 48
**Resource Channel**: `route-schedules`
**Supported Commands**: `read_all`, `read`, `create`, `update`, `delete`

### 11. ROUTES CONTROLLER
**Endpoint**: `GET /api/routes/realtime/ws`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/RoutesController.cs`
**Line**: 58
**Resource Channel**: `routes`
**Supported Commands**: `read_all`, `read`, `create`, `update`, `delete`

### 12. REALTIME CONTROLLER (UNIVERSAL TEST ENDPOINT)
**Endpoint**: `GET /api/realtime/stream`
**File**: `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/Controllers/RealtimeController.cs`
**Line**: 30
**Resource Channel**: N/A (Echo/Ping-Pong test endpoint)
**Purpose**: Simple WebSocket echo/ping-pong test endpoint for connectivity testing

---

## SUMMARY

- **Total WebSocket Endpoints**: 12
- **CRUD Endpoints**: 11 (all controllers except RealtimeController)
- **Test Endpoints**: 1 (RealtimeController)
- **All CRUD endpoints support**: `read_all`, `read`, `create`, `update`, `delete` commands
- **Exception**: TicketSalesController has `update` and `delete` marked as "not implemented"

## WEBSOCKET PROTOCOL

All CRUD endpoints use the same protocol:
1. Client connects via WebSocket to the endpoint
2. Client sends JSON messages with structure:
   ```json
   {
     "Command": "read_all|read|create|update|delete",
     "Id": <optional uint for read/update/delete>,
     "Payload": <optional JSON for create/update>,
     "RequestId": <optional correlation id>
   }
   ```
3. Server responds with command-specific JSON results
4. Server also broadcasts domain events to all subscribed clients

## AUTHENTICATION

All endpoints require authentication:
- Most use `IsAuthenticated()` check
- Some use `ValidateOAuthTokenAsync()` for async token validation
- Authorization varies by controller (admin, permissions, etc.)
