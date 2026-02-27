# BRU AVTOPARK - Bus Fleet Management System

A comprehensive multi-tenant SaaS application for managing bus fleet operations, ticket sales, employee management, and maintenance scheduling. Built with SpacetimeDB 2.0, .NET 9, and Avalonia UI.

## 🏗️ Architecture Overview

### Technology Stack

- **Backend**: SpacetimeDB 2.0 (WebAssembly-based distributed database)
- **API Layer**: ASP.NET Core 9.0 with .NET Aspire orchestration
- **Desktop Client**: Avalonia UI 11.2 (cross-platform XAML framework)
- **Authentication**: OpenID Connect with OAuth 2.0, WebAuthn, TOTP
- **Real-time Sync**: SpacetimeDB client SDK with automatic data synchronization

### Project Structure

```
SpacetimeDB-BRU-AVTOPARK-avtobusov/
├── server/                                    # SpacetimeDB Module (Rust-compiled to WASM)
│   ├── AuthTables.cs                         # User authentication tables
│   ├── BusAndRouteTables.cs                  # Bus and route management
│   ├── EconomicTables.cs                     # Financial and ticket sales
│   ├── EmployeeTables.cs                     # Employee and job management
│   ├── EventTables.cs                        # Event publishing tables
│   ├── OpenIdConnectTables.cs                # OAuth/OIDC tables
│   ├── RoleAndPermReducers.cs                # RBAC reducers
│   ├── INITreducers.cs                       # Initialization and CRUD reducers
│   └── Lib.cs                                # Shared utilities and counters
│
├── BRU-AVTOPARK-AspireAPI/                   # .NET Aspire API Layer
│   ├── BRU-AVTOPARK-AspireAPI.ApiService/    # REST API Controllers
│   ├── BRU-AVTOPARK-AspireAPI.AppHost/       # Aspire orchestration
│   ├── BRU-AVTOPARK-AspireAPI.ServiceDefaults/ # Shared service configuration
│   └── TicketSalesApp.Services/              # Business logic layer
│       ├── Implementations/                   # Service implementations
│       ├── Interfaces/                        # Service contracts
│       └── client/module_bindings/            # Generated SpacetimeDB bindings
│
└── BRU.Avtopark.TicketSalesAPP.Avalonia.Unity/ # Desktop Client
    ├── ViewModels/                            # MVVM ViewModels
    ├── Views/                                 # XAML Views
    ├── Services/                              # Client services
    └── Controls/                              # Custom UI controls
```

## 🗄️ Database Architecture

### SpacetimeDB Module Design

SpacetimeDB is a distributed database that compiles to WebAssembly and runs on the server. The module defines:

- **Tables**: Data structures with automatic indexing
- **Reducers**: Server-side functions that modify data (like stored procedures)
- **Events**: Publish-subscribe event tables for real-time notifications

### Core Tables (40+ tables)

#### Authentication & Authorization
- `UserProfile` - User accounts with identity management
- `UserSettings` - User preferences and 2FA settings
- `Role` - Role definitions with priority levels
- `Permission` - Granular permission definitions
- `UserRole` - Many-to-many user-role assignments
- `RolePermission` - Many-to-many role-permission assignments
- `TwoFactorToken` - Temporary 2FA tokens
- `TotpSecret` - TOTP secrets for authenticator apps
- `WebAuthnCredential` - Hardware security key credentials
- `WebAuthnChallenge` - WebAuthn authentication challenges
- `MagicLinkToken` - Passwordless email login tokens
- `QrSession` - QR code authentication sessions

#### OpenID Connect / OAuth 2.0
- `OpenIdConnect` - OAuth client registrations
- `OpenIdConnectGrant` - Authorization grants
- `OpenIddictSpacetimeAuthorization` - Authorization records
- `OpenIddictSpacetimeToken` - Access/refresh tokens
- `OpenIddictSpacetimeScope` - OAuth scopes

#### Bus Fleet Management
- `Bus` - Bus inventory with registration details
- `BusLocation` - Real-time GPS tracking
- `Maintenance` - Maintenance schedules and history
- `FuelRecord` - Fuel consumption tracking
- `SeatConfiguration` - Bus seating layouts

