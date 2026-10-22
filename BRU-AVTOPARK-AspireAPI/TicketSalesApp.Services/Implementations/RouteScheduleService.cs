using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;
using TicketSalesApp.Services.Models;

namespace TicketSalesApp.Services.Implementations;

public class RouteScheduleService : IRouteScheduleService, IDisposable
{
    private readonly ISpacetimeDBService database;
    private readonly ILogger<RouteScheduleService> logger;
    private readonly int maxPageSize;
    private readonly CancellationTokenSource lifetime = new();
    private int disposed;

    public RouteScheduleService(ISpacetimeDBService spacetimeDBService, ILogger<RouteScheduleService> logger, IConfiguration configuration)
    {
        database = spacetimeDBService ?? throw new ArgumentNullException(nameof(spacetimeDBService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);
        maxPageSize = configuration.GetValue("RouteSchedule:MaxPageSize", 5000);
        if (maxPageSize < 1) maxPageSize = 5000;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    public Task<List<RouteSchedule>> GetAllSchedulesAsync() => database.ReadAsync(c => c.Db.RouteSchedule.Iter().ToList(), lifetime.Token);
    public Task<RouteSchedule?> GetScheduleByIdAsync(uint scheduleId) => database.ReadAsync(c => c.Db.RouteSchedule.ScheduleId.Find(scheduleId), lifetime.Token);
    public Task<List<RouteSchedule>> GetSchedulesByRouteIdAsync(uint routeId) => database.ReadAsync(c =>
        c.Db.RouteSchedule.Iter().Where(s => s.RouteId == routeId).OrderBy(s => s.DepartureTime).ToList(), lifetime.Token);

    public Task<(List<RouteSchedule> items, int totalCount)> GetSchedulesPageAsync(int page, int pageSize, ScheduleQuery? query = null)
    {
        query ??= ScheduleQuery.Empty;
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, maxPageSize);
        return database.ReadAsync(c =>
        {
            IEnumerable<RouteSchedule> filtered = c.Db.RouteSchedule.Iter();
            if (query.RouteId.HasValue) filtered = filtered.Where(s => s.RouteId == query.RouteId);
            if (query.IsActive.HasValue) filtered = filtered.Where(s => s.IsActive == query.IsActive);
            if (query.StartDate.HasValue) filtered = filtered.Where(s => s.DepartureTime >= query.StartDate);
            if (query.EndDate.HasValue) filtered = filtered.Where(s => s.DepartureTime <= query.EndDate);
            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                var text = query.SearchText.Trim();
                filtered = filtered.Where(s => (s.StartPoint ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    (s.EndPoint ?? "").Contains(text, StringComparison.OrdinalIgnoreCase));
            }
            var rows = filtered.ToList();
            var skip = (int)Math.Min(((long)page - 1) * pageSize, int.MaxValue);
            return (rows.Skip(skip).Take(pageSize).ToList(), rows.Count);
        }, lifetime.Token);
    }

    public async Task<uint?> CreateScheduleAsync(uint? routeId = null, string? startPoint = null, string? endPoint = null,
        List<string>? routeStops = null, ulong? departureTime = null, ulong? arrivalTime = null, double? price = null,
        uint? availableSeats = null, List<string>? daysOfWeek = null, List<string>? busTypes = null, uint? stopDurationMinutes = null,
        bool? isRecurring = null, List<string>? estimatedStopTimes = null, List<double>? stopDistances = null, string? notes = null,
        bool? isActive = null, ulong? validFrom = null, ulong? validUntil = null, string? updatedBy = null, Identity? actingUser = null)
    {
        var route = await database.ReadAsync(c => routeId.HasValue ? c.Db.Route.RouteId.Find(routeId.Value) : null, lifetime.Token);
        if (route == null) return null;
        var actor = actingUser ?? database.GetLocalIdentity() ?? throw new DatabaseUnavailableException("Database identity unavailable.");
        var id = Guid.NewGuid().ToString("N");
        // Existing receipt identifies this create exactly. No notes marker, callback subscription,
        // polling or best-effort follow-up cleanup is needed, even for identical concurrent creates.
        return await new CatalogMutationExecutor(database, logger).ExecuteResultAsync(new Reducer.CreateScheduleV2(id, actor,
            route.RouteId, departureTime ?? 0, price ?? 0, availableSeats ?? 0, daysOfWeek, startPoint ?? route.StartPoint,
            endPoint ?? route.EndPoint, routeStops, arrivalTime ?? (departureTime ?? 0) + 3600000, stopDurationMinutes,
            isRecurring, estimatedStopTimes ?? [], stopDistances ?? [], notes, busTypes, isActive ?? true), actor, id, (_, receipt) => (uint?)receipt.EntityId, lifetime.Token);
    }

    public async Task<bool> UpdateScheduleAsync(uint scheduleId, uint? routeId = null, string? startPoint = null, string? endPoint = null,
        List<string>? routeStops = null, ulong? departureTime = null, ulong? arrivalTime = null, double? price = null,
        uint? availableSeats = null, List<string>? daysOfWeek = null, List<string>? busTypes = null, uint? stopDurationMinutes = null,
        bool? isRecurring = null, List<string>? estimatedStopTimes = null, List<double>? stopDistances = null, string? notes = null,
        bool? isActive = null, ulong? validFrom = null, ulong? validUntil = null, string? updatedBy = null, Identity? actingUser = null)
    {
        var schedule = await GetScheduleByIdAsync(scheduleId);
        if (schedule == null) return false;
        if (routeId.HasValue && !await database.ReadAsync(c => c.Db.Route.RouteId.Find(routeId.Value) != null, lifetime.Token)) return false;
        await database.ExecuteReducerAsync(new Reducer.UpdateRouteSchedule(scheduleId, routeId ?? schedule.RouteId,
            startPoint ?? schedule.StartPoint, endPoint ?? schedule.EndPoint, routeStops ?? schedule.RouteStops,
            departureTime ?? schedule.DepartureTime, arrivalTime ?? schedule.ArrivalTime, price ?? schedule.Price,
            availableSeats ?? schedule.AvailableSeats, daysOfWeek ?? schedule.DaysOfWeek, busTypes ?? schedule.BusTypes,
            stopDurationMinutes ?? schedule.StopDurationMinutes, isRecurring ?? schedule.IsRecurring,
            estimatedStopTimes ?? schedule.EstimatedStopTimes, stopDistances ?? schedule.StopDistances, notes ?? schedule.Notes, actingUser), _ => true, lifetime.Token);
        return true;
    }

    public async Task<bool> DeleteScheduleAsync(uint scheduleId, Identity? actingUser = null)
    {
        var schedule = await GetScheduleByIdAsync(scheduleId);
        if (schedule == null) return false;
        if (await database.ReadAsync(c => c.Db.Ticket.Iter().Any(t => t.RouteId == schedule.RouteId), lifetime.Token)) return false;
        await database.ExecuteReducerAsync(new Reducer.DeleteRouteSchedule(scheduleId, actingUser), _ => true, lifetime.Token);
        return true;
    }

    public async Task<List<RouteSchedule>> GetSchedulesByDateAsync(ulong date)
    {
        var day = DateTimeOffset.FromUnixTimeMilliseconds((long)date).DayOfWeek switch
        {
            DayOfWeek.Monday => "Понедельник", DayOfWeek.Tuesday => "Вторник", DayOfWeek.Wednesday => "Среда",
            DayOfWeek.Thursday => "Четверг", DayOfWeek.Friday => "Пятница", DayOfWeek.Saturday => "Суббота", _ => "Воскресенье"
        };
        var result = new List<RouteSchedule>();
        foreach (var row in await GetAllSchedulesAsync())
        {
            if (!row.IsRecurring)
            {
                if (row.DepartureTime >= date && row.DepartureTime < date + 86400000) result.Add(row);
                continue;
            }
            if (row.DaysOfWeek == null || !row.DaysOfWeek.Contains(day, StringComparer.OrdinalIgnoreCase) || date < row.ValidFrom || row.ValidUntil.HasValue && date > row.ValidUntil.Value) continue;
            // Copy rather than altering the cached module row.
            result.Add(new RouteSchedule { ScheduleId = row.ScheduleId, RouteId = row.RouteId,
                DepartureTime = date + row.DepartureTime % 86400000, ArrivalTime = date + row.DepartureTime % 86400000 + row.ArrivalTime - row.DepartureTime,
                IsRecurring = row.IsRecurring, DaysOfWeek = row.DaysOfWeek?.ToList(), ValidFrom = row.ValidFrom, ValidUntil = row.ValidUntil,
                IsActive = row.IsActive, CreatedAt = row.CreatedAt, UpdatedAt = row.UpdatedAt });
        }
        return result.OrderBy(s => s.DepartureTime).ToList();
    }

    // Pure stored-timestamp filtering, not recurring occurrence expansion.
    public Task<List<RouteSchedule>> GetSchedulesByDateRangeAsync(ulong startDate, ulong endDate) => database.ReadAsync(c =>
        c.Db.RouteSchedule.Iter().Where(s => s.DepartureTime >= startDate && s.DepartureTime <= endDate).OrderBy(s => s.DepartureTime).ToList(), lifetime.Token);
}
