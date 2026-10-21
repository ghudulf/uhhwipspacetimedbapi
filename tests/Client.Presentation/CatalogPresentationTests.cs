using Avalonia.Threading;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using System.Threading.Channels;

internal static class CatalogPresentationTests
{
    public static async Task RunAsync(CancellationToken stop)
    {
        var row = new JobRow(31, "Synthetic", "", 1, null);
        CatalogPageResult<JobRow> Page(JobRow value, bool edit = true) => new([value], 1, 100, 1, false, edit, false);
        var updates = Channel.CreateUnbounded<QueryUpdate<CatalogPageResult<JobRow>>>();
        CancellationToken subscription = default;
        var calls = 0;
        var model = new JobManagementViewModel((_, _) => { calls++; return Task.FromResult(Page(row)); },
            (_, ct) => { subscription = ct; return updates.Reader.ReadAllAsync(ct); },
            edit: (_, _) => Task.FromResult(false));
        Assert(calls == 0, "job constructor started work");
        await model.ActivateAsync();
        Assert(model.Jobs.Count == 1 && model.Jobs[0].EmployeeCountDisplay == "—", "unknown employee count fabricated");
        Assert(!model.IsSynchronized && !model.CanCreate && !model.EditCommand.CanExecute(null), "job writes enabled before synchronized snapshot");
        updates.Writer.TryWrite(new(Page(row), true));
        await Until(() => model.IsSynchronized, stop);
        model.SelectedJob = model.Jobs[0];
        var selected = model.SelectedJob;
        Assert(model.CanEdit && model.EditCommand.CanExecute(null) && !model.CanDelete && !model.CanCreate, "job commands ignore permissions");
        var notified = false;
        selected.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(JobDisplayModel.EmployeeCountDisplay)) notified = true; };
        updates.Writer.TryWrite(new(Page(row with { EmployeeCount = 2, JobTitle = "Updated", Revision = 2 }), true));
        await Until(() => selected.JobTitle == "Updated", stop);
        Assert(ReferenceEquals(selected, model.SelectedJob) && notified && selected.EmployeeCountDisplay == "2", "job subscription replaced selection or lost calculated notification");
        updates.Writer.TryWrite(new(null, false, "reconnecting"));
        await Until(() => !model.IsSynchronized, stop);
        Assert(!model.CanEdit && model.Jobs.Count == 1, "job reconnect enabled writes or discarded visible data");
        updates.Writer.TryWrite(new(Page(row, false), true));
        await Until(() => model.IsSynchronized, stop);
        Assert(!model.EditCommand.CanExecute(null), "permission revocation left job command enabled");
        updates.Writer.TryWrite(new(null, false, "forbidden"));
        await Until(() => model.Jobs.Count == 0, stop);
        Assert(model.HasError && model.SelectedJob == null, "revoked query retained job data");
        model.Deactivate();
        Assert(subscription.IsCancellationRequested, "job subscription survived deactivation");
        var before = calls;
        ApiClientService.Instance.AuthToken = "synthetic-catalog-new-session";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert(calls == before, "inactive job screen retained session handler");

        var oldResult = new TaskCompletionSource<CatalogPageResult<JobRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newResult = new TaskCompletionSource<CatalogPageResult<JobRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<CancellationToken>();
        var query = new JobManagementViewModel((request, ct) => { requests.Add(ct); return requests.Count == 1 ? oldResult.Task : newResult.Task; });
        var activation = query.ActivateAsync();
        query.SearchText = "new";
        Assert(requests.Count == 2 && requests[0].IsCancellationRequested, "job search did not cancel superseded request");
        newResult.SetResult(Page(row with { JobTitle = "New query" }));
        await Until(() => query.Jobs.Count == 1, stop);
        oldResult.SetResult(Page(row));
        await activation;
        Assert(query.Jobs[0].JobTitle == "New query", "late job query overwrote current search");
        query.Deactivate();
        var uncertainField = typeof(JobManagementViewModel).GetField("_uncertainDeletes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var retryable = new JobManagementViewModel((_, _) => Task.FromResult(new CatalogPageResult<JobRow>([row], 1, 100, 1, true, true, true)),
            confirmDelete: _ => Task.FromResult(false));
        await retryable.ActivateAsync();
        retryable.SelectedJob = retryable.Jobs[0];
        var uncertain = (HashSet<uint>)uncertainField.GetValue(retryable)!;
        uncertain.Add(row.JobId);
        Assert(!retryable.DeleteCommand.CanExecute(null), "uncertain delete was not blocked");
        await retryable.LoadDataCommand.ExecuteAsync(null);
        Assert(uncertain.Count == 0 && retryable.DeleteCommand.CanExecute(null), "fresh read retained uncertain delete block for existing row");
        retryable.Deactivate();
        var policy = typeof(BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation.JobEditorService)
            .GetMethod("BlocksRetry", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert(!(bool)policy.Invoke(null, [null, "conflict"])! && (bool)policy.Invoke(null, [row, "conflict"])!, "create conflicts became uncorrectable or stale edits became retryable");
        Console.WriteLine("PASS catalog activation, permission gating, stable live selection, nullable counts, revocation, stale state, superseded search and disposal");
    }
    private static async Task Until(Func<bool> test, CancellationToken ct) { while (!test()) await Task.Delay(1, ct); }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