#### Route Management
- `Route` - Bus routes with start/end points
- `RouteSchedule` - Scheduled route times
- `BusStop` - Bus stop locations
- `RouteConductor` - Conductor assignments

#### Employee Management
- `Employee` - Employee records
- `Job` - Job positions and descriptions
- `EmployeeShift` - Shift scheduling
- `ConductorStatistics` - Performance metrics

#### Ticket Sales & Economics
- `Ticket` - Ticket inventory
- `Sale` - Sales transactions
- `CashierDay` - Daily cashier reports
- `Discounts` - Discount rules
- `PassengerCount` - Passenger statistics

#### Event Tables (Real-time Notifications)
- `AuthenticationEvent` - Login/logout events
- `TicketSaleEvent` - Ticket transaction events
- `BusStatusEvent` - Bus status changes
- `RouteScheduleEvent` - Schedule updates
- `MaintenanceEvent` - Maintenance notifications

#### Audit & Logging
- `AdminActionLog` - Administrative action audit trail
- `Incident` - Incident reports

### Auto-Incrementing ID System

SpacetimeDB doesn't have built-in auto-increment, so we implement it using counter tables:

```csharp
[SpacetimeDB.Table]
public partial class UserIdCounter
{
    [PrimaryKey] public string Key = "userId";
    public uint NextId = 0;
}
```

Each entity type has a corresponding counter table that's atomically incremented in reducers.

## 🔐 Role-Based Access Control (RBAC)

### Permission System

The system implements a comprehensive RBAC model with:

1. **Roles**: Named collections of permissions with priority levels
2. **Permissions**: Granular access rights (e.g., `users.create`, `buses.edit`)
3. **Role Hierarchy**: Higher priority roles override lower priority roles
4. **Permission Categories**: Organized by domain (auth, users, buses, routes, etc.)

### Permission Format

Permissions follow the pattern: `<resource>.<action>`

Examples:
- `users.create` - Create new users
- `users.edit` - Modify user accounts
- `users.delete` - Delete users
- `buses.view` - View bus information
- `buses.edit` - Modify bus records
- `roles.assign` - Assign roles to users
- `permissions.grant` - Grant permissions to roles

### Built-in Roles

- **System Administrator** (Priority: 1000)
  - Full system access
  - Can manage all resources
  - Cannot be deleted

- **Administrator** (Priority: 900)
  - Manage users, roles, and permissions
  - Access to all business functions

- **Manager** (Priority: 500)
  - Manage operations
  - View reports and analytics

- **Conductor** (Priority: 300)
  - Sell tickets
  - View routes and schedules

- **Driver** (Priority: 200)
  - View assigned routes
  - Update bus status

- **User** (Priority: 100)
  - Basic access
  - View own profile

### Permission Checking

```csharp
// In reducers
public static bool HasPermission(ReducerContext ctx, Identity userId, string permissionName)
{
    var userRoles = ctx.Db.UserRole.Iter()
        .Where(ur => ur.UserId.Equals(userId))
        .ToList();
    
    foreach (var userRole in userRoles)
    {
        var rolePerms = ctx.Db.RolePermission.Iter()
            .Where(rp => rp.RoleId == userRole.RoleId)
            .ToList();
        
        foreach (var rolePerm in rolePerms)
        {
            var permission = ctx.Db.Permission.PermissionId.Find(rolePerm.PermissionId);
            if (permission != null && permission.Name == permissionName && permission.IsActive)
            {
                return true;
            }
        }
    }
    return false;
}
```

## 🔑 Authentication System

### Multi-Factor Authentication

The system supports multiple authentication methods:

#### 1. Password Authentication
- PBKDF2 password hashing with SHA-256
- Configurable iteration count (default: 200 for WASM performance)
- Salt-based security

#### 2. TOTP (Time-based One-Time Password)
- Compatible with Google Authenticator, Authy, Microsoft Authenticator
- QR code generation for easy setup
- 6-digit codes with 30-second validity

#### 3. WebAuthn (Hardware Security Keys)
- FIDO2/WebAuthn standard support
- YubiKey, Windows Hello, Touch ID compatible
- Passwordless authentication option

#### 4. Magic Links
- Email-based passwordless login
- Time-limited tokens
- Device and IP tracking

