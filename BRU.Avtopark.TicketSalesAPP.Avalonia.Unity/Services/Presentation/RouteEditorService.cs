using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

public sealed class RouteDraft
{
    public string RouteNumber { get; set; } = "";
    public string StartPoint { get; set; } = "";
    public string EndPoint { get; set; } = "";
    public string TravelTime { get; set; } = "";
    public uint StopCount { get; set; }
    public double RouteLength { get; set; }
    public string RouteDescription { get; set; } = "";
    public string RouteType { get; set; } = "";
    public uint BusId { get; set; }
    public uint DriverId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class RouteEditorService(ControlDialogHost host)
{
    private readonly CatalogEditorDrafts<RouteRow, RouteDraft> _drafts = new();
    public static RouteDraft DraftFrom(RouteRow? row) => new()
    {
        RouteNumber = row?.RouteNumber ?? "", StartPoint = row?.StartPoint ?? "", EndPoint = row?.EndPoint ?? "", TravelTime = row?.TravelTime ?? "",
        StopCount = row?.StopCount ?? 0, RouteLength = row?.RouteLength ?? 0, RouteDescription = row?.RouteDescription ?? "", RouteType = row?.RouteType ?? "",
        BusId = row?.BusId ?? 0, DriverId = row?.DriverId ?? 0, IsActive = row?.IsActive ?? true
    };
    public static string? Validate(RouteDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.StartPoint) || draft.StartPoint.Length > 200) return "Укажите начальную точку (не более 200 символов).";
        if (string.IsNullOrWhiteSpace(draft.EndPoint) || draft.EndPoint.Length > 200) return "Укажите конечную точку (не более 200 символов).";
        if (draft.RouteNumber.Length > 50 || draft.TravelTime.Length > 100 || draft.RouteDescription.Length > 2000 || draft.RouteType.Length > 100) return "Превышена допустимая длина номера, времени, описания или типа маршрута.";
        if (draft.StopCount > 10000) return "Количество остановок должно быть от 0 до 10000.";
        if (!double.IsFinite(draft.RouteLength) || draft.RouteLength < 0 || draft.RouteLength > 1000000) return "Длина маршрута должна быть от 0 до 1000000 км.";
        if (draft.BusId == 0) return "Выберите автобус. Если список пуст, проверьте наличие автобусов и право просмотра автобусов.";
        if (draft.DriverId == 0) return "Выберите водителя. Если список пуст, проверьте должность Driver и право просмотра сотрудников.";
        return null;
    }
    public async Task<bool> ShowAsync(RouteRow? original, Func<bool> canSubmit, CancellationToken ct)
    {
        var api = ApiClientService.Instance; var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed");
        var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        List<BusRow> buses = []; DriverLookupResult drivers = new([], false); var notice = "";
        try { buses = await CatalogEditorLookups.BusesAsync(ct); }
        catch (ApplicationCommandException ex) when (ex.Code == "forbidden") { notice = "Нет права просмотра автобусов. "; }
        try { drivers = await ApplicationQueryClient.ReadAsync<DriverLookupRequest, DriverLookupResult>("employees.drivers", new(), ct); }
        catch (ApplicationCommandException ex) when (ex.Code == "forbidden") { notice += "Нет права просмотра водителей. "; }
        if (buses.Count == 0) notice += "Нет доступных автобусов. ";
        if (drivers.Items.Length == 0) notice += "Нет доступных водителей: проверьте должность Driver и сотрудников. ";
        if (drivers.Truncated) notice += "Показаны первые 1000 водителей; нужный водитель может отсутствовать. ";
        ct.ThrowIfCancellationRequested();
        if (api.AuthToken != token || api.CurrentBaseUrl != origin) throw new ApplicationCommandException("session_changed");
        var number = CatalogEditorDialog.Text("Номер маршрута", 50); var start = CatalogEditorDialog.Text("Начальная точка", 200);
        var end = CatalogEditorDialog.Text("Конечная точка", 200); var duration = CatalogEditorDialog.Text("Время в пути", 100);
        var description = CatalogEditorDialog.Text("Описание маршрута", 2000); var type = CatalogEditorDialog.Text("Тип маршрута", 100);
        var stops = new NumericUpDown { Minimum = 0, Maximum = 10000, Increment = 1 };
        var length = new NumericUpDown { Minimum = 0, Maximum = 1000000, Increment = 0.1m };
        AutomationProperties.SetName(stops, "Количество остановок"); AutomationProperties.SetName(length, "Длина маршрута, км");
        var bus = new ComboBox { ItemsSource = buses, ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<BusRow>((r, _) => new TextBlock { Text = $"{r?.Model} · {r?.RegistrationNumber}" }) };
        var driver = new ComboBox { ItemsSource = drivers.Items, ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<DriverRow>((r, _) => new TextBlock { Text = $"{r?.Surname} {r?.Name}" }) };
        AutomationProperties.SetName(bus, "Автобус"); AutomationProperties.SetName(driver, "Водитель");
        var active = new CheckBox { Content = "Активен" };
        var attempt = _drafts.Get(original?.RouteId ?? 0, () => new CatalogEditorAttempt<RouteRow, RouteDraft>(original, DraftFrom, r => r.Revision, Validate,
            (d, revision, id, cancellation) => original == null
                ? ApplicationMutationClient.ExecuteAsync("routes.create", new CreateRouteCommand(d.StartPoint, d.EndPoint, d.DriverId, d.BusId, d.TravelTime, d.IsActive, d.RouteNumber, d.StopCount, d.RouteLength, d.RouteDescription, d.RouteType), id, origin, token, cancellation)
                : ApplicationMutationClient.ExecuteAsync("routes.update", new UpdateRouteCommand(original.RouteId, revision, d.StartPoint, d.EndPoint, d.DriverId, d.BusId, d.TravelTime, d.IsActive, d.RouteNumber, d.StopCount, d.RouteLength, d.RouteDescription, d.RouteType), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, RouteRow>("routes.get", new(original!.RouteId), cancellation), original?.RouteId,
            "Маршрут изменён или связан с билетами и расписаниями; проверьте автобус и водителя. Черновик сохранён."));
        return await CatalogEditorDialog.ShowAsync(host, original == null ? "Добавить маршрут" : "Редактировать маршрут", attempt,
            [number, start, end, duration, stops, length, description, type, bus, driver, active],
            d => { d.RouteNumber = number.Text ?? ""; d.StartPoint = start.Text ?? ""; d.EndPoint = end.Text ?? ""; d.TravelTime = duration.Text ?? "";
                d.StopCount = (uint)(stops.Value ?? 0); d.RouteLength = (double)(length.Value ?? 0); d.RouteDescription = description.Text ?? ""; d.RouteType = type.Text ?? "";
                d.BusId = (bus.SelectedItem as BusRow)?.BusId ?? 0; d.DriverId = (driver.SelectedItem as DriverRow)?.EmployeeId ?? 0; d.IsActive = active.IsChecked == true; },
            d => { number.Text = d.RouteNumber; start.Text = d.StartPoint; end.Text = d.EndPoint; duration.Text = d.TravelTime; stops.Value = d.StopCount;
                length.Value = double.IsFinite(d.RouteLength) && d.RouteLength is >= 0 and <= 1000000 ? (decimal)d.RouteLength : null;
                description.Text = d.RouteDescription; type.Text = d.RouteType; bus.SelectedItem = buses.Find(r => r.BusId == d.BusId);
                driver.SelectedItem = Array.Find(drivers.Items, r => r.EmployeeId == d.DriverId); active.IsChecked = d.IsActive; }, canSubmit, ct, notice);
    }
}
