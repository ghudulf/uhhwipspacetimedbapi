using BRU.Avtopark.Contracts;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Application;

public sealed partial class ApplicationOperations
{
    private void RegisterFleet(ISpacetimeDBService database, IBusService buses)
    {
        Register<BusQuery>("buses.list", "buses.view", false, async (actor, request, _, ct) =>
        {
            ValidatePage(new(request.Page, request.PageSize, request.Search));
            var access = await _access.GetAsync(actor, ct);
            return await database.ReadAsync(c =>
            {
                var rows = c.Db.Bus.Iter().Where(b => (!request.Active.HasValue || b.IsActive == request.Active.Value)
                    && (string.IsNullOrWhiteSpace(request.Search) || b.Model.Contains(request.Search, StringComparison.OrdinalIgnoreCase)
                        || b.RegistrationNumber?.Contains(request.Search, StringComparison.OrdinalIgnoreCase) == true
                        || b.BusType.Contains(request.Search, StringComparison.OrdinalIgnoreCase)
                        || b.Vin?.Contains(request.Search, StringComparison.OrdinalIgnoreCase) == true
                        || b.LicensePlate?.Contains(request.Search, StringComparison.OrdinalIgnoreCase) == true
                        || b.BusId.ToString().Contains(request.Search, StringComparison.Ordinal)))
                    .OrderBy(b => b.BusId).ToArray();
                var page = rows.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArray();
                var ids = page.Select(b => b.BusId).ToHashSet();
                // Aggregate only data this actor may see. Null means unavailable, not an invented zero.
                var routes = access.Allows("routes.view") ? c.Db.Route.Iter().Where(r => r.IsActive && ids.Contains(r.BusId))
                    .GroupBy(r => r.BusId).ToDictionary(g => g.Key, g => g.Count()) : null;
                var maintenance = access.Allows("maintenance.view") ? c.Db.Maintenance.Iter().Where(m => ids.Contains(m.BusId))
                    .GroupBy(m => m.BusId).ToDictionary(g => g.Key, g => g.Count()) : null;
                return new BusPageResult(page.Select(b => new BusRow(b.BusId, b.Model, b.RegistrationNumber, b.IsActive,
                    b.BusType, b.Capacity, b.Year, b.Vin, b.LicensePlate, b.CurrentStatus,
                    routes?.GetValueOrDefault(b.BusId), maintenance?.GetValueOrDefault(b.BusId), c.Db.BusRevision.BusId.Find(b.BusId)?.Revision ?? 0))
                    .ToArray(), request.Page, request.PageSize, rows.Length, access.Allows("buses.create"), access.Allows("buses.edit"), access.Allows("buses.delete"));
            }, ct);
        });
        Register<CreateBusCommand>("buses.create", "buses.create", true, async (actor, request, id, ct) =>
        {
            ValidateBus(request.Model, request.RegistrationNumber);
            if (request.Model == null) throw new OperationException("validation_failed");
            await buses.CreateBusAsync(request.Model, request.RegistrationNumber, actor, id, ct, Details(request.Details));
            return await CommittedReceipt(database, actor, id!, ct);
        });
        Register<UpdateBusCommand>("buses.update", "buses.edit", true, async (actor, request, id, ct) =>
        {
            if (request.Id == 0) throw new OperationException("validation_failed");
            if (request.Model == null && request.RegistrationNumber == null && (request.Details == null || request.Details == new BusDetails()))
                throw new OperationException("validation_failed");
            ValidateBus(request.Model, request.RegistrationNumber);
            await buses.UpdateBusAsync(request.Id, request.Model, request.RegistrationNumber, actor, id, request.ExpectedRevision, ct, Details(request.Details));
            return await CommittedReceipt(database, actor, id!, ct);
        });
        Register<DeleteBusCommand>("buses.delete", "buses.delete", true, async (actor, request, id, ct) =>
        {
            if (request.Id == 0) throw new OperationException("validation_failed");
            await buses.DeleteBusAsync(request.Id, actor, id, request.ExpectedRevision, ct);
            return await CommittedReceipt(database, actor, id!, ct);
        });
        Register<SetBusActiveCommand>("buses.set-active", "buses.edit", true, async (actor, request, id, ct) =>
        {
            if (request.Id == 0) throw new OperationException("validation_failed");
            await buses.SetActiveAsync(request.Id, request.Active, actor, id, request.ExpectedRevision, ct);
            return await CommittedReceipt(database, actor, id!, ct);
        });
    }
    private static BusDetailsInput? Details(BusDetails? details)
    {
        if (details == null) return null;
        if (details.Capacity > 1000 || (details.Year.HasValue && details.Year != 0 && (details.Year < 1886 || details.Year > 2200))
            || details.BusType?.Length > 100 || details.Vin?.Length > 100 || details.LicensePlate?.Length > 100 || details.CurrentStatus?.Length > 100)
            throw new OperationException("validation_failed");
        return new(details.Capacity, details.BusType, details.Year, details.Vin, details.LicensePlate, details.CurrentStatus, details.IsActive);
    }
    private static void ValidateBus(string? model, string? registration)
    {
        if (model != null && (string.IsNullOrWhiteSpace(model) || model.Length > 200) || registration?.Length > 100)
            throw new OperationException("validation_failed");
    }
}