#### 5. QR Code Authentication
- Mobile app integration
- Session-based approval flow
- Real-time status updates

### OAuth 2.0 / OpenID Connect

Full OAuth 2.0 server implementation with:

- **Authorization Code Flow** with PKCE
- **Client Credentials Flow**
- **Refresh Token Flow**
- **Scope-based Authorization**
- **Consent Management**
- **Token Introspection**
- **Dynamic Client Registration**

#### OAuth Endpoints

```
GET  /api/auth/connect/authorize      - Authorization endpoint
POST /api/auth/connect/token          - Token endpoint
POST /api/auth/connect/introspect     - Token introspection
POST /api/auth/connect/revoke         - Token revocation
GET  /api/auth/.well-known/openid-configuration - Discovery
```

### WebView OAuth Integration

The Avalonia desktop client includes embedded WebView for OAuth flows:

```csharp
// Embedded browser for OAuth
private WebView? _webView;

private async Task LoadWithWebViewAsync()
{
    _webView = new WebView
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };
    
    _webView.NavigationStarting += OnWebViewNavigationStarting;
    _webView.Url = new Uri(_authorizationUrl);
}
```

Falls back to system browser if WebView fails.

## � System Purpose & Features

### Business Problem

BRU AVTOPARK is designed to solve the complex operational challenges of bus fleet management companies:

1. **Fleet Management Complexity**: Tracking dozens or hundreds of buses, their maintenance schedules, fuel consumption, and real-time locations
2. **Route Optimization**: Managing multiple routes with varying schedules, conductor assignments, and passenger loads
3. **Employee Coordination**: Scheduling drivers, conductors, and maintenance staff across shifts
4. **Ticket Sales & Revenue**: Processing ticket sales, managing discounts, tracking revenue, and generating financial reports
5. **Compliance & Auditing**: Maintaining detailed logs of all operations for regulatory compliance
6. **Multi-tenant Operations**: Supporting multiple bus companies or divisions with isolated data and permissions

### Core Features

#### 1. Fleet Management System
- **Real-time Bus Tracking**: GPS integration for live vehicle location monitoring
- **Maintenance Scheduling**: Preventive and corrective maintenance planning
- **Fuel Management**: Track fuel consumption, costs, and efficiency metrics
- **Vehicle Status Monitoring**: Active, inactive, in-maintenance status tracking
- **Roadworthiness Tracking**: Compliance with safety regulations
- **Bus Configuration**: Seat layouts, capacity, and amenities

#### 2. Route & Schedule Management
- **Dynamic Route Planning**: Create and modify bus routes with multiple stops
- **Schedule Optimization**: Time-based scheduling with frequency management
- **Conductor Assignment**: Assign conductors to specific routes and shifts
- **Real-time Updates**: Push schedule changes to drivers and passengers
- **Route Analytics**: Performance metrics, passenger counts, revenue per route

#### 3. Employee Management
- **Comprehensive HR System**: Employee records with job assignments
- **Shift Scheduling**: Automated shift planning with conflict detection
- **Performance Tracking**: Conductor statistics, driver safety records
- **Job Management**: Define positions, responsibilities, and requirements
- **Attendance Tracking**: Clock-in/out with shift validation

#### 4. Ticket Sales & Revenue
- **Point-of-Sale System**: Fast ticket sales interface for conductors
- **Multiple Ticket Types**: Single, return, monthly passes, student discounts
- **Discount Management**: Rule-based discount system
- **Payment Processing**: Cash and digital payment support
- **Receipt Generation**: Automatic receipt printing/emailing
- **Revenue Tracking**: Real-time sales monitoring and reporting

#### 5. Financial Management
- **Daily Cashier Reports**: End-of-day reconciliation
- **Revenue Analytics**: Income by route, time period, ticket type
- **Expense Tracking**: Fuel, maintenance, salary costs
- **Profit/Loss Reports**: Comprehensive financial statements
- **Budget Planning**: Forecasting and budget allocation

#### 6. Security & Compliance
- **Multi-Factor Authentication**: TOTP, WebAuthn, Magic Links
- **Role-Based Access Control**: Granular permissions system
- **Audit Logging**: Complete trail of all administrative actions
- **Data Encryption**: Secure storage and transmission
- **Compliance Reports**: Regulatory reporting capabilities

