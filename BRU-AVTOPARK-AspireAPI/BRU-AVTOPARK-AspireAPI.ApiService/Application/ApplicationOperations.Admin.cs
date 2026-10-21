using BRU.Avtopark.Contracts;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Application;

public sealed partial class ApplicationOperations
{
    private void RegisterAdmin(ISpacetimeDBService database)
    {
        Register<PageRequest>("tickets.list", "tickets.view", false, async (actor, request, _, ct) =>
        {
            ValidatePage(request); var access = await _access.GetAsync(actor, ct);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.Ticket.Iter().Where(t => Matches(request.Search, t.TicketId.ToString(), t.SeatNumber.ToString(), t.PaymentMethod,
                    access.Allows("routes.view") ? c.Db.Route.RouteId.Find(t.RouteId)?.StartPoint : null, access.Allows("routes.view") ? c.Db.Route.RouteId.Find(t.RouteId)?.EndPoint : null)).OrderBy(t => t.TicketId).ToArray();
                return new CatalogPageResult<TicketRow>(Page(rows, request).Select(t => new TicketRow(t.TicketId, t.RouteId,
                    access.Allows("routes.view") ? RouteLabel(c, t.RouteId) : null, t.SeatNumber, Amount(t.TicketPrice), t.PaymentMethod, Timestamp(t.PurchaseTime), t.IsActive, Version(c, "tickets", t.TicketId))).ToArray(), request.Page, request.PageSize, rows.Length, access.Allows("tickets.create"), access.Allows("tickets.edit"), access.Allows("tickets.delete"));
            }, ct);
        });
        Register<PageRequest>("schedules.list", "schedules.view", false, async (actor, request, _, ct) =>
        {
            ValidatePage(request); var access = await _access.GetAsync(actor, ct);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.RouteSchedule.Iter().Where(s => Matches(request.Search, s.StartPoint, s.EndPoint, s.Notes)).OrderBy(s => s.ScheduleId).ToArray();
                return new CatalogPageResult<ScheduleRow>(Page(rows, request).Select(s =>
                {
                    var route = c.Db.Route.RouteId.Find(s.RouteId);
                    var bus = access.Allows("buses.view") && route != null ? c.Db.Bus.BusId.Find(route.BusId) : null;
                    var driver = access.Allows("employees.view") && route != null ? c.Db.Employee.EmployeeId.Find(route.DriverId) : null;
                    return new ScheduleRow(s.ScheduleId, s.RouteId, access.Allows("routes.view") ? RouteLabel(c, s.RouteId) : null,
                        bus == null ? null : bus.Model + " " + bus.RegistrationNumber, driver == null ? null : driver.Surname + " " + driver.Name,
                        Timestamp(s.DepartureTime), Timestamp(s.ArrivalTime), Amount(s.Price), s.AvailableSeats, s.IsActive, Version(c, "schedules", s.ScheduleId));
                }).ToArray(), request.Page, request.PageSize, rows.Length, access.Allows("schedules.create"), access.Allows("schedules.edit"), access.Allows("schedules.delete"));
            }, ct);
        });
        Register<PageRequest>("users.list", "users.view", false, async (actor, request, _, ct) =>
        {
            ValidatePage(request); var access = await _access.GetAsync(actor, ct);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.UserProfile.Iter().Where(u => Matches(request.Search, u.Login, u.Email, u.PhoneNumber)).OrderBy(u => u.LegacyUserId).ToArray();
                return new CatalogPageResult<UserRow>(Page(rows, request).Select(u => PublicUser(c, u)).ToArray(), request.Page, request.PageSize, rows.Length, access.Allows("users.create"), access.Allows("users.edit"), access.Allows("users.delete"));
            }, ct);
        });
        Register<EntityRequest>("users.get", "users.view", false, (_, request, _, ct) =>
        {
            AdminId(request.Id);
            return Read(database, c => PublicUser(c, c.Db.UserProfile.LegacyUserId.Find(request.Id) ?? throw new OperationException("not_found")), ct);
        });
        Register<PageRequest>("roles.list", "roles.view", false, async (actor, request, _, ct) =>
        {
            ValidatePage(request); var access = await _access.GetAsync(actor, ct);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.Role.Iter().Where(r => Matches(request.Search, r.Name, r.Description)).OrderBy(r => r.RoleId).ToArray();
                return new CatalogPageResult<RoleRow>(Page(rows, request).Select(r =>
                {
                    var permissions = c.Db.RolePermission.RoleId.Filter(r.RoleId).Select(p => c.Db.Permission.PermissionId.Find(p.PermissionId)).Where(p => p != null).ToArray();
                    return new RoleRow(r.RoleId, r.LegacyRoleId, r.Name, r.Description, r.IsSystem, r.Priority, r.IsActive,
                        permissions.Select(p => p!.PermissionId).ToArray(), permissions.Select(p => p!.Name).ToArray(), c.Db.UserRole.Iter().Count(u => u.RoleId == r.RoleId), Version(c, "roles", r.RoleId));
                }).ToArray(), request.Page, request.PageSize, rows.Length, access.Allows("roles.create"), access.Allows("roles.edit"), access.Allows("roles.delete"));
            }, ct);
        });
        Register<PageRequest>("permissions.list", "permissions.view", false, async (actor, request, _, ct) =>
        {
            ValidatePage(request); var access = await _access.GetAsync(actor, ct);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.Permission.Iter().Where(p => Matches(request.Search, p.Name, p.Description, p.Category)).OrderBy(p => p.PermissionId).ToArray();
                return new CatalogPageResult<PermissionRow>(Page(rows, request).Select(p => new PermissionRow(p.PermissionId, p.Name, p.Description, p.Category, p.IsActive, Version(c, "permissions", p.PermissionId))).ToArray(), request.Page, request.PageSize, rows.Length, access.Allows("permissions.create"), access.Allows("permissions.edit"), access.Allows("permissions.delete"));
            }, ct);
        });

        var executor = new CatalogMutationExecutor(database, _logger);
        async Task<object?> Commit<T>(T reducer, Identity actor, string? id, CancellationToken ct) where T : Reducer, IReducerArgs, new()
        {
            var receipt = await executor.ExecuteAsync(reducer, actor, id!, ct);
            return new MutationResult(id!, receipt.Operation, receipt.EntityId, receipt.Revision, DateTimeOffset.FromUnixTimeMilliseconds(checked((long)receipt.CommittedAt)));
        }
        Register<CreateTicketCommand>("tickets.create", "tickets.create", true, (actor, r, id, ct) =>
        {
            AdminId(r.RouteId); AdminId(r.SeatNumber); AdminPrice(r.TicketPrice); AdminText(r.PaymentMethod, 100, true);
            if (r.PurchaseTime > 253402300799999) throw new OperationException("validation_failed");
            return Commit(new Reducer.CreateTicketV2(id!, actor, r.RouteId, r.TicketPrice, r.SeatNumber, r.PaymentMethod, r.PurchaseTime), actor, id, ct);
        });
        Register<UpdateTicketCommand>("tickets.update", "tickets.edit", true, (actor, r, id, ct) =>
        {
            AdminId(r.Id); if (r.RouteId == 0 || r.SeatNumber == 0 || r.RouteId == null && r.SeatNumber == null && r.TicketPrice == null && r.PaymentMethod == null && r.IsActive == null) throw new OperationException("validation_failed");
            AdminPrice(r.TicketPrice); AdminOptionalName(r.PaymentMethod, 100);
            return Commit(new Reducer.UpdateTicketV2(id!, actor, r.Id, r.ExpectedRevision, r.RouteId, r.SeatNumber, r.TicketPrice, r.PaymentMethod, r.IsActive), actor, id, ct);
        });
        Register<DeleteTicketCommand>("tickets.delete", "tickets.delete", true, (actor, r, id, ct) => { AdminId(r.Id); return Commit(new Reducer.DeleteTicketV2(id!, actor, r.Id, r.ExpectedRevision), actor, id, ct); });
        Register<CreateScheduleCommand>("schedules.create", "schedules.create", true, (actor, r, id, ct) =>
        {
            AdminId(r.RouteId); AdminSchedule(r.DepartureTime, r.ArrivalTime, r.Price, r.StartPoint, r.EndPoint, r.RouteStops, r.DaysOfWeek, r.EstimatedStopTimes, r.StopDistances, r.Notes); AdminArray(r.BusTypes);
            return Commit(new Reducer.CreateScheduleV2(id!, actor, r.RouteId, r.DepartureTime, r.Price, r.AvailableSeats, r.DaysOfWeek?.ToList(), r.StartPoint, r.EndPoint,
                r.RouteStops?.ToList(), r.ArrivalTime, r.StopDurationMinutes, r.IsRecurring, r.EstimatedStopTimes?.ToList(), r.StopDistances?.ToList(), r.Notes, r.BusTypes?.ToList()), actor, id, ct);
        });
        Register<UpdateScheduleCommand>("schedules.update", "schedules.edit", true, (actor, r, id, ct) =>
        {
            AdminId(r.Id); if (r.RouteId == 0 || r.RouteId == null && r.DepartureTime == null && r.ArrivalTime == null && r.Price == null && r.AvailableSeats == null && r.DaysOfWeek == null && r.StartPoint == null && r.EndPoint == null && r.RouteStops == null && r.StopDurationMinutes == null && r.IsRecurring == null && r.EstimatedStopTimes == null && r.StopDistances == null && r.Notes == null && r.BusTypes == null) throw new OperationException("validation_failed");
            AdminSchedule(r.DepartureTime, r.ArrivalTime, r.Price, r.StartPoint, r.EndPoint, r.RouteStops, r.DaysOfWeek, r.EstimatedStopTimes, r.StopDistances, r.Notes); AdminArray(r.BusTypes);
            return Commit(new Reducer.UpdateScheduleV2(id!, actor, r.Id, r.ExpectedRevision, r.RouteId, r.StartPoint, r.EndPoint, r.RouteStops?.ToList(), r.DepartureTime, r.ArrivalTime,
                r.Price, r.AvailableSeats, r.DaysOfWeek?.ToList(), r.BusTypes?.ToList(), r.StopDurationMinutes, r.IsRecurring, r.EstimatedStopTimes?.ToList(), r.StopDistances?.ToList(), r.Notes), actor, id, ct);
        });
        Register<DeleteScheduleCommand>("schedules.delete", "schedules.delete", true, (actor, r, id, ct) => { AdminId(r.Id); return Commit(new Reducer.DeleteScheduleV2(id!, actor, r.Id, r.ExpectedRevision), actor, id, ct); });
        Register<SetScheduleActiveCommand>("schedules.set-active", "schedules.edit", true, (actor, r, id, ct) => { AdminId(r.Id); return Commit(new Reducer.SetScheduleActiveV2(id!, actor, r.Id, r.ExpectedRevision, r.Active), actor, id, ct); });
        Register<CreateUserCommand>("users.create", "users.create", true, (actor, r, id, ct) =>
        {
            AdminText(r.Login, 200, true); AdminText(r.InitialCredential, 1024, true); AdminText(r.Email, 320); AdminText(r.PhoneNumber, 50);
            if (r.InitialCredential.Length < 6 || r.RoleId == 0) throw new OperationException("validation_failed");
            return Commit(new Reducer.CreateUserV2(id!, actor, r.Login, r.InitialCredential, r.Email ?? "", r.PhoneNumber ?? "", r.RoleId), actor, id, ct);
        });
        Register<UpdateUserCommand>("users.update", "users.edit", true, (actor, r, id, ct) =>
        {
            AdminId(r.Id); AdminText(r.Email, 320); AdminText(r.PhoneNumber, 50);
            if (r.Email == null && r.PhoneNumber == null) throw new OperationException("validation_failed");
            return Commit(new Reducer.UpdateUserV2(id!, actor, r.Id, r.ExpectedRevision, r.Email, r.PhoneNumber), actor, id, ct);
        });
        Register<SetUserActiveCommand>("users.set-active", "users.edit", true, (actor, r, id, ct) => { AdminId(r.Id); return Commit(new Reducer.SetUserActiveV2(id!, actor, r.Id, r.ExpectedRevision, r.Active), actor, id, ct); });
        Register<DeleteUserCommand>("users.delete", "users.delete", true, (actor, r, id, ct) => { AdminId(r.Id); return Commit(new Reducer.DeleteUserV2(id!, actor, r.Id, r.ExpectedRevision), actor, id, ct); });
        Register<AssignUserRoleCommand>("users.assign-role", "assign_roles", true, (actor, r, id, ct) => { AdminId(r.Id); AdminId(r.RoleId); return Commit(new Reducer.AssignUserRoleV2(id!, actor, r.Id, r.ExpectedRevision, r.RoleId), actor, id, ct); });
        Register<RemoveUserRoleCommand>("users.remove-role", "assign_roles", true, (actor, r, id, ct) => { AdminId(r.Id); AdminId(r.RoleId); return Commit(new Reducer.RemoveUserRoleV2(id!, actor, r.Id, r.ExpectedRevision, r.RoleId), actor, id, ct); });
        Register<CreateRoleCommand>("roles.create", "roles.create", true, (actor, r, id, ct) => { AdminText(r.Name, 200, true); AdminText(r.Description, 2000); AdminPermissionSet(r.PermissionIds); return Commit(new Reducer.CreateRoleV2(id!, actor, r.LegacyRoleId, r.Name, r.Description ?? "", r.IsSystem, r.Priority, r.PermissionIds?.ToList()), actor, id, ct); });
        Register<UpdateRoleCommand>("roles.update", "roles.edit", true, (actor, r, id, ct) =>
        {
            AdminId(r.Id); AdminOptionalName(r.Name, 200); AdminText(r.Description, 2000); AdminPermissionSet(r.PermissionIds);
            if (r.Name == null && r.Description == null && r.LegacyRoleId == null && r.Priority == null && r.PermissionIds == null) throw new OperationException("validation_failed");
            return Commit(new Reducer.UpdateRoleV2(id!, actor, r.Id, r.ExpectedRevision, r.Name, r.Description, r.LegacyRoleId, r.Priority, r.PermissionIds?.ToList()), actor, id, ct);
        });
        Register<DeleteRoleCommand>("roles.delete", "roles.delete", true, (actor, r, id, ct) => { AdminId(r.Id); return Commit(new Reducer.DeleteRoleV2(id!, actor, r.Id, r.ExpectedRevision), actor, id, ct); });
        Register<GrantRolePermissionCommand>("roles.grant-permission", "grant_permissions", true, (actor, r, id, ct) => { AdminId(r.Id); AdminId(r.PermissionId); return Commit(new Reducer.GrantRolePermissionV2(id!, actor, r.Id, r.ExpectedRevision, r.PermissionId), actor, id, ct); });
        Register<RevokeRolePermissionCommand>("roles.revoke-permission", "grant_permissions", true, (actor, r, id, ct) => { AdminId(r.Id); AdminId(r.PermissionId); return Commit(new Reducer.RevokeRolePermissionV2(id!, actor, r.Id, r.ExpectedRevision, r.PermissionId), actor, id, ct); });
        Register<CreatePermissionCommand>("permissions.create", "permissions.create", true, (actor, r, id, ct) => { AdminText(r.Name, 200, true); AdminText(r.Description, 2000); AdminText(r.Category, 200); return Commit(new Reducer.CreatePermissionV2(id!, actor, r.Name, r.Description ?? "", r.Category ?? ""), actor, id, ct); });
        Register<UpdatePermissionCommand>("permissions.update", "permissions.edit", true, (actor, r, id, ct) =>
        {
            AdminId(r.Id); AdminOptionalName(r.Name, 200); AdminText(r.Description, 2000); AdminText(r.Category, 200);
            if (r.Name == null && r.Description == null && r.Category == null && r.IsActive == null) throw new OperationException("validation_failed");
            return Commit(new Reducer.UpdatePermissionV2(id!, actor, r.Id, r.ExpectedRevision, r.Name, r.Description, r.Category, r.IsActive), actor, id, ct);
        });
        Register<DeletePermissionCommand>("permissions.delete", "permissions.delete", true, (actor, r, id, ct) => { AdminId(r.Id); return Commit(new Reducer.DeletePermissionV2(id!, actor, r.Id, r.ExpectedRevision), actor, id, ct); });
    }
    private static IEnumerable<T> Page<T>(T[] rows, PageRequest request) => rows.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);
    private static bool Matches(string? search, params string?[] fields) => string.IsNullOrWhiteSpace(search) || fields.Any(f => f?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
    private static string? RouteLabel(DbConnection c, uint id) => c.Db.Route.RouteId.Find(id) is { } r ? r.StartPoint + " — " + r.EndPoint : null;
    private static UserRow PublicUser(DbConnection c, UserProfile u) => new(u.LegacyUserId, u.UserId.ToString(), u.Login, u.Email, u.PhoneNumber, u.IsActive,
        Timestamp(u.CreatedAt), u.LastLoginAt.HasValue ? Timestamp(u.LastLoginAt.Value) : null,
        c.Db.UserRole.UserId.Filter(u.UserId).Select(ur => c.Db.Role.RoleId.Find(ur.RoleId)).Where(r => r is { IsActive: true }).Select(r => r!.Name).ToArray(), Version(c, "users", u.LegacyUserId));
    private static void AdminId(uint id) { if (id == 0) throw new OperationException("validation_failed"); }
    private static void AdminOptionalName(string? value, int max) { if (value != null) AdminText(value, max, true); }
    private static void AdminPermissionSet(uint[]? ids) { if (ids != null && (ids.Length > 500 || ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length)) throw new OperationException("validation_failed"); }
    private static void AdminText(string? value, int max, bool required = false)
    { if (required && string.IsNullOrWhiteSpace(value) || value != null && (value.Length > max || value.Length > 0 && string.IsNullOrWhiteSpace(value))) throw new OperationException("validation_failed"); }
    private static void AdminPrice(double? price) { if (price.HasValue && (!double.IsFinite(price.Value) || price < 0 || price > 1e12)) throw new OperationException("validation_failed"); }
    private static void AdminArray(string[]? values)
    { if (values?.Length > 200) throw new OperationException("validation_failed"); if (values != null) foreach (var value in values) AdminText(value, 200, true); }
    private static void AdminSchedule(ulong? departure, ulong? arrival, double? price, string? start, string? end, string[]? stops, string[]? days, string[]? times, double[]? distances, string? notes)
    {
        AdminPrice(price); AdminText(start, 200); AdminText(end, 200); AdminText(notes, 4000); AdminArray(stops); AdminArray(days); AdminArray(times);
        if (departure > 253402300799999 || arrival > 253402300799999 || departure.HasValue && arrival.HasValue && arrival != 0 && arrival < departure || days?.Length > 7 ||
            distances?.Length > 200 || distances?.Any(d => !double.IsFinite(d) || d < 0 || d > 1e9) == true ||
            stops != null && (times is { Length: > 0 } && times.Length != stops.Length || distances is { Length: > 0 } && distances.Length != stops.Length)) throw new OperationException("validation_failed");
    }
}
