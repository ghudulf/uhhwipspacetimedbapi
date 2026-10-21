using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

public sealed class MaintenanceDraft
{
    public uint BusId { get; set; }
    public DateTimeOffset? LastServiceDate { get; set; }
    public DateTimeOffset? NextServiceDate { get; set; }
    public string ServiceEngineer { get; set; } = "";
    public string FoundIssues { get; set; } = "";
    public string Roadworthiness { get; set; } = "";
    public string MaintenanceType { get; set; } = "";
    public string Mileage { get; set; } = "";
}

public sealed class MaintenanceEditorService(ControlDialogHost host)
{
    public static string? Validate(MaintenanceDraft draft)
    {
        if (draft.BusId == 0) return "Выберите автобус.";
        if (string.IsNullOrWhiteSpace(draft.ServiceEngineer)) return "Имя инженера обязательно.";
        if (draft.ServiceEngineer.Length > 200) return "Имя инженера не должно превышать 200 символов.";
        if (draft.FoundIssues.Length > 4000) return "Описание проблем не должно превышать 4000 символов.";
        if (draft.Roadworthiness.Length > 200) return "Состояние не должно превышать 200 символов.";
        if (draft.MaintenanceType.Length > 200) return "Тип обслуживания не должен превышать 200 символов.";
        if (draft.Mileage.Length > 100) return "Пробег не должен превышать 100 символов.";
        if (draft.LastServiceDate == null || draft.NextServiceDate == null) return "Укажите даты обслуживания.";
        if (draft.LastServiceDate.Value.ToUnixTimeMilliseconds() < 0 || draft.NextServiceDate.Value.ToUnixTimeMilliseconds() < 0)
            return "Дата обслуживания не может быть раньше 01.01.1970.";
        if (draft.NextServiceDate < draft.LastServiceDate) return "Следующее обслуживание не может быть раньше предыдущего.";
        return null;
    }
    public static MaintenanceDraft DraftFrom(MaintenanceRow? row) => new()
    {
        BusId = row?.BusId ?? 0, LastServiceDate = row == null ? DateTimeOffset.Now.Date : row.LastServiceDate,
        NextServiceDate = row == null ? DateTimeOffset.Now.Date.AddMonths(3) : row.NextServiceDate,
        ServiceEngineer = row?.ServiceEngineer ?? "", FoundIssues = row?.FoundIssues ?? "", Roadworthiness = row?.Roadworthiness ?? "",
        MaintenanceType = row?.MaintenanceType ?? "", Mileage = row?.MileageThreshold ?? ""
    };
    public async Task<bool> ShowAsync(MaintenanceRow? original, Func<bool> canSubmit, CancellationToken ct)
    {
        var api = ApiClientService.Instance;
        var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed");
        var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        var buses = new List<BusRow>();
        for (var page = 1; ; page++)
        {
            var result = await ApplicationQueryClient.ReadAsync<BusQuery, BusPageResult>("buses.list", new(page, 100), ct);
            buses.AddRange(result.Items);
            if (result.Items.Length == 0 || (long)page * 100 >= result.TotalCount) break;
        }
        ct.ThrowIfCancellationRequested();
        if (api.CurrentBaseUrl != origin || api.AuthToken != token) throw new ApplicationCommandException("session_changed");
        var bus = new ComboBox { ItemsSource = buses, ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<BusRow>((row, _) => new TextBlock { Text = $"{row?.Model} · {row?.RegistrationNumber} (ID {row?.BusId})" }) };
        var last = new DatePicker(); var next = new DatePicker();
        AutomationProperties.SetName(bus, "Автобус"); AutomationProperties.SetName(last, "Дата обслуживания"); AutomationProperties.SetName(next, "Следующее обслуживание");
        var engineer = CatalogEditorDialog.Text("Инженер", 200);
        var issues = CatalogEditorDialog.Text("Найденные проблемы", 4000);
        var roadworthiness = CatalogEditorDialog.Text("Состояние", 200);
        var type = CatalogEditorDialog.Text("Тип обслуживания", 200);
        var mileage = CatalogEditorDialog.Text("Пробег", 100);
        var attempt = new CatalogEditorAttempt<MaintenanceRow, MaintenanceDraft>(original, DraftFrom, row => row.Revision, Validate,
            (draft, revision, id, cancellation) => original == null
                ? ApplicationMutationClient.ExecuteAsync("maintenance.create", new CreateMaintenanceCommand(draft.BusId, Milliseconds(draft.LastServiceDate), draft.ServiceEngineer, draft.FoundIssues,
                    Milliseconds(draft.NextServiceDate), draft.Roadworthiness, draft.MaintenanceType), id, origin, token, cancellation)
                : ApplicationMutationClient.ExecuteAsync("maintenance.update", new UpdateMaintenanceCommand(original.MaintenanceId, revision, draft.BusId, Milliseconds(draft.LastServiceDate), draft.ServiceEngineer,
                    draft.FoundIssues, Milliseconds(draft.NextServiceDate), draft.Roadworthiness, draft.MaintenanceType, draft.Mileage), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, MaintenanceRow>("maintenance.get", new(original!.MaintenanceId), cancellation));
        var fields = new List<Control> { bus, last, next, engineer, issues, roadworthiness, type };
        if (original != null) fields.Add(mileage);
        return await CatalogEditorDialog.ShowAsync(host, original == null ? "Добавить запись обслуживания" : "Редактировать запись обслуживания", attempt, fields.ToArray(),
            draft =>
            {
                draft.BusId = (bus.SelectedItem as BusRow)?.BusId ?? 0; draft.LastServiceDate = last.SelectedDate; draft.NextServiceDate = next.SelectedDate;
                draft.ServiceEngineer = engineer.Text ?? ""; draft.FoundIssues = issues.Text ?? ""; draft.Roadworthiness = roadworthiness.Text ?? ""; draft.MaintenanceType = type.Text ?? ""; draft.Mileage = mileage.Text ?? "";
            },
            draft =>
            {
                bus.SelectedItem = buses.Find(row => row.BusId == draft.BusId); last.SelectedDate = draft.LastServiceDate; next.SelectedDate = draft.NextServiceDate;
                engineer.Text = draft.ServiceEngineer; issues.Text = draft.FoundIssues; roadworthiness.Text = draft.Roadworthiness; type.Text = draft.MaintenanceType; mileage.Text = draft.Mileage;
            }, canSubmit, ct);
    }
    private static ulong Milliseconds(DateTimeOffset? value) => checked((ulong)value!.Value.ToUnixTimeMilliseconds());
}
