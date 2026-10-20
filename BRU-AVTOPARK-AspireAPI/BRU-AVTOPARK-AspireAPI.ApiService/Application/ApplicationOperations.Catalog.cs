using BRU.Avtopark.Contracts;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;
namespace BRU_AVTOPARK_AspireAPI.ApiService.Application;

public sealed partial class ApplicationOperations
{
    private void RegisterCatalog(ISpacetimeDBService database, IRouteService? routes, IEmployeeService? employees, IMaintenanceService? maintenance)
    {
        Register<PageRequest>("routes.list", "routes.view", false, async (_, request, _, ct) =>
        {
            ValidatePage(request);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.Route.Iter().Where(r => string.IsNullOrWhiteSpace(request.Search) || (r.StartPoint + " " + r.EndPoint).Contains(request.Search, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r.RouteId).ToArray();
                return new PageResult<RouteRow>(rows.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
                    .Select(r => new RouteRow(r.RouteId, r.StartPoint, r.EndPoint, r.DriverId, r.BusId, r.TravelTime, r.IsActive, Version(c, "routes", r.RouteId))).ToArray(), request.Page, request.PageSize, rows.Length);
            }, ct);
        });
        Register<PageRequest>("employees.list", "employees.view", false, async (_, request, _, ct) =>
        {
            ValidatePage(request);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.Employee.Iter().Where(r => string.IsNullOrWhiteSpace(request.Search) || (r.Name + " " + r.Surname + " " + r.Patronym).Contains(request.Search, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r.EmployeeId).ToArray();
                return new PageResult<EmployeeRow>(rows.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
                    .Select(r => new EmployeeRow(r.EmployeeId, r.Name, r.Surname, r.Patronym, r.JobId, Timestamp(r.EmployedSince), Version(c, "employees", r.EmployeeId))).ToArray(), request.Page, request.PageSize, rows.Length);
            }, ct);
        });
        Register<PageRequest>("jobs.list", "jobs.view", false, async (_, request, _, ct) =>
        {
            ValidatePage(request);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.Job.Iter().Where(r => string.IsNullOrWhiteSpace(request.Search) || (r.JobTitle).Contains(request.Search, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r.JobId).ToArray();
                return new PageResult<JobRow>(rows.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
                    .Select(r => new JobRow(r.JobId, r.JobTitle, r.Internship, Version(c, "jobs", r.JobId))).ToArray(), request.Page, request.PageSize, rows.Length);
            }, ct);
        });
        Register<PageRequest>("maintenance.list", "maintenance.view", false, async (_, request, _, ct) =>
        {
            ValidatePage(request);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.Maintenance.Iter().Where(r => string.IsNullOrWhiteSpace(request.Search) || (r.ServiceEngineer + " " + r.FoundIssues + " " + r.MaintenanceType).Contains(request.Search, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r.MaintenanceId).ToArray();
                return new PageResult<MaintenanceRow>(rows.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
                    .Select(r => new MaintenanceRow(r.MaintenanceId, r.BusId, Timestamp(r.LastServiceDate), Timestamp(r.NextServiceDate), r.ServiceEngineer, r.FoundIssues, r.Roadworthiness, r.MaintenanceType, r.MileageThreshold, Version(c, "maintenance", r.MaintenanceId))).ToArray(), request.Page, request.PageSize, rows.Length);
            }, ct);
        });
        if (routes != null)
        {
            Register<CreateRouteCommand>("routes.create", "routes.create", true, async (actor, request, id, ct) =>
            {
                ValidateCatalogText(request.StartPoint, 200, true, true);
                ValidateCatalogText(request.EndPoint, 200, true, true);
                if (request.DriverId == 0) throw new OperationException("validation_failed");
                if (request.BusId == 0) throw new OperationException("validation_failed");
                ValidateCatalogText(request.TravelTime, 100, false, false);
                await routes.CreateRouteAsync(request.StartPoint, request.EndPoint, request.DriverId, request.BusId, request.TravelTime, request.IsActive, actor, id, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
            Register<UpdateRouteCommand>("routes.update", "routes.edit", true, async (actor, request, id, ct) =>
            {
                if (request.Id == 0) throw new OperationException("validation_failed");
                ValidateCatalogText(request.StartPoint, 200, false, true);
                ValidateCatalogText(request.EndPoint, 200, false, true);
                if (request.DriverId == 0) throw new OperationException("validation_failed");
                if (request.BusId == 0) throw new OperationException("validation_failed");
                ValidateCatalogText(request.TravelTime, 100, false, false);
                await routes.UpdateRouteAsync(request.Id, request.StartPoint, request.EndPoint, request.DriverId, request.BusId, request.TravelTime, request.IsActive, actor, id, request.ExpectedRevision, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
            Register<DeleteRouteCommand>("routes.delete", "routes.delete", true, async (actor, request, id, ct) =>
            {
                if (request.Id == 0) throw new OperationException("validation_failed");
                await routes.DeleteRouteAsync(request.Id, actor, id, request.ExpectedRevision, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
        }
        if (employees != null)
        {
            Register<CreateEmployeeCommand>("employees.create", "employees.create", true, async (actor, request, id, ct) =>
            {
                ValidateCatalogText(request.EmployeeName, 200, true, true);
                ValidateCatalogText(request.EmployeeSurname, 200, true, true);
                ValidateCatalogText(request.EmployeePatronym, 200, false, false);
                if (request.JobId == 0) throw new OperationException("validation_failed");
                await employees.CreateEmployeeAsync(request.EmployeeName, request.EmployeeSurname, request.EmployeePatronym, request.JobId, actor, id, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
            Register<UpdateEmployeeCommand>("employees.update", "employees.edit", true, async (actor, request, id, ct) =>
            {
                if (request.Id == 0) throw new OperationException("validation_failed");
                ValidateCatalogText(request.EmployeeName, 200, false, true);
                ValidateCatalogText(request.EmployeeSurname, 200, false, true);
                ValidateCatalogText(request.EmployeePatronym, 200, false, false);
                if (request.JobId == 0) throw new OperationException("validation_failed");
                await employees.UpdateEmployeeAsync(request.Id, request.EmployeeName, request.EmployeeSurname, request.EmployeePatronym, request.JobId, actor, id, request.ExpectedRevision, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
            Register<DeleteEmployeeCommand>("employees.delete", "employees.delete", true, async (actor, request, id, ct) =>
            {
                if (request.Id == 0) throw new OperationException("validation_failed");
                await employees.DeleteEmployeeAsync(request.Id, actor, id, request.ExpectedRevision, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
        }
        if (employees != null)
        {
            Register<CreateJobCommand>("jobs.create", "jobs.create", true, async (actor, request, id, ct) =>
            {
                ValidateCatalogText(request.JobTitle, 200, true, true);
                ValidateCatalogText(request.JobInternship, 2000, false, false);
                await employees.CreateJobAsync(request.JobTitle, request.JobInternship, actor, id, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
            Register<UpdateJobCommand>("jobs.update", "jobs.edit", true, async (actor, request, id, ct) =>
            {
                if (request.Id == 0) throw new OperationException("validation_failed");
                ValidateCatalogText(request.JobTitle, 200, false, true);
                ValidateCatalogText(request.JobInternship, 2000, false, false);
                await employees.UpdateJobAsync(request.Id, request.JobTitle, request.JobInternship, actor, id, request.ExpectedRevision, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
            Register<DeleteJobCommand>("jobs.delete", "jobs.delete", true, async (actor, request, id, ct) =>
            {
                if (request.Id == 0) throw new OperationException("validation_failed");
                await employees.DeleteJobAsync(request.Id, actor, id, request.ExpectedRevision, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
        }
        if (maintenance != null)
        {
            Register<CreateMaintenanceCommand>("maintenance.create", "maintenance.create", true, async (actor, request, id, ct) =>
            {
                if (request.BusId == 0) throw new OperationException("validation_failed");
                ValidateCatalogText(request.ServiceEngineer, 200, true, true);
                ValidateCatalogText(request.FoundIssues, 4000, false, false);
                ValidateCatalogText(request.Roadworthiness, 200, false, false);
                ValidateCatalogText(request.MaintenanceType, 200, false, false);
                if (request.LastServiceDate > 253402300799999 || request.NextServiceDate > 253402300799999 || request.NextServiceDate < request.LastServiceDate) throw new OperationException("validation_failed");
                await maintenance.CreateMaintenanceAsync(request.BusId, request.LastServiceDate, request.ServiceEngineer, request.FoundIssues, request.NextServiceDate, request.Roadworthiness, request.MaintenanceType, actor, id, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
            Register<UpdateMaintenanceCommand>("maintenance.update", "maintenance.edit", true, async (actor, request, id, ct) =>
            {
                if (request.Id == 0) throw new OperationException("validation_failed");
                if (request.BusId == 0) throw new OperationException("validation_failed");
                ValidateCatalogText(request.ServiceEngineer, 200, false, true);
                ValidateCatalogText(request.FoundIssues, 4000, false, false);
                ValidateCatalogText(request.Roadworthiness, 200, false, false);
                ValidateCatalogText(request.MaintenanceType, 200, false, false);
                ValidateCatalogText(request.Mileage, 100, false, false);
                if (request.LastServiceDate > 253402300799999 || request.NextServiceDate > 253402300799999 || request.NextServiceDate < request.LastServiceDate) throw new OperationException("validation_failed");
                await maintenance.UpdateMaintenanceAsync(request.Id, request.BusId, request.LastServiceDate, request.ServiceEngineer, request.FoundIssues, request.NextServiceDate, request.Roadworthiness, request.MaintenanceType, request.Mileage, actor, id, request.ExpectedRevision, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
            Register<DeleteMaintenanceCommand>("maintenance.delete", "maintenance.delete", true, async (actor, request, id, ct) =>
            {
                if (request.Id == 0) throw new OperationException("validation_failed");
                await maintenance.DeleteMaintenanceAsync(request.Id, actor, id, request.ExpectedRevision, ct);
                return await CommittedReceipt(database, actor, id!, ct);
            });
        }
        if (routes != null) Register<SetRouteActiveCommand>("routes.set-active", "routes.edit", true, async (actor, request, id, ct) =>
        {
            if (request.Id == 0) throw new OperationException("validation_failed");
            await routes.SetActiveAsync(request.Id, request.Active, actor, id, request.ExpectedRevision, ct);
            return await CommittedReceipt(database, actor, id!, ct);
        });
    }
    private static ulong Version(DbConnection connection, string resource, uint id) => connection.Db.EntityRevision.Key.Find(resource + ":" + id)?.Revision ?? 0;
    private static DateTimeOffset Timestamp(ulong milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(checked((long)milliseconds));
    private static void ValidateCatalogText(string? value, int max, bool required, bool nonempty)
    {
        if (required && value == null || value != null && (value.Length > max || nonempty && string.IsNullOrWhiteSpace(value))) throw new OperationException("validation_failed");
    }
}
