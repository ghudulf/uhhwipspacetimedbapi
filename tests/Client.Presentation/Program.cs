using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
AccessibilityPolicyTests.Run();
DataGridDiagnosticsTests.Run();
SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
Exception? failure = null;
var finished = false;
Dispatcher.UIThread.Post(async () =>
{
    try
    {
        ApiClientService.Instance.AuthToken = "synthetic-presentation-session";
        var first = new TaskCompletionSource<SalesReportSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<SalesReportSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var third = new TaskCompletionSource<SalesReportSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var observed = new List<CancellationToken>();
        var model = new IncomeReportViewModel((_, _, ct) =>
        {
            observed.Add(ct);
            return ++calls switch { 1 => first.Task, 2 => second.Task, _ => third.Task };
        });
        var activation = model.ActivateAsync();
        await DrainUntil(() => calls == 1, stop.Token);
        Assert(model.IsBusy && !activation.IsCompleted, "activation did not await actual work");
        model.StartDate = model.StartDate.AddDays(1);
        await DrainUntil(() => calls == 2, stop.Token);
        Assert(observed[0].IsCancellationRequested, "superseded query not cancelled");
        second.SetResult(Report(20));
        await DrainUntil(() => model.TotalIncome == 20, stop.Token);
        first.SetResult(Report(10));
        await activation;
        Assert(model.TotalIncome == 20, "late report replaced newer result");
        model.EndDate = model.EndDate.AddDays(-1);
        await DrainUntil(() => calls == 3, stop.Token);
        model.Deactivate();
        Assert(observed[2].IsCancellationRequested && !model.IsBusy, "deactivation retained work/spinner");
        third.SetResult(Report(30));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert(model.TotalIncome == 20, "deactivated screen accepted a response");
        ApiClientService.Instance.AuthToken = "synthetic-other-session";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert(calls == 3, "deactivated screen retained session event handler");
        await model.ActivateAsync();
        Assert(model.TotalIncome == 30, "reactivation failed to load current report");
        model.Deactivate();

        var statistics = new SalesStatisticsViewModel((_, _, _) => Task.FromResult(Report(40)));
        await statistics.ActivateAsync();
        Assert(statistics.TotalRevenue == 40 && statistics.TotalSales == 1 && !statistics.IsBusy, "statistics activation failed");
        statistics.Deactivate();
        Console.WriteLine("PASS real Avalonia dispatcher: report activation, superseded-query cancellation, late-result rejection, deactivation/unsubscription and reactivation");
        var busCalls = 0;
        var busRow = new BusRow(7, "Synthetic", "A", true, "Regular", 30, 2020, null, null, null, 2, null, 1);
        var buses = new BusManagementViewModel((query, ct) =>
        {
            busCalls++;
            return Task.FromResult(new BusPageResult([busRow], query.Page, query.PageSize, 1, false, true, false));
        });
        await buses.ActivateAsync();
        Assert(buses.Buses.Count == 1 && buses.Buses[0].MaintenanceCount == null && !buses.CanGoNext, "paged bus projection incorrectly presented counts/navigation");
        var selected = buses.Buses[0];
        buses.SelectedBus = selected;
        Assert(!buses.CanCreate && buses.CanEdit && !buses.CanDelete && !buses.AddCommand.CanExecute(null), "fleet command availability ignored permissions");
        var modelChanged = false;
        selected.PropertyChanged += (_, args) => { if (args.PropertyName == "Model") modelChanged = true; };
        busRow = busRow with { Model = "Updated", Revision = 2 };
        await buses.LoadDataCommand.ExecuteAsync(null);
        Assert(ReferenceEquals(selected, buses.Buses[0]) && ReferenceEquals(selected, buses.SelectedBus)
            && selected.Model == "Updated" && modelChanged, "bus refresh replaced selected row or missed calculated property notification");
        buses.Deactivate();
        ApiClientService.Instance.AuthToken = "synthetic-fleet-other-session";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert(busCalls == 2, "deactivated bus screen retained session subscription");
        await buses.ActivateAsync();
        Assert(buses.SelectedBus == null, "prior-account bus selection survived reactivation");
        buses.Deactivate();
        Console.WriteLine("PASS bus DTO presentation, null counts, stable row/selection, calculated notifications and session lifecycle");
        var liveUpdates = System.Threading.Channels.Channel.CreateUnbounded<BRU.Avtopark.Client.QueryUpdate<BusPageResult>>();
        CancellationToken watchToken = default;
        var liveBuses = new BusManagementViewModel((query, ct) => Task.FromResult(new BusPageResult([busRow], 1, 100, 1, true, true, true)),
            (query, ct) => { watchToken = ct; return liveUpdates.Reader.ReadAllAsync(ct); });
        await liveBuses.ActivateAsync();
        Assert(!liveBuses.IsSynchronized && !liveBuses.CanCreate, "mutations enabled before subscription snapshot");
        liveUpdates.Writer.TryWrite(new(new BusPageResult([busRow], 1, 100, 1, true, true, true), true));
        await DrainUntil(() => liveBuses.IsSynchronized, stop.Token);
        liveBuses.SelectedBus = liveBuses.Buses[0];
        var liveSelection = liveBuses.SelectedBus;
        Assert(liveBuses.CanEdit, "synchronized permitted mutation disabled");
        liveUpdates.Writer.TryWrite(new(null, false, "reconnecting"));
        await DrainUntil(() => !liveBuses.IsSynchronized, stop.Token);
        Assert(!liveBuses.CanEdit && liveBuses.Buses.Count == 1, "reconnect lost rows or enabled unsafe mutation");
        liveUpdates.Writer.TryWrite(new(new BusPageResult([busRow with { Model = "Live update" }], 1, 100, 1, true, true, true), true));
        await DrainUntil(() => liveBuses.IsSynchronized, stop.Token);
        Assert(ReferenceEquals(liveSelection, liveBuses.SelectedBus) && liveSelection.Model == "Live update", "live refresh lost selection or row update");
        liveBuses.Deactivate();
        Assert(watchToken.IsCancellationRequested && !liveBuses.CanEdit, "deactivation did not cancel live subscription");
        Console.WriteLine("PASS live fleet synchronization gating, stale-row retention, stable selection and subscription disposal");
        await CatalogPresentationTests.RunAsync(stop.Token);
        await EmployeeMaintenancePresentationTests.EmployeesAsync(stop.Token);
        await EmployeeMaintenancePresentationTests.MaintenanceAsync(stop.Token);
        await RouteTicketPresentationTests.RunAsync(stop.Token);
        await ScheduleSalePresentationTests.RunAsync(stop.Token);
        await AdministrationPresentationTests.RunAsync(stop.Token);
        await CatalogHttpPresentationTests.RunAsync(stop.Token);
        await QrPresentationTests.RunAsync();
        await ProviderPresentationTests.RunAsync();
        await PasskeyPresentationTests.RunAsync();
        await NativeSessionPresentationTests.RunAsync();
        await AccessibilityRuntimeTests.RunAsync(stop.Token);
        var navigation = new CentralViewWindowViewModel();
        var applyAccess = typeof(CentralViewWindowViewModel).GetMethod("ApplyAccess", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        applyAccess.Invoke(navigation, [new SessionAccess(true, false, [], ["sales.view"]), true]);
        Assert(navigation.CanShowIncomeReport && navigation.CanShowSalesStatistics, "report screens do not honor sales.view");
        applyAccess.Invoke(navigation, [new SessionAccess(true, false, [], ["reports.view"]), true]);
        Assert(!navigation.CanShowIncomeReport && !navigation.CanShowSalesStatistics, "reports.view incorrectly grants reports.sales");
        var failureState = typeof(BusManagementViewModel).GetMethod("ApplyMutationFailure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await buses.ActivateAsync();
        failureState.Invoke(buses, [new BRU.Avtopark.Client.ApplicationCommandException("validation_failed")]);
        Assert(!buses.HasError && buses.CanMutate, "validation failure blocked corrected fleet retry");
        failureState.Invoke(buses, [new BRU.Avtopark.Client.ApplicationCommandException("commit_outcome_unknown")]);
        Assert(buses.HasError && !buses.CanMutate, "uncertain fleet result did not block unsafe retry");
        buses.Deactivate();
        var recoverable = typeof(ApplicationClientCoordinator).GetMethod("Recoverable", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert((bool)recoverable.Invoke(null, [new BRU.Avtopark.Client.ApplicationCommandException("cancelled"), CancellationToken.None])!, "server cancellation terminated subscription recovery");
        Assert(!(bool)recoverable.Invoke(null, [new BRU.Avtopark.Client.ApplicationCommandException("cancelled"), new CancellationToken(true)])!, "caller cancellation was retried");
        var saleJson = System.Text.Json.Nodes.JsonNode.Parse("""{"saleId":1,"saleDate":"2026-10-20T12:00:00Z","ticketId":1}""")!.AsObject();
        var parsedSale = BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers.BusParsingExtensions.ParseSale(saleJson);
        Assert(parsedSale?.SaleDate == (ulong)DateTimeOffset.Parse("2026-10-20T12:00:00Z").ToUnixTimeMilliseconds(), "ISO sale date became zero");
        Assert((string)BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Converters.UnixMillisecondsConverter.Instance.Convert(0UL, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture) == "—", "missing date rendered as 1970");
        Console.WriteLine("PASS report permission guards, correctable fleet validation, unknown-result blocking, subscription cancellation recovery and ISO/missing sale dates");
        finished = true;
    }
    catch (Exception ex) { failure = ex; }
    finally { stop.Cancel(); }
});
Dispatcher.UIThread.MainLoop(stop.Token);
if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
if (!finished) throw new Exception("Presentation tests timed out.");

static SalesReportSnapshot Report(decimal amount) => new("UTC", DateTimeOffset.UtcNow, new(amount, 1, 0),
    [new(new DateOnly(2026, 10, 20), new(amount, 1, 0))], [new(2026, 10, new(amount, 1, 0))], [new(1, "Synthetic route", new(amount, 1, 0))]);
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task DrainUntil(Func<bool> condition, CancellationToken ct)
{
    while (!condition()) { ct.ThrowIfCancellationRequested(); await Task.Delay(1, ct); }
}
