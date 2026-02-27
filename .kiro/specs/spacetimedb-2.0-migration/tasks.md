# Implementation Plan: SpacetimeDB 2.0 Migration

## Overview

This implementation plan breaks down the migration from SpacetimeDB 1.0 to 2.0 into discrete, manageable tasks. The migration preserves the custom authentication system while updating to the new 2.0 API and event model.

## Tasks

- [x] 1. Update Package References and Dependencies
  - Update all `.csproj` files to reference SpacetimeDB.Runtime 2.0.x
  - Verify dependency resolution without conflicts
  - Test that the module builds successfully
  - _Requirements: 1.1, 1.2, 1.3_

- [x] 2. Define Event Tables in Server Module
  - [x] 2.1 Create AuthenticationEvent table
    - Define table with `Event = true` attribute
    - Include fields: UserId, EventType, Timestamp, Details, IpAddress
    - _Requirements: 4.1_
  
  - [x] 2.2 Create TicketSaleEvent table
    - Define table with `Event = true` attribute
    - Include fields: SaleId, TicketId, RouteId, BuyerId, Amount, Timestamp, PaymentMethod
    - _Requirements: 4.1_
  
  - [x] 2.3 Create BusStatusEvent table
    - Define table with `Event = true` attribute
    - Include fields: BusId, PreviousStatus, NewStatus, Timestamp, ChangedBy, Reason
    - _Requirements: 4.1_
  
  - [x] 2.4 Create RouteScheduleEvent table
    - Define table with `Event = true` attribute
    - Include fields: ScheduleId, RouteId, EventType, Timestamp, ChangedBy
    - _Requirements: 4.1_
  
  - [x] 2.5 Create MaintenanceEvent table
    - Define table with `Event = true` attribute
    - Include fields: MaintenanceId, BusId, EventType, Timestamp, ChangedBy
    - _Requirements: 4.1_

- [x] 3. Update Reducers to Publish Events
  - [x] 3.1 Update authentication reducers
    - Add event publishing to RegisterUser reducer
    - Add event publishing to AuthenticateUser reducer
    - Add event publishing to logout/token refresh reducers
    - _Requirements: 4.2_
  
  - [x] 3.2 Update ticket sale reducers
    - Add event publishing to CreateTicketSale reducer
    - Add event publishing to UpdateTicketSale reducer
    - Add event publishing to CancelTicketSale reducer
    - _Requirements: 4.2_
  
  - [x] 3.3 Update bus management reducers
    - Add event publishing to ActivateBus/DeactivateBus reducers
    - Add event publishing to CreateBus/UpdateBus/DeleteBus reducers
    - _Requirements: 4.2_
  
  - [x] 3.4 Update route schedule reducers
    - Add event publishing to CreateRouteSchedule reducer
    - Add event publishing to UpdateRouteSchedule reducer
    - Add event publishing to CancelRouteSchedule reducer
    - _Requirements: 4.2_
  
  - [x] 3.5 Update maintenance reducers
    - Add event publishing to ScheduleMaintenance reducer
    - Add event publishing to StartMaintenance reducer
    - Add event publishing to CompleteMaintenance reducer
    - _Requirements: 4.2_

- [x] 4. Remove Scheduled Reducer Authorization Checks
  - Scan for `ctx.Sender == ctx.Identity` patterns in scheduled reducers
  - Remove manual authorization checks (now private by default)
  - Create public wrapper reducers if scheduled functions need client access
  - _Requirements: 6.2, 6.3_

- [x] 5. Update Client Connection API in SpacetimeDBService
  - [x] 5.1 Replace WithModuleName with WithDatabaseName
    - Update DbConnection.Builder() call
    - Change configuration key from "ModuleName" to "DatabaseName"
    - _Requirements: 2.1_
  
  - [x] 5.2 Remove WithLightMode call
    - Remove WithLightMode() from connection builder
    - _Requirements: 9.1_
  
  - [x] 5.3 Add WithConfirmedReads configuration
    - Add WithConfirmedReads(true) to connection builder (optional, true by default)
    - Document the option for low-latency scenarios
    - _Requirements: 2.4_

