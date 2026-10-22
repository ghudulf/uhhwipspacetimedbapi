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

public sealed class MaintenanceDisplayModel(MaintenanceRow row) : ReactiveObject
{
    public MaintenanceRow Row { get; private set; } = row;
    public uint MaintenanceId => Row.MaintenanceId;
    public uint BusId => Row.BusId;
    public string? ServiceEngineer => Row.ServiceEngineer;
    public string? FoundIssues => Row.FoundIssues;
    public string? Roadworthiness => Row.Roadworthiness;
    public string? MaintenanceType => Row.MaintenanceType;
    public string? MileageThreshold => Row.MileageThreshold;
    public decimal? MaintenanceCost => Row.MaintenanceCost;
    public decimal? PartsCost => Row.PartsCost;
    public decimal? LaborCost => Row.LaborCost;
    public decimal? TotalMaintenanceCost => MaintenanceCost.HasValue && PartsCost.HasValue && LaborCost.HasValue
        ? MaintenanceCost + PartsCost + LaborCost : null;
    public string? MaintenanceStatus => Row.MaintenanceStatus;
    public string BusModel => Row.BusModel ?? "Неизвестный автобус";
    public string BusRegistrationNumber => Row.RegistrationNumber ?? "Н/Д";
    public string LastServiceDateDisplay => Row.LastServiceDate?.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "Не указано";
    public string NextServiceDateDisplay => Row.NextServiceDate?.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "Не указано";
    public void Update(MaintenanceRow value)
    {
        Row = value;
        foreach (var property in new[] { nameof(BusId), nameof(ServiceEngineer), nameof(FoundIssues), nameof(Roadworthiness), nameof(MaintenanceType),
            nameof(MileageThreshold), nameof(MaintenanceCost), nameof(PartsCost), nameof(LaborCost), nameof(TotalMaintenanceCost), nameof(MaintenanceStatus),
            nameof(BusModel), nameof(BusRegistrationNumber), nameof(LastServiceDateDisplay), nameof(NextServiceDateDisplay) }) this.RaisePropertyChanged(property);
    }
}

public sealed class MaintenanceManagementViewModel : EditableCatalogViewModel<MaintenanceRow, MaintenanceDisplayModel>
{
    public ObservableCollection<MaintenanceDisplayModel> MaintenanceRecords => Items;
    public MaintenanceDisplayModel? SelectedRecord { get => SelectedItem; set => SelectedItem = value; }
    public MaintenanceManagementViewModel() : this(null) { }
    public MaintenanceManagementViewModel(Func<PageRequest, CancellationToken, Task<CatalogPageResult<MaintenanceRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<MaintenanceRow>>>>? watch = null,
        Func<MaintenanceRow?, Func<bool>, CancellationToken, Task<bool>>? edit = null,
        Func<MaintenanceRow, CancellationToken, Task<bool>>? confirmDelete = null,
        Func<MaintenanceRow, string, CancellationToken, Task<MutationResult>>? delete = null)
        : base("maintenance.list", row => row.MaintenanceId, item => item.MaintenanceId, row => new(row), (item, row) => item.Update(row), item => item.Row, row => row.Revision,
            delete ?? DeleteAsync, read, watch, edit, confirmDelete) { }
    protected override void SelectionChanged() => this.RaisePropertyChanged(nameof(SelectedRecord));
    private static Task<MutationResult> DeleteAsync(MaintenanceRow row, string operationId, CancellationToken ct)
    {
        var api = ApiClientService.Instance;
        return ApplicationMutationClient.ExecuteAsync("maintenance.delete", new DeleteMaintenanceCommand(row.MaintenanceId, row.Revision), operationId,
            api.CurrentBaseUrl!, api.AuthToken!, ct);
    }
}
