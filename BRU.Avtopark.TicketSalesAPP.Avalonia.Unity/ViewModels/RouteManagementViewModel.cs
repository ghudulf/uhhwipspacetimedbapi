using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using ReactiveUI;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public sealed class RouteDisplayModel(RouteRow row) : ReactiveObject
{
    public RouteRow Row { get; private set; } = row;
    public uint RouteId => Row.RouteId;
    public string RouteNumber => Row.RouteNumber;
    public string StartPoint => Row.StartPoint;
    public string EndPoint => Row.EndPoint;
    public string? TravelTime => Row.TravelTime;
    public uint BusId => Row.BusId;
    public uint DriverId => Row.DriverId;
    public string? BusModel => Row.BusModel;
    public string? DriverName => Row.DriverName;
    public string TicketCount => Row.TicketCount?.ToString(CultureInfo.InvariantCulture) ?? "—";
    public uint StopCount => Row.StopCount;
    public double RouteLength => Row.RouteLength;
    public string RouteLengthDisplay => Row.RouteLength.ToString("N1", CultureInfo.CurrentCulture) + " км";
    public string? RouteType => Row.RouteType;
    public string? RouteDescription => Row.RouteDescription;
    public bool IsActive => Row.IsActive;
    public void Update(RouteRow value)
    {
        Row = value;
        foreach (var property in new[] { nameof(RouteNumber), nameof(StartPoint), nameof(EndPoint), nameof(TravelTime), nameof(BusId), nameof(DriverId), nameof(BusModel), nameof(DriverName),
            nameof(TicketCount), nameof(StopCount), nameof(RouteLength), nameof(RouteLengthDisplay), nameof(RouteType), nameof(RouteDescription), nameof(IsActive) }) this.RaisePropertyChanged(property);
    }
}

public sealed class RouteManagementViewModel : EditableCatalogViewModel<RouteRow, RouteDisplayModel>
{
    public ObservableCollection<RouteDisplayModel> Routes => Items;
    public RouteDisplayModel? SelectedRoute { get => SelectedItem; set => SelectedItem = value; }
    public bool IsDialogCapable => true;
    public RouteManagementViewModel() : this(null) { }
    public RouteManagementViewModel(Func<PageRequest, CancellationToken, Task<CatalogPageResult<RouteRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<RouteRow>>>>? watch = null,
        Func<RouteRow?, Func<bool>, CancellationToken, Task<bool>>? edit = null,
        Func<RouteRow, CancellationToken, Task<bool>>? confirmDelete = null,
        Func<RouteRow, string, CancellationToken, Task<MutationResult>>? delete = null)
        : base("routes.list", row => row.RouteId, item => item.RouteId, row => new(row), (item, row) => item.Update(row), item => item.Row, row => row.Revision,
            delete ?? DeleteAsync, read, watch, edit, confirmDelete) { }
    protected override void SelectionChanged() => this.RaisePropertyChanged(nameof(SelectedRoute));
    private static Task<MutationResult> DeleteAsync(RouteRow row, string operationId, CancellationToken ct)
    {
        var api = ApiClientService.Instance;
        return ApplicationMutationClient.ExecuteAsync("routes.delete", new DeleteRouteCommand(row.RouteId, row.Revision), operationId, api.CurrentBaseUrl!, api.AuthToken!, ct);
    }
}