- [x] 6. Update Subscription Patterns
  - [x] 6.1 Keep existing SubscribeToAllTables for regular tables
    - Verify SubscribeToAllTables() call works for non-event tables
    - _Requirements: 8.1_
  
  - [x] 6.2 Add explicit event table subscriptions
    - Subscribe to AuthenticationEvent table
    - Subscribe to TicketSaleEvent table
    - Subscribe to BusStatusEvent table
    - Subscribe to RouteScheduleEvent table
    - Subscribe to MaintenanceEvent table
    - _Requirements: 8.3_

- [x] 7. Remove Old Reducer Callbacks
  - Scan codebase for `OnReducerName` callback patterns
  - Remove all global reducer callback registrations
  - Document which callbacks were removed
  - _Requirements: 3.1, 3.2_

- [x] 8. Implement Event Table Callbacks
  - [x] 8.1 Add AuthenticationEvent.OnInsert handler
    - Handle "Login", "Logout", "Failed" event types
    - Log authentication events
    - _Requirements: 3.4, 4.5_
  
  - [x] 8.2 Add TicketSaleEvent.OnInsert handler
    - Handle ticket sale notifications
    - Update UI with sale information
    - _Requirements: 3.4, 4.5_
  
  - [x] 8.3 Add BusStatusEvent.OnInsert handler
    - Handle bus status change notifications
    - Update UI with bus status
    - _Requirements: 3.4, 4.5_
  
  - [x] 8.4 Add RouteScheduleEvent.OnInsert handler
    - Handle schedule change notifications
    - Update UI with schedule information
    - _Requirements: 3.4, 4.5_
  
  - [x] 8.5 Add MaintenanceEvent.OnInsert handler
    - Handle maintenance event notifications
    - Update UI with maintenance status
    - _Requirements: 3.4, 4.5_

- [x] 9. Update Event Context Handling
  - Update table callbacks to use new Event.tag property
  - Handle "Reducer", "Transaction", "SubscribeApplied", "UnsubscribeApplied", "SubscribeError" variants
  - Remove handling of "UnknownTransaction" (removed in 2.0)
  - _Requirements: 7.1, 7.2, 7.3_

- [x] 10. Update Reducer Invocation Patterns
  - [x] 10.1 Update to use await pattern for own reducer results
    - Replace fire-and-forget calls with await where needed
    - Add try-catch for error handling
    - _Requirements: 3.3_
  
  - [x] 10.2 Remove SetReducerFlags and CallReducerFlags usage
    - Scan for SetReducerFlags() calls
    - Remove all CallReducerFlags references
    - _Requirements: 10.1, 10.2_

- [x] 11. Checkpoint - Verify Module Builds and Publishes
  - Build the server module
  - Publish to local SpacetimeDB 2.0 instance with `--delete-data`
  - Verify no compilation or publish errors
  - _Requirements: 12.1_

- [x] 12. Regenerate Client Bindings
  - Update generation command to output directly to service project
  - Run `spacetime generate --lang cs --out-dir BRU-AVTOPARK-AspireAPI/TicketSalesApp.Services/client/module_bindings --module-path server/`
  - Verify generated code excludes private items by default
  - Add `--include-private` flag if needed for private table access
  - Verify event table types are generated (AuthenticationEvent, TicketSaleEvent, etc.)
  - Confirm reducer callback methods are removed (expected in 2.0)
  - Test that SpacetimeDBService can reference generated types
  - Consider adding automated generation to build process to eliminate manual copying
  - _Requirements: 11.1, 11.2, 11.3_

