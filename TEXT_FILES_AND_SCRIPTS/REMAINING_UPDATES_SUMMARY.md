# Remaining ViewModel Updates - Implementation Guide

## Critical Understanding
ALL complexity is ALREADY in the helper methods. The ViewModels just need to CALL them.

## UserManagementViewModel.cs
**Current State:** Has extensive manual parsing for Users, Roles, Permissions
**Required Changes:**
```csharp
// Replace manual User parsing with:
var usersArray = JsonReferenceHelper.ParseArrayWithReferences(usersJsonString, "User");
foreach(var userNode in usersArray)
{
    if (userNode is JsonObject userObj)
    {
        var user = userObj.ParseUserProfile();
        if (user != null) loadedUsers.Add(user);
    }
}

// Replace manual Role parsing with:
var rolesArray = JsonReferenceHelper.ParseArrayWithReferences(rolesJsonString, "Role");
foreach(var roleNode in rolesArray)
{
    if (roleNode is JsonObject roleObj)
    {
        var role = roleObj.ParseRole();
        if (role != null) loadedRoles.Add(role);
    }
}

// Replace manual Permission parsing with:
var permissionsArray = JsonReferenceHelper.ParseArrayWithReferences(permissionsJsonString, "Permission");
foreach(var permNode in permissionsArray)
{
    if (permNode is JsonObject permObj)
    {
        var permission = permObj.ParsePermission();
        if (permission != null) loadedPermissions.Add(permission);
    }
}
```

## SalesManagementViewModel.cs
**Current State:** Has partial parsing for Sales, needs Ticket/Route helpers
**Required Changes:**
```csharp
// For Routes:
var routesArray = JsonReferenceHelper.ParseArrayWithReferences(routeJsonString, "Route");
foreach(var routeNode in routesArray)
{
    if (routeNode is JsonObject routeObj)
    {
        var route = routeObj.ParseRoute();
        if (route != null) _allRoutes.Add(route);
    }
}

// For Tickets:
var ticketsArray = JsonReferenceHelper.ParseArrayWithReferences(ticketsJsonString, "Ticket");
foreach(var ticketNode in ticketsArray)
{
    if (ticketNode is JsonObject ticketObj)
    {
        var ticket = ticketObj.ParseTicket();
        if (ticket != null) _allTicketsDict.TryAdd(ticket.TicketId, ticket);
    }
}

// For Sales:
var salesArray = JsonReferenceHelper.ParseArrayWithReferences(salesJsonString, "Sale");
foreach(var saleNode in salesArray)
{
    if (saleNode is JsonObject saleObj)
    {
        var sale = saleObj.ParseSale();
        if (sale != null) _allSales.Add(sale);
    }
}
```

## RouteSchedulesManagementViewModel.cs
**Current State:** Has manual parsing for Routes and RouteSchedules
**Required Changes:**
```csharp
// For Routes:
var routesArray = JsonReferenceHelper.ParseArrayWithReferences(routesJsonString, "Route");
foreach(var routeNode in routesArray)
{
    if (routeNode is JsonObject routeObj)
    {
        var route = routeObj.ParseRoute();
        if (route != null) routes.Add(route);
    }
}

// For RouteSchedules:
var schedulesArray = JsonReferenceHelper.ParseArrayWithReferences(jsonString, "RouteSchedule");
foreach(var scheduleNode in schedulesArray)
{
    if (scheduleNode is JsonObject scheduleObj)
    {
        var schedule = scheduleObj.ParseRouteSchedule();
        if (schedule != null) schedules.Add(schedule);
    }
}
```

## SalesStatisticsViewModel.cs
**Current State:** Already mostly correct, just needs to use helpers consistently
**Required Changes:** Minimal - already uses proper parsing patterns

## Key Pattern for ALL ViewModels
```csharp
// 1. Parse array with reference handling
var array = JsonReferenceHelper.ParseArrayWithReferences(jsonString, "EntityName");
if (array == null)
{
    Log.Error("Failed to parse EntityName array");
    return;
}

// 2. Iterate and parse each object
foreach(var node in array)
{
    if (node is JsonObject obj)
    {
        var entity = obj.ParseEntity(); // Use appropriate Parse* method
        if (entity != null)
        {
            collection.Add(entity);
        }
    }
}
```

## Benefits
- Removes 100+ lines of manual parsing per ViewModel
- All edge cases handled in ONE place (the helper methods)
- Consistent error handling and logging
- Easier to maintain and debug
- All DateTime conversions handled correctly
- All null checks and type conversions centralized
