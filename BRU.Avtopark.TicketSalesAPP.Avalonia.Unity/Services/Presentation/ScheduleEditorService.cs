using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

public sealed class ScheduleDraft
{
    public uint RouteId { get; set; }
    public DateTimeOffset? DepartureTime { get; set; }
    public DateTimeOffset? ArrivalTime { get; set; }
    public double? Price { get; set; }
    public uint AvailableSeats { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsRecurring { get; set; }
    public string StartPoint { get; set; } = "";
    public string EndPoint { get; set; } = "";
    public string[] DaysOfWeek { get; set; } = [];
    public string[] RouteStops { get; set; } = [];
    public string[] EstimatedStopTimes { get; set; } = [];
    public double[] StopDistances { get; set; } = [];
    public uint StopDurationMinutes { get; set; }
    public string Notes { get; set; } = "";
    public string[] BusTypes { get; set; } = [];
}

public sealed class ScheduleEditorService(ControlDialogHost host)
{
    private readonly CatalogEditorDrafts<ScheduleRow, ScheduleDraft> _drafts = new();
    public static ScheduleDraft DraftFrom(ScheduleRow? row) => new()
    {
        RouteId = row?.RouteId ?? 0, DepartureTime = row == null ? DateTimeOffset.UtcNow : row.DepartureTime,
        ArrivalTime = row == null ? DateTimeOffset.UtcNow.AddHours(1) : row.ArrivalTime,
        Price = row == null ? 0 : row.Price.HasValue ? (double)row.Price.Value : null, AvailableSeats = row?.AvailableSeats ?? 0,
        IsActive = row?.IsActive ?? true, IsRecurring = row?.IsRecurring ?? false,
        StartPoint = row?.StartPoint ?? "", EndPoint = row?.EndPoint ?? "", DaysOfWeek = row?.DaysOfWeek?.ToArray() ?? [],
        RouteStops = row?.RouteStops?.ToArray() ?? [], EstimatedStopTimes = row?.EstimatedStopTimes?.ToArray() ?? [],
        StopDistances = row?.StopDistances?.ToArray() ?? [], StopDurationMinutes = row?.StopDurationMinutes ?? 0,
        Notes = row?.Notes ?? "", BusTypes = row?.BusTypes?.ToArray() ?? []
    };
    public static string? Validate(ScheduleDraft d)
    {
        if (d.RouteId == 0) return "Выберите маршрут. Если список пуст, проверьте право просмотра маршрутов.";
        if (!d.DepartureTime.HasValue || !d.ArrivalTime.HasValue || d.DepartureTime < DateTimeOffset.UnixEpoch || d.ArrivalTime < d.DepartureTime) return "Укажите даты и время: прибытие не может быть раньше отправления.";
        if (!d.Price.HasValue || !double.IsFinite(d.Price.Value) || d.Price < 0 || d.Price > 1e12) return "Цена должна быть конечным числом от 0 до 1000000000000.";
        if (string.IsNullOrWhiteSpace(d.StartPoint) || string.IsNullOrWhiteSpace(d.EndPoint) || d.StartPoint.Length > 200 || d.EndPoint.Length > 200) return "Укажите начальную и конечную остановки (до 200 символов).";
        if (d.DaysOfWeek.Length > 7 || d.DaysOfWeek.Any(day => !Enum.TryParse<DayOfWeek>(day, true, out var value) || !Enum.IsDefined(value)) || d.DaysOfWeek.Distinct(StringComparer.OrdinalIgnoreCase).Count() != d.DaysOfWeek.Length || d.IsRecurring && d.DaysOfWeek.Length == 0) return "Выберите уникальные дни недели для повторяющегося расписания (Monday — Sunday).";
        if (d.RouteStops.Length < 2 || d.RouteStops.Length > 200 || d.RouteStops.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 200)) return "Укажите от 2 до 200 остановок, по одной в строке (до 200 символов).";
        if (d.EstimatedStopTimes.Length > 0 && (d.EstimatedStopTimes.Length != d.RouteStops.Length || d.EstimatedStopTimes.Any(t => !TimeSpan.TryParseExact(t, @"hh\:mm", CultureInfo.InvariantCulture, out _)))) return "Время каждой остановки задаётся в формате HH:mm; число времён должно совпадать с числом остановок.";
        if (d.StopDistances.Length > 0 && (d.StopDistances.Length != d.RouteStops.Length || d.StopDistances.Any(v => !double.IsFinite(v) || v < 0 || v > 1e9))) return "Расстояния должны соответствовать остановкам и быть конечными числами от 0 до 1000000000.";
        if (d.Notes.Length > 4000 || d.Notes.Length > 0 && string.IsNullOrWhiteSpace(d.Notes) || d.BusTypes.Length > 200 || d.BusTypes.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 200)) return "Проверьте примечание (до 4000 символов) и типы автобусов (до 200 строк по 200 символов).";
        return null;
    }
    public async Task<bool> ShowAsync(ScheduleRow? original, Func<bool> canSubmit, CancellationToken ct)
    {
        var api = ApiClientService.Instance; var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed");
        var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        List<RouteRow> routes = []; var notice = "Даты и время в UTC, формат yyyy-MM-dd HH:mm. Списки: одна запись в строке.";
        try { routes = await CatalogEditorLookups.CatalogAsync<RouteRow>("routes.list", ct); }
        catch (ApplicationCommandException ex) when (ex.Code == "forbidden") { notice += " Нет права просмотра маршрутов."; }
        if (routes.Count == 0) notice += " Нет доступных маршрутов.";
        ct.ThrowIfCancellationRequested();
        if (api.AuthToken != token || api.CurrentBaseUrl != origin) throw new ApplicationCommandException("session_changed");
        var route = new ComboBox { ItemsSource = routes, ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<RouteRow>((r, _) => new TextBlock { Text = $"{r?.RouteNumber} {r?.StartPoint} — {r?.EndPoint}" }) };
        AutomationProperties.SetName(route, "Маршрут");
        var departure = CatalogEditorDialog.Text("Отправление UTC, yyyy-MM-dd HH:mm", 40); var arrival = CatalogEditorDialog.Text("Прибытие UTC, yyyy-MM-dd HH:mm", 40);
        var start = CatalogEditorDialog.Text("Начальная остановка", 200); var end = CatalogEditorDialog.Text("Конечная остановка", 200);
        TextBox Lines(string label) { var box = CatalogEditorDialog.Text(label, 50000); box.AcceptsReturn = true; return box; }
        var days = Lines("Дни недели, Monday — Sunday"); var stops = Lines("Остановки"); var times = Lines("Время остановок, HH:mm"); var distances = Lines("Расстояния остановок, км"); var types = Lines("Типы автобусов"); var notes = CatalogEditorDialog.Text("Примечание", 4000);
        NumericUpDown Number(string label, decimal max) { var n = new NumericUpDown { Minimum = 0, Maximum = max, Increment = 1 }; AutomationProperties.SetName(n, label); return n; }
        var price = Number("Цена, BYN", 1000000000000m); var seats = Number("Доступные места", uint.MaxValue); var duration = Number("Стоянка, минут", uint.MaxValue);
        var active = new CheckBox { Content = "Активен" }; var recurring = new CheckBox { Content = "Повторяющееся" };
        var attempt = _drafts.Get(original?.Id ?? 0, () => new CatalogEditorAttempt<ScheduleRow, ScheduleDraft>(original, DraftFrom, r => r.Revision, Validate,
            (d, revision, id, cancellation) => original == null
                ? ApplicationMutationClient.ExecuteAsync("schedules.create", new CreateScheduleCommand(d.RouteId, Millis(d.DepartureTime), d.Price!.Value, d.AvailableSeats, d.DaysOfWeek, d.StartPoint, d.EndPoint, d.RouteStops, Millis(d.ArrivalTime), d.StopDurationMinutes, d.IsRecurring, d.EstimatedStopTimes, d.StopDistances, d.Notes, d.BusTypes, d.IsActive), id, origin, token, cancellation)
                : ApplicationMutationClient.ExecuteAsync("schedules.update", new UpdateScheduleCommand(original.Id, d.RouteId, Millis(d.DepartureTime), d.Price, d.AvailableSeats, d.DaysOfWeek, d.StartPoint, d.EndPoint, d.RouteStops, Millis(d.ArrivalTime), d.StopDurationMinutes, d.IsRecurring, d.EstimatedStopTimes, d.StopDistances, d.Notes, d.BusTypes, revision, d.IsActive), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, ScheduleRow>("schedules.get", new(original!.Id), cancellation), original?.Id,
            "Расписание изменено или связано с продажами. Черновик сохранён; загрузите актуальную запись."));
        return await CatalogEditorDialog.ShowAsync(host, original == null ? "Добавить расписание" : "Редактировать расписание", attempt,
            [route, departure, arrival, start, end, price, seats, active, recurring, days, stops, times, distances, duration, types, notes],
            d => { d.RouteId = (route.SelectedItem as RouteRow)?.RouteId ?? 0; d.DepartureTime = ParseDate(departure.Text); d.ArrivalTime = ParseDate(arrival.Text);
                d.StartPoint = start.Text ?? ""; d.EndPoint = end.Text ?? ""; d.Price = price.Value.HasValue ? (double)price.Value.Value : null;
                d.AvailableSeats = Integer(seats); d.StopDurationMinutes = Integer(duration); d.IsActive = active.IsChecked == true; d.IsRecurring = recurring.IsChecked == true;
                d.DaysOfWeek = Split(days.Text); d.RouteStops = Split(stops.Text); d.EstimatedStopTimes = Split(times.Text); d.BusTypes = Split(types.Text); d.Notes = notes.Text ?? "";
                d.StopDistances = Split(distances.Text).Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray(); },
            d => { route.SelectedItem = routes.Find(r => r.RouteId == d.RouteId); departure.Text = Format(d.DepartureTime); arrival.Text = Format(d.ArrivalTime);
                start.Text = d.StartPoint; end.Text = d.EndPoint; price.Value = d.Price is { } v && double.IsFinite(v) && v is >= 0 and <= 1e12 ? (decimal)v : null;
                seats.Value = d.AvailableSeats; duration.Value = d.StopDurationMinutes; active.IsChecked = d.IsActive; recurring.IsChecked = d.IsRecurring;
                days.Text = string.Join('\n', d.DaysOfWeek); stops.Text = string.Join('\n', d.RouteStops); times.Text = string.Join('\n', d.EstimatedStopTimes);
                distances.Text = string.Join('\n', d.StopDistances.Select(v => v.ToString(CultureInfo.InvariantCulture))); types.Text = string.Join('\n', d.BusTypes); notes.Text = d.Notes; }, canSubmit, ct, notice);
    }
    private static uint Integer(NumericUpDown field) => field.Value is { } v && v == decimal.Truncate(v) ? (uint)v : 0;
    private static ulong Millis(DateTimeOffset? date) => (ulong)date!.Value.ToUnixTimeMilliseconds();
    private static string[] Split(string? text) => string.IsNullOrEmpty(text) ? [] : text.Replace("\r", "").Split('\n');
    private static string? Format(DateTimeOffset? date) => date?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private static DateTimeOffset? ParseDate(string? text) => DateTimeOffset.TryParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ? value : null;
}
