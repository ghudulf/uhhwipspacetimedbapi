using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

public sealed class EmployeeDraft
{
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public string Patronym { get; set; } = "";
    public uint JobId { get; set; }
}

public sealed class EmployeeEditorService(ControlDialogHost host)
{
    public static string? Validate(EmployeeDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name)) return "Имя сотрудника обязательно.";
        if (draft.Name.Length > 200) return "Имя сотрудника не должно превышать 200 символов.";
        if (string.IsNullOrWhiteSpace(draft.Surname)) return "Фамилия сотрудника обязательна.";
        if (draft.Surname.Length > 200) return "Фамилия сотрудника не должна превышать 200 символов.";
        if (draft.Patronym.Length > 200) return "Отчество не должно превышать 200 символов.";
        if (draft.JobId == 0) return "Выберите должность сотрудника.";
        return null;
    }
    public static EmployeeDraft DraftFrom(EmployeeRow? row) => new()
    {
        Name = row?.Name ?? "", Surname = row?.Surname ?? "", Patronym = row?.Patronym ?? "", JobId = row?.JobId ?? 0
    };
    public async Task<bool> ShowAsync(EmployeeRow? original, Func<bool> canSubmit, CancellationToken ct)
    {
        var api = ApiClientService.Instance;
        var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed");
        var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        var jobs = new List<JobRow>();
        for (var page = 1; ; page++)
        {
            var result = await ApplicationQueryClient.ReadAsync<PageRequest, CatalogPageResult<JobRow>>("jobs.list", new(page, 100), ct);
            jobs.AddRange(result.Items);
            if (result.Items.Length == 0 || (long)page * 100 >= result.TotalCount) break;
        }
        ct.ThrowIfCancellationRequested();
        if (api.CurrentBaseUrl != origin || api.AuthToken != token) throw new ApplicationCommandException("session_changed");
        var name = CatalogEditorDialog.Text("Имя", 200);
        var surname = CatalogEditorDialog.Text("Фамилия", 200);
        var patronym = CatalogEditorDialog.Text("Отчество", 200);
        var job = new ComboBox { ItemsSource = jobs, ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<JobRow>((row, _) => new TextBlock { Text = row?.JobTitle }) };
        AutomationProperties.SetName(job, "Должность");
        var attempt = new CatalogEditorAttempt<EmployeeRow, EmployeeDraft>(original, DraftFrom, row => row.Revision, Validate,
            (draft, revision, id, cancellation) => original == null
                ? ApplicationMutationClient.ExecuteAsync("employees.create", new CreateEmployeeCommand(draft.Name, draft.Surname, draft.Patronym, draft.JobId), id, origin, token, cancellation)
                : ApplicationMutationClient.ExecuteAsync("employees.update", new UpdateEmployeeCommand(original.EmployeeId, revision, draft.Name, draft.Surname, draft.Patronym, draft.JobId), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, EmployeeRow>("employees.get", new(original!.EmployeeId), cancellation));
        return await CatalogEditorDialog.ShowAsync(host, original == null ? "Добавить сотрудника" : "Редактировать сотрудника", attempt,
            [surname, name, patronym, job],
            draft => { draft.Name = name.Text ?? ""; draft.Surname = surname.Text ?? ""; draft.Patronym = patronym.Text ?? ""; draft.JobId = (job.SelectedItem as JobRow)?.JobId ?? 0; },
            draft => { name.Text = draft.Name; surname.Text = draft.Surname; patronym.Text = draft.Patronym; job.SelectedItem = jobs.Find(row => row.JobId == draft.JobId); }, canSubmit, ct);
    }
}
