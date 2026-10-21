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

public sealed class EmployeeDisplayModel(EmployeeRow row) : ReactiveObject
{
    public EmployeeRow Row { get; private set; } = row;
    public uint EmployeeId => Row.EmployeeId;
    public uint JobId => Row.JobId;
    public string Name => Row.Name;
    public string Surname => Row.Surname;
    public string? Patronym => Row.Patronym;
    public string JobTitle => Row.JobTitle ?? "Должность не указана";
    public string EmployedSinceDisplay => Row.EmployedSince?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "Не указано";
    public void Update(EmployeeRow value)
    {
        Row = value;
        foreach (var property in new[] { nameof(JobId), nameof(Name), nameof(Surname), nameof(Patronym), nameof(JobTitle), nameof(EmployedSinceDisplay) }) this.RaisePropertyChanged(property);
    }
}

public sealed class EmployeeManagementViewModel : EditableCatalogViewModel<EmployeeRow, EmployeeDisplayModel>
{
    public ObservableCollection<EmployeeDisplayModel> Employees => Items;
    public EmployeeDisplayModel? SelectedEmployee { get => SelectedItem; set => SelectedItem = value; }
    public EmployeeManagementViewModel() : this(null) { }
    public EmployeeManagementViewModel(Func<PageRequest, CancellationToken, Task<CatalogPageResult<EmployeeRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<EmployeeRow>>>>? watch = null,
        Func<EmployeeRow?, Func<bool>, CancellationToken, Task<bool>>? edit = null,
        Func<EmployeeRow, CancellationToken, Task<bool>>? confirmDelete = null,
        Func<EmployeeRow, string, CancellationToken, Task<MutationResult>>? delete = null)
        : base("employees.list", row => row.EmployeeId, item => item.EmployeeId, row => new(row), (item, row) => item.Update(row), item => item.Row,
            delete ?? DeleteAsync, read, watch, edit, confirmDelete) { }
    protected override void SelectionChanged() => this.RaisePropertyChanged(nameof(SelectedEmployee));
    private static Task<MutationResult> DeleteAsync(EmployeeRow row, string operationId, CancellationToken ct)
    {
        var api = ApiClientService.Instance;
        return ApplicationMutationClient.ExecuteAsync("employees.delete", new DeleteEmployeeCommand(row.EmployeeId, row.Revision), operationId,
            api.CurrentBaseUrl!, api.AuthToken!, ct);
    }
}
