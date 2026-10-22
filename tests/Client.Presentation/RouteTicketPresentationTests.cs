using BRU.Avtopark.Client;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

internal static class RouteTicketPresentationTests
{
    public static async Task RunAsync(CancellationToken stop)
    {
        var route = new RouteRow(8, "A", "B", 4, 7, "00:10", true, 1, "Bus", "N", "Driver", "R-14", 12, 25.5, "Описание", "Тип");
        await EmployeeMaintenancePresentationTests.ScreenAsync<RouteRow, RouteDisplayModel>(
            (read, watch, edit, confirm, delete) => new RouteManagementViewModel(read, watch, edit, confirm, delete),
            route, route with { StartPoint = "Обновлено", Revision = 2 }, item => item.StartPoint, "Обновлено", stop);
        Check(new RouteDisplayModel(route).TicketCount == "—", "hidden route count fabricated as zero");
        Check(new RouteDisplayModel(route with { TicketCount = 0 }).TicketCount == "0", "known route zero count lost");
        foreach (var invalid in new Action<RouteDraft>[] { d => d.StartPoint = " ", d => d.EndPoint = "", d => d.StartPoint = new('x', 201), d => d.EndPoint = new('x', 201),
            d => d.RouteNumber = new('x', 51), d => d.RouteDescription = new('x', 2001), d => d.RouteType = new('x', 101), d => d.TravelTime = new('x', 101),
            d => d.StopCount = 10001, d => d.RouteLength = -1, d => d.RouteLength = 1000001, d => d.RouteLength = double.NaN, d => d.RouteLength = double.PositiveInfinity,
            d => d.BusId = 0, d => d.DriverId = 0 })
        { var draft = RouteEditorService.DraftFrom(route); invalid(draft); Check(RouteEditorService.Validate(draft) != null, "invalid route draft accepted"); }
        var boundary = RouteEditorService.DraftFrom(route); boundary.RouteNumber = new('x', 50); boundary.RouteDescription = new('x', 2000); boundary.RouteType = new('x', 100);
        boundary.StartPoint = boundary.EndPoint = new('x', 200); boundary.TravelTime = new('x', 100); boundary.StopCount = 10000; boundary.RouteLength = 1000000;
        Check(RouteEditorService.Validate(boundary) == null, "valid route metadata boundary rejected");
        await EmployeeMaintenancePresentationTests.EditorAsync(route, route with { Revision = 7 }, RouteEditorService.DraftFrom, r => r.Revision,
            RouteEditorService.Validate, d => d.StartPoint = "Черновик", d => d.StartPoint, stop);
        using var routeDelete = new RouteManagementViewModel((_, _) => Task.FromResult(new CatalogPageResult<RouteRow>([route], 1, 100, 1, true, true, true)),
            confirmDelete: (_, _) => Task.FromResult(true), delete: (_, _, _) => throw new ApplicationCommandException("conflict"));
        await routeDelete.ActivateAsync(); routeDelete.SelectedRoute = routeDelete.Routes[0]; await routeDelete.DeleteCommand.ExecuteAsync(null);
        Check(routeDelete.ErrorMessage.Contains("расписаниями") && routeDelete.Routes.Count == 1, "referenced route conflict lost rows or Russian reason");

        var ticket = new TicketRow(9, 8, "A — B", 2, 12.5m, "Cash", null, true, 1, "A", "B", "00:10");
        await EmployeeMaintenancePresentationTests.ScreenAsync<TicketRow, TicketDisplayModel>(
            (read, watch, edit, confirm, delete) => new TicketManagementViewModel(read, watch, edit, confirm, delete),
            ticket, ticket with { RouteStartPoint = "Обновлено", Revision = 2 }, item => item.StartPoint ?? "", "Обновлено", stop);
        Check(new TicketDisplayModel(ticket with { TicketPrice = null }).TicketPrice == null, "unknown ticket price fabricated");
        foreach (var price in new double?[] { null, -1, 1000000000001, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        { var draft = TicketEditorService.DraftFrom(ticket); draft.TicketPrice = price; Check(TicketEditorService.Validate(draft) != null, "invalid ticket price accepted"); }
        foreach (var price in new double[] { 0, 1000000000000 })
        { var draft = TicketEditorService.DraftFrom(ticket); draft.TicketPrice = price; Check(TicketEditorService.Validate(draft) == null, "ticket price boundary rejected"); }
        var seat = TicketEditorService.DraftFrom(ticket); seat.SeatNumber = 31;
        Check(TicketEditorService.Validate(seat, 30)?.Contains("вместимость") == true && TicketEditorService.Validate(seat) == null, "seat capacity guard either ignored visible bus or fabricated hidden capacity");
        seat.SeatNumber = 0; Check(TicketEditorService.Validate(seat) != null, "zero seat accepted");
        seat.SeatNumber = 1; seat.RouteId = 0; Check(TicketEditorService.Validate(seat) != null, "missing route accepted");
        await EmployeeMaintenancePresentationTests.EditorAsync(ticket, ticket with { Revision = 7 }, TicketEditorService.DraftFrom, r => r.Revision,
            d => TicketEditorService.Validate(d), d => d.PaymentMethod = "Сохранённый черновик", d => d.PaymentMethod, stop);
        using var ticketDelete = new TicketManagementViewModel((_, _) => Task.FromResult(new CatalogPageResult<TicketRow>([ticket], 1, 100, 1, true, true, true)),
            confirmDelete: (_, _) => Task.FromResult(true), delete: (_, _, _) => throw new ApplicationCommandException("conflict"));
        await ticketDelete.ActivateAsync(); ticketDelete.SelectedTicket = ticketDelete.Tickets[0]; await ticketDelete.DeleteCommand.ExecuteAsync(null);
        Check(ticketDelete.ErrorMessage.Contains("продажа") && ticketDelete.Tickets.Count == 1, "sale reference conflict lost ticket or Russian reason");
        var theme = new FluentTheme(); Application.Current!.Styles.Add(theme);
        try
        {
            using var routeRuntime = new RouteManagementViewModel((_, _) => Task.FromResult(new CatalogPageResult<RouteRow>([route], 1, 100, 101, true, true, true)));
            using var ticketRuntime = new TicketManagementViewModel((_, _) => Task.FromResult(new CatalogPageResult<TicketRow>([ticket], 1, 100, 101, true, true, true)));
            await Pager(new BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.RouteManagementToolWindow(), routeRuntime, () => routeRuntime.IsSynchronized);
            await Pager(new BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.TicketManagementToolWindow(), ticketRuntime, () => ticketRuntime.IsSynchronized);
        }
        finally { Application.Current.Styles.Remove(theme); }
        Console.WriteLine("PASS routes/tickets shared lifecycle/editor matrices: typed paging/search/selection, permission revocation, cancellation/session disposal, bound-row logs, unknown-delete reconciliation, metadata/finite-price/seat boundaries and reference-conflict draft retention");

        async Task Pager(UserControl view, object model, Func<bool> synchronized)
        {
            view.DataContext = model;
            var window = new Window { Content = view, Width = 1200, Height = 600 };
            try
            {
                window.Show(); while (!synchronized()) await Task.Delay(1, stop);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); window.UpdateLayout();
                foreach (var text in new[] { "Назад", "Далее", "Обновить" })
                {
                    var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text);
                    Check(ControlAutomationPeer.CreatePeerForElement(button)?.GetName() == text, "runtime pager automation name lost");
                    Check(button.Focusable && button.IsVisible, "runtime pager button hidden or not keyboard eligible");
                    if (button.IsEffectivelyEnabled) Check(button.Focus(), $"runtime pager action could not receive focus: {view.GetType().Name} / {text}");
                }
            }
            finally { window.Close(); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); }
            Check(!synchronized(), "detached shipped screen remained synchronized");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
