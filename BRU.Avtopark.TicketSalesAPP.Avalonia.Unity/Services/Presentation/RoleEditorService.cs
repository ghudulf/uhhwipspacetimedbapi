using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

public sealed class RoleDraft
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int LegacyRoleId { get; set; }
    public uint Priority { get; set; }
    public uint[] PermissionIds { get; set; } = [];
    public bool IntegralCounts { get; set; } = true;
    public bool ReplacePermissions { get; set; }
}
public sealed class RoleEditorService(ControlDialogHost host, Func<bool> canGrant, CatalogEditorDrafts<RoleRow, RoleDraft>? drafts = null)
{
    private readonly CatalogEditorDrafts<RoleRow, RoleDraft> _drafts = drafts ?? new();
    public static RoleDraft DraftFrom(RoleRow? row) => new() { Name = row?.Name ?? "", Description = row?.Description ?? "", LegacyRoleId = row?.LegacyRoleId ?? 0, Priority = row?.Priority ?? 0, PermissionIds = row?.PermissionIds.ToArray() ?? [] };
    public static string? Validate(RoleDraft d) => string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 200 ? "Укажите имя роли (до 200 символов)."
        : d.Description.Length > 2000 || d.Description.Length > 0 && string.IsNullOrWhiteSpace(d.Description) ? "Описание: до 2000 символов."
        : !d.IntegralCounts || d.PermissionIds.Length > 500 || d.PermissionIds.Any(id => id == 0) || d.PermissionIds.Distinct().Count() != d.PermissionIds.Length ? "Проверьте целые ID/приоритет и уникальные разрешения (до 500)." : null;
    public async Task<bool> ShowAsync(RoleRow? original, Func<bool> canSubmit, CancellationToken ct)
    {
        var readOnly = original is { IsSystem: true } || original?.Name == "Administrator";
        var api = ApiClientService.Instance; var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed"); var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        List<PermissionRow> permissions = []; var visible = true; var notice = "Изменение набора разрешений выполняется атомарно. Нельзя выдавать права сверх своих.";
        try { permissions = await CatalogEditorLookups.CatalogAsync<PermissionRow>("permissions.list", ct); }
        catch (ApplicationCommandException ex) when (ex.Code == "forbidden") { visible = false; notice += " Нет права просмотра разрешений; существующий набор сохраняется."; }
        ct.ThrowIfCancellationRequested(); if (api.AuthToken != token || api.CurrentBaseUrl != origin) throw new ApplicationCommandException("session_changed");
        var name = CatalogEditorDialog.Text("Название роли", 200); var description = CatalogEditorDialog.Text("Описание роли", 2000);
        var legacy = new NumericUpDown { Minimum = int.MinValue, Maximum = int.MaxValue, Increment = 1 }; var priority = new NumericUpDown { Minimum = 0, Maximum = uint.MaxValue, Increment = 1 };
        AutomationProperties.SetName(legacy, "Legacy ID роли"); AutomationProperties.SetName(priority, "Приоритет роли");
        var choices = new ListBox { ItemsSource = permissions, SelectionMode = SelectionMode.Multiple, MaxHeight = 250,
            ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<PermissionRow>((p, _) => new TextBlock { Text = $"{p?.Name} — {p?.Description}" }) };
        AutomationProperties.SetName(choices, "Набор разрешений роли");
        foreach (var control in new Control[] { name, description, legacy, priority }) control.IsEnabled = !readOnly;
        choices.IsEnabled = !readOnly && visible && canGrant();
        var attempt = _drafts.Get(original?.Id ?? 0, () => new CatalogEditorAttempt<RoleRow, RoleDraft>(original, DraftFrom, r => r.Revision, Validate,
            (d, revision, id, cancellation) => original == null
                ? ApplicationMutationClient.ExecuteAsync("roles.create", new CreateRoleCommand(d.Name, d.Description, d.LegacyRoleId, false, d.Priority, d.ReplacePermissions ? d.PermissionIds : null), id, origin, token, cancellation)
                : ApplicationMutationClient.ExecuteAsync("roles.update", new UpdateRoleCommand(original.Id, d.Name, d.Description, d.LegacyRoleId, d.Priority, revision, d.ReplacePermissions ? d.PermissionIds : null), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, RoleRow>("roles.get", new(original!.Id), cancellation), original?.Id,
            failureMessage: code => $"[{code}] Роль уже существует, запись изменилась или сервер запретил выдачу прав сверх ваших. Administrator и системные роли не изменяются. Черновик сохранён."));
        return await CatalogEditorDialog.ShowAsync(host, readOnly ? "Роль только для чтения" : original == null ? "Добавить роль" : "Редактировать роль", attempt,
            [name, description, legacy, priority, choices],
            d => { d.Name = name.Text ?? ""; d.Description = description.Text ?? ""; d.IntegralCounts = legacy.Value is { } l && l == decimal.Truncate(l) && priority.Value is { } p && p == decimal.Truncate(p);
                d.LegacyRoleId = (int)(legacy.Value ?? 0); d.Priority = (uint)(priority.Value ?? 0);
                d.ReplacePermissions = visible && canGrant(); if (d.ReplacePermissions) d.PermissionIds = choices.SelectedItems!.Cast<PermissionRow>().Select(p => p.Id).ToArray(); },
            d => { name.Text = d.Name; description.Text = d.Description; legacy.Value = d.LegacyRoleId; priority.Value = d.Priority;
                choices.SelectedItems!.Clear(); foreach (var p in permissions.Where(p => d.PermissionIds.Contains(p.Id))) choices.SelectedItems.Add(p); },
            () => !readOnly && canSubmit(), ct, readOnly ? "Administrator и системные роли доступны только для чтения." : notice);
    }
}
