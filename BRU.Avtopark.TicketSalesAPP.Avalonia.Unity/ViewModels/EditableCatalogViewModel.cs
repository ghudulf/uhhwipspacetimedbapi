using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

/// <summary>Jobs-style catalog commands with a cancellable screen lifetime and fresh-read delete reconciliation.</summary>
public abstract class EditableCatalogViewModel<TRow, TItem> : LiveCatalogViewModel<TRow, TItem> where TItem : class
{
    private readonly Func<TRow?, Func<bool>, CancellationToken, Task<bool>>? _edit;
    private readonly Func<TRow, CancellationToken, Task<bool>>? _confirmDelete;
    private readonly Func<TRow, string, CancellationToken, Task<MutationResult>> _delete;
    private readonly Func<TItem, TRow> _row;
    private readonly Func<TRow, uint> _key;
    private readonly Func<TRow, ulong> _revision;
    private readonly HashSet<uint> _uncertainDeletes = new();
    private bool _editing;
    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }

    protected EditableCatalogViewModel(string operation, Func<TRow, uint> key, Func<TItem, uint> itemKey,
        Func<TRow, TItem> create, Action<TItem, TRow> update, Func<TItem, TRow> row, Func<TRow, ulong> revision,
        Func<TRow, string, CancellationToken, Task<MutationResult>> delete,
        Func<PageRequest, CancellationToken, Task<CatalogPageResult<TRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<TRow>>>>? watch,
        Func<TRow?, Func<bool>, CancellationToken, Task<bool>>? edit, Func<TRow, CancellationToken, Task<bool>>? confirmDelete)
        : base(operation, key, itemKey, create, update, read, watch)
    {
        _key = key; _row = row; _revision = revision; _delete = delete; _edit = edit; _confirmDelete = confirmDelete;
        AddCommand = new AsyncRelayCommand(() => EditAsync(default, true), () => CanCreate && !_editing && _edit != null);
        EditCommand = new AsyncRelayCommand(() => EditAsync(SelectedItem == null ? default : _row(SelectedItem), false), () => CanEdit && !_editing && _edit != null);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanDelete && !_editing && _confirmDelete != null && !_uncertainDeletes.Contains(_key(_row(SelectedItem!))));
    }
    protected override void EligibilityChanged()
    {
        AddCommand?.NotifyCanExecuteChanged(); EditCommand?.NotifyCanExecuteChanged(); DeleteCommand?.NotifyCanExecuteChanged();
    }
    protected override void SnapshotApplied(CatalogPageResult<TRow> page) => _uncertainDeletes.Clear();
    protected virtual string DeleteConflictMessage => "Удаление отклонено. Запись изменилась или используется связанными записями. Обновите данные.";
    private async Task EditAsync(TRow? row, bool create)
    {
        if (_edit == null || (create ? !CanCreate : !CanEdit) || _editing) return;
        var ct = ActivityToken;
        var api = ApiClientService.Instance;
        var token = api.AuthToken; var origin = api.CurrentBaseUrl;
        bool Eligible() => !ct.IsCancellationRequested && api.AuthToken == token && api.CurrentBaseUrl == origin &&
            (create ? CanCreate : CanEdit && SelectedItem != null && _key(_row(SelectedItem)) == _key(row!));
        _editing = true; EligibilityChanged();
        try { if (await _edit(row, Eligible, ct) && Eligible()) await LoadData(); }
        catch (Exception exception)
        {
            if (ct.IsCancellationRequested) return;
            HasError = true; ErrorMessage = "Не удалось открыть редактор. Черновик не отправлен.";
            Log.Warning("{Screen} editor stopped: {Code}", GetType().Name, exception is ApplicationCommandException failure ? failure.Code : exception.GetType().Name);
        }
        finally { _editing = false; EligibilityChanged(); }
    }
    private async Task DeleteAsync()
    {
        if (!CanDelete || _editing || _confirmDelete == null || SelectedItem == null) return;
        var row = _row(SelectedItem);
        if (_uncertainDeletes.Contains(_key(row))) return;
        var ct = ActivityToken;
        var api = ApiClientService.Instance;
        var token = api.AuthToken; var origin = api.CurrentBaseUrl;
        var operationId = Guid.NewGuid().ToString("N");
        _editing = true; EligibilityChanged();
        try
        {
            if (!await _confirmDelete(row, ct) || ct.IsCancellationRequested || !CanDelete || api.AuthToken != token || api.CurrentBaseUrl != origin ||
                SelectedItem == null || _key(_row(SelectedItem)) != _key(row)) return;
            var result = await _delete(row, operationId, ct);
            Log.Information("{Screen} delete {OperationId}: {Code}, revision {Revision}", GetType().Name, operationId, "ok", result.Revision);
            if (!ct.IsCancellationRequested) await LoadData();
        }
        catch (Exception exception)
        {
            if (ct.IsCancellationRequested) return;
            var code = exception is ApplicationCommandException failure ? failure.Code : "commit_outcome_unknown";
            if (code == "commit_outcome_unknown") _uncertainDeletes.Add(_key(row));
            HasError = true;
            ErrorMessage = code == "commit_outcome_unknown"
                ? "Результат удаления неизвестен. Обновите список; повторная отправка заблокирована."
                : code == "conflict" ? DeleteConflictMessage : "Удаление отклонено. Обновите данные и проверьте связанные записи и права доступа.";
            Log.Information("{Screen} delete {OperationId}: {Code}, entity {EntityId}, revision {Revision}", GetType().Name, operationId, code, _key(row), _revision(row));
        }
        finally { _editing = false; EligibilityChanged(); }
    }
}