#### 7. Real-time Notifications
- **Event-Driven Architecture**: Instant updates via SpacetimeDB events
- **Authentication Events**: Login/logout notifications
- **Ticket Sales Events**: Real-time sales monitoring
- **Bus Status Events**: Vehicle status changes
- **Maintenance Alerts**: Upcoming maintenance reminders
- **Schedule Changes**: Route and schedule update notifications

#### 8. Multi-Tenant Architecture
- **Data Isolation**: Complete separation between tenants
- **Custom Branding**: Per-tenant UI customization
- **Independent Permissions**: Tenant-specific role configurations
- **Scalable Design**: Support for unlimited tenants

## 🎨 User Interface

### Avalonia Desktop Client

Modern, responsive desktop application with:

- **Yandex ID-inspired Design**: Clean, professional UI
- **Dark/Light Themes**: Automatic theme switching
- **Responsive Layouts**: Adapts to different screen sizes
- **Real-time Updates**: Automatic data synchronization
- **Offline Support**: Local caching with sync on reconnect

### Key Views

1. **Authentication**
   - Login with multiple 2FA options
   - Registration with role selection
   - Profile management
   - Security settings

2. **Bus Management**
   - Bus inventory CRUD
   - Maintenance scheduling
   - Real-time location tracking
   - Status monitoring

3. **Route Management**
   - Route planning
   - Schedule management
   - Conductor assignments
   - Stop management

4. **Employee Management**
   - Employee records
   - Job assignments
   - Shift scheduling
   - Performance tracking

5. **Ticket Sales**
   - Point-of-sale interface
   - Ticket printing
   - Payment processing
   - Sales reports

6. **Analytics & Reports**
   - Revenue reports
   - Passenger statistics
   - Route performance
   - Maintenance costs

### Profile Page Features

The web-based profile page (`/api/auth/profile`) includes:

- User information display
- Role and permission overview
- 2FA management (TOTP, WebAuthn)
- Security settings
- Session management
- OAuth client management (for admins)

## 🔧 API Layer

### REST API Controllers

The API layer provides RESTful endpoints for all system operations. Each controller is secured with JWT authentication and role-based authorization.

#### AuthController (`/api/auth`)

**Purpose**: Complete authentication and authorization system with OAuth 2.0 server capabilities.

**Key Endpoints**:

```http
# Authentication
POST   /api/auth/login                    # User login with 2FA support
POST   /api/auth/register                 # New user registration
POST   /api/auth/logout                   # User logout
GET    /api/auth/profile                  # User profile page (HTML)

# OAuth 2.0 / OpenID Connect
GET    /api/auth/connect/authorize        # OAuth authorization endpoint
POST   /api/auth/connect/token            # Token endpoint (access/refresh)
POST   /api/auth/connect/introspect       # Token introspection
POST   /api/auth/connect/revoke           # Token revocation
GET    /api/auth/.well-known/openid-configuration  # OIDC discovery

# OAuth Client Management
GET    /api/auth/clients                  # List OAuth clients (HTML)
GET    /api/auth/clients/{clientId}       # Get client details
POST   /api/auth/clients/register         # Register new OAuth client
PUT    /api/auth/clients/{clientId}       # Update client
DELETE /api/auth/clients/{clientId}       # Delete client
GET    /api/auth/connect/scopes           # List available scopes (HTML)

# Two-Factor Authentication
GET    /api/auth/totp/setup               # TOTP setup page (QR code)
POST   /api/auth/totp/verify              # Verify TOTP code
POST   /api/auth/totp/enable              # Enable TOTP for user
POST   /api/auth/totp/disable             # Disable TOTP

# WebAuthn (Hardware Keys)
GET    /api/auth/webauthn/register        # WebAuthn registration page
POST   /api/auth/webauthn/register/complete  # Complete registration
GET    /api/auth/webauthn/login           # WebAuthn login page
POST   /api/auth/webauthn/login/complete  # Complete login

# Magic Links
POST   /api/auth/magiclink/send           # Send magic link email
GET    /api/auth/magiclink/verify         # Verify magic link token

# QR Code Authentication
POST   /api/auth/qr/create                # Create QR session
POST   /api/auth/qr/approve               # Approve QR session
GET    /api/auth/qr/status/{sessionId}    # Check QR session status
```

