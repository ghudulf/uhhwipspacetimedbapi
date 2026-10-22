using Avalonia.Threading;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Threading.Channels;

internal static class EmployeeMaintenancePresentationTests
{
    internal delegate EditableCatalogViewModel<TRow, TItem> Factory<TRow, TItem>(
        Func<PageRequest, CancellationToken, Task<CatalogPageResult<TRow>>> read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<TRow>>>>? watch,
        Func<TRow?, Func<bool>, CancellationToken, Task<bool>>? edit,
        Func<TRow, CancellationToken, Task<bool>>? confirm,
        Func<TRow, string, CancellationToken, Task<MutationResult>>? delete) where TItem : class;

    public static async Task EmployeesAsync(CancellationToken stop)
    {
        var row = new EmployeeRow(8, "Ирина", "Синтетическая", null, 4, null, 1, "Диспетчер");
        await ScreenAsync<EmployeeRow, EmployeeDisplayModel>(
            (read, watch, edit, confirm, delete) => new EmployeeManagementViewModel(read, watch, edit, confirm, delete),
            row, row with { Name = "Обновлено", Revision = 2 }, item => item.Name, "Обновлено", stop);
        Assert(new EmployeeDisplayModel(row).EmployedSinceDisplay == "Не указано", "missing employment date fabricated");
        var draft = EmployeeEditorService.DraftFrom(row);
        Assert(EmployeeEditorService.Validate(draft) == null, "valid employee rejected");
        foreach (var invalid in new Action<EmployeeDraft>[] {
            d => d.Name = " ", d => d.Name = new('я', 201), d => d.Surname = "", d => d.Surname = new('я', 201),
            d => d.Patronym = new('я', 201), d => d.JobId = 0 })
        {
            draft = EmployeeEditorService.DraftFrom(row); invalid(draft);
            Assert(EmployeeEditorService.Validate(draft) is { Length: > 0 }, "invalid employee draft accepted");
        }
        draft = EmployeeEditorService.DraftFrom(row); draft.Name = draft.Surname = draft.Patronym = new('я', 200);
        Assert(EmployeeEditorService.Validate(draft) == null, "employee validation boundary rejected");
        await EditorAsync(row, row with { Revision = 7 }, EmployeeEditorService.DraftFrom, r => r.Revision,
            EmployeeEditorService.Validate, d => d.Name = "Сохранённый черновик", d => d.Name, stop);
        Console.WriteLine("PASS employees: typed paged activation/search, permissions/revocation, stable selection, live invalidation/stale rows, session/editor cancellation, delete reconciliation, bound-row capture and editor validation/draft/operation-ID/receipt/conflict state");
    }

