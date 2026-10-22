using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

internal static class ScheduleSalePresentationTests
{
    internal static readonly DateTimeOffset Date = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
    internal static readonly ScheduleRow Schedule = new(10, 8, "A — B", "Bus", "Driver", Date, Date.AddHours(1), 12.5m, 30, true, 1,
        "A", "B", ["Thursday"], ["A", "B"], ["08:00", "09:00"], [0, 12.5], 5, true, "Примечание", ["Regular"]);
    internal static readonly SaleRow Sale = new(11, Date, 9, "Покупатель", "", "7", null, 1);
    public static async Task RunAsync(CancellationToken stop)
    {
        await EmployeeMaintenancePresentationTests.ScreenAsync<ScheduleRow, RouteScheduleDisplayModel>(
            (read, watch, edit, confirm, delete) => new RouteSchedulesManagementViewModel(read, watch, edit, confirm, delete),
            Schedule, Schedule with { StartPoint = "Обновлено", Revision = 2 }, item => item.StartPoint ?? "", "Обновлено", stop,
            r => Check(r.Page == 1 && r.PageSize == 100 && r.Search == "" && r.Date.HasValue && r.RouteId == null, "schedule filter request lost"));
        await EmployeeMaintenancePresentationTests.ScreenAsync<SaleRow, SaleDisplayModel>(
            (read, watch, edit, confirm, delete) => new SalesManagementViewModel(read, watch, edit, confirm, delete),
            Sale, Sale with { BuyerName = "Обновлено", Revision = 2 }, item => item.TicketSoldToUser, "Обновлено", stop,
            r => Check(r.Page == 1 && r.PageSize == 100 && r.Search == "" && r.StartDate.HasValue && r.EndDate >= r.StartDate, "sale filter request lost"));
        foreach (var invalid in new Action<ScheduleDraft>[] {
            d => d.RouteId = 0, d => d.DepartureTime = null, d => d.ArrivalTime = null, d => d.DepartureTime = DateTimeOffset.UnixEpoch.AddMilliseconds(-1),
            d => d.ArrivalTime = d.DepartureTime!.Value.AddMinutes(-1), d => d.Price = null, d => d.Price = -1, d => d.Price = double.NaN,
            d => d.Price = double.PositiveInfinity, d => d.Price = 1e12 + 1, d => d.StartPoint = " ", d => d.EndPoint = new('x', 201),
            d => d.DaysOfWeek = [], d => d.DaysOfWeek = ["Thursday", "thursday"], d => d.DaysOfWeek = ["8"], d => d.DaysOfWeek = ["Funday"],
            d => d.RouteStops = ["A"], d => d.RouteStops = ["A", " "], d => d.RouteStops = Enumerable.Repeat("A", 201).ToArray(),
            d => d.EstimatedStopTimes = ["08:00"], d => d.EstimatedStopTimes = ["24:00", "09:00"],
            d => d.StopDistances = [0], d => d.StopDistances = [0, double.NaN], d => d.StopDistances = [-1, 2],
            d => d.Notes = new('x', 4001), d => d.BusTypes = [" "], d => d.HasIntegralCounts = false })
        { var draft = ScheduleEditorService.DraftFrom(Schedule); invalid(draft); Check(ScheduleEditorService.Validate(draft) != null, "invalid schedule accepted"); }
        var boundary = ScheduleEditorService.DraftFrom(Schedule); boundary.Price = 0; boundary.AvailableSeats = 0;
        Check(ScheduleEditorService.Validate(boundary) == null, "valid zero schedule rejected"); boundary.Price = 1e12;
        Check(ScheduleEditorService.Validate(boundary) == null, "schedule maximum price rejected");
        await EmployeeMaintenancePresentationTests.EditorAsync(Schedule, Schedule with { Revision = 7 }, ScheduleEditorService.DraftFrom, r => r.Revision,
            ScheduleEditorService.Validate, d => d.Notes = "Черновик", d => d.Notes, stop);
        foreach (var invalid in new Action<SaleDraft>[] { d => d.TicketId = 0, d => d.BuyerName = " ", d => d.BuyerName = new('x', 201), d => d.BuyerPhone = " ", d => d.BuyerPhone = new('x', 51) })
        { var draft = SaleEditorService.DraftFrom(Sale); invalid(draft); Check(SaleEditorService.Validate(draft) != null, "invalid buyer accepted"); }
        var buyer = SaleEditorService.DraftFrom(null); buyer.TicketId = 9;
        Check(buyer.BuyerName == "ФИЗ.ПРОДАЖА" && buyer.BuyerPhone == "" && SaleEditorService.Validate(buyer) == null, "buyer defaults or empty phone rejected");
        await EmployeeMaintenancePresentationTests.EditorAsync(Sale, Sale with { Revision = 7 }, SaleEditorService.DraftFrom, r => r.Revision,
            SaleEditorService.Validate, d => d.BuyerName = "Черновик", d => d.BuyerName, stop);
        var unknownSchedule = new RouteScheduleDisplayModel(Schedule with { Price = null, DepartureTime = null, ArrivalTime = null });
        var unknownSale = new SaleDisplayModel(Sale with { SaleDate = null });
        Check(unknownSchedule.Price == null && unknownSchedule.DepartureTimeDisplay == "Не указано" && unknownSchedule.ArrivalTimeDisplay == "Не указано" && unknownSale.RecordedAmount == null && unknownSale.SaleDateDisplay == "Не указано", "unknown values fabricated");
        var recurring = new RouteScheduleDisplayModel(Schedule with { ArrivalTime = Date.AddHours(25) }); recurring.SetDate(new(2026, 11, 5));
        Check(recurring.DepartureTimeDisplay == "05.11.2026 08:00" && recurring.ArrivalTimeDisplay == "06.11.2026 09:00", "recurring date or overnight duration lost");
        var previous = Environment.GetEnvironmentVariable("BRU_DEBUG_TRAFFIC");
        try
        {
            Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", "redacted");
            var scheduleDump = DataGridDiagnostics.DumpRows("RouteSchedulesManagementViewModel", [unknownSchedule]);
            var saleDump = DataGridDiagnostics.DumpRows("SalesManagementViewModel", [unknownSale]);
            using var scheduleJson = System.Text.Json.JsonDocument.Parse(scheduleDump); using var saleJson = System.Text.Json.JsonDocument.Parse(saleDump);
            Check(scheduleJson.RootElement[0].GetProperty("Price").ValueKind == System.Text.Json.JsonValueKind.Null && scheduleJson.RootElement[0].GetProperty("DepartureTimeDisplay").GetString() == "Не указано" &&
                saleJson.RootElement[0].GetProperty("RecordedAmount").ValueKind == System.Text.Json.JsonValueKind.Null && saleJson.RootElement[0].GetProperty("RecordedAmountDisplay").GetString() == "Неизвестно", "unknown bound-row dump changed");
            File.WriteAllText("/tmp/opencode/h15-schedule-grid.json", scheduleDump); File.WriteAllText("/tmp/opencode/h15-sale-grid.json", saleDump);
        }
        finally { Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", previous); }
        var requests = new List<PageRequest>();
        var route = new RouteRow(8, "A", "B", 4, 7, null, true, 1);
        using var schedules = new RouteSchedulesManagementViewModel((r, _) => { requests.Add(r); return Task.FromResult(new CatalogPageResult<ScheduleRow>([Schedule], r.Page, 100, 201, true, true, true)); }, lookup: _ => Task.FromResult(new List<RouteRow> { route }));
        await schedules.ActivateAsync(); await schedules.NextPageCommand.ExecuteAsync(null); schedules.SelectedRoute = schedules.Routes[0];
        Check(requests[^1].RouteId == 8 && requests[^1].Page == 1, "route filter failed server query/reset");
        schedules.SelectedDate = schedules.SelectedDate.AddDays(1); Check(requests[^1].Date == DateOnly.FromDateTime(schedules.SelectedDate.Date), "schedule date not sent"); schedules.Deactivate();
        using var forbidden = new RouteSchedulesManagementViewModel((r, _) => Task.FromResult(new CatalogPageResult<ScheduleRow>([Schedule], 1, 100, 1, false, false, false)), lookup: _ => throw new BRU.Avtopark.Client.ApplicationCommandException("forbidden"));
        await forbidden.ActivateAsync(); Check(forbidden.Schedules.Count == 1 && forbidden.Routes.Count == 0, "lookup denial hid schedule list"); forbidden.Deactivate();
        var edits = 0;
        using var sales = new SalesManagementViewModel((r, _) => { requests.Add(r); return Task.FromResult(new CatalogPageResult<SaleRow>([Sale], r.Page, 100, 201, true, true, true, 12.5m, 1)); }, edit: (_, _, _) => { edits++; return Task.FromResult(false); });
        await sales.ActivateAsync(); sales.MinPrice = 2; sales.MaxPrice = 20; sales.StartDate = sales.StartDate.AddDays(-1);
        Check(requests[^1].MinPrice == 2 && requests[^1].MaxPrice == 20 && requests[^1].StartDate == sales.StartDate && sales.TotalIncome == null && sales.TotalIncomeDisplay.Contains("1"), "sale filters/unknown aggregate fabricated"); sales.Deactivate();
        var theme = new FluentTheme(); Application.Current!.Styles.Add(theme);
        try
        {
            await Pager(new BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews.RouteSchedulesManagementToolWindow(), schedules, () => schedules.IsSynchronized);
            await Pager(new BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.SalesManagementToolWindow(), sales, () => sales.IsSynchronized);
        }
        finally { Application.Current.Styles.Remove(theme); }
        Console.WriteLine("PASS schedules/sales shared lifecycle/editor matrices: paging/search/filter cancellation, permission revocation, stable rows, unknown delete reconciliation, operation outcome logs, draft/receipt/conflict state, days/stops/times/price validation, buyer defaults and unknown amount/date dumps");

        async Task Pager(UserControl view, object model, Func<bool> synchronized)
        {
            view.DataContext = model; var window = new Window { Content = view, Width = 1200, Height = 600 };
            try
            {
                window.Show(); while (!synchronized()) await Task.Delay(1, stop);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); window.UpdateLayout();
                foreach (var text in new[] { "Назад", "Далее", "Обновить" })
                {
                    var button = view.GetVisualDescendants().OfType<Button>().Last(b => b.Content as string == text);
                    Check(ControlAutomationPeer.CreatePeerForElement(button)?.GetName() == text && button.Focusable && button.IsVisible, "pager accessible name/focus lost");
                    if (button.IsEffectivelyEnabled) Check(button.Focus(), "pager keyboard focus failed");
                }
                if (model is SalesManagementViewModel saleModel)
                {
                    saleModel.SelectedSale = saleModel.Sales[0];
                    var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
                    var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
                    grid.RaiseEvent(key); Check(key.Handled && edits == 1, "Enter did not open eligible sale editor");
                    saleModel.Deactivate(); grid.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                    Check(edits == 1, "stale sale keyboard gesture opened editor");
                }
            }
            finally { window.Close(); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); }
            Check(!synchronized(), "detached schedule/sale stayed synchronized");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
