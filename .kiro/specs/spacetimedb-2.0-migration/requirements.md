# Requirements Document

## Introduction

This document outlines the requirements for migrating the BRU-AVTOPARK SpacetimeDB project from version 1.0 to version 2.0. The migration involves updating the server module (C# WASM), client services (C# .NET), and ensuring compatibility with the new SpacetimeDB 2.0 API and protocol changes.

### C# Syntax Reference

For reference, here are the key syntax patterns used in SpacetimeDB 2.0 C# modules:

**Table Definition:**
```csharp
[SpacetimeDB.Table(Public = true)]
public partial struct Player {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    
    [SpacetimeDB.Unique]
    public string Username;
    
    [SpacetimeDB.Index.BTree]
    public int Score;
}
```

**Reducer Definition:**
```csharp
[SpacetimeDB.Reducer]
public static void CreatePlayer(ReducerContext ctx, string username) {
    ctx.Db.Player.Insert(new Player { Id = 0, Username = username, Score = 0 });
}
```

**Lifecycle Reducers:**
```csharp
[SpacetimeDB.Reducer(ReducerKind.Init)]
public static void Init(ReducerContext ctx) { /* ... */ }

[SpacetimeDB.Reducer(ReducerKind.ClientConnected)]
public static void OnConnect(ReducerContext ctx) { /* ... */ }

[SpacetimeDB.Reducer(ReducerKind.ClientDisconnected)]
public static void OnDisconnect(ReducerContext ctx) { /* ... */ }
```

**Context Properties:**
```csharp
ctx.Db                  // Database access
ctx.Sender              // Identity of caller
ctx.ConnectionId        // ConnectionId?
ctx.Timestamp           // Timestamp
ctx.Identity            // Module's identity
ctx.Rng                 // Random number generator
```

## Glossary

### Core Concepts

- **SpacetimeDB**: A database that is also a server. Clients connect directly to it without any server in between. It is a relational database with tables, queries, and transactions, but application logic runs inside it as stored procedures on steroids.
- **Module**: The application's server-side code compiled to WebAssembly. It defines tables, reducers, views, and procedures. The module is the entire backend in a single deployable unit.
- **Reducer**: A function that modifies database state. Reducers run inside a database transaction - either all changes commit or none do. They are like transactional RPC endpoints.
- **Table**: Core data storage in SpacetimeDB. Tables support primary keys, unique constraints, and indexes. Clients can subscribe to tables and receive real-time updates when rows change.
- **View**: A read-only function that computes derived data from tables. Clients can subscribe to views just like tables, and they update automatically when underlying data changes.
- **Procedure**: Similar to reducers but with additional capabilities. They can make HTTP requests to external services and manually manage transactions.
- **Event_Table**: A new table type in 2.0 for publishing transient events to subscribers. Event tables are always empty outside of a transaction and don't accumulate rows.

### Client Concepts

- **Client**: The .NET application that connects to SpacetimeDB
- **DbConnection**: The client-side connection object to SpacetimeDB
- **Subscription**: A client's request to receive updates for specific tables or queries. SpacetimeDB evaluates subscriptions and pushes incremental updates when underlying data changes.
- **Local_Cache**: The client-side cache maintained by the SDK that mirrors server state. Clients query this cache directly with no round trips.

### Authentication & Identity

- **Identity**: A unique identifier for a user/client in SpacetimeDB. Every reducer call includes the caller's Identity for authorization logic.
- **OIDC**: OpenID Connect - the authentication protocol used by SpacetimeDB
- **SpacetimeAuth**: A fully managed OIDC provider built specifically for SpacetimeDB

### Technical Terms

- **ReducerContext**: The context object passed to reducers containing database access, timestamp, sender identity, and other metadata
- **WebSocket_Protocol_v2**: The new protocol in SpacetimeDB 2.0 for client-server communication
- **Confirmed_Reads**: A 2.0 feature where subscription updates and SQL results are only sent after the transaction is confirmed durable
- **Commit_Log**: The persistence mechanism - similar to a write-ahead log. SpacetimeDB holds all data in memory but persists everything to a commit log.
- **Hot_Swap**: The ability to update a module without downtime. When publishing an update, SpacetimeDB hot-swaps the module code while keeping clients connected.

## Requirements

### Requirement 1: Update SpacetimeDB Runtime Package

**User Story:** As a developer, I want to update the SpacetimeDB.Runtime package to version 2.0, so that I can use the latest API features and protocol improvements.

#### Acceptance Criteria

1. WHEN the project file is updated, THE Build_System SHALL reference SpacetimeDB.Runtime version 2.0 or later
2. WHEN the package is updated, THE Build_System SHALL resolve all dependencies without conflicts
3. WHEN the build is executed, THE Compiler SHALL compile the module without package-related errors

### Requirement 2: Update Client Connection API

**User Story:** As a developer, I want to update the client connection code to use the new 2.0 API, so that clients can connect to the upgraded SpacetimeDB instance.

#### Acceptance Criteria

1. WHEN building a database connection, THE Client SHALL use `WithDatabaseName()` instead of `WithModuleName()`
2. WHEN the connection is established, THE Client SHALL use the new WebSocket protocol v2
3. WHEN confirmed reads are enabled (default), THE Client SHALL only receive subscription updates after transaction confirmation
4. IF the application requires low latency over durability, THE Client SHALL explicitly opt out using `WithConfirmedReads(false)`

### Requirement 3: Remove Reducer Callback Registrations

**User Story:** As a developer, I want to remove global reducer callback registrations, so that the code is compatible with SpacetimeDB 2.0 which no longer broadcasts reducer arguments.

#### Acceptance Criteria

1. WHEN scanning the codebase, THE System SHALL identify all reducer callback registrations
2. WHEN reducer callbacks are removed, THE System SHALL not contain any `OnReducerName()` callback registrations
3. WHEN a client needs to observe its own reducer results, THE System SHALL use `await` or `_then()` callbacks
4. WHEN clients need to observe cross-client events, THE System SHALL use event tables with `onInsert` callbacks

### Requirement 4: Implement Event Tables for Cross-Client Notifications

**User Story:** As a developer, I want to implement event tables for cross-client notifications, so that clients can observe important system events without exposing sensitive reducer arguments.

#### Acceptance Criteria

1. WHEN a reducer needs to notify other clients, THE Module SHALL define an event table with `[SpacetimeDB.Table(Event = true)]`
2. WHEN a reducer executes successfully, THE Module SHALL insert events into the appropriate event table
3. WHEN an event is inserted, THE System SHALL publish it only to subscribed clients
4. WHEN a transaction fails, THE System SHALL not publish any events from that transaction
5. WHEN clients subscribe to event tables, THE Client SHALL register `onInsert` callbacks to handle events

### Requirement 5: Update Table and Index Definitions

**User Story:** As a developer, I want to ensure table and index definitions are compatible with 2.0, so that the module can be published without migration issues.

#### Acceptance Criteria

1. WHEN defining tables, THE Module SHALL use the `[SpacetimeDB.Table]` attribute correctly
2. WHEN using the `Name` parameter, THE Module SHALL understand it sets the canonical name (not the accessor)
3. WHEN tables have unique constraints, THE Module SHALL only provide `Update()` methods for primary key columns
4. WHEN updating non-primary-key unique columns, THE Module SHALL use delete-then-insert pattern

### Requirement 6: Update Scheduled Reducer Security

**User Story:** As a developer, I want to update scheduled reducers to leverage the new private-by-default behavior, so that security is improved without manual authorization checks.

#### Acceptance Criteria

1. WHEN a reducer is scheduled, THE System SHALL treat it as private by default
2. WHEN a scheduled reducer is defined, THE Module SHALL remove manual authorization checks for `ctx.Sender == ctx.Identity`
3. IF a scheduled reducer needs to be callable by clients, THE Module SHALL define a separate public wrapper reducer
4. WHEN a scheduled reducer executes, THE System SHALL only allow invocation by the database itself or authorized users

### Requirement 7: Update Event Handling in Client Code

**User Story:** As a developer, I want to update client event handling to work with the new event model, so that table callbacks receive correct event information.

#### Acceptance Criteria

1. WHEN a table callback is triggered by the calling client's reducer, THE Event SHALL have tag `Reducer` with full reducer information
2. WHEN a table callback is triggered by another client's action, THE Event SHALL have tag `Transaction` without reducer details
3. WHEN processing events, THE Client SHALL not expect or handle `UnknownTransaction` events (removed in 2.0)
4. WHEN clients need metadata about other clients' actions, THE System SHALL use event tables instead of reducer events

### Requirement 8: Update Subscription API Usage

**User Story:** As a developer, I want to update subscription code to use the new API, so that clients can subscribe to tables and queries correctly.

#### Acceptance Criteria

1. WHEN subscribing to all tables, THE Client SHALL use `SubscribeToAllTables()` method
2. WHEN subscribing to specific queries, THE Client SHALL use the subscription builder with query strings or typed query builders
3. WHEN subscribing to event tables, THE Client SHALL explicitly subscribe to them (they are excluded from `SubscribeToAllTables()`)
4. WHEN subscription is applied, THE Client SHALL receive the `OnApplied` callback
5. WHEN subscription fails, THE Client SHALL receive the `OnError` callback with error details

### Requirement 9: Remove Light Mode Configuration

**User Story:** As a developer, I want to remove light mode configuration, so that the code is simplified and compatible with 2.0's event model.

#### Acceptance Criteria

1. WHEN building a connection, THE Client SHALL not call `WithLightMode()`
2. WHEN the connection is established, THE System SHALL automatically use the new event model without light mode
3. WHEN reducer events occur, THE System SHALL only send event data to the calling client

### Requirement 10: Remove CallReducerFlags Usage

**User Story:** As a developer, I want to remove CallReducerFlags usage, so that the code is compatible with 2.0 which removed this feature.

#### Acceptance Criteria

1. WHEN scanning the codebase, THE System SHALL identify all `SetReducerFlags()` calls
2. WHEN reducer flags are removed, THE System SHALL not contain any `SetReducerFlags()` or `CallReducerFlags` references
3. WHEN reducers are called, THE System SHALL automatically send lightweight success notifications

### Requirement 11: Update Code Generation Configuration

**User Story:** As a developer, I want to update code generation to handle private items correctly, so that client bindings are generated appropriately.

#### Acceptance Criteria

1. WHEN generating client bindings, THE System SHALL exclude private tables and functions by default
2. IF the client needs access to private items, THE System SHALL use `--include-private` flag with `spacetime generate`
3. WHEN private items are excluded, THE Generated_Code SHALL only contain public tables and reducers

### Requirement 12: Test and Validate Migration

**User Story:** As a developer, I want to test the migrated system, so that I can verify all functionality works correctly with SpacetimeDB 2.0.

#### Acceptance Criteria

1. WHEN the module is published, THE System SHALL successfully deploy to SpacetimeDB 2.0
2. WHEN clients connect, THE Connection SHALL establish successfully using the new protocol
3. WHEN reducers are called, THE System SHALL execute them and return results correctly
4. WHEN subscriptions are active, THE Clients SHALL receive table updates in real-time
5. WHEN event tables are used, THE Clients SHALL receive event notifications correctly
6. WHEN the system is under load, THE Performance SHALL be comparable or better than 1.0

### Requirement 13: Update C# Client SDK API Usage

**User Story:** As a developer, I want to update C# client code to use the correct 2.0 SDK APIs, so that the client properly interacts with SpacetimeDB 2.0.

#### Acceptance Criteria

1. WHEN building connections, THE Client SHALL use `DbConnection.Builder()` with proper method chaining
2. WHEN accessing tables, THE Client SHALL use the `ctx.Db` property to access `RemoteTables`
3. WHEN invoking reducers, THE Client SHALL use the `ctx.Reducers` property to access `RemoteReducers`
4. WHEN registering row callbacks, THE Client SHALL use `OnInsert`, `OnDelete`, and `OnUpdate` event handlers
5. WHEN handling events, THE Client SHALL properly handle `EventContext` with `Event` property containing `Reducer`, `SubscribeApplied`, `UnsubscribeApplied`, or `SubscribeError` variants
6. WHEN advancing the connection, THE Client SHALL call `FrameTick()` regularly (e.g., every frame in Unity)
7. WHEN using typed queries, THE Client SHALL use the Query Builder API with `AddQuery()` method
8. WHEN accessing indexed columns, THE Client SHALL use unique constraint index access with `.Find()` or BTree index access with `.Filter()`

### Requirement 14: Update Documentation and Configuration

**User Story:** As a developer, I want to update project documentation and configuration files, so that the migration is properly documented and the project is configured correctly.

#### Acceptance Criteria

1. WHEN configuration files are updated, THE System SHALL reference the correct SpacetimeDB 2.0 endpoints
2. WHEN documentation is updated, THE Documentation SHALL reflect all API changes and migration steps
3. WHEN new developers join, THE Documentation SHALL provide clear guidance on the 2.0 architecture
4. WHEN deployment occurs, THE Configuration SHALL specify the correct database name and connection parameters