    public static async Task MaintenanceAsync(CancellationToken stop)
    {
        var date = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var row = new MaintenanceRow(9, 7, date, date.AddDays(1), "Инженер", null, null, null, null, 1, "Синтетический автобус");
        await ScreenAsync<MaintenanceRow, MaintenanceDisplayModel>(
            (read, watch, edit, confirm, delete) => new MaintenanceManagementViewModel(read, watch, edit, confirm, delete),
            row, row with { ServiceEngineer = "Обновлено", Revision = 2 }, item => item.ServiceEngineer ?? "", "Обновлено", stop);
        var unknown = new MaintenanceDisplayModel(row with { LastServiceDate = null, NextServiceDate = null });
        Assert(unknown.LastServiceDateDisplay == "Не указано" && unknown.NextServiceDateDisplay == "Не указано" && unknown.MaintenanceCost == null && unknown.TotalMaintenanceCost == null, "unknown maintenance projection fabricated");
        var known = new MaintenanceDisplayModel(row with { MaintenanceCost = 1.25m, PartsCost = 2.5m, LaborCost = 3.75m });
        Assert(known.TotalMaintenanceCost == 7.5m, "decimal maintenance total incorrect");
        var draft = MaintenanceEditorService.DraftFrom(row);
        Assert(MaintenanceEditorService.Validate(draft) == null, "valid maintenance rejected");
        foreach (var invalid in new Action<MaintenanceDraft>[] {
            d => d.BusId = 0, d => d.ServiceEngineer = " ", d => d.ServiceEngineer = new('я', 201), d => d.FoundIssues = new('я', 4001),
            d => d.Roadworthiness = new('я', 201), d => d.MaintenanceType = new('я', 201), d => d.Mileage = new('я', 101),
            d => d.LastServiceDate = null, d => d.NextServiceDate = null, d => d.LastServiceDate = DateTimeOffset.UnixEpoch.AddMilliseconds(-1),
            d => d.NextServiceDate = d.LastServiceDate!.Value.AddMilliseconds(-1) })
        {
            draft = MaintenanceEditorService.DraftFrom(row); invalid(draft);
            Assert(MaintenanceEditorService.Validate(draft) is { Length: > 0 }, "invalid maintenance draft accepted");
        }
        draft = MaintenanceEditorService.DraftFrom(row);
        draft.ServiceEngineer = draft.Roadworthiness = draft.MaintenanceType = new('я', 200); draft.FoundIssues = new('я', 4000); draft.Mileage = new('я', 100);
        draft.LastServiceDate = DateTimeOffset.UnixEpoch; draft.NextServiceDate = DateTimeOffset.MaxValue;
        Assert(MaintenanceEditorService.Validate(draft) == null, "maintenance validation boundary rejected");
        await EditorAsync(row, row with { Revision = 7 }, MaintenanceEditorService.DraftFrom, r => r.Revision,
            MaintenanceEditorService.Validate, d => d.ServiceEngineer = "Сохранённый черновик", d => d.ServiceEngineer, stop);
        Console.WriteLine("PASS maintenance: typed paged activation/search, permissions/revocation, stable selection, live invalidation/stale rows, session/editor cancellation, delete reconciliation, bound-row capture and editor date/text validation/draft/operation-ID/receipt/conflict state");
    }