**Features**:
- Multiple authentication methods (password, TOTP, WebAuthn, magic links, QR)
- Full OAuth 2.0 server with PKCE support
- Dynamic client registration
- Scope-based authorization
- Consent management
- HTML profile and management pages
- JWT token generation and validation

---

#### UsersController (`/api/users`)

**Purpose**: User account management and administration.

**Key Endpoints**:

```http
GET    /api/users                         # List all users (paginated)
GET    /api/users/{id}                    # Get user by ID
POST   /api/users                         # Create new user
PUT    /api/users/{id}                    # Update user
DELETE /api/users/{id}                    # Delete user
GET    /api/users/search?q={query}        # Search users
POST   /api/users/{id}/roles              # Assign role to user
DELETE /api/users/{id}/roles/{roleId}     # Remove role from user
GET    /api/users/{id}/permissions        # Get user's effective permissions
POST   /api/users/{id}/activate           # Activate user account
POST   /api/users/{id}/deactivate         # Deactivate user account
```

**Features**:
- Full CRUD operations
- Role assignment and management
- Permission viewing
- User search and filtering
- Account activation/deactivation
- Bulk operations support

**Required Permissions**:
- `users.view` - View users
- `users.create` - Create users
- `users.edit` - Modify users
- `users.delete` - Delete users
- `roles.assign` - Assign roles

---

#### BusesController (`/api/buses`)

**Purpose**: Bus fleet inventory and management.

**Key Endpoints**:

```http
GET    /api/buses                         # List all buses
GET    /api/buses/{id}                    # Get bus details
POST   /api/buses                         # Add new bus
PUT    /api/buses/{id}                    # Update bus
DELETE /api/buses/{id}                    # Delete bus
GET    /api/buses/search?model={model}    # Search buses
POST   /api/buses/{id}/activate           # Activate bus
POST   /api/buses/{id}/deactivate         # Deactivate bus
GET    /api/buses/{id}/location           # Get current location
GET    /api/buses/{id}/maintenance        # Get maintenance history
```

**Features**:
- Bus inventory management
- Model and registration tracking
- Status management (active/inactive/maintenance)
- Real-time location tracking
- Maintenance history
- Search and filtering

**Required Permissions**:
- `buses.view` - View buses
- `buses.create` - Add buses
- `buses.edit` - Modify buses
- `buses.delete` - Delete buses

---

#### RoutesController (`/api/routes`)

**Purpose**: Bus route planning and management.

**Key Endpoints**:

```http
GET    /api/routes                        # List all routes
GET    /api/routes/{id}                   # Get route details
POST   /api/routes                        # Create new route
PUT    /api/routes/{id}                   # Update route
DELETE /api/routes/{id}                   # Delete route
GET    /api/routes/{id}/schedules         # Get route schedules
POST   /api/routes/{id}/activate          # Activate route
POST   /api/routes/{id}/deactivate        # Deactivate route
GET    /api/routes/{id}/conductors        # Get assigned conductors
POST   /api/routes/{id}/conductors        # Assign conductor
```

**Features**:
- Route creation with start/end points
- Bus assignment to routes
- Driver assignment
- Route activation/deactivation
- Schedule management
- Conductor assignments

**Required Permissions**:
- `routes.view` - View routes
- `routes.create` - Create routes
- `routes.edit` - Modify routes
- `routes.delete` - Delete routes

---

#### RouteSchedulesController (`/api/routeschedules`)

**Purpose**: Schedule management for bus routes.

**Key Endpoints**:

```http
GET    /api/routeschedules                # List all schedules
GET    /api/routeschedules/{id}           # Get schedule details
POST   /api/routeschedules                # Create schedule
PUT    /api/routeschedules/{id}           # Update schedule
DELETE /api/routeschedules/{id}           # Delete schedule
GET    /api/routeschedules/route/{routeId}  # Get schedules for route
GET    /api/routeschedules/today          # Get today's schedules
POST   /api/routeschedules/{id}/activate  # Activate schedule
POST   /api/routeschedules/{id}/cancel    # Cancel schedule
```

