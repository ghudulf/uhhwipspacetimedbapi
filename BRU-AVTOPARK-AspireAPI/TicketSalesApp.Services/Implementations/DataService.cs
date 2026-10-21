using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class DataService : IDataService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<DataService> _logger;

        public DataService(ISpacetimeDBService spacetimeService, ILogger<DataService> logger)
        {
            _spacetimeService = spacetimeService;
            _logger = logger;
        }

        public async Task<T?> GetAsync<T>(uint id) where T : class
        {
            try
            {
                return await _spacetimeService.ReadAsync(conn =>
                {
                
                // Handle different entity types
                if (typeof(T) == typeof(UserProfile))
                {
                    var userProfile = conn.Db.UserProfile.Iter()
                        .FirstOrDefault(u => u.LegacyUserId == id);
                    
                    if (userProfile == null)
                        return null;
                    
                    return userProfile as T;
                }
                else if (typeof(T) == typeof(Role))
                {
                    var role = conn.Db.Role.Iter()
                        .FirstOrDefault(r => r.LegacyRoleId == (int)id);
                    
                    if (role == null)
                        return null;
                    
                    return role as T;
                }
                else if (typeof(T) == typeof(Permission))
                {
                    var permission = conn.Db.Permission.PermissionId.Find(id);
                    return permission as T;
                }
                else if (typeof(T) == typeof(Bus))
                {
                    var bus = conn.Db.Bus.BusId.Find(id);
                    return bus as T;
                }
                else if (typeof(T) == typeof(Route))
                {
                    var route = conn.Db.Route.RouteId.Find(id);
                    return route as T;
                }
                else if (typeof(T) == typeof(Ticket))
                {
                    var ticket = conn.Db.Ticket.TicketId.Find(id);
                    return ticket as T;
                }
                else if (typeof(T) == typeof(Sale))
                {
                    var sale = conn.Db.Sale.SaleId.Find(id);
                    return sale as T;
                }
                
                _logger.LogWarning("Unsupported entity type: {Type}", typeof(T).Name);
                return null;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity of type {Type} with ID {Id}", typeof(T).Name, id);
                throw;
            }
        }

        public async Task<List<T>> GetAllAsync<T>() where T : class
        {
            try
            {
                return await _spacetimeService.ReadAsync(conn =>
                {
                
                // Handle different entity types
                if (typeof(T) == typeof(UserProfile))
                {
                    var userProfiles = conn.Db.UserProfile.Iter().ToList();
                    return userProfiles.Cast<T>().ToList();
                }
                else if (typeof(T) == typeof(Role))
                {
                    var roles = conn.Db.Role.Iter().ToList();
                    return roles.Cast<T>().ToList();
                }
                else if (typeof(T) == typeof(Permission))
                {
                    var permissions = conn.Db.Permission.Iter().ToList();
                    return permissions.Cast<T>().ToList();
                }
                else if (typeof(T) == typeof(Bus))
                {
                    var buses = conn.Db.Bus.Iter().ToList();
                    return buses.Cast<T>().ToList();
                }
                else if (typeof(T) == typeof(Route))
                {
                    var routes = conn.Db.Route.Iter().ToList();
                    return routes.Cast<T>().ToList();
                }
                else if (typeof(T) == typeof(Ticket))
                {
                    var tickets = conn.Db.Ticket.Iter().ToList();
                    return tickets.Cast<T>().ToList();
                }
                else if (typeof(T) == typeof(Sale))
                {
                    var sales = conn.Db.Sale.Iter().ToList();
                    return sales.Cast<T>().ToList();
                }
                
                _logger.LogWarning("Unsupported entity type: {Type}", typeof(T).Name);
                return new List<T>();
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all entities of type {Type}", typeof(T).Name);
                throw;
            }
        }

        public async Task<T?> AddAsync<T>(T entity) where T : class
        {
            try
            {
                if (typeof(T) == typeof(UserProfile))
                    throw new InvalidOperationException("User imports require an explicit password through the normal RegisterUser account-registration path. Profile data is not a credential.");
                
                // Handle different entity types
                if (typeof(T) == typeof(Role))
                {
                    var role = entity as Role;
                    if (role == null)
                        throw new ArgumentException("Invalid entity type");

                    // Call the CreateRole reducer
                    var newRole = await _spacetimeService.ExecuteReducerAsync(
                        new Reducer.CreateRoleReducer(role.LegacyRoleId, role.Name, role.Description, role.IsSystem, role.Priority, null),
                        c => c.Db.Role.Iter().Single(r => r.Name == role.Name));
                    
                    return newRole as T;
                }
                else if (typeof(T) == typeof(Permission))
                {
                    var permission = entity as Permission;
                    if (permission == null)
                        throw new ArgumentException("Invalid entity type");
                    
                    // Call the AddNewPermission reducer
                    var newPermission = await _spacetimeService.ExecuteReducerAsync(
                        new Reducer.AddNewPermission(permission.Name, permission.Description, permission.Category, null),
                        c => c.Db.Permission.Iter().Single(p => p.Name == permission.Name));
                    
                    return newPermission as T;
                }
                else if (typeof(T) == typeof(Bus))
                {
                    var bus = entity as Bus;
                    if (bus == null)
                        throw new ArgumentException("Invalid entity type");
                    
                    // Call the CreateBus reducer
                    var newBus = await _spacetimeService.ExecuteReducerAsync(new Reducer.CreateBus(bus.Model, bus.RegistrationNumber, null),
                        c => c.Db.Bus.Iter().Single(b => b.RegistrationNumber == bus.RegistrationNumber));
                    
                    return newBus as T;
                }
                
                _logger.LogWarning("Unsupported entity type for adding: {Type}", typeof(T).Name);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding entity of type {Type}", typeof(T).Name);
                throw;
            }
        }

        public async Task<T?> UpdateAsync<T>(T entity) where T : class
        {
            try
            {
                
                // Handle different entity types
                if (typeof(T) == typeof(UserProfile))
                {
                    var user = entity as UserProfile;
                    if (user == null)
                        throw new ArgumentException("Invalid entity type");
                    
                    // Call the UpdateUser reducer
                    await _spacetimeService.ExecuteReducerAsync(new Reducer.UpdateUser(
                        user.UserId,
                        user.Login,
                        null, // Profile imports never reset credentials.
                        null, // role
                        user.PhoneNumber,
                        user.Email,
                        user.IsActive,
                        null  // actingUser
                    ), _ => true);
                    
                    // Return the updated user
                    return await GetAsync<T>(user.LegacyUserId);
                }
                else if (typeof(T) == typeof(Role))
                {
                    var role = entity as Role;
                    if (role == null)
                        throw new ArgumentException("Invalid entity type");

                    // Call the UpdateRole reducer
                    await _spacetimeService.ExecuteReducerAsync(new Reducer.UpdateRoleReducer(role.RoleId, role.Name, role.Description, role.LegacyRoleId, role.Priority, null), _ => true);
                    
                    // Return the updated role
                    return await GetAsync<T>((uint)role.LegacyRoleId);
                }
                else if (typeof(T) == typeof(Permission))
                {
                    var permission = entity as Permission;
                    if (permission == null)
                        throw new ArgumentException("Invalid entity type");
                    
                    // Call the UpdatePermission reducer
                    await _spacetimeService.ExecuteReducerAsync(new Reducer.UpdatePermission(permission.PermissionId, permission.Name, permission.Description, permission.Category, permission.IsActive, null), _ => true);
                    
                    // Return the updated permission
                    return await GetAsync<T>(permission.PermissionId);
                }
                else if (typeof(T) == typeof(Bus))
                {
                    var bus = entity as Bus;
                    if (bus == null)
                        throw new ArgumentException("Invalid entity type");
                    
                    // Call the UpdateBus reducer
                    await _spacetimeService.ExecuteReducerAsync(new Reducer.UpdateBus(bus.BusId, bus.Model, bus.RegistrationNumber, null), _ => true);
                    
                    // Return the updated bus
                    return await GetAsync<T>(bus.BusId);
                }
                
                _logger.LogWarning("Unsupported entity type for updating: {Type}", typeof(T).Name);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating entity of type {Type}", typeof(T).Name);
                throw;
            }
        }

        public async Task<bool> DeleteAsync<T>(uint id) where T : class
        {
            try
            {
                
                // Handle different entity types
                if (typeof(T) == typeof(UserProfile))
                {
                    // Find user by legacy ID
                    var userProfile = await _spacetimeService.ReadAsync(c => c.Db.UserProfile.Iter()
                        .FirstOrDefault(u => u.LegacyUserId == id));
                    
                    if (userProfile == null)
                        return false;
                    
                    // Call the DeleteUser reducer
                    await _spacetimeService.ExecuteReducerAsync(new Reducer.DeleteUser(userProfile.UserId, null), _ => true);
                    return true;
                }
                else if (typeof(T) == typeof(Role))
                {
                    // Find role by ID
                    var role = await _spacetimeService.ReadAsync(c => c.Db.Role.RoleId.Find(id));
                    if (role == null)
                        return false;
                    
                    // Call the DeleteRole reducer
                    await _spacetimeService.ExecuteReducerAsync(new Reducer.DeleteRole(role.RoleId, null), _ => true);
                    return true;
                }
                else if (typeof(T) == typeof(Permission))
                {
                    // Find permission by ID
                    var permission = await _spacetimeService.ReadAsync(c => c.Db.Permission.PermissionId.Find(id));
                    if (permission == null)
                        return false;
                    
                    // Call the DeletePermission reducer
                    await _spacetimeService.ExecuteReducerAsync(new Reducer.DeletePermission(permission.PermissionId, null), _ => true);
                    return true;
                }
                else if (typeof(T) == typeof(Bus))
                {
                    // Find bus by ID
                    var bus = await _spacetimeService.ReadAsync(c => c.Db.Bus.BusId.Find(id));
                    if (bus == null)
                        return false;
                    
                    // Call the DeleteBus reducer
                    await _spacetimeService.ExecuteReducerAsync(new Reducer.DeleteBus(bus.BusId, null), _ => true);
                    return true;
                }
                
                _logger.LogWarning("Unsupported entity type for deletion: {Type}", typeof(T).Name);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting entity of type {Type} with ID {Id}", typeof(T).Name, id);
                throw;
            }
        }
    }
}
