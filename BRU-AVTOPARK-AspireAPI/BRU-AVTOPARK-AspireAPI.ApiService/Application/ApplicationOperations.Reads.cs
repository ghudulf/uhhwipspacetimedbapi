using BRU.Avtopark.Contracts;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Application;

public sealed partial class ApplicationOperations
{
    private void RegisterCatalogReads(ISpacetimeDBService database)
    {
        RegisterEntityRead(database, "routes", "routes.view", (c, id, access) =>
        {
            var r = c.Db.Route.RouteId.Find(id) ?? throw new OperationException("not_found");
            return ProjectRoute(c, r, access, access.Allows("tickets.view") ? c.Db.Ticket.Iter().Count(t => t.RouteId == r.RouteId && t.IsActive) : null);
        });
        RegisterEntityRead(database, "employees", "employees.view", (c, id, access) =>
        {
            var r = c.Db.Employee.EmployeeId.Find(id) ?? throw new OperationException("not_found");
            return new EmployeeRow(r.EmployeeId, r.Name, r.Surname, r.Patronym, r.JobId, Timestamp(r.EmployedSince), Version(c, "employees", r.EmployeeId),
                access.Allows("jobs.view") ? c.Db.Job.JobId.Find(r.JobId)?.JobTitle : null);
        });
        RegisterEntityRead(database, "maintenance", "maintenance.view", (c, id, access) =>
        {
            var r = c.Db.Maintenance.MaintenanceId.Find(id) ?? throw new OperationException("not_found");
            return new MaintenanceRow(r.MaintenanceId, r.BusId, Timestamp(r.LastServiceDate), Timestamp(r.NextServiceDate), r.ServiceEngineer, r.FoundIssues, r.Roadworthiness,
                r.MaintenanceType, r.MileageThreshold, Version(c, "maintenance", r.MaintenanceId),
                access.Allows("buses.view") ? c.Db.Bus.BusId.Find(r.BusId)?.Model : null,
                access.Allows("buses.view") ? c.Db.Bus.BusId.Find(r.BusId)?.RegistrationNumber : null,
                Amount(r.MaintenanceCost), Amount(r.PartsCost), Amount(r.LaborCost), r.MaintenanceStatus);
        });
        RegisterEntityRead(database, "tickets", "tickets.view", (c, id, access) =>
        {
            var t = c.Db.Ticket.TicketId.Find(id) ?? throw new OperationException("not_found");
            return ProjectTicket(c, t, access);
        });
        RegisterEntityRead(database, "schedules", "schedules.view", (c, id, access) =>
        {
            var s = c.Db.RouteSchedule.ScheduleId.Find(id) ?? throw new OperationException("not_found");
            return ProjectSchedule(c, s, access);
        });
        RegisterEntityRead(database, "roles", "roles.view", (c, id, access) =>
        {
            var r = c.Db.Role.RoleId.Find(id) ?? throw new OperationException("not_found");
            var permissions = c.Db.RolePermission.RoleId.Filter(r.RoleId).Select(p => c.Db.Permission.PermissionId.Find(p.PermissionId)).Where(p => p != null).ToArray();
            return new RoleRow(r.RoleId, r.LegacyRoleId, r.Name, r.Description, r.IsSystem, r.Priority, r.IsActive,
                permissions.Select(p => p!.PermissionId).ToArray(), permissions.Select(p => p!.Name).ToArray(), c.Db.UserRole.Iter().Count(u => u.RoleId == r.RoleId), Version(c, "roles", r.RoleId),
                access.Allows("roles.view.permissions") ? permissions.Select(p => ProjectPermission(c, p!)).ToArray() : null);
        });
        RegisterEntityRead(database, "permissions", "permissions.view", (c, id, _) =>
        {
            var p = c.Db.Permission.PermissionId.Find(id) ?? throw new OperationException("not_found");
            return new PermissionRow(p.PermissionId, p.Name, p.Description, p.Category, p.IsActive, Version(c, "permissions", p.PermissionId));
        });
    }

    private void RegisterEntityRead<T>(ISpacetimeDBService database, string resource, string permission, Func<DbConnection, uint, UserAccessSnapshot, T> project) =>
        Register<EntityRequest>(resource + ".get", permission, false, async (actor, request, _, ct) =>
        {
            AdminId(request.Id);
            var access = await _access.GetAsync(actor, ct);
            return await database.ReadAsync(c => project(c, request.Id, access), ct);
        });

    private static RouteRow ProjectRoute(DbConnection c, SpacetimeDB.Types.Route r, UserAccessSnapshot access, int? ticketCount) => new(r.RouteId, r.StartPoint, r.EndPoint, r.DriverId, r.BusId, r.TravelTime, r.IsActive, Version(c, "routes", r.RouteId),
        access.Allows("buses.view") ? c.Db.Bus.BusId.Find(r.BusId)?.Model : null,
        access.Allows("buses.view") ? c.Db.Bus.BusId.Find(r.BusId)?.RegistrationNumber : null,
        access.Allows("employees.view") && c.Db.Employee.EmployeeId.Find(r.DriverId) is { } driver ? driver.Surname + " " + driver.Name : null,
        r.RouteNumber, r.StopCount, r.RouteLength, r.RouteDescription, r.RouteType, ticketCount);

    private static TicketRow ProjectTicket(DbConnection c, Ticket t, UserAccessSnapshot access)
    {
        var route = access.Allows("routes.view") ? c.Db.Route.RouteId.Find(t.RouteId) : null;
        return new TicketRow(t.TicketId, t.RouteId, route == null ? null : route.StartPoint + " — " + route.EndPoint,
            t.SeatNumber, Amount(t.TicketPrice), t.PaymentMethod, Timestamp(t.PurchaseTime), t.IsActive, Version(c, "tickets", t.TicketId),
            route?.StartPoint, route?.EndPoint, route?.TravelTime);
    }
    private static ScheduleRow ProjectSchedule(DbConnection c, RouteSchedule s, UserAccessSnapshot access)
    {
        var route = c.Db.Route.RouteId.Find(s.RouteId);
        var bus = access.Allows("buses.view") && route != null ? c.Db.Bus.BusId.Find(route.BusId) : null;
        var driver = access.Allows("employees.view") && route != null ? c.Db.Employee.EmployeeId.Find(route.DriverId) : null;
        return new ScheduleRow(s.ScheduleId, s.RouteId, access.Allows("routes.view") ? RouteLabel(c, s.RouteId) : null,
            bus == null ? null : bus.Model + " " + bus.RegistrationNumber, driver == null ? null : driver.Surname + " " + driver.Name,
            Timestamp(s.DepartureTime), Timestamp(s.ArrivalTime), Amount(s.Price), s.AvailableSeats, s.IsActive, Version(c, "schedules", s.ScheduleId),
            s.StartPoint, s.EndPoint, s.DaysOfWeek?.ToArray(), s.RouteStops?.ToArray(), s.EstimatedStopTimes?.ToArray(), s.StopDistances?.ToArray(), s.StopDurationMinutes, s.IsRecurring, s.Notes, s.BusTypes?.ToArray());
    }
}
