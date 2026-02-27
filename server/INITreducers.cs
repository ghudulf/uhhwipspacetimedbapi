using System.Collections.Generic;
using System.Text;
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Reducer(SpacetimeDB.ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        // Log the start of initialization
        Log.Info("Initializing the system...");
        // Change the order of initialization to avoid permission issues
        // 1. Initialize Admin User first (so we have an identity with permissions)
        Log.Info("Starting Admin User initialization...");
        InitializeAdminUser(ctx);
        Log.Info("Admin User initialization completed");

        // 2. Initialize Permissions
        Log.Info("Starting Permissions initialization...");
        InitializePermissions(ctx);
        Log.Info("Permissions initialization completed");

        // 3. Initialize Roles
        Log.Info("Starting Roles initialization...");
        InitializeRoles(ctx);
        Log.Info("Roles initialization completed");

        // 4. Initialize Jobs
        Log.Info("Starting Jobs initialization...");
        InitializeJobs(ctx);
        Log.Info("Jobs initialization completed");

        // 5. Initialize Employees
        Log.Info("Starting Employees initialization...");
        InitializeEmployees(ctx);
        Log.Info("Employees initialization completed");

        // 6. Initialize Buses
        Log.Info("Starting Buses initialization...");
        InitializeBuses(ctx);
        Log.Info("Buses initialization completed");

        // 7. Initialize Routes
        Log.Info("Starting Routes initialization...");
        InitializeRoutes(ctx);
        Log.Info("Routes initialization completed");

        // 8. Initialize Tickets
        Log.Info("Starting Tickets initialization...");
        InitializeTickets(ctx);
        Log.Info("Tickets initialization completed");

        // 9. Initialize Maintenance Records
        Log.Info("Starting Maintenance Records initialization...");
        InitializeMaintenance(ctx);
        Log.Info("Maintenance Records initialization completed");

        // 10. Initialize Route Schedules
        Log.Info("Starting Route Schedules initialization...");
        InitializeRouteSchedules(ctx);
        Log.Info("Route Schedules initialization completed");

        // 11. Initialize Sales
        Log.Info("Starting Sales initialization...");
        InitializeSales(ctx);
        Log.Info("Sales initialization completed");

        // 12. Log permission counts and assignment status
        Log.Info("Checking permission assignments...");
        
        // Count total permissions in the system
        int totalPermissions = ctx.Db.Permission.Iter().Count();
        Log.Info($"Total permissions in system: {totalPermissions}");
        
        // Count users and check their role assignments
        var users = ctx.Db.UserProfile.Iter().ToList();
        int totalUsers = users.Count;
        int usersWithRoles = 0;
        
        foreach (var user in users)
        {
            // Check if user has any roles assigned
            bool hasRoles = ctx.Db.UserRole.Iter().Any(ur => ur.UserId == user.UserId);
            if (hasRoles)
            {
                usersWithRoles++;
                
                // Get all roles for this user
                var userRoles = ctx.Db.UserRole.Iter()
                    .Where(ur => ur.UserId == user.UserId)
                    .Select(ur => ur.RoleId)
                    .ToList();
                
                // Count permissions for this user through their roles
                var userPermissions = new HashSet<uint>();
                foreach (var roleId in userRoles)
                {
                    var rolePermissions = ctx.Db.RolePermission.Iter()
                        .Where(rp => rp.RoleId == roleId)
                        .Select(rp => rp.PermissionId);
                    
                    foreach (var permId in rolePermissions)
                    {
                        userPermissions.Add(permId);
                    }
                }
                
                Log.Info($"User {user.Login} has {userRoles.Count} roles with access to {userPermissions.Count} permissions");
            }
            else
            {
                Log.Warn($"User {user.Login} has no roles assigned");
            }
        }
        
        Log.Info($"Role assignment summary: {usersWithRoles} out of {totalUsers} users have roles assigned");

        // Log successful initialization
        Log.Info("System initialized successfully");
    }

    private static void InitializeAdminUser(ReducerContext ctx)
    {
        Log.Info("Initializing admin user...");
        // Check if admin user already exists
        if (!ctx.Db.UserProfile.Iter().Any(u => u.Login == "admin"))
        {
            Log.Info("Admin user does not exist, creating new admin user");
            // Get the next user ID
            uint userId = GetNextId(ctx, "userId");
            Log.Debug($"Generated userId: {userId}");

            // Create admin user with the module's identity
            var admin = new UserProfile
            {
                UserId = ctx.Sender,         // Module's identity
                LegacyUserId = userId,
                Login = "admin",
                Email = "admin@example.com",
                PhoneNumber = "+375333000000",
                PasswordHash = HashPassword("admin"),
                IsActive = true,
                CreatedAt = GetSafeUnixTimestamp(),
                LastLoginAt = null,
                LegacyGuid = Guid.NewGuid().ToString()
            };

            ctx.Db.UserProfile.Insert(admin);
            Log.Info("Admin user created successfully");

            // Create admin role directly here instead of waiting for InitializeRoles
            if (!ctx.Db.Role.Iter().Any(r => r.Name == "Administrator"))
            {
                Log.Info("Administrator role does not exist, creating new role");
                uint roleId = GetNextId(ctx, "roleId");
                Log.Debug($"Generated roleId: {roleId}");

                var adminRole = new Role
                {
                    RoleId = roleId,
                    LegacyRoleId = 1,
                    Name = "Administrator",
                    Description = "Full system access",
                    IsSystem = true,
                    Priority = 100,
                    IsActive = true,
                    CreatedAt = GetSafeUnixTimestamp(),
                    UpdatedAt = GetSafeUnixTimestamp(),
                    CreatedBy = "System",
                    UpdatedBy = "System",
                    NormalizedName = "ADMINISTRATOR"
                };
                ctx.Db.Role.Insert(adminRole);
                Log.Info("Administrator role created successfully");

                // Assign admin role to admin user using userRoleId counter
                uint userRoleId = GetNextId(ctx, "userRoleId");
                Log.Debug($"Generated userRoleId: {userRoleId}");

                var userRole = new UserRole
                {
                    Id = userRoleId,
                    UserId = admin.UserId,
                    RoleId = adminRole.RoleId,
                    AssignedAt = GetSafeUnixTimestamp(),
                    AssignedBy = "System"
                };
                ctx.Db.UserRole.Insert(userRole);
                Log.Info("Admin role created and assigned to admin user");
            }
            else
            {
                Log.Info("Administrator role already exists");
            }

            // Create a placeholder for guest user - will be claimed later
            Log.Info("Creating guest user placeholder");
            // We'll use a special placeholder identity derived from the module's identity
            var placeholderIdentity = new Identity();
            userId = GetNextId(ctx, "userId");
            Log.Debug($"Generated userId for guest: {userId}");

            var guest = new UserProfile
            {
                UserId = placeholderIdentity,
                LegacyUserId = userId,
                Login = "guest",
                Email = "guest@example.com",
                PhoneNumber = "+375333000001",
                PasswordHash = HashPassword("gX9#mP2$kL5"),
                IsActive = false, // Not active until claimed
                CreatedAt = GetSafeUnixTimestamp(),
                LastLoginAt = null,
                LegacyGuid = Guid.NewGuid().ToString()
            };

            ctx.Db.UserProfile.Insert(guest);
            Log.Info("Guest user placeholder created successfully");

            // Create user role directly here
            if (!ctx.Db.Role.Iter().Any(r => r.Name == "User"))
            {
                Log.Info("User role does not exist, creating new role");
                uint roleId = GetNextId(ctx, "roleId");
                Log.Debug($"Generated roleId for User role: {roleId}");

                var userRoleObj = new Role
                {
                    RoleId = roleId,
                    LegacyRoleId = 0,
                    Name = "User",
                    Description = "Basic access",
                    IsSystem = true,
                    Priority = 1,
                    IsActive = true,
                    CreatedAt = GetSafeUnixTimestamp(),
                    UpdatedAt = GetSafeUnixTimestamp(),
                    CreatedBy = "System",
                    UpdatedBy = "System",
                    NormalizedName = "USER"
                };
                ctx.Db.Role.Insert(userRoleObj);
                Log.Info("User role created successfully");

                // Assign user role to guest user with proper ID
                uint guestUserRoleId = GetNextId(ctx, "userRoleId");
                Log.Debug($"Generated userRoleId for guest: {guestUserRoleId}");

                var guestUserRole = new UserRole
                {
                    Id = guestUserRoleId,
                    UserId = guest.UserId,
                    RoleId = userRoleObj.RoleId,
                    AssignedAt = GetSafeUnixTimestamp(),
                    AssignedBy = "System"
                };
                ctx.Db.UserRole.Insert(guestUserRole);
                Log.Info("User role created and assigned to guest user placeholder");
            }
            else
            {
                Log.Info("User role already exists");
            }
        }
        else
        {
            Log.Info("Admin user already exists, skipping creation");
        }
    }

    private static void InitializePermissions(ReducerContext ctx)
    {
        Log.Info("Initializing permissions...");
        // Define and insert permissions here
        var permissions = new[]
        {
            // User Management
            ("users.view", "View users", "User Management"),
            ("users.create", "Create users", "User Management"),
            ("users.edit", "Edit users", "User Management"),
            ("users.delete", "Delete users", "User Management"),
            
            // Role Management
            ("roles.view", "View roles", "Role Management"),
            ("roles.create", "Create roles", "Role Management"),
            ("roles.edit", "Edit roles", "Role Management"),
            ("roles.delete", "Delete roles", "Role Management"),
            
            // Add "assign_roles" and "grant_permissions" permissions
            ("assign_roles", "Assign roles to users", "Role Management"),
            ("grant_permissions", "Grant permissions to roles", "Permission Management"),
            
            // Employee Management
            ("employees.view", "View employees", "Employee Management"),
            ("employees.create", "Create employees", "Employee Management"),
            ("employees.edit", "Edit employees", "Employee Management"),
            ("employees.delete", "Delete employees", "Employee Management"),
            ("employees.view.self", "View own employee information", "Employee Management"),
            ("employees.edit.self", "Edit own employee information", "Employee Management"),
            
            // Bus Management
            ("buses.view", "View buses", "Bus Management"),
            ("buses.create", "Create buses", "Bus Management"),
            ("buses.edit", "Edit buses", "Bus Management"),
            ("buses.delete", "Delete buses", "Bus Management"),
            
            // Route Management
            ("routes.view", "View routes", "Route Management"),
            ("routes.create", "Create routes", "Route Management"),
            ("routes.edit", "Edit routes", "Route Management"),
            ("routes.delete", "Delete routes", "Route Management"),
            
            // Ticket Management
            ("tickets.view", "View tickets", "Ticket Management"),
            ("tickets.create", "Create tickets", "Ticket Management"),
            ("tickets.edit", "Edit tickets", "Ticket Management"),
            ("tickets.delete", "Delete tickets", "Ticket Management"),
            
            // Sales Management
            ("sales.view", "View sales", "Sales Management"),
            ("sales.create", "Create sales", "Sales Management"),
            ("sales.edit", "Edit sales", "Sales Management"),
            ("sales.delete", "Delete sales", "Sales Management"),


            // regular user permisssions for buying and reserving tickets
            ("tickets.buy", "Buy tickets from the app", "Ticket Sale to user"), // will create both a ticket and a sale
            ("tickets.reserve", "Reserve tickets from the app", "Ticket Sale to user"), // will create both a ticket and a sale - so this permission
            //will be used as generic user side check for buying and reserving tickets in special reducers
            // we already have permissions for viewing  and creating and editing and deleting tickets, so we don't need to add them
            
            // Maintenance Management
            ("maintenance.view", "View maintenance records", "Maintenance Management"),
            ("maintenance.create", "Create maintenance records", "Maintenance Management"),
            ("maintenance.edit", "Edit maintenance records", "Maintenance Management"),
            ("maintenance.delete", "Delete maintenance records", "Maintenance Management"),
            
            // Reports
            ("reports.view", "View reports", "Reports"),
            ("reports.create", "Create reports", "Reports"),
            ("reports.export", "Export reports", "Reports"),

            // OpenID Connect Management
            ("openid.connect.view", "View OpenID Connect clients", "OpenID Connect Management"),
            ("openid.connect.create", "Create OpenID Connect clients", "OpenID Connect Management"),
            ("openid.connect.edit", "Edit OpenID Connect clients", "OpenID Connect Management"),
            ("openid.connect.delete", "Delete OpenID Connect clients", "OpenID Connect Management"),
            ("openid.connect.grant", "Grant OpenID Connect clients", "OpenID Connect Management"),
            ("openid.connect.revoke", "Revoke OpenID Connect clients", "OpenID Connect Management"),
            ("openid.connect.list", "List OpenID Connect clients", "OpenID Connect Management"),
            ("openid.connect.refresh", "Refresh OpenID Connect clients", "OpenID Connect Management"),
            
             
            // Magic Link Management
            ("magiclink.view", "View Magic Links", "Magic Link Management"),
            ("magiclink.create", "Create Magic Links", "Magic Link Management"),
            ("magiclink.edit", "Edit Magic Links", "Magic Link Management"),
            ("magiclink.delete", "Delete Magic Links", "Magic Link Management"),
            ("magiclink.grant", "Grant Magic Links", "Magic Link Management"),
            ("magiclink.revoke", "Revoke Magic Links", "Magic Link Management"),
            ("magiclink.list", "List Magic Links", "Magic Link Management"),
            ("magiclink.refresh", "Refresh Magic Links", "Magic Link Management"),

            // we have magic link permissions  above, so no need to duplicate them

            // other permissions will go below this for categories we might add later 
            // maybe i will add a system for bus conductor performance or payment handling

            
        };

        Log.Info($"Creating {permissions.Length} permissions");
        foreach (var (name, description, category) in permissions)
        {
            Log.Debug($"Creating permission: {name} in category {category}");
            CreatePermission(ctx, name, description, category);
        }

        // Create assign_roles permission if it doesn't exist
        var assignRolesPermission = ctx.Db.Permission.Iter().FirstOrDefault(p => p.Name == "assign_roles");
        if (assignRolesPermission == null)
        {
            Log.Info("Creating assign_roles permission");
            uint assignRolesPermId = GetNextId(ctx, "permissionId");
            Log.Debug($"Generated permissionId: {assignRolesPermId}");

            assignRolesPermission = new Permission
            {
                PermissionId = assignRolesPermId,
                Name = "assign_roles",
                Description = "Assign roles to users",
                Category = "Role Management",
                IsActive = true,
                CreatedAt = GetSafeUnixTimestamp()
            };
            ctx.Db.Permission.Insert(assignRolesPermission);
            Log.Info("assign_roles permission created successfully");
        }

        // Directly grant the admin user the necessary permissions
        Log.Info("Granting permissions to admin user");
        var adminUser = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == "admin");
        var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");

        if (adminUser != null && adminRole != null)
        {
            Log.Info("Found admin user and admin role, granting permissions");
            // Find or create the grant_permissions permission
            var grantPermission = ctx.Db.Permission.Iter().FirstOrDefault(p => p.Name == "grant_permissions");
            if (grantPermission == null)
            {
                Log.Info("Creating grant_permissions permission");
                uint permId = GetNextId(ctx, "permissionId");
                Log.Debug($"Generated permissionId: {permId}");

                grantPermission = new Permission
                {
                    PermissionId = permId,
                    Name = "grant_permissions",
                    Description = "Grant permissions to roles",
                    Category = "Permission Management",
                    IsActive = true,
                    CreatedAt = GetSafeUnixTimestamp()
                };
                ctx.Db.Permission.Insert(grantPermission);
                Log.Info("grant_permissions permission created successfully");
            }
            else
            {
                Log.Info("grant_permissions permission already exists");
            }

            // Directly create the role permission for grant_permissions
            if (!ctx.Db.RolePermission.Iter().Any(rp => rp.RoleId == adminRole.RoleId && rp.PermissionId == grantPermission.PermissionId))
            {
                Log.Info("Assigning grant_permissions to admin role");
                uint rolePermId = GetNextId(ctx, "rolePermissionId");
                Log.Debug($"Generated rolePermissionId: {rolePermId}");

                var rolePermission = new RolePermission
                {
                    Id = rolePermId,
                    RoleId = adminRole.RoleId,
                    PermissionId = grantPermission.PermissionId,
                    GrantedAt = GetSafeUnixTimestamp(),
                    GrantedBy = "System"
                };
                ctx.Db.RolePermission.Insert(rolePermission);
                Log.Info("grant_permissions assigned to admin role successfully");
            }
            else
            {
                Log.Info("grant_permissions already assigned to admin role");
            }

            // Directly create the role permission for assign_roles
            if (!ctx.Db.RolePermission.Iter().Any(rp => rp.RoleId == adminRole.RoleId && rp.PermissionId == assignRolesPermission.PermissionId))
            {
                Log.Info("Assigning assign_roles to admin role");
                uint rolePermId = GetNextId(ctx, "rolePermissionId");
                Log.Debug($"Generated rolePermissionId: {rolePermId}");

                var rolePermission = new RolePermission
                {
                    Id = rolePermId,
                    RoleId = adminRole.RoleId,
                    PermissionId = assignRolesPermission.PermissionId,
                    GrantedAt = GetSafeUnixTimestamp(),
                    GrantedBy = "System"
                };
                ctx.Db.RolePermission.Insert(rolePermission);
                Log.Info("assign_roles assigned to admin role successfully");
            }
            
            // Assign all other permissions to admin role
            Log.Info("Assigning all permissions to admin role");
            foreach (var permission in ctx.Db.Permission.Iter())
            {
                // Skip grant_permissions and assign_roles as they were handled separately
                if (permission.Name == "grant_permissions" || permission.Name == "assign_roles")
                {
                    continue;
                }
                
                if (!ctx.Db.RolePermission.Iter().Any(rp => rp.RoleId == adminRole.RoleId && rp.PermissionId == permission.PermissionId))
                {
                    uint rolePermId = GetNextId(ctx, "rolePermissionId");
                    var rolePermission = new RolePermission
                    {
                        Id = rolePermId,
                        RoleId = adminRole.RoleId,
                        PermissionId = permission.PermissionId,
                        GrantedAt = GetSafeUnixTimestamp(),
                        GrantedBy = "System"
                    };
                    ctx.Db.RolePermission.Insert(rolePermission);
                    Log.Debug($"Assigned permission {permission.Name} to admin role");
                }
            }
            
            // Assign basic permissions to User role
            var userRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "User");
            if (userRole != null)
            {
                Log.Info("Assigning basic permissions to User role");
                
                // Define the permissions that regular users should have
                var userPermissions = new[]
                {
                    "users.view",           // View users
                    "roles.view",           // View roles
                    "employees.view", // View employees
                    "employees.view.self",  // View own employee information
                    "employees.edit.self", // Edit own employee information
                    "buses.view",           // View buses
                    "routes.view",          // View routes
                    "tickets.view",         // View tickets
                    "tickets.buy",          // Buy tickets
                    "tickets.reserve",      // Reserve tickets
                    "maintenance.view",      // View maintenance records
                    "reports.view",          // View reports
                    // IF EMPLOYEE = SALES DEPARTMENT THEN HE'LL HAVE ACCESS TO VIEW AND CREATE AND EDIT SALES
                    "sales.view",            // View sales 
                    "sales.create",          // Create sales
                    "sales.edit",            // Edit sales
                    // ADD PERMISSIONS FOR GENERAL OIDC AND MAGIC LINK MANAGEMENT BECAUSE THAT WILL BE USED
                    // FOR SALES DEPARTMENT TO LOGIN TO THE SYSTEM VIA OIDC AND MAGIC LINK
                    "openid.connect.view",   // View OpenID Connect clients
                    "openid.connect.create", // Create OpenID Connect clients
                    "openid.connect.edit",   // Edit OpenID Connect clients
                    "openid.connect.delete", // Delete OpenID Connect clients
                    "openid.connect.grant",  // Grant OpenID Connect clients
                    "openid.connect.revoke", // Revoke OpenID Connect clients
                    "openid.connect.list",   // List OpenID Connect clients
                    "openid.connect.refresh",// Refresh OpenID Connect clients
                    "magiclink.view",        // View Magic Links
                    "magiclink.create",      // Create Magic Links
                    "magiclink.edit",        // Edit Magic Links
                    "magiclink.delete",      // Delete Magic Links
                    "magiclink.grant",       // Grant Magic Links
                    "magiclink.revoke",      // Revoke Magic Links
                    "magiclink.list",        // List Magic Links
                    "magiclink.refresh",     // Refresh Magic Links
                };
                
                foreach (var permName in userPermissions)
                {
                    var permission = ctx.Db.Permission.Iter().FirstOrDefault(p => p.Name == permName);
                    if (permission != null)
                    {
                        if (!ctx.Db.RolePermission.Iter().Any(rp => rp.RoleId == userRole.RoleId && rp.PermissionId == permission.PermissionId))
                        {
                            uint rolePermId = GetNextId(ctx, "rolePermissionId");
                            var rolePermission = new RolePermission
                            {
                                Id = rolePermId,
                                RoleId = userRole.RoleId,
                                PermissionId = permission.PermissionId,
                                GrantedAt = GetSafeUnixTimestamp(),
                                GrantedBy = "System"
                            };
                            ctx.Db.RolePermission.Insert(rolePermission);
                            Log.Debug($"Assigned permission {permission.Name} to User role");
                        }
                    }
                    else
                    {
                        Log.Warn($"Permission {permName} not found, skipping assignment to User role");
                    }
                }
                
                Log.Info("Basic permissions assigned to User role successfully");
            }
            else
            {
                Log.Warn("User role not found, skipping permission assignment");
            }
        }
        else
        {
            Log.Warn("Admin user or admin role not found, skipping permission assignment");
        }
    }

    private static void InitializeRoles(ReducerContext ctx)
    {
        Log.Info("Initializing roles...");
        // Create default roles if they don't already exist
        if (!ctx.Db.Role.Iter().Any(r => r.Name == "Administrator"))
        {
            Log.Info("Creating Administrator role");
            CreateRole(ctx, 1, "Administrator", "Full system access", true, 100);
        }
        else
        {
            Log.Info("Administrator role already exists");
        }

        if (!ctx.Db.Role.Iter().Any(r => r.Name == "User"))
        {
            Log.Info("Creating User role");
            CreateRole(ctx, 0, "User", "Basic access", true, 1);
        }
        else
        {
            Log.Info("User role already exists");
        }

        if (!ctx.Db.Role.Iter().Any(r => r.Name == "Manager"))
        {
            Log.Info("Creating Manager role");
            CreateRole(ctx, 2, "Manager", "System management access", true, 50);
        }
        else
        {
            Log.Info("Manager role already exists");
        }

        // Get the role IDs for roles
        Log.Info("Retrieving roles for permission assignment");
        var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
        var userRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "User");
        var managerRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Manager");

        // Check if the roles were successfully created and retrieved
        if (adminRole != null && userRole != null && managerRole != null)
        {
            Log.Info("All roles retrieved successfully, assigning permissions");

            // Assign all permissions to the Administrator role
            Log.Info("Assigning all permissions to Administrator role");
            var allPermissions = ctx.Db.Permission.Iter().ToList();
            Log.Debug($"Found {allPermissions.Count} permissions to assign to Administrator role");

            foreach (var perm in allPermissions)
            {
                // More robust uniqueness check - check both RoleId and PermissionId together
                if (!ctx.Db.RolePermission.Iter().Any(rp =>
                    rp.RoleId == adminRole.RoleId &&
                    rp.PermissionId == perm.PermissionId))
                {
                    Log.Debug($"Assigning permission {perm.Name} to Administrator role");
                    uint rolePermId = GetNextId(ctx, "rolePermissionId");
                    var rolePermission = new RolePermission
                    {
                        Id = rolePermId,
                        RoleId = adminRole.RoleId,
                        PermissionId = perm.PermissionId,
                        GrantedAt = GetSafeUnixTimestamp(),
                        GrantedBy = "System"
                    };
                    ctx.Db.RolePermission.Insert(rolePermission);
                }
                else
                {
                    Log.Debug($"Permission {perm.Name} already assigned to Administrator role");
                }
            }
            Log.Info("All permissions assigned to Administrator role");

            // Assign view permissions to user role
            Log.Info("Assigning view permissions to User role");
            var viewPermissions = ctx.Db.Permission.Iter()
                .Where(p => p.Name.EndsWith(".view"))
                .ToList();
            Log.Debug($"Found {viewPermissions.Count} view permissions to assign to User role");

            foreach (var perm in viewPermissions)
            {
                // More robust uniqueness check
                if (!ctx.Db.RolePermission.Iter().Any(rp =>
                    rp.RoleId == userRole.RoleId &&
                    rp.PermissionId == perm.PermissionId))
                {
                    Log.Debug($"Assigning permission {perm.Name} to User role");
                    uint rolePermId = GetNextId(ctx, "rolePermissionId");
                    var rolePermission = new RolePermission
                    {
                        Id = rolePermId,
                        RoleId = userRole.RoleId,
                        PermissionId = perm.PermissionId,
                        GrantedAt = GetSafeUnixTimestamp(),
                        GrantedBy = "System"
                    };
                    ctx.Db.RolePermission.Insert(rolePermission);
                }
                else
                {
                    Log.Debug($"Permission {perm.Name} already assigned to User role");
                }
            }
            Log.Info("View permissions assigned to User role");

            // Assign manager permissions (view + create + edit)
            Log.Info("Assigning view, create, and edit permissions to Manager role");
            var managerPermissions = ctx.Db.Permission.Iter()
                .Where(p => p.Name.EndsWith(".view") || p.Name.EndsWith(".create") || p.Name.EndsWith(".edit"))
                .ToList();
            Log.Debug($"Found {managerPermissions.Count} permissions to assign to Manager role");

            foreach (var perm in managerPermissions)
            {
                // More robust uniqueness check
                if (!ctx.Db.RolePermission.Iter().Any(rp =>
                    rp.RoleId == managerRole.RoleId &&
                    rp.PermissionId == perm.PermissionId))
                {
                    Log.Debug($"Assigning permission {perm.Name} to Manager role");
                    uint rolePermId = GetNextId(ctx, "rolePermissionId");
                    var rolePermission = new RolePermission
                    {
                        Id = rolePermId,
                        RoleId = managerRole.RoleId,
                        PermissionId = perm.PermissionId,
                        GrantedAt = GetSafeUnixTimestamp(),
                        GrantedBy = "System"
                    };
                    ctx.Db.RolePermission.Insert(rolePermission);
                }
                else
                {
                    Log.Debug($"Permission {perm.Name} already assigned to Manager role");
                }
            }
            Log.Info("Permissions assigned to Manager role");
        }
        else
        {
            Log.Warn("Warning: could not seed admin and user with roles and permissions.");
        }
    }


    private static void InitializeJobs(ReducerContext ctx)
    {
        Log.Info("Initializing jobs...");
        // Check if jobs already exist
        if (ctx.Db.Job.Iter().Any())
        {
            Log.Info("Jobs already exist, skipping initialization");
            return; // Jobs already exist
        }

        Log.Info("No existing jobs found, creating new jobs");

        var jobs = new[]
        {
            ("Водитель автобуса", "Стажировка (2 года)", 1200.0, "Транспортный", "Управление автобусом и перевозка пассажиров", 2u,
                new[] {"Вождение", "Обслуживание пассажиров"}, new[] {"Категория D"}, "Среднее специальное",
                "Сменный график", true, false, true, new[] {"Медицинская страховка", "Проездной"},
                "Начальник автопарка", 24u, 12u, "Безопасность и пунктуальность"),

            ("Механик", "Стажировка (3 года)", 1300.0, "Технический", "Обслуживание и ремонт автобусов", 3u,
                new[] {"Автомеханика", "Диагностика"}, new[] {"Автослесарь"}, "Среднее специальное",
                "5/2", true, false, false, new[] {"Медицинская страховка", "Спецодежда"},
                "Главный инженер", 24u, 12u, "Качество ремонта"),

            ("Диспетчер", "Стажировка (1 год)", 1100.0, "Логистика", "Координация движения автобусов", 1u,
                new[] {"Логистика", "Коммуникация"}, new[] {"Диспетчер"}, "Среднее специальное",
                "Сменный график", true, false, true, new[] {"Медицинская страховка"},
                "Начальник автопарка", 24u, 12u, "Эффективность координации"),

            ("Начальник автопарка", "Стажировка (5 лет)", 1800.0, "Управление", "Руководство автопарком", 5u,
                new[] {"Управление персоналом", "Логистика"}, new[] {"Управление транспортом"}, "Высшее",
                "5/2", true, false, false, new[] {"Медицинская страховка", "Служебный автомобиль"},
                "Директор", 28u, 14u, "Эффективность автопарка"),

            ("Кассир", "Стажировка (6 месяцев)", 900.0, "Финансы", "Продажа билетов и работа с деньгами", 1u,
                new[] {"Работа с кассой", "Обслуживание клиентов"}, new[] {"Кассир"}, "Среднее",
                "Сменный график", true, true, true, new[] {"Медицинская страховка"},
                "Бухгалтер", 24u, 12u, "Точность расчетов"),

            ("Инженер по безопасности", "Стажировка (3 года)", 1400.0, "Безопасность", "Обеспечение безопасности перевозок", 3u,
                new[] {"Техника безопасности", "Инспектирование"}, new[] {"Охрана труда"}, "Высшее",
                "5/2", true, false, false, new[] {"Медицинская страховка", "Спецодежда"},
                "Начальник автопарка", 24u, 12u, "Снижение аварийности"),

            ("Автоэлектрик", "Стажировка (2 года)", 1250.0, "Технический", "Обслуживание электрооборудования автобусов", 2u,
                new[] {"Электрика", "Диагностика"}, new[] {"Автоэлектрик"}, "Среднее специальное",
                "5/2", true, false, false, new[] {"Медицинская страховка", "Спецодежда"},
                "Главный инженер", 24u, 12u, "Качество ремонта"),

            ("Мойщик автобусов", "Стажировка (1 месяц)", 800.0, "Обслуживание", "Мойка и уборка автобусов", 0u,
                new[] {"Клининг"}, new string[0], "Среднее",
                "Сменный график", true, true, true, new[] {"Медицинская страховка", "Спецодежда"},
                "Мастер", 24u, 12u, "Чистота автобусов"),

            ("Сменный мастер", "Стажировка (4 года)", 1350.0, "Технический", "Руководство сменой технического персонала", 4u,
                new[] {"Управление персоналом", "Автомеханика"}, new[] {"Мастер"}, "Среднее специальное",
                "Сменный график", true, false, true, new[] {"Медицинская страховка", "Спецодежда"},
                "Главный инженер", 24u, 12u, "Эффективность смены"),

            ("Контролер", "Стажировка (1 год)", 950.0, "Контроль", "Проверка билетов и соблюдения правил", 1u,
                new[] {"Проверка документов", "Коммуникация"}, new[] {"Контролер"}, "Среднее",
                "Сменный график", true, true, true, new[] {"Медицинская страховка", "Проездной"},
                "Начальник службы контроля", 24u, 12u, "Выявление нарушений")
        };

        Log.Info($"Creating {jobs.Length} job positions");
        foreach (var (title, internship, baseSalary, department, jobDescription, requiredExperience,
                 requiredSkills, requiredCertifications, educationRequirements, workSchedule,
                 isFullTime, isPartTime, isShiftWork, benefits, reportingTo, vacationDays,
                 sickDays, performanceMetrics) in jobs)
        {
            Log.Debug($"Creating job: {title} in department {department}");
            uint jobId = GetNextId(ctx, "jobId");
            var job = new Job
            {
                JobId = jobId,
                JobTitle = title,
                Internship = internship,
                BaseSalary = baseSalary,
                Department = department,
                JobDescription = jobDescription,
                RequiredExperience = requiredExperience,
                RequiredSkills = requiredSkills,
                RequiredCertifications = requiredCertifications,
                EducationRequirements = educationRequirements,
                WorkSchedule = workSchedule,
                IsFullTime = isFullTime,
                IsPartTime = isPartTime,
                IsShiftWork = isShiftWork,
                Benefits = benefits,
                ReportingTo = reportingTo,
                VacationDays = vacationDays,
                SickDays = sickDays,
                PerformanceMetrics = performanceMetrics
            };
            ctx.Db.Job.Insert(job);
            Log.Debug($"Job {title} created with ID {jobId}");
        }

        Log.Info("Jobs initialized successfully");
    }

    private static void InitializeEmployees(ReducerContext ctx)
    {
        // Check if employees already exist
        if (ctx.Db.Employee.Iter().Any())
        {
            return; // Employees already exist
        }

        Log.Info("Initializing employees...");

        // Get job IDs
        var jobs = ctx.Db.Job.Iter().ToList();
        if (jobs.Count == 0)
        {
            Log.Error("Cannot initialize employees: No jobs found");
            return;
        }

        var employees = new[]
        {
            ("Иванов", "Иван", "Иванович", new DateTime(2020, 1, 15), 0, "В-001", "+375291234567", "ivanov@buspark.by",
             new DateTime(1985, 5, 10), "MP1234567", "РОВД г. Могилева", new DateTime(2015, 3, 15),
             "photos/ivanov.jpg", "г. Могилев, ул. Первомайская 1-1", "Иванова Мария +375291234568",
             new DateTime(2023, 1, 10), "Действителен", "На маршруте", new[] {"Категория D"},
             new DateTime(2025, 1, 10), "МС-123456", new DateTime(2024, 6, 15), "AB1234567", "D",
             new DateTime(2026, 5, 20), 5u, new[] {"Русский", "Белорусский"}, "Утро",
             new[] {"Вождение автобуса", "Первая помощь"}, "Хорошо", 20u, 2u),

            ("Петров", "Петр", "Петрович", new DateTime(2019, 3, 20), 0, "В-002", "+375291234569", "petrov@buspark.by",
             new DateTime(1982, 7, 15), "MP2345678", "РОВД г. Могилева", new DateTime(2014, 5, 20),
             "photos/petrov.jpg", "г. Могилев, ул. Ленинская 2-2", "Петрова Анна +375291234570",
             new DateTime(2023, 2, 15), "Действителен", "На маршруте", new[] {"Категория D"},
             new DateTime(2025, 2, 15), "МС-234567", new DateTime(2024, 7, 20), "AB2345678", "D",
             new DateTime(2026, 6, 25), 7u, new[] {"Русский", "Белорусский"}, "День",
             new[] {"Вождение автобуса", "Ремонт"}, "Отлично", 18u, 3u),

            ("Сидоров", "Алексей", "Михайлович", new DateTime(2018, 6, 10), 1, "М-001", "+375291234571", "sidorov@buspark.by",
             new DateTime(1980, 9, 20), "MP3456789", "РОВД г. Могилева", new DateTime(2013, 7, 25),
             "photos/sidorov.jpg", "г. Могилев, ул. Космонавтов 3-3", "Сидорова Елена +375291234572",
             new DateTime(2023, 3, 20), "Действителен", "На работе", new[] {"Автослесарь"},
             new DateTime(2025, 3, 20), "МС-345678", new DateTime(2024, 8, 25), "AB3456789", "B",
             new DateTime(2026, 7, 30), 10u, new[] {"Русский", "Белорусский"}, "День",
             new[] {"Ремонт двигателей", "Диагностика"}, "Хорошо", 22u, 1u),

            ("Козлов", "Дмитрий", "Сергеевич", new DateTime(2021, 2, 5), 2, "Д-001", "+375291234573", "kozlov@buspark.by",
             new DateTime(1988, 11, 25), "MP4567890", "РОВД г. Могилева", new DateTime(2016, 9, 30),
             "photos/kozlov.jpg", "г. Могилев, ул. Пушкинская 4-4", "Козлова Ольга +375291234574",
             new DateTime(2023, 4, 25), "Действителен", "На работе", new[] {"Диспетчер"},
             new DateTime(2025, 4, 25), "МС-456789", new DateTime(2024, 9, 30), "AB4567890", "B",
             new DateTime(2026, 8, 5), 3u, new[] {"Русский", "Белорусский", "Английский"}, "Вечер",
             new[] {"Логистика", "Коммуникация"}, "Отлично", 24u, 0u),

            ("Морозов", "Андрей", "Владимирович", new DateTime(2017, 8, 25), 3, "Н-001", "+375291234575", "morozov@buspark.by",
             new DateTime(1975, 3, 30), "MP5678901", "РОВД г. Могилева", new DateTime(2012, 11, 5),
             "photos/morozov.jpg", "г. Могилев, ул. Якубовского 5-5", "Морозова Ирина +375291234576",
             new DateTime(2023, 5, 30), "Действителен", "На работе", new[] {"Управление транспортом"},
             new DateTime(2025, 5, 30), "МС-567890", new DateTime(2024, 10, 5), "AB5678901", "B",
             new DateTime(2026, 9, 10), 15u, new[] {"Русский", "Белорусский", "Английский"}, "День",
             new[] {"Управление персоналом", "Логистика", "Бюджетирование"}, "Отлично", 28u, 0u),

            ("Новиков", "Сергей", "Александрович", new DateTime(2022, 4, 12), 4, "К-001", "+375291234577", "novikov@buspark.by",
             new DateTime(1990, 5, 5), "MP6789012", "РОВД г. Могилева", new DateTime(2017, 12, 10),
             "photos/novikov.jpg", "г. Могилев, ул. Мовчанского 6-6", "Новикова Татьяна +375291234578",
             new DateTime(2023, 6, 5), "Действителен", "На работе", new[] {"Кассир"},
             new DateTime(2025, 6, 5), "МС-678901", new DateTime(2024, 11, 10), "AB6789012", "B",
             new DateTime(2026, 10, 15), 2u, new[] {"Русский", "Белорусский"}, "День",
             new[] {"Работа с кассой", "Обслуживание клиентов"}, "Хорошо", 24u, 0u),

            ("Волков", "Михаил", "Дмитриевич", new DateTime(2020, 11, 30), 0, "В-003", "+375291234579", "volkov@buspark.by",
             new DateTime(1983, 7, 10), "MP7890123", "РОВД г. Могилева", new DateTime(2015, 1, 15),
             "photos/volkov.jpg", "г. Могилев, ул. Крупской 7-7", "Волкова Екатерина +375291234580",
             new DateTime(2023, 7, 10), "Действителен", "На маршруте", new[] {"Категория D"},
             new DateTime(2025, 7, 10), "МС-789012", new DateTime(2024, 12, 15), "AB7890123", "D",
             new DateTime(2026, 11, 20), 6u, new[] {"Русский", "Белорусский"}, "Утро",
             new[] {"Вождение автобуса", "Первая помощь"}, "Хорошо", 20u, 2u),

            ("Соловьев", "Артем", "Игоревич", new DateTime(2019, 9, 15), 1, "М-002", "+375291234581", "soloviev@buspark.by",
             new DateTime(1984, 9, 15), "MP8901234", "РОВД г. Могилева", new DateTime(2014, 2, 20),
             "photos/soloviev.jpg", "г. Могилев, ул. Островского 8-8", "Соловьева Наталья +375291234582",
             new DateTime(2023, 8, 15), "Действителен", "На работе", new[] {"Автослесарь"},
             new DateTime(2025, 8, 15), "МС-890123", new DateTime(2025, 1, 20), "AB8901234", "B",
             new DateTime(2026, 12, 25), 8u, new[] {"Русский", "Белорусский"}, "День",
             new[] {"Ремонт трансмиссии", "Диагностика"}, "Отлично", 22u, 1u),

            ("Васильев", "Николай", "Андреевич", new DateTime(2021, 7, 8), 0, "В-004", "+375291234583", "vasiliev@buspark.by",
             new DateTime(1986, 11, 20), "MP9012345", "РОВД г. Могилева", new DateTime(2016, 3, 25),
             "photos/vasiliev.jpg", "г. Могилев, ул. Гагарина 9-9", "Васильева Светлана +375291234584",
             new DateTime(2023, 9, 20), "Действителен", "На маршруте", new[] {"Категория D"},
             new DateTime(2025, 9, 20), "МС-901234", new DateTime(2025, 2, 25), "AB9012345", "D",
             new DateTime(2027, 1, 30), 4u, new[] {"Русский", "Белорусский"}, "Вечер",
             new[] {"Вождение автобуса", "Первая помощь"}, "Хорошо", 20u, 2u),

            ("Зайцев", "Владимир", "Петрович", new DateTime(2018, 12, 3), 2, "Д-002", "+375291234585", "zaitsev@buspark.by",
             new DateTime(1987, 1, 25), "MP0123456", "РОВД г. Могилева", new DateTime(2015, 4, 30),
             "photos/zaitsev.jpg", "г. Могилев, ул. Симонова 10-10", "Зайцева Анастасия +375291234586",
             new DateTime(2023, 10, 25), "Действителен", "На работе", new[] {"Диспетчер"},
             new DateTime(2025, 10, 25), "МС-012345", new DateTime(2025, 3, 30), "AB0123456", "B",
             new DateTime(2027, 2, 5), 5u, new[] {"Русский", "Белорусский"}, "День",
             new[] {"Логистика", "Коммуникация"}, "Хорошо", 24u, 1u)
        };

        foreach (var (surname, name, patronym, employedSince, jobIndex, badgeNumber, contactPhone, contactEmail,
                 dateOfBirth, passportNumber, passportIssuedBy, passportIssuedDate, photoUrl, address,
                 emergencyContact, lastTrainingDate, trainingStatus, currentStatus, certifications,
                 certificationExpiry, medicalCertificate, medicalCertificateExpiry, driverLicenseNumber,
                 driverLicenseCategory, driverLicenseExpiry, yearsOfExperience, languagesSpoken,
                 preferredShiftType, skillsAndQualifications, performanceRating, vacationDaysRemaining,
                 sickDaysUsed) in employees)
        {
            uint employeeId = GetNextId(ctx, "employeeId");
            var jobId = jobs[jobIndex % jobs.Count].JobId;

            // Convert DateTime to Unix timestamp (milliseconds) safely
            // First get clean DateTime objects
            DateTime employedSinceClean = employedSince;
            DateTime? dateOfBirthClean = dateOfBirth;
            DateTime? passportIssuedDateClean = passportIssuedDate;
            DateTime? lastTrainingDateClean = lastTrainingDate;
            DateTime? certificationExpiryClean = certificationExpiry;
            DateTime? medicalCertificateExpiryClean = medicalCertificateExpiry;
            DateTime? driverLicenseExpiryClean = driverLicenseExpiry;

            // Get the local timezone
            TimeZoneInfo localZone = TimeZoneInfo.Local;

            // Convert to DateTimeOffset with proper timezone
            DateTimeOffset employedSinceOffset = new DateTimeOffset(employedSinceClean, localZone.GetUtcOffset(employedSinceClean));
            DateTimeOffset? dateOfBirthOffset = dateOfBirthClean.HasValue ? new DateTimeOffset(dateOfBirthClean.Value, localZone.GetUtcOffset(dateOfBirthClean.Value)) : null;
            DateTimeOffset? passportIssuedDateOffset = passportIssuedDateClean.HasValue ? new DateTimeOffset(passportIssuedDateClean.Value, localZone.GetUtcOffset(passportIssuedDateClean.Value)) : null;
            DateTimeOffset? lastTrainingDateOffset = lastTrainingDateClean.HasValue ? new DateTimeOffset(lastTrainingDateClean.Value, localZone.GetUtcOffset(lastTrainingDateClean.Value)) : null;
            DateTimeOffset? certificationExpiryOffset = certificationExpiryClean.HasValue ? new DateTimeOffset(certificationExpiryClean.Value, localZone.GetUtcOffset(certificationExpiryClean.Value)) : null;
            DateTimeOffset? medicalCertificateExpiryOffset = medicalCertificateExpiryClean.HasValue ? new DateTimeOffset(medicalCertificateExpiryClean.Value, localZone.GetUtcOffset(medicalCertificateExpiryClean.Value)) : null;
            DateTimeOffset? driverLicenseExpiryOffset = driverLicenseExpiryClean.HasValue ? new DateTimeOffset(driverLicenseExpiryClean.Value, localZone.GetUtcOffset(driverLicenseExpiryClean.Value)) : null;

            // Convert to Unix timestamps
            ulong employedSinceMs = (ulong)employedSinceOffset.ToUnixTimeMilliseconds();
            ulong? dateOfBirthMs = dateOfBirthOffset?.ToUnixTimeMilliseconds() != null ? (ulong)dateOfBirthOffset.Value.ToUnixTimeMilliseconds() : null;
            ulong? passportIssuedDateMs = passportIssuedDateOffset?.ToUnixTimeMilliseconds() != null ? (ulong)passportIssuedDateOffset.Value.ToUnixTimeMilliseconds() : null;
            ulong? lastTrainingDateMs = lastTrainingDateOffset?.ToUnixTimeMilliseconds() != null ? (ulong)lastTrainingDateOffset.Value.ToUnixTimeMilliseconds() : null;
            ulong? certificationExpiryMs = certificationExpiryOffset?.ToUnixTimeMilliseconds() != null ? (ulong)certificationExpiryOffset.Value.ToUnixTimeMilliseconds() : null;
            ulong? medicalCertificateExpiryMs = medicalCertificateExpiryOffset?.ToUnixTimeMilliseconds() != null ? (ulong)medicalCertificateExpiryOffset.Value.ToUnixTimeMilliseconds() : null;
            ulong? driverLicenseExpiryMs = driverLicenseExpiryOffset?.ToUnixTimeMilliseconds() != null ? (ulong)driverLicenseExpiryOffset.Value.ToUnixTimeMilliseconds() : null;

            var employee = new Employee
            {
                EmployeeId = employeeId,
                Surname = surname,
                Name = name,
                Patronym = patronym,
                EmployedSince = employedSinceMs,
                JobId = jobId,
                BadgeNumber = badgeNumber,
                ContactPhone = contactPhone,
                ContactEmail = contactEmail,
                DateOfBirth = dateOfBirthMs,
                PassportNumber = passportNumber,
                PassportIssuedBy = passportIssuedBy,
                PassportIssuedDate = passportIssuedDateMs,
                PhotoUrl = photoUrl,
                Address = address,
                EmergencyContact = emergencyContact,
                LastTrainingDate = lastTrainingDateMs,
                TrainingStatus = trainingStatus,
                CurrentStatus = currentStatus,
                Certifications = certifications,
                CertificationExpiry = certificationExpiryMs,
                MedicalCertificate = medicalCertificate,
                MedicalCertificateExpiry = medicalCertificateExpiryMs,
                DriverLicenseNumber = driverLicenseNumber,
                DriverLicenseCategory = driverLicenseCategory,
                DriverLicenseExpiry = driverLicenseExpiryMs,
                YearsOfExperience = yearsOfExperience,
                LanguagesSpoken = languagesSpoken,
                PreferredShiftType = preferredShiftType,
                SkillsAndQualifications = skillsAndQualifications,
                PerformanceRating = performanceRating,
                VacationDaysRemaining = vacationDaysRemaining,
                SickDaysUsed = sickDaysUsed
            };
            ctx.Db.Employee.Insert(employee);
        }

        Log.Info("Employees initialized successfully");
    }


    private static void InitializeRoutes(ReducerContext ctx)
    {
        // Check if routes already exist
        if (ctx.Db.Route.Iter().Any())
        {
            return; // Routes already exist
        }

        Log.Info("Initializing routes...");

        // Get drivers (employees with job title "Водитель автобуса")
        var driverJob = ctx.Db.Job.Iter().FirstOrDefault(j => j.JobTitle == "Водитель автобуса");
        if (driverJob == null)
        {
            Log.Error("Cannot initialize routes: Driver job not found");
            return;
        }

        var drivers = ctx.Db.Employee.Iter()
            .Where(e => e.JobId == driverJob.JobId)
            .ToList();

        if (drivers.Count == 0)
        {
            // Fallback to any employee if no drivers found
            drivers = ctx.Db.Employee.Iter().ToList();
        }

        // Get buses
        var buses = ctx.Db.Bus.Iter().ToList();
        if (buses.Count == 0)
        {
            Log.Error("Cannot initialize routes: No buses found");
            return;
        }

        // Current timestamp in milliseconds
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Типы маршрутов
        var routeTypes = new[] { "Городской", "Пригородный", "Экспресс", "Обычный", "Междугородний", "Сельский", "Кольцевой", "Сезонный", "Туристический", "Школьный", "Дачный", "Ночной" };

        // Peak hours
        var peakHours = new[] { "07:00-09:00", "17:00-19:00" };

        // Special instructions
        var specialInstructions = new[] {
            "Соблюдать осторожность на перекрестке у центрального рынка",
            "Снижать скорость возле школы на ул. Ленина",
            "Обратить внимание на крутой поворот у моста"
        };

        // Route features
        var routeFeatures = new[] {
            "Wi-Fi",
            "USB-зарядка",
            "Кондиционер",
            "Низкий пол",
            "Электронное табло",
            "Система оповещения",
            "Пандус для инвалидных колясок",
            "Велосипедная стойка"
        };

        // Alternative routes
        var alternativeRoutes = new[] {
            "Через ул. Советскую в случае перекрытия пр. Мира",
            "Через ул. Первомайскую в случае ремонта на ул. Ленина",
            "Через м-н Юбилейный при заторах на центральных улицах",
            "Через ул. Крупской в случае аварии на пр. Шмидта",
            "Через Пушкинский проспект при перекрытии ул. Симонова",
            "Через ул. Габровскую в случае ремонта на ул. Фатина",
            "Через поселок Пашково при заторах на основном маршруте",
            "Через Днепровский бульвар в случае перекрытия центра",
            "Через ул. Златоустовского при ремонте моста",
            "Через Любужский лесопарк в случае перекрытия ул. 30 лет Победы",
            "Через Заднепровье при затруднении движения в центре",
            "Через Гребеневский рынок в случае перекрытия ул. Крупской",
            "Через Могилев-2 для троллейбусных маршрутов при обрыве контактной сети",
            "Через объездную дорогу для междугородних автобусов при заторах на въезде в город",
            "Через Буйничи для маршрутов на Минск при ремонте основной трассы",
            "Через Шклов для маршрутов на Оршу при перекрытии прямой дороги",
            "Через Быхов для маршрутов на Гомель в случае ДТП на основной трассе",
            "Через Чаусы для междугородних маршрутов на восток области"
        };

        var routes = new[]
        {
            // Внутригородские маршруты
            ("ул. Фатина", "завод «Могилевтрансмаш»", "40 минут"),
            ("Могилевская больница №1", "пос. Броды-1", "35 минут"),
            ("Автовокзал", "Могилевоблнефтепродут", "30 минут"),
            ("м-н Казимировка", "ул. Златоустовского", "25 минут"),
            ("Областная больница", "пл. Ленина", "20 минут"),
            ("Любужский лесопарк", "Железнодорожный вокзал", "45 минут"),
            ("ул. Симонова", "завод «Могилевтрансмаш»", "35 минут"),
            ("Средняя школа №13", "железнодорожный вокзал", "30 минут"),
            ("Поселок Пашково", "ул. 30 лет Победы", "40 минут"),
            ("пл. Космонавтов", "завод «Могилевлифтмаш»", "25 минут"),
            ("бул. Днепровский", "поселок Гребенево", "50 минут"),
            ("м-н Юбилейный", "Областная больница", "30 минут"),
            ("ж/д вокзал", "Больница мед. реабилитации", "20 минут"),
            ("пл. Орджоникидзе", "Любужский лесопарк", "25 минут"),
            ("Могилевоблнефтепродукт", "Могилевская больница №1", "35 минут"),
            ("пер. Ватутина (Переезд)", "м-н Казимировка", "40 минут"),
            ("Городская ветеринарная станция", "м-н Казимировка", "45 минут"),
            ("ОАО «Техноприбор»", "ОАО «Техноприбор»", "60 минут"),
            ("ул. 30 лет Победы", "ул. Фатина", "30 минут"),
            ("Поселок Броды-1", "Могилевская больница №1", "35 минут"),
            ("ул. Пионерская", "завод «Вентзаготовок»", "25 минут"),
            ("Автовокзал", "деревня Новоселки", "45 минут"),
            ("м-н Казимировка", "поселок Ямницкий", "40 минут"),
            ("Областная больница", "Областная больница", "50 минут"),
            ("Средняя школа №13", "м-н Соломинка", "30 минут"),
            ("Поселок Любуж", "железнодорожный вокзал", "35 минут"),
            ("ул. Маневича (поселок Малая Боровка)", "железнодорожный вокзал", "40 минут"),
            ("Областная больница", "Облтипо", "25 минут"),
            ("м-н Юбилейный", "железнодорожный вокзал", "35 минут"),
            ("м-н Казимировка", "железнодорожный вокзал", "40 минут"),
            ("м-н Заря", "железнодорожный вокзал", "35 минут"),
            // Троллейбусные маршруты
            ("м-н Казимировка", "Автовокзал", "30 минут"),
            ("м-н Казимировка", "Железнодорожный вокзал", "35 минут"),
            ("м-н Казимировка", "ул. Фатина", "40 минут"),
            ("м-н Казимировка", "железнодорожный вокзал", "35 минут"),
            ("м-н Казимировка", "ул. Крупской", "45 минут"),
            ("м-н Казимировка", "Автовокзал", "30 минут"),
            ("м-н Казимировка", "ул. Габровская", "40 минут"),
            ("м-н Казимировка", "м-н Юбилейный", "50 минут"),
            // Дополнительные внутригородские и пригородные маршруты
            ("Вейнянка", "Фатина", "45 минут"),
            ("Мал. Боровка", "Солтановка", "50 минут"),
            ("Вокзал", "Спутник", "40 минут"),
            ("Мясокомбинат", "Заводская", "35 минут"),
            ("Броды", "Казимировка", "55 минут"),
            ("Гребеневский рынок", "Холмы", "45 минут"),
            ("Автовокзал", "Полыковичи", "40 минут"),
            ("Центр", "Сидоровичи", "60 минут"),
            ("Площадь Славы", "Буйничи", "30 минут"),
            ("Заднепровье", "Химволокно", "25 минут"),
            ("Вокзал", "Соломинка", "35 минут"),
            ("Площадь Ленина", "Чаусы", "50 минут"),
            ("Могилев-2", "Дашковка", "40 минут"),
            ("Кожзавод", "Сухари", "45 минут"),
            ("Гребеневский рынок", "Любуж", "30 минут"),
            // Междугородние маршруты
            ("Могилев", "Минск", "3 часа"),
            ("Могилев", "Гомель", "2.5 часа"),
            ("Могилев", "Москва", "10 часов"),
            ("Могилев", "Смоленск", "3 часа"),
            ("Могилев", "Бобруйск", "2 часа"),
            ("Могилев", "Горки", "1 час 30 минут"),
            ("Могилев", "Витебск", "3 часа"),
            ("Могилев", "Славгород", "1 час 30 минут"),
            ("Могилев", "Мстиславль", "1 час 30 минут"),
            
            // Пригородные маршруты
            ("Могилев", "Шклов", "45 минут"),
            ("Могилев", "Быхов", "1 час"),
            ("Могилев", "Круглое", "1 час")
        };

        Random random = new Random();

        for (int i = 0; i < routes.Length; i++)
        {
            var (startPoint, endPoint, travelTime) = routes[i];
            uint routeId = GetNextId(ctx, "routeId");

            // Generate route number based on route type
            string routeNumber;
            if (i < 31)
            {
                Log.Debug($"Processing city route index {i} with start: {startPoint}, end: {endPoint}");

                // Special cases first - check both endpoints and route number as failsafe
                if ((startPoint == "пл. Орджоникидзе" && endPoint == "Любужский лесопарк") || i == 13)
                {
                    Log.Debug("Assigning route 14к");
                    routeNumber = "14к";
                }
                else if ((startPoint == "Могилевоблнефтепродукт" && endPoint == "Могилевская больница №1") || i == 14)
                {
                    Log.Debug("Assigning route 15");
                    routeNumber = "15";
                }
                else if ((startPoint == "пер. Ватутина (Переезд)" && endPoint == "м-н Казимировка") || i == 15)
                {
                    Log.Debug("Assigning route 16");
                    routeNumber = "16";
                }
                else if ((startPoint == "Городская ветеринарная станция" && endPoint == "м-н Казимировка") || i == 16)
                {
                    Log.Debug("Assigning route 16д");
                    routeNumber = "16д";
                }
                else
                {
                    // For routes after 16д, adjust the numbering
                    if (i <= 13)
                    {
                        Log.Debug($"Regular route before 14к: {i + 1}");
                        routeNumber = (i + 1).ToString();
                    }
                    else if (i > 16)
                    {
                        // After 16д, continue with 17 and up
                        if (i == 17)
                        {
                            // Special case for route 17
                            Log.Debug($"Special case for route 17");
                            routeNumber = "17";
                        }
                        else
                        { // This now means i > 17
                          // For indices after 17, the route number should match the index value
                          // because the special cases have already shifted the numbering.
                            Log.Debug($"Route after 17, assigning number: {i}"); // Исправленный лог
                            routeNumber = i.ToString();                          // Исправленная логика
                        }
                    }
                    else
                    {
                        // Fallback for any edge cases
                        Log.Debug($"Edge case route number: {i + 1}");
                        routeNumber = (i + 1).ToString();
                    }
                }
                Log.Debug($"Final route number assigned: {routeNumber}");
            }
            else if (i < 38)
            {
                // Trolleybus routes (T1-T8)
                routeNumber = "T" + (i - 30).ToString();
                Log.Debug($"Assigned trolleybus route: {routeNumber}");
            }
            else if (i < 55)
            {
                // Additional city and suburban routes (101-116)
                routeNumber = (100 + (i - 38)).ToString();
                Log.Debug($"Assigned additional route: {routeNumber}");
            }
            else if (i < 64)
            {
                // Intercity routes (501-509)
                routeNumber = (500 + (i - 54)).ToString();
                Log.Debug($"Assigned intercity route: {routeNumber}");
            }
            else
            {
                // Suburban routes (201-203)
                routeNumber = (200 + (i - 63)).ToString();
                Log.Debug($"Assigned suburban route: {routeNumber}");
            }

            // Generate random values for other fields
            uint stopCount = (uint)random.Next(5, 15);
            string routeDescription = $"Маршрут {routeNumber} от {startPoint} до {endPoint}";

            // Set route length based on route type
            double routeLength;
            string routeType;

            // Determine route type based on the start and end points


            if (startPoint == endPoint || (i < routes.Length && i < 31))
            {
                // City routes (within the same city)
                routeType = i >= 31 && i < 38 ? "Троллейбусный" : "Городской";
                routeLength = Math.Round(random.NextDouble() * 15 + 5, 1); // 5-20 km
            }
            else if (routes[i].Item3.Contains("час"))
            {
                // Intercity routes (longer travel time)
                routeType = "Междугородний";
                routeLength = Math.Round(random.NextDouble() * 400 + 80, 1); // 80-480 km
            }
            else
            {
                // Suburban routes (shorter travel time)
                routeType = "Пригородный";
                routeLength = Math.Round(random.NextDouble() * 40 + 20, 1); // 20-60 km
            }

            bool isAccessible = random.Next(0, 10) < 8; // 80% chance of being accessible

            // Select features based on route type and bus model
            var bus = buses[i % buses.Count];
            var selectedFeatures = new List<string>();

            // Basic features for all modern buses
            if (bus.Year >= 2018)
            {
                selectedFeatures.Add("Система оповещения");
                selectedFeatures.Add("Электронное табло");
            }

            // Accessibility features
            if (isAccessible)
            {
                selectedFeatures.Add("Низкий пол");
                selectedFeatures.Add("Пандус для инвалидных колясок");
            }

            // Premium features for suburban/express routes or newer buses
            if (routeType == "Междугородний" || routeType == "Пригородный" || bus.Year >= 2021)
            {
                if (random.Next(0, 10) < 7) // 70% chance for premium buses
                {
                    selectedFeatures.Add("Wi-Fi");
                    selectedFeatures.Add("USB-зарядка");
                }
            }

            // Air conditioning for newer buses
            if (bus.Year >= 2019)
            {
                selectedFeatures.Add("Кондиционер");
            }

            // Bike racks for suburban routes
            if (routeType == "Пригородный" && random.Next(0, 10) < 4) // 40% chance
            {
                selectedFeatures.Add("Велосипедная стойка");
            }

            // Select random alternative routes
            string[] selectedAlternativeRoutes = i % 3 == 0 ?
                alternativeRoutes.OrderBy(x => random.Next()).Take(1).ToArray() :
                Array.Empty<string>();

            // Select random special instructions
            string[] selectedSpecialInstructions = i % 4 == 0 ?
                specialInstructions.OrderBy(x => random.Next()).Take(1).ToArray() :
                Array.Empty<string>();

            // Frequency in minutes - varies by route type
            uint frequencyPeak, frequencyOffPeak;

            if (routeType == "Городской" || routeType == "Троллейбусный")
            {
                frequencyPeak = (uint)random.Next(10, 21); // 10-20 minutes
                frequencyOffPeak = (uint)random.Next(20, 41); // 20-40 minutes
            }
            else if (routeType == "Пригородный")
            {
                frequencyPeak = (uint)random.Next(30, 61); // 30-60 minutes
                frequencyOffPeak = (uint)random.Next(60, 121); // 60-120 minutes
            }
            else
            { // Междугородний
                frequencyPeak = (uint)random.Next(60, 121); // 60-120 minutes
                frequencyOffPeak = (uint)random.Next(120, 241); // 120-240 minutes
            }

            var route = new Route
            {
                RouteId = routeId,
                RouteNumber = routeNumber,
                StartPoint = startPoint,
                EndPoint = endPoint,
                DriverId = drivers[i % drivers.Count].EmployeeId,
                BusId = buses[i % buses.Count].BusId,
                TravelTime = travelTime,
                StopCount = stopCount,
                RouteDescription = routeDescription,
                RouteLength = routeLength,
                IsActive = true,
                RouteType = routeType,
                AlternativeRoutes = selectedAlternativeRoutes,
                PeakHours = peakHours,
                FrequencyPeak = frequencyPeak,
                FrequencyOffPeak = frequencyOffPeak,
                SpecialInstructions = selectedSpecialInstructions,
                IsAccessible = isAccessible,
                RouteFeatures = selectedFeatures.ToArray(),
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = "System"
            };
            ctx.Db.Route.Insert(route);
        }

        Log.Info("Routes initialized successfully");
    }


    private static void InitializeRouteSchedules(ReducerContext ctx)
    {
        Log.Info("Starting InitializeRouteSchedules...");

        // Проверяем, существуют ли уже расписания маршрутов
        if (ctx.Db.RouteSchedule.Iter().Any())
        {
            Log.Info("Route schedules already exist, skipping initialization");
            return; // Расписания уже существуют
        }

        Log.Info("Initializing route schedules...");

        // Получаем маршруты
        Log.Debug("Fetching routes from database");
        var routes = ctx.Db.Route.Iter().ToList();
        if (routes.Count == 0)
        {
            Log.Error("Cannot initialize route schedules: No routes found");
            return;
        }
        Log.Debug($"Found {routes.Count} routes to create schedules for");

        // Получаем автобусы для получения их характеристик
        Log.Debug("Fetching buses from database");
        var buses = ctx.Db.Bus.Iter().ToDictionary(b => b.BusId);
        if (buses.Count == 0)
        {
            Log.Error("Cannot initialize route schedules: No buses found");
            return;
        }
        Log.Debug($"Found {buses.Count} buses for schedule assignment");

        // Типы автобусов
        Log.Debug("Setting up bus type arrays");
        var cityBusTypes = new[] { "МАЗ-103", "МАЗ-203.069", "МАЗ-206.068", "МАЗ-215" };
        var intercityBusTypes = new[] { "МАЗ-231", "МАЗ-251", "Неман-4202", "Yutong ZK6128BEVG" };
        var trolleybusTypes = new[] { "Белкоммунмаш BKM-321", "МАЗ-203Т" };
        var electrobusTypes = new[] { "МАЗ-303E10", "Белкоммунмаш E433 Vitovt Max Electro", "CRRC TEG6125BEV03", "BYD K9MD" };

        // Дни недели
        Log.Debug("Setting up days of week arrays");
        var weekdaysRu = new[] { "Понедельник", "Вторник", "Среда", "Четверг", "Пятница" };
        var weekendRu = new[] { "Суббота", "Воскресенье" };
        var allDaysRu = weekdaysRu.Concat(weekendRu).ToArray();

        // Типы маршрутов
        Log.Debug("Setting up route type arrays");
        var routeTypes = new[] { "Городской", "Пригородный", "Экспресс", "Обычный", "Междугородний", "Сельский", "Кольцевой", "Сезонный", "Туристический", "Школьный", "Дачный", "Ночной" };

        // Типы расписаний
        Log.Debug("Setting up schedule type arrays");
        var scheduleTypes = new[] { "Обычный", "Экспресс", "Укороченный", "Ночной", "Пиковый" };

        // Праздничные дни (месяц, день) - основные государственные праздники РБ
        Log.Debug("Setting up holiday data");
        var holidays = new List<(int Month, int Day, string Name)>
        {
            (12, 31, "Новый год"), // Changed from Jan 1 to Dec 31
            (1, 7, "Рождество Христово (православное)"),
            (3, 8, "День женщин"),
            (5, 1, "Праздник труда"),
            (5, 9, "День Победы"),
            (7, 3, "День Независимости Республики Беларусь"),
            (11, 7, "День Октябрьской революции"),
            (12, 25, "Рождество Христово (католическое)")
        };
        Log.Debug($"Set up {holidays.Count} holidays for schedule generation");

        Log.Debug("Setting up time constants");
        DateTime currentDatebeforeconv = new DateTime(2025, 4, 3);
        DateTimeOffset currentDate = new DateTimeOffset(currentDatebeforeconv);
        ulong now = (ulong)currentDate.ToUnixTimeMilliseconds();
        Log.Debug($"Current timestamp: {now} ({currentDate:yyyy-MM-dd HH:mm:ss zzz})");

        // Вспомогательные константы для времени
        ulong minutesInMs = 60 * 1000UL;
        ulong hoursInMs = 60 * minutesInMs;
        ulong daysInMs = 24 * hoursInMs;

        ulong thirtyDaysAgo = now - (30 * daysInMs);
        ulong sixtyDaysAhead = now + (60 * daysInMs);
        Log.Debug($"Valid date range: from {DateTimeOffset.FromUnixTimeMilliseconds((long)thirtyDaysAgo)} to {DateTimeOffset.FromUnixTimeMilliseconds((long)sixtyDaysAhead)}");

        Random random = new Random();
        Log.Debug("Random number generator initialized");

        int routeCounter = 0;
        int scheduleCounter = 0;

        foreach (var route in routes)
        {
            routeCounter++;
            Log.Debug($"Processing route {routeCounter}/{routes.Count}: {route.RouteNumber} from {route.StartPoint} to {route.EndPoint}");

            // Получаем данные автобуса, назначенного на этот маршрут
            Bus assignedBus = null;
            if (!buses.TryGetValue(route.BusId, out assignedBus))
            {
                Log.Warn($"Bus with ID {route.BusId} not found for route {route.RouteNumber}. Skipping schedule generation.");
                continue;
            }
            Log.Debug($"Assigned bus: {assignedBus.Model} (ID: {assignedBus.BusId})");

            // Определяем подходящие типы автобусов для этого маршрута
            string[] possibleBusTypes;
            if (route.RouteType == "Троллейбусный")
            {
                possibleBusTypes = trolleybusTypes;
                Log.Debug($"Route {route.RouteNumber} is trolleybus route, assigning trolleybus types");
                // Проверяем, может ли это быть электробус (например, 16Э)
                if (route.RouteNumber.EndsWith("Э"))
                {
                    possibleBusTypes = electrobusTypes;
                    Log.Debug($"Route {route.RouteNumber} ends with 'Э', reassigning to electrobus types");
                }
            }
            else if (route.RouteType == "Междугородний")
            {
                possibleBusTypes = intercityBusTypes;
                Log.Debug($"Route {route.RouteNumber} is intercity route, assigning intercity bus types");
            }
            else // Городской, Пригородный, Кольцевой и др.
            {
                // Могут быть и обычные и электробусы
                var cityAndElectro = cityBusTypes.Concat(electrobusTypes).Distinct().ToArray();
                // Если маршрут 16 или 16д, предпочтительно указать электробусы тоже
                if (route.RouteNumber == "16" || route.RouteNumber == "16д")
                {
                    possibleBusTypes = cityAndElectro;
                    Log.Debug($"Route {route.RouteNumber} is special route 16/16д, assigning mixed city and electrobus types");
                }
                else
                {
                    // Для остальных - преимущественно обычные городские
                    possibleBusTypes = cityBusTypes;
                    // С некоторой вероятностью добавим электробус
                    if (random.Next(0, 5) == 0)
                    {
                        possibleBusTypes = possibleBusTypes.Concat(new[] { electrobusTypes[random.Next(electrobusTypes.Length)] }).Distinct().ToArray();
                        Log.Debug($"Route {route.RouteNumber} randomly assigned an electrobus in addition to city buses");
                    }
                    else
                    {
                        Log.Debug($"Route {route.RouteNumber} is city route, assigning city bus types");
                    }
                }
            }

            // Если список пуст, используем модель назначенного автобуса
            if (possibleBusTypes == null || possibleBusTypes.Length == 0)
            {
                possibleBusTypes = new[] { assignedBus.Model };
                Log.Debug($"No bus types assigned for route {route.RouteNumber}, using assigned bus model: {assignedBus.Model}");
            }
            Log.Debug($"Possible bus types for route {route.RouteNumber}: {string.Join(", ", possibleBusTypes)}");

            // Генерируем случайные значения для дополнительных полей
            uint seatedCapacity = assignedBus.SeatedCapacity ?? (uint)random.Next(20, 45);
            uint standingCapacity = assignedBus.StandingCapacity ?? (uint)random.Next(30, 80);
            double peakHourLoad = Math.Round(random.NextDouble() * 0.4 + 0.6, 2); // 0.6-1.0 (60-100%)
            double offPeakHourLoad = Math.Round(random.NextDouble() * 0.5 + 0.1, 2); // 0.1-0.6 (10-60%)
            bool isSpecialEvent = random.Next(0, 20) == 0; // 5% шанс
            string specialEventName = isSpecialEvent ? (new[] { "День города", "Фестиваль", "Спортивное мероприятие" })[random.Next(0, 3)] : null;
            uint? seatConfigurationId = (uint)random.Next(1, 4); // 3 варианта конфигурации
            bool requiresSeatReservation = (route.RouteType == "Междугородний") && random.Next(0, 10) < 8; // 80% для междугорода

            Log.Debug($"Generated parameters for route {route.RouteNumber}: seatedCapacity={seatedCapacity}, standingCapacity={standingCapacity}, " +
                      $"peakHourLoad={peakHourLoad}, offPeakHourLoad={offPeakHourLoad}, isSpecialEvent={isSpecialEvent}, " +
                      $"specialEventName={specialEventName}, seatConfigurationId={seatConfigurationId}, requiresSeatReservation={requiresSeatReservation}");

            // Создаем утреннее расписание (6:00)
            Log.Debug($"Creating morning schedule for route {route.RouteNumber}");

            // Calculate date range
            DateTime startDate = new DateTime(2024, 1, 1); // Start from beginning of 2024
            //will it stop dying on inserts now-fuck knows
            DateTime endDate = new DateTime(2025, 12, 31); // WILL THIS MAKE THE CODE NOT KILL ITSELF?
            TimeZoneInfo localZone = TimeZoneInfo.Local;

            for (DateTime date = startDate; date <= endDate; date = date.AddDays(1))
            {
                // Create clean DateTime objects for this day
                DateTime morningTime = date.Date.AddHours(6);  // 6:00 AM
                DateTime afternoonTime = date.Date.AddHours(14); // 2:00 PM
                DateTime eveningTime = currentDate.Date.AddHours(18);  // 6:00 PM

                // Convert to DateTimeOffset with local timezone
                DateTimeOffset morningOffset = new DateTimeOffset(morningTime, localZone.GetUtcOffset(morningTime));
                DateTimeOffset afternoonOffset = new DateTimeOffset(afternoonTime, localZone.GetUtcOffset(afternoonTime));
                DateTimeOffset eveningOffset = new DateTimeOffset(eveningTime, localZone.GetUtcOffset(eveningTime));

                // Convert to Unix timestamps
                ulong morningDepartureTime = (ulong)morningOffset.ToUnixTimeMilliseconds();
                ulong afternoonDepartureTime = (ulong)afternoonOffset.ToUnixTimeMilliseconds();
                ulong eveningDepartureTime = (ulong)eveningOffset.ToUnixTimeMilliseconds();

                Log.Debug($"Generating schedules for date: {date:yyyy-MM-dd}");
                Log.Debug($"Morning departure time: {DateTimeOffset.FromUnixTimeMilliseconds((long)morningDepartureTime).ToString("yyyy-MM-dd HH:mm:ss zzz")}");

                // Рассчитываем время прибытия на основе времени в пути маршрута
                string travelTimeString = route.TravelTime ?? "30 минут"; // Запасной вариант
                int travelMinutes = 30; // Значение по умолчанию

                try
                {
                    Log.Debug($"Parsing travel time string: '{travelTimeString}'");
                    if (travelTimeString.Contains("час"))
                    {
                        double hours = 0;
                        var parts = travelTimeString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0 && double.TryParse(parts[0].Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double h))
                        {
                            hours = h;
                            Log.Debug($"Parsed hours: {hours}");
                        }
                        // Учтем "час 30 минут" или "2.5 часа"
                        if (travelTimeString.Contains("30 минут") || travelTimeString.Contains(".5"))
                        {
                            hours += 0.5;
                            Log.Debug($"Added 30 minutes, total hours: {hours}");
                        }
                        travelMinutes = (int)(hours * 60);
                        Log.Debug($"Converted to minutes: {travelMinutes}");
                    }
                    else if (travelTimeString.Contains("минут"))
                    {
                        var parts = travelTimeString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0 && int.TryParse(parts[0], out int m))
                        {
                            travelMinutes = m;
                            Log.Debug($"Parsed minutes directly: {travelMinutes}");
                        }
                    }
                    Log.Debug($"Final travel time for route {route.RouteNumber}: {travelMinutes} minutes");
                }
                catch (Exception ex)
                {
                    Log.Warn($"Failed to parse travel time '{travelTimeString}' for route {route.RouteNumber}. Using default value of 30 minutes. Error: {ex.Message}");
                    travelMinutes = 30;
                }

                ulong morningArrivalTime = morningDepartureTime + (ulong)travelMinutes * minutesInMs;
                Log.Debug($"Initial morning arrival time: {DateTimeOffset.FromUnixTimeMilliseconds((long)morningArrivalTime)}");

                // Проверка на праздник для утреннего расписания
                bool isMorningHoliday = false;
                string morningHolidayName = null;
                DateTimeOffset morningDto = DateTimeOffset.FromUnixTimeMilliseconds((long)morningDepartureTime);

                // Check if the current date matches any holiday
                Log.Debug($"Checking if {morningDto.Date} is a holiday");
                foreach (var (month, day, name) in holidays)
                {
                    try
                    {
                        // Create holiday date for the same year as morningDto
                        var holidayDate = new DateTimeOffset(morningDto.Year, month, day, 0, 0, 0, morningDto.Offset);

                        // Compare full dates (year, month, day)
                        if (morningDto.Date == holidayDate.Date)
                        {
                            isMorningHoliday = true;
                            morningHolidayName = name;
                            Log.Debug($"Morning schedule date is a holiday: {name}");
                            break;
                        }
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Skip invalid dates (e.g., Feb 29 in non-leap years)
                        Log.Debug($"Skipped invalid holiday date: {month}/{day}");
                        continue;
                    }
                }

                // Определяем, является ли день выходным
                bool isMorningWeekend = morningDto.DayOfWeek == DayOfWeek.Saturday || morningDto.DayOfWeek == DayOfWeek.Sunday;
                Log.Debug($"Is morning schedule on weekend: {isMorningWeekend}");

                // Остановки (упрощенный пример)
                Log.Debug($"Generating stops for route {route.RouteNumber}");
                var routeStops = new List<string> { route.StartPoint };
                int intermediateStopsCount = Math.Max(0, (int)route.StopCount - 2);
                Log.Debug($"Intermediate stops count: {intermediateStopsCount}");

                // Добавим несколько типовых остановок, если это городской/пригородный
                if (route.RouteType != "Междугородний" && intermediateStopsCount > 0)
                {
                    Log.Debug("Adding common stops for city/suburban route");
                    var commonStops = new[] { "Центр", "Площадь Ленина", "Рынок", "Больница", "Парк", "Школа", "Университет" };
                    for (int k = 0; k < Math.Min(intermediateStopsCount, 3); k++)
                    {
                        routeStops.Add(commonStops[random.Next(commonStops.Length)]);
                    }
                    // Уберем дубликаты
                    routeStops = routeStops.Distinct().ToList();
                    Log.Debug($"After adding common stops and removing duplicates: {routeStops.Count} stops");

                    // Добавим еще случайных, если нужно
                    int remainingStops = intermediateStopsCount - (routeStops.Count - 1);
                    Log.Debug($"Need to add {remainingStops} more random stops");
                    for (int k = 0; k < remainingStops; k++)
                    {
                        routeStops.Add($"Остановка {k + 1}");
                    }
                }
                routeStops.Add(route.EndPoint);
                routeStops = routeStops.Distinct().ToList();
                Log.Debug($"Final stops for route {route.RouteNumber}: {string.Join(" -> ", routeStops)}");

                // Примерное время по остановкам и дистанции
                Log.Debug("Calculating estimated stop times and distances for morning schedule");
                string[] morningEstimatedStopTimes = new string[routeStops.Count];
                double[] morningStopDistances = new double[routeStops.Count];
                ulong currentTime = morningDepartureTime;
                double currentDistance = 0.0;
                double totalLength = route.RouteLength;
                int avgStopTimeMinutes = (route.RouteType == "Междугородний") ? 10 : 2;
                Log.Debug($"Average stop time: {avgStopTimeMinutes} minutes");

                for (int k = 0; k < routeStops.Count; k++)
                {
                    morningEstimatedStopTimes[k] = DateTimeOffset.FromUnixTimeMilliseconds((long)currentTime).ToString("HH:mm");
                    morningStopDistances[k] = Math.Round(currentDistance, 1);
                    Log.Debug($"Stop {k + 1} ({routeStops[k]}): time={morningEstimatedStopTimes[k]}, distance={morningStopDistances[k]}km");

                    if (k < routeStops.Count - 1)
                    {
                        // Время до следующей остановки
                        double segmentDistance = (k == routeStops.Count - 2) ? (totalLength - currentDistance) : (totalLength / (routeStops.Count - 1));
                        if (segmentDistance < 0) segmentDistance = 0;
                        currentDistance += segmentDistance;

                        // Время в пути между остановками
                        double avgSpeed = (route.RouteType == "Междугородний") ? 60.0 : (route.RouteType == "Пригородный" ? 40.0 : 25.0);
                        ulong travelTimeMs = (ulong)((segmentDistance / avgSpeed) * 60.0) * minutesInMs;

                        currentTime += travelTimeMs + (ulong)avgStopTimeMinutes * minutesInMs;
                        Log.Debug($"To next stop: distance={segmentDistance}km, speed={avgSpeed}km/h, travel time={travelTimeMs / minutesInMs}min");
                    }
                }

                // Корректируем время прибытия на основе расчета по остановкам
                morningArrivalTime = currentTime;
                Log.Debug($"Adjusted morning arrival time: {DateTimeOffset.FromUnixTimeMilliseconds((long)morningArrivalTime)}");

                // Цена билета
                Log.Debug("Calculating ticket price");
                double price;
                if (route.RouteType == "Междугородний")
                {
                    price = Math.Round(5.0 + route.RouteLength / 20.0 + random.NextDouble() * 2, 2);
                    Log.Debug($"Intercity route price calculation: 5.0 + {route.RouteLength}/20.0 + random = {price}");
                }
                else if (route.RouteType == "Пригородный")
                {
                    price = Math.Round(1.0 + route.RouteLength / 30.0 + random.NextDouble() * 0.5, 2);
                    Log.Debug($"Suburban route price calculation: 1.0 + {route.RouteLength}/30.0 + random = {price}");
                }
                else // Городской, Троллейбусный, Кольцевой
                {
                    price = 0.80 + random.NextDouble() * 0.1;
                    price = Math.Round(price, 2);
                    Log.Debug($"City route price calculation: 0.80 + random = {price}");
                }
                if (price < 0.80) price = 0.80;
                Log.Debug($"Final ticket price for route {route.RouteNumber}: {price}");

                // Дни недели
                Log.Debug("Determining days of week for schedule");
                string[] daysOfWeek;
                int dayType = random.Next(0, 10);
                if (dayType < 6)
                { // 60% - все дни
                    daysOfWeek = allDaysRu;
                    Log.Debug("Schedule will run all days (60% probability)");
                }
                else if (dayType < 9)
                { // 30% - будние
                    daysOfWeek = weekdaysRu;
                    Log.Debug("Schedule will run weekdays only (30% probability)");
                }
                else
                { // 10% - выходные
                    daysOfWeek = weekendRu;
                    Log.Debug("Schedule will run weekends only (10% probability)");
                }

                uint morningScheduleId = GetNextId(ctx, "scheduleId");
                Log.Debug($"Generated morning scheduleId: {morningScheduleId}");

                var morningSchedule = new RouteSchedule
                {
                    ScheduleId = morningScheduleId,
                    RouteId = route.RouteId,
                    StartPoint = route.StartPoint,
                    EndPoint = route.EndPoint,
                    RouteStops = routeStops.ToArray(),
                    DepartureTime = morningDepartureTime,
                    ArrivalTime = morningArrivalTime,
                    Price = price,
                    AvailableSeats = seatedCapacity,
                    SeatedCapacity = seatedCapacity,
                    StandingCapacity = standingCapacity,
                    DaysOfWeek = daysOfWeek,
                    BusTypes = possibleBusTypes.OrderBy(x => random.Next()).Take(random.Next(1, Math.Min(3, possibleBusTypes.Length) + 1)).ToArray(),
                    IsActive = true,
                    ValidFrom = thirtyDaysAgo,
                    ValidUntil = sixtyDaysAhead,
                    StopDurationMinutes = (uint)random.Next(1, (route.RouteType == "Междугородний" ? 11 : 4)),
                    IsRecurring = true,
                    EstimatedStopTimes = morningEstimatedStopTimes,
                    StopDistances = morningStopDistances,
                    Notes = $"Утренний рейс {route.RouteNumber} от {route.StartPoint} до {route.EndPoint}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    UpdatedBy = "System",
                    PeakHourLoad = peakHourLoad,
                    OffPeakHourLoad = offPeakHourLoad,
                    IsSpecialEvent = isSpecialEvent,
                    SpecialEventName = specialEventName,
                    IsHoliday = isMorningHoliday,
                    HolidayName = morningHolidayName,
                    IsWeekend = isMorningWeekend,
                    SeatConfigurationId = seatConfigurationId,
                    RequiresSeatReservation = requiresSeatReservation,
                    RouteType = scheduleTypes[random.Next(0, scheduleTypes.Length)]
                };
                ctx.Db.RouteSchedule.Insert(morningSchedule);
                scheduleCounter++;
                Log.Debug($"Morning schedule created for route {route.RouteNumber}");

                // Создаем дневное расписание (14:00) - только для будних дней
                Log.Debug($"Creating afternoon schedule for route {route.RouteNumber} on {date:yyyy-MM-dd}");
                Log.Debug($"Afternoon departure time: {DateTimeOffset.FromUnixTimeMilliseconds((long)afternoonDepartureTime).ToString("yyyy-MM-dd HH:mm:ss zzz")}");
                ulong afternoonArrivalTime = afternoonDepartureTime + (ulong)travelMinutes * minutesInMs;
                Log.Debug($"Initial afternoon arrival time: {DateTimeOffset.FromUnixTimeMilliseconds((long)afternoonArrivalTime).ToString("yyyy-MM-dd HH:mm:ss zzz")}");

                // Проверка на праздник для дневного расписания
                bool isAfternoonHoliday = false;
                string afternoonHolidayName = null;
                DateTimeOffset afternoonDto = DateTimeOffset.FromUnixTimeMilliseconds((long)afternoonDepartureTime);

                // Check if the current date matches any holiday
                Log.Debug($"Checking if {afternoonDto.ToString("yyyy-MM-dd HH:mm:ss")} is a holiday");
                foreach (var (month, day, name) in holidays)
                {
                    try
                    {
                        // Create holiday date for the same year as afternoonDto
                        var holidayDate = new DateTimeOffset(afternoonDto.Year, month, day, 0, 0, 0, afternoonDto.Offset);

                        // Compare full dates (year, month, day)
                        if (afternoonDto.Date == holidayDate.Date)
                        {
                            isAfternoonHoliday = true;
                            afternoonHolidayName = name;
                            Log.Debug($"Afternoon schedule date is a holiday: {name}");
                            break;
                        }
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Skip invalid dates
                        Log.Debug($"Skipped invalid holiday date: {month}/{day}");
                        continue;
                    }
                }

                // Определяем, является ли день выходным
                bool isAfternoonWeekend = afternoonDto.DayOfWeek == DayOfWeek.Saturday || afternoonDto.DayOfWeek == DayOfWeek.Sunday;
                Log.Debug($"Is afternoon schedule on weekend: {isAfternoonWeekend}");

                // Примерное время по остановкам и дистанции для дневного расписания
                Log.Debug("Calculating estimated stop times and distances for afternoon schedule");
                string[] afternoonEstimatedStopTimes = new string[routeStops.Count];
                double[] afternoonStopDistances = new double[routeStops.Count];
                currentTime = afternoonDepartureTime;
                currentDistance = 0.0;

                for (int k = 0; k < routeStops.Count; k++)
                {
                    afternoonEstimatedStopTimes[k] = DateTimeOffset.FromUnixTimeMilliseconds((long)currentTime).ToString("HH:mm");
                    afternoonStopDistances[k] = Math.Round(currentDistance, 1);
                    Log.Debug($"Stop {k + 1} ({routeStops[k]}): time={afternoonEstimatedStopTimes[k]}, distance={afternoonStopDistances[k]}km");

                    if (k < routeStops.Count - 1)
                    {
                        double segmentDistance = (k == routeStops.Count - 2) ? (totalLength - currentDistance) : (totalLength / (routeStops.Count - 1));
                        if (segmentDistance < 0) segmentDistance = 0;
                        currentDistance += segmentDistance;

                        double avgSpeed = (route.RouteType == "Междугородний") ? 60.0 : (route.RouteType == "Пригородный" ? 40.0 : 25.0);
                        ulong travelTimeMs = (ulong)((segmentDistance / avgSpeed) * 60.0) * minutesInMs;

                        currentTime += travelTimeMs + (ulong)avgStopTimeMinutes * minutesInMs;
                        Log.Debug($"To next stop: distance={segmentDistance}km, speed={avgSpeed}km/h, travel time={travelTimeMs / minutesInMs}min");
                    }
                }

                // Корректируем время прибытия на основе расчета по остановкам
                afternoonArrivalTime = currentTime;
                Log.Debug($"Adjusted afternoon arrival time: {DateTimeOffset.FromUnixTimeMilliseconds((long)afternoonArrivalTime).ToString("yyyy-MM-dd HH:mm:ss zzz")}");

                uint afternoonScheduleId = GetNextId(ctx, "scheduleId");
                Log.Debug($"Generated afternoon scheduleId: {afternoonScheduleId}");

                var afternoonSchedule = new RouteSchedule
                {
                    ScheduleId = afternoonScheduleId,
                    RouteId = route.RouteId,
                    StartPoint = route.StartPoint,
                    EndPoint = route.EndPoint,
                    RouteStops = routeStops.ToArray(),
                    DepartureTime = afternoonDepartureTime,
                    ArrivalTime = afternoonArrivalTime,
                    Price = price,
                    AvailableSeats = seatedCapacity,
                    SeatedCapacity = seatedCapacity,
                    StandingCapacity = standingCapacity,
                    DaysOfWeek = weekdaysRu, // Только будние дни
                    BusTypes = possibleBusTypes.OrderBy(x => random.Next()).Take(random.Next(1, Math.Min(3, possibleBusTypes.Length) + 1)).ToArray(),
                    IsActive = true,
                    ValidFrom = thirtyDaysAgo,
                    ValidUntil = sixtyDaysAhead,
                    StopDurationMinutes = (uint)random.Next(1, (route.RouteType == "Междугородний" ? 11 : 4)),
                    IsRecurring = true,
                    EstimatedStopTimes = afternoonEstimatedStopTimes,
                    StopDistances = afternoonStopDistances,
                    Notes = $"Дневной рейс {route.RouteNumber} от {route.StartPoint} до {route.EndPoint}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    UpdatedBy = "System",
                    PeakHourLoad = peakHourLoad,
                    OffPeakHourLoad = offPeakHourLoad,
                    IsSpecialEvent = isSpecialEvent,
                    SpecialEventName = specialEventName,
                    IsHoliday = isAfternoonHoliday,
                    HolidayName = afternoonHolidayName,
                    IsWeekend = isAfternoonWeekend,
                    SeatConfigurationId = seatConfigurationId,
                    RequiresSeatReservation = requiresSeatReservation,
                    RouteType = scheduleTypes[random.Next(0, scheduleTypes.Length)]
                };
                ctx.Db.RouteSchedule.Insert(afternoonSchedule);

                // Создаем вечернее расписание (18:00)
                Log.Debug($"Evening departure time: {DateTimeOffset.FromUnixTimeMilliseconds((long)eveningDepartureTime).ToString("yyyy-MM-dd HH:mm:ss zzz")}");
                ulong eveningArrivalTime = eveningDepartureTime + (ulong)travelMinutes * minutesInMs;
                Log.Debug($"Initial evening arrival time: {DateTimeOffset.FromUnixTimeMilliseconds((long)eveningArrivalTime).ToString("yyyy-MM-dd HH:mm:ss zzz")}");

                // Проверка на праздник для вечернего расписания
                bool isEveningHoliday = false;
                string eveningHolidayName = null;
                DateTimeOffset eveningDto = DateTimeOffset.FromUnixTimeMilliseconds((long)eveningDepartureTime);

                // Check if the current date matches any holiday
                Log.Debug($"Checking if {eveningDto.ToString("yyyy-MM-dd HH:mm:ss")} is a holiday");
                foreach (var (month, day, name) in holidays)
                {
                    try
                    {
                        // Create holiday date for the same year as eveningDto
                        var holidayDate = new DateTimeOffset(eveningDto.Year, month, day, 0, 0, 0, eveningDto.Offset);

                        // Compare full dates (year, month, day)
                        if (eveningDto.Date == holidayDate.Date)
                        {
                            isEveningHoliday = true;
                            eveningHolidayName = name;
                            break;
                        }
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Skip invalid dates
                        continue;
                    }
                }

                // Определяем, является ли день выходным
                bool isEveningWeekend = eveningDto.DayOfWeek == DayOfWeek.Saturday || eveningDto.DayOfWeek == DayOfWeek.Sunday;

                // Примерное время по остановкам и дистанции для вечернего расписания
                string[] eveningEstimatedStopTimes = new string[routeStops.Count];
                double[] eveningStopDistances = new double[routeStops.Count];
                currentTime = eveningDepartureTime;
                currentDistance = 0.0;

                for (int k = 0; k < routeStops.Count; k++)
                {
                    eveningEstimatedStopTimes[k] = DateTimeOffset.FromUnixTimeMilliseconds((long)currentTime).ToString("HH:mm");
                    eveningStopDistances[k] = Math.Round(currentDistance, 1);

                    if (k < routeStops.Count - 1)
                    {
                        double segmentDistance = (k == routeStops.Count - 2) ? (totalLength - currentDistance) : (totalLength / (routeStops.Count - 1));
                        if (segmentDistance < 0) segmentDistance = 0;
                        currentDistance += segmentDistance;

                        double avgSpeed = (route.RouteType == "Междугородний") ? 60.0 : (route.RouteType == "Пригородный" ? 40.0 : 25.0);
                        ulong travelTimeMs = (ulong)((segmentDistance / avgSpeed) * 60.0) * minutesInMs;

                        currentTime += travelTimeMs + (ulong)avgStopTimeMinutes * minutesInMs;
                    }
                }

                // Корректируем время прибытия на основе расчета по остановкам
                eveningArrivalTime = currentTime;
                Log.Debug($"Adjusted evening arrival time: {DateTimeOffset.FromUnixTimeMilliseconds((long)eveningArrivalTime).ToString("yyyy-MM-dd HH:mm:ss zzz")}");

                uint eveningScheduleId = GetNextId(ctx, "scheduleId");
                var eveningSchedule = new RouteSchedule
                {
                    ScheduleId = eveningScheduleId,
                    RouteId = route.RouteId,
                    StartPoint = route.StartPoint,
                    EndPoint = route.EndPoint,
                    RouteStops = routeStops.ToArray(),
                    DepartureTime = eveningDepartureTime,
                    ArrivalTime = eveningArrivalTime,
                    Price = price,
                    AvailableSeats = seatedCapacity,
                    SeatedCapacity = seatedCapacity,
                    StandingCapacity = standingCapacity,
                    DaysOfWeek = allDaysRu, // Все дни
                    BusTypes = possibleBusTypes.OrderBy(x => random.Next()).Take(random.Next(1, Math.Min(3, possibleBusTypes.Length) + 1)).ToArray(),
                    IsActive = true,
                    ValidFrom = thirtyDaysAgo,
                    ValidUntil = sixtyDaysAhead,
                    StopDurationMinutes = (uint)random.Next(1, (route.RouteType == "Междугородний" ? 11 : 4)),
                    IsRecurring = true,
                    EstimatedStopTimes = eveningEstimatedStopTimes,
                    StopDistances = eveningStopDistances,
                    Notes = $"Вечерний рейс {route.RouteNumber} от {route.StartPoint} до {route.EndPoint}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    UpdatedBy = "System",
                    PeakHourLoad = peakHourLoad,
                    OffPeakHourLoad = offPeakHourLoad,
                    IsSpecialEvent = isSpecialEvent,
                    SpecialEventName = specialEventName,
                    IsHoliday = isEveningHoliday,
                    HolidayName = eveningHolidayName,
                    IsWeekend = isEveningWeekend,
                    SeatConfigurationId = seatConfigurationId,
                    RequiresSeatReservation = requiresSeatReservation,
                    RouteType = scheduleTypes[random.Next(0, scheduleTypes.Length)]
                };
                ctx.Db.RouteSchedule.Insert(eveningSchedule);
            }
        }

        Log.Info("Route schedules initialized successfully");
    }

    private static void InitializeBuses(ReducerContext ctx)
    {
        // Check if buses already exist
        if (ctx.Db.Bus.Iter().Any())
        {
            return; // Buses already exist
        }

        Log.Info("Initializing buses...");

        // Определение моделей автобусов и их характеристик (модель, год, тип автобуса, вместимость, сидячих мест, стоячих мест, тип топлива, расход топлива)
        var busData = new[]
        {
            // Современные белорусские автобусы
            ("МАЗ-303E10", 2022, "Электробус", 82u, 30u, 52u, "Электричество", 0.0),
            ("МАЗ-215", 2020, "Сочлененный", 170u, 45u, 125u, "Дизель", 30.0),
            ("МАЗ-203.069", 2018, "Городской", 100u, 25u, 75u, "Дизель", 26.0),
            ("МАЗ-206.068", 2019, "Городской", 72u, 22u, 50u, "Дизель", 22.0),
            
            // Электробусы Belkommunmash
            ("Белкоммунмаш E433 Vitovt Max Electro", 2021, "Электробус", 153u, 38u, 115u, "Электричество", 0.0),
            ("Белкоммунмаш BKM-321", 2019, "Троллейбус", 115u, 30u, 85u, "Электричество", 0.0),
            
            // Китайские автобусы CRRC
            ("CRRC TEG6125BEV03", 2023, "Электробус", 95u, 35u, 60u, "Электричество", 0.0),
            ("CRRC C12AI", 2022, "Электробус", 90u, 32u, 58u, "Электричество", 0.0),
            ("CRRC C08", 2021, "Электробус", 70u, 25u, 45u, "Электричество", 0.0),
            
            // Китайские автобусы BYD
            ("BYD K9MD", 2023, "Электробус", 85u, 42u, 43u, "Электричество", 0.0),
            ("BYD K11M", 2022, "Сочлененный электробус", 102u, 47u, 55u, "Электричество", 0.0),
            ("BYD K7MER", 2021, "Электробус", 65u, 20u, 45u, "Электричество", 0.0),
            
            // Китайские автобусы Yutong
            ("Yutong ZK6128BEVG", 2022, "Электробус", 100u, 36u, 64u, "Электричество", 0.0),
            ("Yutong ZK6180BEVG1", 2021, "Сочлененный электробус", 160u, 50u, 110u, "Электричество", 0.0)
        };

        // Generate random VIN pattern for Soviet-era buses
        Random random = new Random();

        foreach (var (model, year, busType, capacity, seatedCapacity, standingCapacity, fuelType, fuelConsumption) in busData)
        {
            uint busId = GetNextId(ctx, "busId");

            // Generate a simple VIN for old Soviet buses
            string vin = $"MAZ{year}{random.Next(10000, 99999)}";

            // Generate random mileage for old buses (high values)
            uint mileageTotal = (uint)random.Next(500000, 1500000);
            uint mileageSinceService = (uint)random.Next(5000, 20000);

            // Current status - randomly assign some to maintenance
            string currentStatus = random.Next(0, 10) < 8 ? "In Service" : "Maintenance";

            var bus = new Bus
            {
                BusId = busId,
                Model = model,
                RegistrationNumber = $"AB {busId + 1000} 7",
                IsActive = true,
                BusType = busType,
                Capacity = capacity,
                SeatedCapacity = seatedCapacity,
                StandingCapacity = standingCapacity,
                Year = (uint)year,
                VIN = vin,
                LicensePlate = $"AB {busId + 1000} 7", // Same as registration for simplicity
                CurrentStatus = currentStatus,
                CurrentLocation = null, // No GPS tracking in Soviet-era buses
                LastLocationUpdate = null,
                FuelConsumption = fuelConsumption,
                CurrentFuelLevel = random.Next(10, 100),
                FuelType = fuelType,
                MileageTotal = mileageTotal,
                MileageSinceService = mileageSinceService,
                HasAccessibility = false, // No accessibility features in old Soviet buses
                HasAirConditioning = false, // No AC in old Soviet buses
                HasWifi = false, // No WiFi in old Soviet buses
                HasUSBCharging = false // No USB charging in old Soviet buses
            };

            ctx.Db.Bus.Insert(bus);
        }

        Log.Info("Buses initialized successfully");
    }



    private static void InitializeTickets(ReducerContext ctx)
    {
        // Check if tickets already exist
        if (ctx.Db.Ticket.Iter().Any())
        {
            return; // Tickets already exist
        }

        Log.Info("Initializing tickets...");

        // Get routes
        var routes = ctx.Db.Route.Iter().ToList();
        if (routes.Count == 0)
        {
            Log.Error("Cannot initialize tickets: No routes found");
            return;
        }

        // Current timestamp in milliseconds
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Create tickets for each route with realistic prices
        foreach (var route in routes)
        {
            uint ticketId = GetNextId(ctx, "ticketId");

            var ticket = new Ticket
            {
                TicketId = ticketId,
                RouteId = route.RouteId,
                TicketPrice = 0.75 + (route.RouteId % 3) * 0.10, // Prices between 0.75 and 0.95
                SeatNumber = 1, // Default seat number
                PaymentMethod = "cash", // Default payment method
                IsActive = true, // Set ticket as active
                CreatedAt = now,
                UpdatedAt = null,
                UpdatedBy = null,
                PurchaseTime = now // Set purchase time to current time
            };
            ctx.Db.Ticket.Insert(ticket);
        }

        Log.Info("Tickets initialized successfully");
    }

    private static void InitializeMaintenance(ReducerContext ctx)
    {
        // Check if maintenance records already exist
        if (ctx.Db.Maintenance.Iter().Any())
        {
            return; // Maintenance records already exist
        }

        Log.Info("Initializing maintenance records...");

        // Get buses
        var buses = ctx.Db.Bus.Iter().ToList();
        if (buses.Count == 0)
        {
            Log.Error("Cannot initialize maintenance: No buses found");
            return;
        }

        var maintenanceTypes = new[]
        {
            // Общее обслуживание для всех типов
            ("Замена масла, фильтров", "Закончилось масло, грязные фильтры", "Исправен"),
            ("Регулировка тормозов", "Тормоза", "Исправен"),
            ("Замена тормозных колодок", "Тормозные колодки", "Исправен"),
            ("Диагностика двигателя", "Диагностика двигателя", "Требует внимания"),
            ("Плановый осмотр", "Плановый осмотр", "Исправен"),
            ("Замена ремня ГРМ", "Ремень ГРМ", "Исправен"),
            ("Ремонт системы охлаждения", "Ремонт системы охлаждения", "Исправен"),
            ("Замена аккумулятора", "Аккумулятор", "Исправен"),
            ("Диагностика электрики", "Диагностика электрики", "Требует внимания"),
            ("Плановое ТО", "Плановое ТО", "Исправен"),
            
            // Специфичное для троллейбусов
            ("Проверка токоприемников", "Износ контактных вставок", "Требует замены"),
            ("Обслуживание тяговых двигателей", "Плановая проверка", "Исправен"),
            ("Замена графитовых вставок", "Сильный износ", "Исправен после замены"),
            ("Проверка изоляции высоковольтных цепей", "Плановая проверка", "Исправен"),
            ("Обслуживание компрессора", "Утечка воздуха", "Исправен после ремонта"),
            
            // Специфичное для электробусов
            ("Диагностика батарейного блока", "Проверка емкости", "Исправен"),
            ("Калибровка системы управления батареями", "Плановая калибровка", "Исправен"),
            ("Проверка зарядного устройства", "Нестабильная зарядка", "Требует настройки"),
            ("Обслуживание инвертора", "Плановое обслуживание", "Исправен"),
            ("Диагностика системы рекуперации", "Снижение эффективности", "Требует настройки"),
            ("Проверка высоковольтных разъемов", "Окисление контактов", "Исправен после очистки"),
            ("Обновление программного обеспечения BMS", "Плановое обновление", "Исправен")
        };

        var engineers = new[] { "Сидоров А.М.", "Соловьев А.И." };

        DateTime cleanDateTime = new DateTime(2025, 4, 3);
        // Get the local timezone
        TimeZoneInfo localZone = TimeZoneInfo.Local;
        Log.Debug($"Local timezone: {localZone.DisplayName}");
        // Convert to DateTimeOffset with proper timezone
        DateTimeOffset dateTimeOffset = new DateTimeOffset(cleanDateTime, localZone.GetUtcOffset(cleanDateTime));
        ulong now = (ulong)dateTimeOffset.ToUnixTimeMilliseconds();

        for (int i = 0; i < buses.Count; i++)
        {
            uint maintenanceId = GetNextId(ctx, "maintenanceId");
            var (maintenanceType, foundIssues, roadworthiness) = maintenanceTypes[i % maintenanceTypes.Length];

            // Random dates within the last 2 months
            var daysAgo = (i * 5) % 60;
            // Convert current timestamp to DateTimeOffset
            DateTimeOffset currentDateOffset = DateTimeOffset.FromUnixTimeMilliseconds((long)now);
            // Convert to clean DateTime for manipulation
            DateTime currentDateTime = currentDateOffset.DateTime;
            // Calculate dates using DateTime operations
            DateTime lastServiceDateTime = currentDateTime.AddDays(-daysAgo);
            DateTime nextServiceDateTime = currentDateTime.AddDays(90 - daysAgo);
            // Convert back to DateTimeOffset with proper timezone
            DateTimeOffset lastServiceDateOffset = new DateTimeOffset(lastServiceDateTime, localZone.GetUtcOffset(lastServiceDateTime));
            DateTimeOffset nextServiceDateOffset = new DateTimeOffset(nextServiceDateTime, localZone.GetUtcOffset(nextServiceDateTime));
            // Convert to Unix timestamps
            ulong lastServiceDate = (ulong)lastServiceDateOffset.ToUnixTimeMilliseconds();
            ulong nextServiceDate = (ulong)nextServiceDateOffset.ToUnixTimeMilliseconds();

            var maintenance = new Maintenance
            {
                MaintenanceId = maintenanceId,
                BusId = buses[i].BusId,
                LastServiceDate = lastServiceDate,
                NextServiceDate = nextServiceDate,
                ServiceEngineer = engineers[i % engineers.Length],
                FoundIssues = foundIssues,
                Roadworthiness = roadworthiness,
                MaintenanceType = maintenanceType,
                MileageThreshold = "100000 km"
            };
            ctx.Db.Maintenance.Insert(maintenance);
        }

        Log.Info("Maintenance records initialized successfully");
    }



    private static void InitializeSales(ReducerContext ctx)
    {
        // Check if sales already exist
        if (ctx.Db.Sale.Iter().Any())
        {
            return; // Sales already exist
        }

        Log.Info("Initializing sales...");

        // Get tickets
        var tickets = ctx.Db.Ticket.Iter().ToList();
        if (tickets.Count == 0)
        {
            Log.Error("Cannot initialize sales: No tickets found");
            return;
        }

        // Get admin user
        var adminUser = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == "admin");
        var guestUser = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == "guest");

        DateTime cleanDateTime = new DateTime(2025, 4, 3);
        // Get the local timezone
        TimeZoneInfo localZone = TimeZoneInfo.Local;
        Log.Debug($"Local timezone: {localZone.DisplayName}");
        // Convert to DateTimeOffset with proper timezone
        DateTimeOffset dateTimeOffset = new DateTimeOffset(cleanDateTime, localZone.GetUtcOffset(cleanDateTime));
        ulong now = (ulong)dateTimeOffset.ToUnixTimeMilliseconds();

        // Calculate time constants safely
        ulong hoursInMs = 60 * 60 * 1000UL;
        ulong daysInMs = 24 * hoursInMs;
        ulong monthInMs = 30 * daysInMs;

        // Create historical sales (last 6 months)
        for (int month = 6; month >= 0; month--)
        {
            for (int day = 1; day <= 5; day++)
            {
                // Skip some days randomly
                if (day % 3 == 0 && month % 2 == 0) continue;

                for (int i = 0; i < 3; i++)
                {
                    uint saleId = GetNextId(ctx, "saleId");
                    var ticketIndex = (month * day + i) % tickets.Count;

                    // Calculate sale date based on months and days ago
                    var daysAgo = month * 30 + day + (i % 5);
                    // Convert current timestamp to DateTimeOffset
                    DateTimeOffset currentDateOffset = DateTimeOffset.FromUnixTimeMilliseconds((long)now);
                    // Convert to clean DateTime for manipulation
                    DateTime currentDateTime = currentDateOffset.DateTime;
                    // Calculate sale date using DateTime operations
                    DateTime saleDateDateTime = currentDateTime.AddDays(-daysAgo);
                    // Convert back to DateTimeOffset with proper timezone
                    DateTimeOffset saleDateOffset = new DateTimeOffset(saleDateDateTime, localZone.GetUtcOffset(saleDateDateTime));
                    // Convert to Unix timestamp
                    ulong saleDate = (ulong)saleDateOffset.ToUnixTimeMilliseconds();
                    var sale = new Sale
                    {
                        SaleId = saleId,
                        TicketId = tickets[ticketIndex].TicketId,
                        SaleDate = saleDate,
                        TicketSoldToUser = "Физическая продажа",
                        TicketSoldToUserPhone = "", // No phone number for physical sales
                        SellerId = (month < 1 && i % 2 == 0) ? adminUser?.UserId : null, // Recent sales by admin
                        SaleLocation = "В автобусе", // Sale made inside the bus
                        SaleNotes = "Продажа билета физически"
                    };
                    ctx.Db.Sale.Insert(sale);
                }
            }
        }

        // Create recent sales (last few days) with admin and guest users
        if (adminUser != null && guestUser != null)
        {
            for (int day = 5; day >= 0; day--)
            {
                uint saleId = GetNextId(ctx, "saleId");
                var ticketIndex = day % tickets.Count;

                // Calculate sale date (X days ago)
                ulong saleDate = now - ((ulong)day * daysInMs);

                var sale = new Sale
                {
                    SaleId = saleId,
                    TicketId = tickets[ticketIndex].TicketId,
                    SaleDate = saleDate,
                    TicketSoldToUser = day % 2 == 0 ? "admin" : "guest",
                    TicketSoldToUserPhone = "+375291234567",
                    SellerId = day % 2 == 0 ? adminUser.UserId : guestUser.UserId,
                    SaleLocation = "Онлайн", // Online sale
                    SaleNotes = "Продажа билета онлайн"
                };
                ctx.Db.Sale.Insert(sale);
            }
        }

        Log.Info("Sales initialized successfully");
    }

    /// <summary>
    /// Safely converts a DateTime to Unix timestamp in milliseconds, avoiding 1970 bugs.
    /// </summary>
    /// <param name="dateTime">The DateTime to convert (defaults to 2025-04-03 if not provided)</param>
    /// <returns>Unix timestamp in milliseconds as ulong</returns>
    public static ulong GetSafeUnixTimestamp(DateTime? dateTime = null)
    {
        Log.Debug("GetSafeUnixTimestamp: Starting conversion");
        
        // Use provided date or default to a safe future date
        DateTime cleanDateTime = dateTime ?? new DateTime(2025, 4, 3);
        Log.Debug($"GetSafeUnixTimestamp: Using clean DateTime: {cleanDateTime:yyyy-MM-dd HH:mm:ss}");
        
        // Get the local timezone
        TimeZoneInfo localZone = TimeZoneInfo.Local;
        Log.Debug($"GetSafeUnixTimestamp: Local timezone: {localZone.DisplayName}");
        
        // Convert to DateTimeOffset with proper timezone
        DateTimeOffset dateTimeOffset = new DateTimeOffset(cleanDateTime, localZone.GetUtcOffset(cleanDateTime));
        Log.Debug($"GetSafeUnixTimestamp: Converted to DateTimeOffset: {dateTimeOffset:yyyy-MM-dd HH:mm:ss zzz}");
        
        // Convert to Unix timestamp (milliseconds)
        ulong unixTimestamp = (ulong)dateTimeOffset.ToUnixTimeMilliseconds();
        Log.Debug($"GetSafeUnixTimestamp: Final Unix timestamp: {unixTimestamp}");
        Log.Debug($"GetSafeUnixTimestamp: Verification - converting back: {DateTimeOffset.FromUnixTimeMilliseconds((long)unixTimestamp):yyyy-MM-dd HH:mm:ss zzz}");
        
        return unixTimestamp;
    }


}