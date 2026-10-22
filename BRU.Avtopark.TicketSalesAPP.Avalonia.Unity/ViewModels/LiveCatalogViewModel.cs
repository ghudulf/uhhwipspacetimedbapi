using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

/// <summary>Shared activation, paging, stable rows and synchronization state for catalog screens.</summary>
public abstract class LiveCatalogViewModel<TRow, TItem> : ReactiveObject, IDisposable where TItem : class
{
    private readonly string _operation;
    private readonly bool _discover;
    private readonly Func<PageRequest, CancellationToken, Task<CatalogPageResult<TRow>>> _read;
    private readonly Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<TRow>>>>? _watch;
    private readonly Func<TRow, uint> _key;
    private readonly Func<TItem, uint> _itemKey;
    private readonly Func<TRow, TItem> _create;
    private readonly Action<TItem, TRow> _update;
    private bool _active, _busy, _synchronized, _hasError, _createAllowed, _editAllowed, _deleteAllowed;
    private string _search = "", _error = "";
    private string? _token, _origin;
    private long _generation;
    private int _page = 1, _total;
    private TItem? _selected;
    private Task? _loading, _watching;
    private CancellationTokenSource? _loadCancellation, _watchCancellation;
    private CancellationTokenSource? _activityCancellation;
    private CancellationToken _activityToken = new(true);
    private CancellationToken _activationToken;
    private bool _disposed;
    public const int PageSize = 100;
    protected CancellationToken ActivityToken => _activityToken;
    public ObservableCollection<TItem> Items { get; } = new();
    public TItem? SelectedItem
    {
        get => _selected;
        set { this.RaiseAndSetIfChanged(ref _selected, value); SelectionChanged(); NotifyEligibility(); }
    }
    protected virtual void SelectionChanged() { }
    protected virtual void EligibilityChanged() { }
    public string SearchText
    {
        get => _search;
        set { if (_search == value) return; this.RaiseAndSetIfChanged(ref _search, value); QueryChanged(); }
    }
    public bool IsBusy { get => _busy; protected set { this.RaiseAndSetIfChanged(ref _busy, value); NotifyEligibility(); } }
    public bool IsSynchronized { get => _synchronized; private set { this.RaiseAndSetIfChanged(ref _synchronized, value); NotifyEligibility(); this.RaisePropertyChanged(nameof(SynchronizationMessage)); } }
    public bool HasError { get => _hasError; protected set { this.RaiseAndSetIfChanged(ref _hasError, value); NotifyEligibility(); } }
    public string ErrorMessage { get => _error; protected set => this.RaiseAndSetIfChanged(ref _error, value); }
    public string SynchronizationMessage => IsSynchronized ? "" : "Синхронизация… Изменения временно недоступны.";
    public string PageInfo => $"Страница {_page} · Всего: {_total}";
    public bool CanGoPrevious => !IsBusy && _page > 1;
    public bool CanGoNext => !IsBusy && (long)_page * PageSize < _total;
    public bool CanMutate => _active && !ActivityToken.IsCancellationRequested && IsSynchronized && !IsBusy && !HasError && SessionMatches();
    public bool CanCreate => CanMutate && _createAllowed;
    public bool CanEdit => CanMutate && _editAllowed && SelectedItem != null && CanEditItem(SelectedItem);
    public bool CanDelete => CanMutate && _deleteAllowed && SelectedItem != null && CanDeleteItem(SelectedItem);
    protected virtual bool CanEditItem(TItem item) => true;
    protected virtual bool CanDeleteItem(TItem item) => true;
    public IAsyncRelayCommand LoadDataCommand { get; }
    public IAsyncRelayCommand PreviousPageCommand { get; }
    public IAsyncRelayCommand NextPageCommand { get; }