**Features**:
- Time-based scheduling
- Frequency management
- Schedule activation/cancellation
- Day-of-week scheduling
- Real-time schedule updates
- Conflict detection

**Required Permissions**:
- `schedules.view` - View schedules
- `schedules.create` - Create schedules
- `schedules.edit` - Modify schedules
- `schedules.delete` - Delete schedules

---

#### EmployeesController (`/api/employees`)

**Purpose**: Employee records and management.

**Key Endpoints**:

```http
GET    /api/employees                     # List all employees
GET    /api/employees/{id}                # Get employee details
POST   /api/employees                     # Create employee
PUT    /api/employees/{id}                # Update employee
DELETE /api/employees/{id}                # Delete employee
GET    /api/employees/job/{jobId}         # Get employees by job
GET    /api/employees/{id}/shifts         # Get employee shifts
POST   /api/employees/{id}/shifts         # Assign shift
```

**Features**:
- Employee record management
- Job assignment
- Shift scheduling
- Performance tracking
- Search and filtering

**Required Permissions**:
- `employees.view` - View employees
- `employees.create` - Create employees
- `employees.edit` - Modify employees
- `employees.delete` - Delete employees

---

#### JobsController (`/api/jobs`)

**Purpose**: Job position management.

**Key Endpoints**:

```http
GET    /api/jobs                          # List all jobs
GET    /api/jobs/{id}                     # Get job details
POST   /api/jobs                          # Create job
PUT    /api/jobs/{id}                     # Update job
DELETE /api/jobs/{id}                     # Delete job
GET    /api/jobs/{id}/employees           # Get employees in job
```

**Features**:
- Job position definitions
- Internship/training requirements
- Employee assignment tracking

---

#### MaintenanceController (`/api/maintenance`)

**Purpose**: Vehicle maintenance scheduling and tracking.

**Key Endpoints**:

```http
GET    /api/maintenance                   # List all maintenance records
GET    /api/maintenance/{id}              # Get maintenance details
POST   /api/maintenance                   # Schedule maintenance
PUT    /api/maintenance/{id}              # Update maintenance
DELETE /api/maintenance/{id}              # Delete maintenance
GET    /api/maintenance/bus/{busId}       # Get maintenance for bus
GET    /api/maintenance/upcoming          # Get upcoming maintenance
POST   /api/maintenance/{id}/complete     # Mark as completed
```

**Features**:
- Preventive maintenance scheduling
- Maintenance history tracking
- Cost tracking
- Roadworthiness status
- Service reminders
- Vendor management

**Required Permissions**:
- `maintenance.view` - View maintenance
- `maintenance.create` - Schedule maintenance
- `maintenance.edit` - Modify maintenance
- `maintenance.delete` - Delete maintenance

---

#### TicketsController (`/api/tickets`)

**Purpose**: Ticket inventory and management.

**Key Endpoints**:

```http
GET    /api/tickets                       # List all tickets
GET    /api/tickets/{id}                  # Get ticket details
POST   /api/tickets                       # Create ticket
PUT    /api/tickets/{id}                  # Update ticket
DELETE /api/tickets/{id}                  # Delete ticket
GET    /api/tickets/route/{routeId}       # Get tickets for route
POST   /api/tickets/{id}/cancel           # Cancel ticket
```

**Features**:
- Ticket type management
- Pricing configuration
- Validity periods
- Route-specific tickets

---

#### TicketSalesController (`/api/ticketsales`)

**Purpose**: Point-of-sale system for ticket sales.

**Key Endpoints**:

```http
GET    /api/ticketsales                   # List all sales
GET    /api/ticketsales/{id}              # Get sale details
POST   /api/ticketsales                   # Process sale
GET    /api/ticketsales/today             # Today's sales
GET    /api/ticketsales/conductor/{id}    # Sales by conductor
GET    /api/ticketsales/route/{routeId}   # Sales by route
POST   /api/ticketsales/{id}/refund       # Process refund
GET    /api/ticketsales/report            # Sales report
```

**Features**:
- Fast POS interface
- Multiple payment methods
- Discount application
- Receipt generation
- Real-time sales tracking
- Refund processing
- Sales analytics

**Required Permissions**:
- `sales.view` - View sales
- `sales.create` - Process sales
- `sales.refund` - Process refunds

---