    internal static async Task ScreenAsync<TRow, TItem>(Factory<TRow, TItem> factory, TRow row, TRow changed,
        Func<TItem, string> display, string changedText, CancellationToken stop, Action<PageRequest>? checkRequest = null) where TItem : class
    {
        CatalogPageResult<TRow> Page(TRow value, bool allowed = true, int page = 1) => new([value], page, 100, 201, allowed, allowed, allowed);
        var sink = new Capture(); var previousLogger = Log.Logger; var previousTraffic = Environment.GetEnvironmentVariable("BRU_DEBUG_TRAFFIC");
        using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        Log.Logger = logger; Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", "redacted");
        try
        {
            var requests = new List<PageRequest>();
            var updates = Channel.CreateUnbounded<QueryUpdate<CatalogPageResult<TRow>>>();
            CancellationToken subscription = default;
            using var model = factory((request, _) => { requests.Add(request); return Task.FromResult(Page(row, page: request.Page)); },
                (_, ct) => { subscription = ct; return updates.Reader.ReadAllAsync(ct); }, (_, _, _) => Task.FromResult(false), (_, _) => Task.FromResult(false), null);
            Assert(requests.Count == 0, "screen constructor started IO");
            await model.ActivateAsync();
            var request = requests.Single();
            if (checkRequest == null) Assert(request == new PageRequest(1, 100, ""), "typed default request failed"); else checkRequest(request);
            Assert(model.Items.Count == 1 && !model.CanCreate, "typed activation/paging/sync gating failed");
            var dump = sink.Events.LastOrDefault(e => e.Properties.TryGetValue("Screen", out var screen) && screen is ScalarValue { Value: string name } && name == model.GetType().Name && e.Properties.ContainsKey("BoundRows"));
            Assert(dump != null && dump.Properties["RowCount"].ToString() == "1", "snapshot did not dump bound row count");
            using (var dumped = System.Text.Json.JsonDocument.Parse((string)((ScalarValue)dump!.Properties["BoundRows"]).Value!))
                Assert(System.Text.Json.JsonElement.DeepEquals(dumped.RootElement[0].GetProperty("Row"), System.Text.Json.JsonSerializer.SerializeToElement(row)), "snapshot dump differs from actual bound row");
            updates.Writer.TryWrite(new(Page(row), true)); await Until(() => model.IsSynchronized, stop);
            model.SelectedItem = model.Items[0]; var selected = model.SelectedItem;
            Assert(model.CanCreate && model.CanEdit && model.CanDelete && model.EditCommand.CanExecute(null), "page permissions not applied");
            updates.Writer.TryWrite(new(Page(changed), true)); await Until(() => display(selected) == changedText, stop);
            Assert(ReferenceEquals(selected, model.SelectedItem), "live update replaced selection");
            updates.Writer.TryWrite(new(null, false, "invalidated")); await Until(() => !model.IsSynchronized, stop);
            Assert(!model.CanMutate && model.Items.Count == 1, "invalidation discarded stale rows or allowed edits");
            updates.Writer.TryWrite(new(Page(changed, false), true)); await Until(() => model.IsSynchronized, stop);
            Assert(!model.CanCreate && !model.CanEdit && !model.CanDelete && !model.AddCommand.CanExecute(null), "permission revocation left mutations enabled");
            updates.Writer.TryWrite(new(null, false, "forbidden")); await Until(() => model.Items.Count == 0, stop);
            Assert(model.SelectedItem == null && !model.CanMutate, "query revocation retained eligibility/selection");
            model.Deactivate(); Assert(subscription.IsCancellationRequested, "detached screen kept subscription");

            var pageRequests = new List<PageRequest>();
            using var paged = factory((request, _) => { pageRequests.Add(request); return Task.FromResult(Page(row, page: request.Page)); }, null, null, null, null);
            await paged.ActivateAsync(); await paged.NextPageCommand.ExecuteAsync(null);
            Assert(pageRequests[^1].Page == 2 && pageRequests[^1].PageSize == 100 && paged.CanGoPrevious, "next-page request ignored fixed pager");
            await paged.PreviousPageCommand.ExecuteAsync(null); Assert(pageRequests[^1].Page == 1, "previous-page request ignored pager");
            paged.Deactivate();

            var old = new TaskCompletionSource<CatalogPageResult<TRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var fresh = new TaskCompletionSource<CatalogPageResult<TRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tokens = new List<CancellationToken>(); var searches = new List<PageRequest>();
            using var search = factory((request, ct) => { tokens.Add(ct); searches.Add(request); return tokens.Count == 1 ? old.Task : fresh.Task; }, null, null, null, null);
            var activation = search.ActivateAsync(); search.SearchText = "серверный поиск";
            Assert(tokens.Count == 2 && tokens[0].IsCancellationRequested && searches[1].Search == "серверный поиск", "server search failed to cancel superseded work");
            fresh.SetResult(Page(changed)); await Until(() => search.Items.Count == 1, stop);
            old.SetResult(Page(row)); await activation;
            Assert(display(search.Items[0]) == changedText, "superseded search response won"); search.Deactivate();

            var deleteCount = 0; var deleteIds = new List<string>(); var keep = true;
            using var deletion = factory((request, _) => Task.FromResult(keep ? Page(row, page: request.Page) : new CatalogPageResult<TRow>([], 1, 100, 0, true, true, true)), null, null,
                (_, _) => Task.FromResult(true), (_, id, _) => { deleteCount++; deleteIds.Add(id); throw new ApplicationCommandException("commit_outcome_unknown"); });
            await deletion.ActivateAsync(); deletion.SelectedItem = deletion.Items[0];
            await deletion.DeleteCommand.ExecuteAsync(null);
            Assert(deleteCount == 1 && !deletion.DeleteCommand.CanExecute(null) && deletion.Items.Count == 1, "uncertain delete not blocked/retained");
            Assert(sink.Events.Any(e => e.Properties.TryGetValue("OperationId", out var operation) && operation.ToString().Contains(deleteIds[0]) &&
                e.Properties.TryGetValue("Code", out var code) && code.ToString().Contains("commit_outcome_unknown") &&
                e.Properties.TryGetValue("Revision", out var revision) && revision.ToString() == "1"), "failed delete did not log operation ID, code and entity revision");
            await deletion.LoadDataCommand.ExecuteAsync(null);
            Assert(deletion.DeleteCommand.CanExecute(null), "fresh snapshot did not reconcile existing deleted row");
            await deletion.DeleteCommand.ExecuteAsync(null);
            Assert(deleteCount == 2 && deleteIds.Distinct().Count() == 2, "reconciled delete reused prior attempt ID");
            keep = false; await deletion.LoadDataCommand.ExecuteAsync(null);
            Assert(deletion.SelectedItem == null && !deletion.DeleteCommand.CanExecute(null), "deleted-row snapshot retained eligibility");

            var editStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var editGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sessionReads = 0;
            using var session = factory((_, _) => { sessionReads++; return Task.FromResult(Page(row)); }, null,
                async (_, eligible, ct) => { Assert(eligible(), "editor initial eligibility lost"); editStarted.SetResult(ct); await editGate.Task; Assert(!eligible(), "old-session editor allowed submission"); return false; }, null, null);
            await session.ActivateAsync(); var editing = session.AddCommand.ExecuteAsync(null); var editorToken = await editStarted.Task;
            ApiClientService.Instance.AuthToken = Guid.NewGuid().ToString("N");
            Assert(editorToken.IsCancellationRequested && !session.CanMutate, "session change failed to cancel editor/gate edits immediately");
            editGate.SetResult(); await editing; await Until(() => sessionReads == 2, stop);
            session.Deactivate(); var readsBefore = sessionReads;
            ApiClientService.Instance.AuthToken = Guid.NewGuid().ToString("N");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert(sessionReads == readsBefore, "inactive screen retained session handler");

            using var life = new CancellationTokenSource(); CancellationToken pending = default;
            var wait = new TaskCompletionSource<CatalogPageResult<TRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = factory((_, ct) => { pending = ct; return wait.Task; }, null, null, null, null);
            var loading = cancelled.ActivateAsync(life.Token); life.Cancel();
            Assert(pending.IsCancellationRequested && !cancelled.CanMutate, "screen lifetime failed to cancel read");
            wait.SetResult(Page(row)); await loading; Assert(cancelled.Items.Count == 0, "cancelled screen accepted late snapshot");
            cancelled.Dispose();
            try { await cancelled.ActivateAsync(); throw new Exception("disposed screen reactivated"); } catch (ObjectDisposedException) { }
            Assert(model.Items.Count == 0, "disposed live screen received unrelated rows");
            if (typeof(TRow) == typeof(ScheduleRow) || typeof(TRow) == typeof(SaleRow))
                File.WriteAllLines("/tmp/opencode/h15-" + typeof(TRow).Name + "-mutation-outcomes.log", sink.Events.Where(e => e.Properties.ContainsKey("OperationId") && e.Properties.ContainsKey("Code")).Select(e => e.RenderMessage()));
        }
        finally { Log.Logger = previousLogger; Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", previousTraffic); }
    }

    internal static async Task EditorAsync<TRow, TDraft>(TRow row, TRow current, Func<TRow?, TDraft> draft,
        Func<TRow, ulong> revision, Func<TDraft, string?> validate, Action<TDraft> change, Func<TDraft, string> text, CancellationToken stop)
        where TRow : class where TDraft : class
    {
        var ids = new List<string>(); var mode = "conflict"; var receiptExists = false; var reads = 0;
        MutationResult Result(string id) => new(id, "synthetic.update", 1, 9, DateTimeOffset.UtcNow);
        var attempt = new CatalogEditorAttempt<TRow, TDraft>(row, draft, revision, validate,
            (_, expected, id, _) => { ids.Add(id); Assert(expected == revision(row) || expected == revision(current), "editor dropped expected revision"); if (mode == "ok") return Task.FromResult(Result(id)); throw new ApplicationCommandException(mode); },
            (id, _) => Task.FromResult(receiptExists ? Result(id) : null), _ => { reads++; return Task.FromResult(current); });
        change(attempt.Draft); var retained = attempt.Draft; var retainedText = text(retained);
        await attempt.SubmitAsync(() => false, stop); Assert(ids.Count == 0, "editor sent without eligibility");
        await attempt.SubmitAsync(() => true, stop);
        Assert(attempt.IsBlocked && attempt.CanReload && ReferenceEquals(retained, attempt.Draft), "conflict discarded draft or omitted reload");
        await attempt.SubmitAsync(() => true, stop); Assert(ids.Count == 1, "conflict resubmitted before reload");
        await attempt.ReloadAsync(stop);
        Assert(reads == 1 && attempt.Current == current && text(attempt.Draft) == retainedText && attempt.ExpectedRevision == revision(row), "conflict read silently overwrote draft/revision");
        attempt.ReplaceWithCurrent(); Assert(attempt.ExpectedRevision == revision(current) && !attempt.IsBlocked, "explicit replacement did not adopt current revision");
        change(attempt.Draft); mode = "commit_outcome_unknown"; var uncertainDraft = attempt.Draft;
        await attempt.SubmitAsync(() => true, stop); var uncertainId = attempt.OperationId;
        Assert(ids.Count == 2 && ids[0] != uncertainId && attempt.CanReconcile && attempt.IsBlocked, "uncertain attempt ID/block wrong");
        await attempt.SubmitAsync(() => true, stop); await attempt.ReconcileAsync(stop);
        Assert(ids.Count == 2 && attempt.OperationId == uncertainId && attempt.IsBlocked && !attempt.IsCommitted && ReferenceEquals(uncertainDraft, attempt.Draft), "missing receipt permitted replay or lost draft/ID");
        var drafts = new CatalogEditorDrafts<TRow, TDraft>();
        Assert(ReferenceEquals(drafts.Get(1, () => attempt), drafts.Get(1, () => throw new Exception("uncertain dialog reopening created a new attempt"))), "dialog reopening lost uncertain draft");
        receiptExists = true; await attempt.ReconcileAsync(stop);
        Assert(attempt.IsCommitted && ids.Count == 2 && attempt.OperationId == uncertainId, "receipt did not settle same attempt without replay");

        var createIds = new List<string>();
        var create = new CatalogEditorAttempt<TRow, TDraft>(null, r => draft(r ?? row), revision, validate,
            (_, expected, id, _) => { Assert(expected == null, "create sent expected revision"); createIds.Add(id); throw new ApplicationCommandException("conflict"); },
            (_, _) => Task.FromResult<MutationResult?>(null), _ => Task.FromResult(current));
        await create.SubmitAsync(() => true, stop); change(create.Draft); await create.SubmitAsync(() => true, stop);
        Assert(!create.IsBlocked && !create.CanReload && createIds.Count == 2 && createIds.Distinct().Count() == 2, "correctable create conflict blocked or reused a settled attempt ID");
        var pending = new TaskCompletionSource<MutationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingCount = 0;
        var inFlight = new CatalogEditorAttempt<TRow, TDraft>(row, draft, revision, validate,
            (_, _, _, _) => { pendingCount++; return pending.Task; }, (_, _) => Task.FromResult<MutationResult?>(null), _ => Task.FromResult(current));
        var saving = inFlight.SubmitAsync(() => true, stop); var stable = inFlight.OperationId;
        await inFlight.SubmitAsync(() => true, stop); Assert(pendingCount == 1 && inFlight.OperationId == stable, "concurrent save changed operation ID or duplicated mutation");
        pending.SetResult(Result(stable)); await saving; Assert(inFlight.IsCommitted, "known mutation success not settled");
        Assert(ReferenceEquals(drafts.Get(1, () => inFlight), inFlight), "settled attempt was retained on reopening");
        ApiClientService.Instance.AuthToken = Guid.NewGuid().ToString("N");
        Assert(ReferenceEquals(drafts.Get(1, () => create), create), "prior-session draft leaked into a new session");
    }
    private sealed class Capture : ILogEventSink { public List<LogEvent> Events { get; } = []; public void Emit(LogEvent value) => Events.Add(value); }
    private static async Task Until(Func<bool> predicate, CancellationToken ct) { while (!predicate()) await Task.Delay(1, ct); }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