    protected LiveCatalogViewModel(string operation, Func<TRow, uint> key, Func<TItem, uint> itemKey,
        Func<TRow, TItem> create, Action<TItem, TRow> update,
        Func<PageRequest, CancellationToken, Task<CatalogPageResult<TRow>>>? read = null,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<TRow>>>>? watch = null)
    {
        _discover = read == null;
        _operation = operation; _key = key; _itemKey = itemKey; _create = create; _update = update;
        _read = read ?? ((query, ct) => ApplicationQueryClient.ReadAsync<PageRequest, CatalogPageResult<TRow>>(operation, query, ct));
        _watch = read != null ? watch : Environment.GetEnvironmentVariable("BRU_TRANSPORT") == "http" ? null
            : (query, ct) => ApplicationQueryClient.WatchAsync<PageRequest, CatalogPageResult<TRow>>(operation, query, ct);
        LoadDataCommand = new AsyncRelayCommand(LoadData);
        PreviousPageCommand = new AsyncRelayCommand(async () => { _page--; await LoadData(); }, () => CanGoPrevious);
        NextPageCommand = new AsyncRelayCommand(async () => { _page++; await LoadData(); }, () => CanGoNext);
    }
    public virtual Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_active) return _loading ?? Task.CompletedTask;
        _activationToken = cancellationToken;
        _activityCancellation?.Dispose();
        _activityCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activityToken = _activityCancellation.Token;
        _active = true;
        var api = ApiClientService.Instance;
        if (_token != api.AuthToken || _origin != api.CurrentBaseUrl) Clear();
        _token = api.AuthToken; _origin = api.CurrentBaseUrl;
        api.OnAuthTokenChanged += SessionChanged;
        return _loading = LoadData();
    }
    public virtual void Deactivate()
    {
        _active = false; _generation++;
        _activityCancellation?.Cancel();
        _activityCancellation?.Dispose(); _activityCancellation = null;
        _loadCancellation?.Cancel(); _watchCancellation?.Cancel();
        IsBusy = false; IsSynchronized = false;
        ApiClientService.Instance.OnAuthTokenChanged -= SessionChanged;
    }
    private void SessionChanged(object? sender, string? token)
    {
        // Cancel network/editor work immediately, even when the notification is off-dispatcher.
        _activityCancellation?.Cancel();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_active || _disposed) return;
            Clear(); _token = ApiClientService.Instance.AuthToken; _origin = ApiClientService.Instance.CurrentBaseUrl;
            _activityCancellation?.Dispose();
            _activityCancellation = CancellationTokenSource.CreateLinkedTokenSource(_activationToken);
            _activityToken = _activityCancellation.Token;
            _loading = LoadData();
        });
    }
    private bool SessionMatches() => !string.IsNullOrEmpty(_token) && _token == ApiClientService.Instance.AuthToken
        && _origin == ApiClientService.Instance.CurrentBaseUrl;
    protected void Clear()
    {
        Items.Clear(); SelectedItem = null; _page = 1; _total = 0;
        _createAllowed = _editAllowed = _deleteAllowed = false;
        IsSynchronized = false; NotifyEligibility(); this.RaisePropertyChanged(nameof(PageInfo));
    }
    protected async Task LoadData()
    {
        if (!_active || ActivityToken.IsCancellationRequested) return;
        _loadCancellation?.Cancel(); _watchCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ActivityToken); _loadCancellation = cancellation;
        var generation = ++_generation;
        IsSynchronized = false; IsBusy = true; HasError = false; ErrorMessage = "";
        try
        {
            if (_discover)
            {
                var origin = await ApiClientService.Instance.DiscoverApiBaseUrlAsync(cancellation.Token);
                if (generation != _generation || cancellation.IsCancellationRequested) return;
                if (_origin != null && _origin != origin) Clear();
                _origin = origin;
            }
            await PrepareQueryAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var request = BuildRequest();
            var page = await _read(request, cancellation.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_active || generation != _generation || cancellation.IsCancellationRequested) return;
                // Discovery can establish the first origin; a later server/account switch cannot reuse it.
                _origin ??= ApiClientService.Instance.CurrentBaseUrl;
                if (!SessionMatches()) { Clear(); return; }
                Apply(page); IsSynchronized = _watch == null;
                if (_watch != null)
                {
                    _watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ActivityToken);
                    _watching = WatchAsync(request, generation, _watchCancellation);
                }
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { if (_active && generation == _generation && !SessionMatches()) Clear(); });
        }
        catch (Exception exception) { await FailedAsync(exception, generation); }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => { if (generation == _generation) IsBusy = false; });
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
            cancellation.Dispose();
        }
    }
    private async Task WatchAsync(PageRequest request, long generation, CancellationTokenSource cancellation)
    {
        try
        {
            await foreach (var update in _watch!(request, cancellation.Token))
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_active || generation != _generation || cancellation.IsCancellationRequested) return;
                    if (!SessionMatches()) { Clear(); return; }
                    IsSynchronized = update.IsSynchronized;
                    if (update.Data is { } page) { Apply(page); HasError = false; }
                    if (update.Reason == "forbidden") { Clear(); HasError = true; ErrorMessage = "Доступ к данным отозван."; }
                });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { if (_active && generation == _generation && !SessionMatches()) Clear(); });
        }
        catch (Exception exception) { await FailedAsync(exception, generation); }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => { if (generation == _generation) IsSynchronized = false; });
            if (ReferenceEquals(_watchCancellation, cancellation)) _watchCancellation = null;
            cancellation.Dispose();
        }
    }
    private async Task FailedAsync(Exception exception, long generation)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
        if (!_active || generation != _generation) return;
        IsSynchronized = false;
        if (!SessionMatches() || exception is ApplicationCommandException { Code: "forbidden" or "session_expired" or "session_changed" or "unauthenticated" or "invalid_credentials" }) Clear();
        HasError = true; ErrorMessage = "Не удалось обновить данные. Повторите загрузку.";
        Log.Warning("Catalog query {Operation} stopped: {ErrorType}", _operation, exception.GetType().Name);
        });
    }
    private void Apply(CatalogPageResult<TRow> page)
    {
        var selectedKey = SelectedItem == null ? (uint?)null : _itemKey(SelectedItem);
        var existing = Items.ToDictionary(_itemKey);
        var updated = page.Items.Select(row => { var item = existing.GetValueOrDefault(_key(row)) ?? _create(row); _update(item, row); return item; }).ToArray();
        for (var i = Items.Count - 1; i >= 0; i--) if (!updated.Contains(Items[i])) Items.RemoveAt(i);
        for (var i = 0; i < updated.Length; i++) { var old = Items.IndexOf(updated[i]); if (old < 0) Items.Insert(i, updated[i]); else if (old != i) Items.Move(old, i); }
        SelectedItem = Items.FirstOrDefault(item => _itemKey(item) == selectedKey);
        _page = page.Page; _total = page.TotalCount;
        _createAllowed = page.CanCreate; _editAllowed = page.CanEdit; _deleteAllowed = page.CanDelete;
        SnapshotApplied(page);
        DataGridDiagnostics.DumpRows(GetType().Name, Items, page.Page, page.TotalCount);
        this.RaisePropertyChanged(nameof(PageInfo)); NotifyEligibility();
    }
    protected virtual void SnapshotApplied(CatalogPageResult<TRow> page) { }
    protected virtual PageRequest BuildRequest() => new(_page, PageSize, SearchText);
    protected virtual Task PrepareQueryAsync(CancellationToken ct) => Task.CompletedTask;
    protected void QueryChanged() { _page = 1; if (_active) _loading = LoadData(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Deactivate();
        // Consumers retain a cancelled token, not a linked source/parent registration.
        GC.SuppressFinalize(this);
    }
    private void NotifyEligibility()
    {
        foreach (var property in new[] { nameof(CanMutate), nameof(CanCreate), nameof(CanEdit), nameof(CanDelete), nameof(CanGoPrevious), nameof(CanGoNext) }) this.RaisePropertyChanged(property);
        PreviousPageCommand?.NotifyCanExecuteChanged(); NextPageCommand?.NotifyCanExecuteChanged(); EligibilityChanged();
    }
}