#### RolesController (`/api/roles`)

**Purpose**: Role management for RBAC system.

**Key Endpoints**:

```http
GET    /api/roles                         # List all roles
GET    /api/roles/{id}                    # Get role details
POST   /api/roles                         # Create role
PUT    /api/roles/{id}                    # Update role
DELETE /api/roles/{id}                    # Delete role
GET    /api/roles/{id}/permissions        # Get role permissions
POST   /api/roles/{id}/permissions        # Grant permission
DELETE /api/roles/{id}/permissions/{permId}  # Revoke permission
GET    /api/roles/{id}/users              # Get users with role
```

**Features**:
- Role creation and management
- Permission assignment
- Priority-based hierarchy
- System role protection
- User assignment tracking

**Required Permissions**:
- `roles.view` - View roles
- `roles.create` - Create roles
- `roles.edit` - Modify roles
- `roles.delete` - Delete roles
- `permissions.grant` - Grant permissions

---

#### PermissionsController (`/api/permissions`)

**Purpose**: Permission management for RBAC system.

**Key Endpoints**:

```http
GET    /api/permissions                   # List all permissions
GET    /api/permissions/{id}              # Get permission details
POST   /api/permissions                   # Create permission
PUT    /api/permissions/{id}              # Update permission
DELETE /api/permissions/{id}              # Delete permission
GET    /api/permissions/category/{cat}    # Get permissions by category
```

**Features**:
- Permission definition
- Category organization
- Active/inactive status
- Usage tracking

**Required Permissions**:
- `permissions.view` - View permissions
- `permissions.create` - Create permissions
- `permissions.edit` - Modify permissions
- `permissions.delete` - Delete permissions

---

#### DebugController (`/api/debug`) - Development Only

**Purpose**: Database inspection and debugging tools.

**Key Endpoints**:

```http
GET    /api/debug/tables                  # Database table viewer (HTML)
GET    /api/debug/tables?tab=UserProfile  # View specific table
GET    /api/debug/query                   # Execute test queries
```

**Features**:
- Web-based table browser
- Paginated results
- JSON export
- Query testing
- Data inspection

**Security**: Only available in Development environment

### Service Layer

Business logic is encapsulated in service classes:

```csharp
public interface IAuthenticationService
{
    Task<UserProfile?> AuthenticateAsync(string login, string password);
    Task<bool> RegisterAsync(string login, string password, int role, ...);
    Task<Identity?> GetUserIdentityByLoginAsync(string login);
    int GetUserRole(Identity userId);
}
```

Services handle:
- Data validation
- Business rules
- SpacetimeDB reducer calls
- Error handling
- Logging

## 🚀 SpacetimeDB 2.0 Migration

### Key Changes from 1.0 to 2.0

1. **Acting User Pattern**
   ```csharp
   // All reducers now accept actingUser parameter
   [SpacetimeDB.Reducer]
   public static void CreateBus(ReducerContext ctx, string model, 
       string? registrationNumber, Identity? actingUser = null)
   {
       Identity effectiveUser = actingUser ?? ctx.Sender;
       // Use effectiveUser for permission checks
   }
   ```

2. **Updated Client SDK**
   - New `RemoteReducers` and `RemoteTables` API
   - Improved type safety
   - Better async/await support

3. **Package Updates**
   - `SpacetimeDB.Runtime` 2.0.1
   - `SpacetimeDB.ClientSDK` 2.0.1

### Workarounds & Solutions

#### 1. API Server Identity Problem

**Problem**: When the API server calls reducers, `ctx.Sender` is the API server's identity, not the actual user.

**Solution**: Pass `actingUser` parameter through all reducer calls:

```csharp
// API Service
conn.Reducers.CreateBus(model, registrationNumber, userIdentity);

// Reducer
public static void CreateBus(ReducerContext ctx, string model, 
    string? registrationNumber, Identity? actingUser = null)
{
    Identity effectiveUser = actingUser ?? ctx.Sender;
    if (!HasPermission(ctx, effectiveUser, "buses.create"))
        throw new Exception("Permission denied");
    // ...
}
```

#### 2. WASI Workload Compatibility

**Problem**: .NET 9 doesn't support the `wasi-experimental` workload used by SpacetimeDB.

