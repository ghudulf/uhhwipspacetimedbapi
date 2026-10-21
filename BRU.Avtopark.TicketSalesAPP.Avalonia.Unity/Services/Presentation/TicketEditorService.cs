using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

public sealed class TicketDraft
{
    public uint RouteId { get; set; }
    public double? TicketPrice { get; set; }
    public uint SeatNumber { get; set; } = 1;
    public string PaymentMethod { get; set; } = "Cash";
}

public sealed class TicketEditorService(ControlDialogHost host)
{
    private readonly CatalogEditorDrafts<TicketRow, TicketDraft> _drafts = new();
    private List<RouteRow> _routes = [];
    private List<BusRow> _buses = [];
    public static TicketDraft DraftFrom(TicketRow? row) => new()
    {
        RouteId = row?.RouteId ?? 0, TicketPrice = row == null ? 0 : row.TicketPrice.HasValue ? (double)row.TicketPrice.Value : null,
        SeatNumber = row?.SeatNumber ?? 1, PaymentMethod = row?.PaymentMethod ?? "Cash"
    };
    public static string? Validate(TicketDraft draft, uint? capacity = null)
    {
        if (draft.RouteId == 0) return "Выберите маршрут. Если список пуст, проверьте маршруты и право их просмотра.";
        if (!draft.TicketPrice.HasValue || !double.IsFinite(draft.TicketPrice.Value) || draft.TicketPrice < 0 || draft.TicketPrice > 1000000000000) return "Цена билета должна быть конечным числом от 0 до 1000000000000.";
        if (draft.SeatNumber == 0) return "Номер места должен быть целым положительным числом.";
        if (capacity.HasValue && draft.SeatNumber > capacity.Value) return $"Номер места превышает вместимость автобуса ({capacity.Value}). Выберите место в пределах вместимости.";
        return null;
    }
    public async Task<bool> ShowAsync(TicketRow? original, Func<bool> canSubmit, CancellationToken ct)
    {
        var api = ApiClientService.Instance; var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed");
        var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        List<RouteRow> routes = []; List<BusRow> buses = []; var notice = "";
        try { routes = await CatalogEditorLookups.CatalogAsync<RouteRow>("routes.list", ct); }
        catch (ApplicationCommandException ex) when (ex.Code == "forbidden") { notice = "Нет права просмотра маршрутов. "; }
        if (routes.Count == 0) notice += "Нет доступных маршрутов. ";
        try { buses = await CatalogEditorLookups.BusesAsync(ct); }
        catch (ApplicationCommandException ex) when (ex.Code == "forbidden") { notice += "Вместимость автобуса недоступна; номер места проверит сервер. "; }
        ct.ThrowIfCancellationRequested();
        if (api.AuthToken != token || api.CurrentBaseUrl != origin) throw new ApplicationCommandException("session_changed");
        _routes = routes; _buses = buses;
        var route = new ComboBox { ItemsSource = routes, ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<RouteRow>((r, _) => new TextBlock { Text = $"{r?.RouteNumber} ({r?.StartPoint} — {r?.EndPoint})" }) };
        var price = new NumericUpDown { Minimum = 0, Maximum = 1000000000000m, Increment = 0.5m, FormatString = "N2" };
        var seat = new NumericUpDown { Minimum = 1, Maximum = uint.MaxValue, Increment = 1 };
        AutomationProperties.SetName(route, "Маршрут"); AutomationProperties.SetName(price, "Цена билета, BYN"); AutomationProperties.SetName(seat, "Номер места");
        var attempt = _drafts.Get(original?.Id ?? 0, () => new CatalogEditorAttempt<TicketRow, TicketDraft>(original, DraftFrom, r => r.Revision,
            d => Validate(d, _buses.Find(b => b.BusId == _routes.Find(r => r.RouteId == d.RouteId)?.BusId)?.Capacity),
            (d, revision, id, cancellation) => original == null
                ? ApplicationMutationClient.ExecuteAsync("tickets.create", new CreateTicketCommand(d.RouteId, d.TicketPrice!.Value, d.SeatNumber, d.PaymentMethod), id, origin, token, cancellation)
                : ApplicationMutationClient.ExecuteAsync("tickets.update", new UpdateTicketCommand(original.Id, d.RouteId, d.TicketPrice, d.SeatNumber, d.PaymentMethod, ExpectedRevision: revision), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, TicketRow>("tickets.get", new(original!.Id), cancellation), original?.Id,
            "Билет связан с продажей, запись изменена или место недоступно. Проверьте маршрут, цену и номер места. Черновик сохранён."));
        return await CatalogEditorDialog.ShowAsync(host, original == null ? "Добавить билет" : "Редактировать билет", attempt, [route, price, seat],
            d => { d.RouteId = (route.SelectedItem as RouteRow)?.RouteId ?? 0; d.TicketPrice = price.Value.HasValue ? (double)price.Value.Value : null;
                d.SeatNumber = seat.Value is { } number && number == decimal.Truncate(number) && number is >= 1 and <= uint.MaxValue ? (uint)number : 0; },
            d => { route.SelectedItem = routes.Find(r => r.RouteId == d.RouteId); price.Value = d.TicketPrice is { } value && double.IsFinite(value) && value is >= 0 and <= 1000000000000 ? (decimal)value : null; seat.Value = d.SeatNumber; },
            canSubmit, ct, notice);
    }
}
