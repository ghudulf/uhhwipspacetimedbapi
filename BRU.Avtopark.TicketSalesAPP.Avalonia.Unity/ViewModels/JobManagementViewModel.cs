using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public sealed class JobDisplayModel(JobRow row) : ReactiveObject
{
    public JobRow Row { get; private set; } = row;
    public uint JobId => Row.JobId;
    public string JobTitle => Row.JobTitle;
    public string? Internship => Row.Internship;
    public string? Department => Row.Department;
    public string EmployeeCountDisplay => Row.EmployeeCount?.ToString() ?? "—";
    public void Update(JobRow value)
    {
        Row = value;
        foreach (var name in new[] { nameof(JobTitle), nameof(Internship), nameof(Department), nameof(EmployeeCountDisplay) }) this.RaisePropertyChanged(name);
    }
}

public sealed class JobManagementViewModel : LiveCatalogViewModel<JobRow, JobDisplayModel>
{
    private readonly Func<JobRow?, Func<bool>, Task<bool>>? _edit;
    private readonly Func<JobRow, Task<bool>>? _confirmDelete;
    private bool _editing;
    private readonly HashSet<uint> _uncertainDeletes = new();
    public ObservableCollection<JobDisplayModel> Jobs => Items;
    public JobDisplayModel? SelectedJob { get => SelectedItem; set => SelectedItem = value; }
    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public JobManagementViewModel() : this(null, null) { }
    public JobManagementViewModel(Func<PageRequest, CancellationToken, Task<CatalogPageResult<JobRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<JobRow>>>>? watch = null,
        Func<JobRow?, Func<bool>, Task<bool>>? edit = null, Func<JobRow, Task<bool>>? confirmDelete = null)
        : base("jobs.list", row => row.JobId, item => item.JobId, row => new(row), (item, row) => item.Update(row), read, watch)
    {
        _edit = edit; _confirmDelete = confirmDelete;
        AddCommand = new AsyncRelayCommand(() => EditAsync(null), () => CanCreate && !_editing && _edit != null);
        EditCommand = new AsyncRelayCommand(() => EditAsync(SelectedJob?.Row), () => CanEdit && !_editing && _edit != null);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanDelete && !_editing && _confirmDelete != null && !_uncertainDeletes.Contains(SelectedJob!.JobId));
    }
    protected override void SelectionChanged() => this.RaisePropertyChanged(nameof(SelectedJob));
    protected override void SnapshotApplied(CatalogPageResult<JobRow> page) => _uncertainDeletes.Clear();
    protected override void EligibilityChanged()
    {
        AddCommand?.NotifyCanExecuteChanged(); EditCommand?.NotifyCanExecuteChanged(); DeleteCommand?.NotifyCanExecuteChanged();
    }
    private async Task EditAsync(JobRow? row)
    {
        if (_edit == null || (row == null ? !CanCreate : !CanEdit)) return;
        _editing = true; EligibilityChanged();
        try
        {
            if (await _edit(row, () => row == null ? CanCreate : CanMutate && CanEdit && SelectedJob?.JobId == row.JobId)) await LoadData();
        }
        catch (Exception exception)
        {
            HasError = true; ErrorMessage = "Не удалось открыть редактор. Повторите попытку.";
            Log.Warning("Job editor failed: {ErrorType}", exception.GetType().Name);
        }
        finally { _editing = false; EligibilityChanged(); }
    }
    private async Task DeleteAsync()
    {
        if (!CanDelete || _confirmDelete == null || SelectedJob is not { } selected) return;
        var row = selected.Row;
        var api = ApiClientService.Instance;
        var token = api.AuthToken; var origin = api.CurrentBaseUrl;
        var operationId = Guid.NewGuid().ToString("N");
        _editing = true; EligibilityChanged();
        try
        {
            if (!await _confirmDelete(row) || !CanDelete || api.AuthToken != token || api.CurrentBaseUrl != origin || SelectedJob?.JobId != row.JobId) return;
            await ApplicationMutationClient.ExecuteAsync("jobs.delete", new DeleteJobCommand(row.JobId, row.Revision), operationId, origin!, token!, CancellationToken.None);
            await LoadData();
        }
        catch (Exception exception)
        {
            var uncertain = exception is not ApplicationCommandException failure || failure.Code == "commit_outcome_unknown";
            if (uncertain) _uncertainDeletes.Add(row.JobId);
            HasError = true;
            ErrorMessage = uncertain ? "Результат удаления неизвестен. Обновите список; повторная отправка заблокирована."
                : "Удаление отклонено. Обновите данные и проверьте связанные записи и права доступа.";
            Log.Warning("Job delete {OperationId} stopped: {ErrorType}", operationId, exception.GetType().Name);
        }
        finally { _editing = false; EligibilityChanged(); }
    }
}
