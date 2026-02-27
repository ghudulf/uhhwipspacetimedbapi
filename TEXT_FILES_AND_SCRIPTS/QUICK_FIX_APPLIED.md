# Quick Fix Applied - OAuth Permission Claims

## Problem
Avalonia client receiving 403 Forbidden on endpoints like `/api/Buses`, `/api/Employees`, `/api/Users` because OAuth tokens didn't contain permission claims.

## Solution Applied
Modified `AuthController.cs` Exchange() method to add permission claims to OAuth tokens.

## Changes Made

### 1. Authorization Code Flow (lines ~4130-4160)
Added after role claims:
- Query user's permissions from `RolePermission` and `Permission` tables
- Add `permission` claims for each permission
- Add `primary_role` claim for admin checks
- Add `identity` claim for SpacetimeDB operations
- Add `xuid` claim for user identification

### 2. Refresh Token Flow (lines ~4260-4290)
Same additions as authorization code flow to ensure refreshed tokens also have permissions.

## What Gets Added to Token

The token now includes:
```json
{
  "sub": "1",
  "name": "admin",
  "role": "Administrator",
  "primary_role": "1",
  "permission": "users.view",
  "permission": "users.create",
  "permission": "employees.view",
  "permission": "buses.view",
  "permission": "routes.view",
  "permission": "tickets.view",
  "permission": "sales.view",
  "identity": "C200A612C52D6987B10DEC7091FD4034DC17076C28CD9A72E78E10EF52DAD167",
  "xuid": "1"
}
```

**Note**: The `identity` claim contains the SpacetimeDB Identity (from `user.UserId.ToString()`), and `xuid` contains either the `Xuid` field if available, or falls back to `LegacyUserId`.

## Testing Steps

1. **Clear existing tokens** on client:
   ```
   Delete: %LOCALAPPDATA%\BRU.Avtopark.TicketSalesApp\tokens.dat
   ```

2. **Restart API server** to load changes

3. **Login through Avalonia client** - OAuth flow will issue new token with permissions

4. **Verify endpoints work**:
   - Buses endpoint should return 200 OK
   - Employees endpoint should return 200 OK
   - Users endpoint should return 200 OK

5. **Check server logs** for confirmation:
   ```
   [INF] Added 15 permissions to token for user admin
   [INF] Added primary_role claim: 1
   [INF] HasPermission check - User has permission 'users.view'
   ```

## Database Requirements

The fix assumes these tables exist in SpacetimeDB:
- `UserRole` - maps users to roles
- `RolePermission` - maps roles to permissions
- `Permission` - contains permission names (e.g., "users.view")
- `Role` - contains role names

## Fallback Behavior

If permission tables don't exist or are empty:
- Token will still be issued with basic claims
- Endpoints requiring permissions will still return 403
- Need to populate permission data in database

## Next Steps

1. Verify permission data exists in SpacetimeDB
2. Test with different user roles (not just admin)
3. Consider caching permissions to reduce database queries
4. Add permission management UI for admins
