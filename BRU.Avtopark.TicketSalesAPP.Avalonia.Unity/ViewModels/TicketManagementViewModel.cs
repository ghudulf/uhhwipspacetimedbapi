using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using ReactiveUI;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public sealed class TicketDisplayModel(TicketRow row) : ReactiveObject
{
    public TicketRow Row { get; private set; } = row;
    public uint TicketId => Row.Id;
    public uint RouteId => Row.RouteId;
    public string? StartPoint => Row.RouteStartPoint;
    public string? EndPoint => Row.RouteEndPoint;
    public string? TravelTime => Row.RouteTravelTime;
    public decimal? TicketPrice => Row.TicketPrice;
    public uint SeatNumber => Row.SeatNumber;
    public bool IsActive => Row.IsActive;
    public string RouteDisplay => Row.RouteLabel ?? "Маршрут недоступен";
    public void Update(TicketRow value)
    {
        Row = value;
        foreach (var property in new[] { nameof(RouteId), nameof(StartPoint), nameof(EndPoint), nameof(TravelTime), nameof(TicketPrice), nameof(SeatNumber), nameof(IsActive), nameof(RouteDisplay) }) this.RaisePropertyChanged(property);
    }
}

public sealed class TicketManagementViewModel : EditableCatalogViewModel<TicketRow, TicketDisplayModel>
{
    public ObservableCollection<TicketDisplayModel> Tickets => Items;
    public TicketDisplayModel? SelectedTicket { get => SelectedItem; set => SelectedItem = value; }
    public TicketManagementViewModel() : this(null) { }
    public TicketManagementViewModel(Func<PageRequest, CancellationToken, Task<CatalogPageResult<TicketRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<TicketRow>>>>? watch = null,
        Func<TicketRow?, Func<bool>, CancellationToken, Task<bool>>? edit = null,
        Func<TicketRow, CancellationToken, Task<bool>>? confirmDelete = null,
        Func<TicketRow, string, CancellationToken, Task<MutationResult>>? delete = null)
        : base("tickets.list", row => row.Id, item => item.TicketId, row => new(row), (item, row) => item.Update(row), item => item.Row, row => row.Revision,
            delete ?? DeleteAsync, read, watch, edit, confirmDelete) { }
    protected override void SelectionChanged() => this.RaisePropertyChanged(nameof(SelectedTicket));
    protected override string DeleteConflictMessage => "Билет нельзя удалить: на него ссылается продажа или запись изменена. Обновите данные; продажа должна сохранять ссылку на билет.";
    private static Task<MutationResult> DeleteAsync(TicketRow row, string operationId, CancellationToken ct)
    {
        var api = ApiClientService.Instance;
        return ApplicationMutationClient.ExecuteAsync("tickets.delete", new DeleteTicketCommand(row.Id, row.Revision), operationId, api.CurrentBaseUrl!, api.AuthToken!, ct);
    }
}
