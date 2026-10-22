using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public sealed class RouteScheduleDisplayModel(ScheduleRow row) : ReactiveObject
{
    public ScheduleRow Row { get; private set; } = row;
    private DateOnly? _date;
    public uint ScheduleId => Row.Id;
    public uint RouteId => Row.RouteId;
    public string? StartPoint => Row.StartPoint;
    public string? EndPoint => Row.EndPoint;
    public decimal? Price => Row.Price;
    public uint AvailableSeats => Row.AvailableSeats;
    public bool IsActive => Row.IsActive;
    public string[]? RouteStops => Row.RouteStops;
    public string? BusLabel => Row.BusLabel;
    public string? DriverLabel => Row.DriverLabel;
    private DateTimeOffset? Departure => Row.IsRecurring && _date.HasValue && Row.DepartureTime.HasValue
        ? new DateTimeOffset(_date.Value.ToDateTime(TimeOnly.FromTimeSpan(Row.DepartureTime.Value.UtcDateTime.TimeOfDay)), TimeSpan.Zero) : Row.DepartureTime;
    private DateTimeOffset? Arrival => Row.IsRecurring && Departure.HasValue && Row.DepartureTime.HasValue && Row.ArrivalTime.HasValue
        ? Departure + (Row.ArrivalTime - Row.DepartureTime) : Row.ArrivalTime;
    public string DepartureTimeDisplay => Departure?.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "Не указано";
    public string ArrivalTimeDisplay => Arrival?.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "Не указано";
    public void SetDate(DateOnly date) { _date = date; this.RaisePropertyChanged(nameof(DepartureTimeDisplay)); this.RaisePropertyChanged(nameof(ArrivalTimeDisplay)); }
    public void Update(ScheduleRow value)
    {
        Row = value;
        foreach (var name in new[] { nameof(RouteId), nameof(StartPoint), nameof(EndPoint), nameof(Price), nameof(AvailableSeats), nameof(IsActive), nameof(RouteStops), nameof(BusLabel), nameof(DriverLabel), nameof(DepartureTimeDisplay), nameof(ArrivalTimeDisplay) }) this.RaisePropertyChanged(name);
    }
}

public sealed class RouteSchedulesManagementViewModel : EditableCatalogViewModel<ScheduleRow, RouteScheduleDisplayModel>
{
    private readonly Func<CancellationToken, Task<List<RouteRow>>> _lookup;
    private RouteRow? _selectedRoute;
    private DateTimeOffset _date = DateTimeOffset.Now.Date;
    public ObservableCollection<RouteRow> Routes { get; } = new();
    public ObservableCollection<RouteScheduleDisplayModel> Schedules => Items;
    public RouteScheduleDisplayModel? SelectedSchedule { get => SelectedItem; set => SelectedItem = value; }
    public RouteRow? SelectedRoute { get => _selectedRoute; set { if (ReferenceEquals(_selectedRoute, value)) return; this.RaiseAndSetIfChanged(ref _selectedRoute, value); QueryChanged(); } }
    public DateTimeOffset SelectedDate { get => _date; set { if (_date == value) return; this.RaiseAndSetIfChanged(ref _date, value); QueryChanged(); } }
    public IAsyncRelayCommand RefreshCommand => LoadDataCommand;
    public RouteSchedulesManagementViewModel() : this(null) { }
    public RouteSchedulesManagementViewModel(Func<PageRequest, CancellationToken, Task<CatalogPageResult<ScheduleRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<ScheduleRow>>>>? watch = null,
        Func<ScheduleRow?, Func<bool>, CancellationToken, Task<bool>>? edit = null, Func<ScheduleRow, CancellationToken, Task<bool>>? confirmDelete = null,
        Func<ScheduleRow, string, CancellationToken, Task<MutationResult>>? delete = null, Func<CancellationToken, Task<List<RouteRow>>>? lookup = null)
        : base("schedules.list", r => r.Id, item => item.ScheduleId, r => new(r), (item, r) => item.Update(r), item => item.Row, r => r.Revision,
            delete ?? DeleteAsync, read, watch, edit, confirmDelete)
    { _lookup = lookup ?? (read == null ? ct => CatalogEditorLookups.CatalogAsync<RouteRow>("routes.list", ct) : _ => Task.FromResult(new List<RouteRow>())); }
    protected override PageRequest BuildRequest() => base.BuildRequest() with { RouteId = SelectedRoute?.RouteId, Date = DateOnly.FromDateTime(SelectedDate.Date) };
    protected override async Task PrepareQueryAsync(CancellationToken ct)
    {
        List<RouteRow> rows;
        try { rows = await _lookup(ct); } catch (ApplicationCommandException ex) when (ex.Code == "forbidden") { rows = []; }
        ct.ThrowIfCancellationRequested(); var selected = _selectedRoute?.RouteId;
        Routes.Clear(); foreach (var row in rows) Routes.Add(row);
        _selectedRoute = rows.Find(r => r.RouteId == selected);
        this.RaisePropertyChanged(nameof(SelectedRoute));
    }
    protected override void SnapshotApplied(CatalogPageResult<ScheduleRow> page) { base.SnapshotApplied(page); foreach (var item in Items) item.SetDate(DateOnly.FromDateTime(SelectedDate.Date)); }
    protected override void SelectionChanged() => this.RaisePropertyChanged(nameof(SelectedSchedule));
    protected override string DeleteConflictMessage => "Расписание изменено или связано с продажами. Проверьте актуальную запись и связанные билеты.";
    private static Task<MutationResult> DeleteAsync(ScheduleRow row, string id, CancellationToken ct)
    { var api = ApiClientService.Instance; return ApplicationMutationClient.ExecuteAsync("schedules.delete", new DeleteScheduleCommand(row.Id, row.Revision), id, api.CurrentBaseUrl!, api.AuthToken!, ct); }
}