**Solution**: 
- Server module uses .NET 8 SDK
- API and client projects use .NET 9
- Build server independently: `dotnet build server/StdbModule.csproj`

#### 3. WebAssembly Cryptography Limitations

**Problem**: Standard .NET cryptography APIs don't work in WASM.

**Solution**: Custom SHA-256 and PBKDF2 implementations:

```csharp
// Custom SHA-256 for WASM environment
public static string ComputeSha256(string input)
{
    byte[] inputBytes = Encoding.UTF8.GetBytes(input);
    uint[] h = { 0x6a09e667, 0xbb67ae85, ... }; // Initial hash values
    // ... full SHA-256 implementation
}

// Reduced iteration count for performance
private static readonly int Iterations = 200; // vs 10000+ in normal .NET
```

#### 4. Duplicate Interface Definitions

**Problem**: Merge conflicts created duplicate interface files.

**Solution**: Removed root-level duplicates, kept subdirectory versions with `actingUser` parameters.

## 📦 Deployment

### Prerequisites

- .NET 8 SDK (for server module)
- .NET 9 SDK (for API and client)
- SpacetimeDB CLI 2.0.1+
- Node.js (for some build tools)

### Building the Server Module

```bash
cd server
dotnet build StdbModule.csproj
```

This compiles the C# code to WASM using SpacetimeDB's build system.

### Publishing the Module

```bash
spacetime publish --project-path server --clear-database
```

Options:
- `--clear-database`: Wipes existing data (use for fresh start)
- `--skip-clippy`: Skip Rust linting

### Running the API Server

```bash
cd BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.AppHost
dotnet run
```

This starts the .NET Aspire orchestration with:
- API Service on `https://localhost:7001`
- Service Defaults configuration
- Automatic service discovery

### Running the Desktop Client

```bash
cd BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop
dotnet run
```

### Configuration

#### SpacetimeDB Connection

```json
// spacetime.json
{
  "server_url": "https://testnet.spacetimedb.com",
  "module_name": "bru-avtopark-avtobusov",
  "host_type": "testnet"
}
```

#### API Configuration

```json
// appsettings.json
{
  "SpacetimeDB": {
    "ModuleName": "bru-avtopark-avtobusov",
    "ServerUrl": "https://testnet.spacetimedb.com"
  },
  "Authentication": {
    "JwtSecret": "your-secret-key",
    "JwtIssuer": "bru-avtopark-api",
    "JwtAudience": "bru-avtopark-client"
  }
}
```

## 🧪 Testing

### Debug Endpoints

The `DebugController` provides web-based database inspection (development only):

```
GET /api/debug/tables?tab=UserProfile&page=1&pageSize=20
```

Features:
- Browse all tables
- Paginated results
- JSON export
- Query testing

### PowerShell Test Scripts

```powershell
# Test JWT token generation
.\test-jwt-token.ps1

# Register OAuth client
.\test-register-client.ps1

# Verify JWT kid claim
.\verify-jwt-kid.ps1

# Decode JWT claims
.\decode-jwt-claims.ps1
```

## 📚 Documentation

- `OAUTH_WEBVIEW_IMPLEMENTATION.md` - OAuth and WebView integration guide
- `WEBVIEW_MIGRATION_SUMMARY.md` - WebView migration details
- `AVALONIA_LICENSE_SETUP.md` - Avalonia licensing information
- `TESTING_AND_DEBUGGING_GUIDE.md` - Testing procedures
- `.kiro/specs/spacetimedb-2.0-migration/` - SpacetimeDB 2.0 migration specs

## 🤝 Contributing

### Code Style

- C# 12 with nullable reference types enabled
- Async/await for all I/O operations
- Dependency injection for services
- MVVM pattern for UI
- Repository pattern for data access

### Commit Guidelines

- Use conventional commits: `feat:`, `fix:`, `docs:`, `refactor:`
- Reference issue numbers
- Keep commits atomic and focused

## 📄 License

Eclipse Public License - v 2.0

## 🙏 Acknowledgments

- **SpacetimeDB** - Distributed database platform
- **Avalonia UI** - Cross-platform XAML framework
- **.NET Team** - Runtime and SDK
- **OpenIddict** - OAuth 2.0 framework inspiration

 