- [x] 13. Update API Controllers
  - [x] 13.1 Update AuthController
    - Verify JWT token generation still works
    - Verify identity generation pattern still works
    - Test all 5 authentication methods (password, QR, 2FA, magic link, WebAuthn)
    - _Requirements: 13.1, 13.2_
  
  - [x] 13.2 Update BusesController
    - Update to use new SDK API
    - Test CRUD operations
    - _Requirements: 13.1, 13.2_
  
  - [x] 13.3 Update RoutesController
    - Update to use new SDK API
    - Test CRUD operations
    - _Requirements: 13.1, 13.2_
  
  - [x] 13.4 Update TicketsController
    - Update to use new SDK API
    - Test CRUD operations
    - _Requirements: 13.1, 13.2_
  
  - [x] 13.5 Update TicketSalesController
    - Update to use new SDK API
    - Test CRUD operations
    - _Requirements: 13.1, 13.2_

- [x] 14. Fix JSON Parsing Issues
  - [x] 14.1 Implement custom JsonConverter for SpacetimeDB types
    - Handle `$id` and `$values` metadata
    - Test with complex nested objects
    - _Requirements: 14.1_
  
  - [x] 14.2 Create DTOs for API responses
    - Define DTOs that match client expectations
    - Map SpacetimeDB types to DTOs
    - _Requirements: 14.1_
  
  - [x] 14.3 Fix missing field issues
    - Debug which fields are not displaying
    - Verify JSON deserialization for all fields
    - _Requirements: 14.1_

- [ ] 15. Update Avalonia Client ViewModels
  - [ ] 15.1 Update AuthViewModel
    - Verify login/logout still works
    - Test JWT token storage
    - _Requirements: 13.3, 13.4, 13.5_
  
  - [ ] 15.2 Update BusManagementViewModel
    - Update to handle new event notifications
    - Fix missing field display issues
    - _Requirements: 13.3, 13.4, 13.5_
  
  - [ ] 15.3 Update RouteManagementViewModel
    - Update to handle new event notifications
    - Test route CRUD operations
    - _Requirements: 13.3, 13.4, 13.5_
  
  - [ ] 15.4 Update TicketManagementViewModel
    - Update to handle new event notifications
    - Test ticket CRUD operations
    - _Requirements: 13.3, 13.4, 13.5_
  
  - [ ] 15.5 Update SalesManagementViewModel
    - Update to handle new event notifications
    - Test sales CRUD operations
    - _Requirements: 13.3, 13.4, 13.5_

- [ ] 16. Checkpoint - Test End-to-End Functionality
  - Test client connection to SpacetimeDB 2.0
  - Test authentication flow (login, logout, token refresh)
  - Test CRUD operations for all entities
  - Test event notifications are received
  - Verify UI updates correctly
  - _Requirements: 12.2, 12.3, 12.4, 12.5_

- [ ] 17. Update Configuration Files
  - Update `appsettings.json` with correct database name
  - Update `spacetime.json` if needed
  - Document configuration changes
  - _Requirements: 14.1, 14.4_

- [ ] 18. Update Documentation
  - Document migration steps performed
  - Document new event table architecture
  - Document JSON parsing solutions
  - Document authentication system (unchanged)
  - Create troubleshooting guide
  - _Requirements: 14.2, 14.3_

- [ ] 19. Final Testing and Validation
  - Run all unit tests
  - Run integration tests
  - Test with multiple concurrent clients
  - Verify performance is acceptable
  - Test all authentication methods
  - Test all CRUD operations
  - Test event notifications
  - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

- [ ] 20. Deployment Preparation
  - Create deployment checklist
  - Document rollback procedure
  - Prepare production configuration
  - Plan data migration if needed
  - _Requirements: 14.4_

## Notes

- Tasks are ordered to minimize breaking changes and allow incremental testing
- Checkpoints (tasks 11, 16) ensure the system is working before proceeding
- The custom authentication system is preserved with minimal changes
- JSON parsing issues should be addressed during the migration (task 14)
- Event tables are the key new feature - they replace reducer callbacks
- All tasks reference specific requirements for traceability
