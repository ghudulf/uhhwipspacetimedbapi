using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

public sealed class RoleAssignmentDraft { public uint RoleId { get; set; } public bool Remove { get; set; } }
public sealed class UserActivationDraft { public bool Active { get; set; } }
public sealed class RoleAssignmentService(ControlDialogHost host)
{
    private readonly CatalogEditorDrafts<UserRow, RoleAssignmentDraft> _assign = new();
    private readonly CatalogEditorDrafts<UserRow, UserActivationDraft> _active = new();
    public async Task<bool> ShowAsync(UserRow user, RoleRow? role, bool remove, Func<bool> canSubmit, CancellationToken ct)
    {
        if (role == null || role.IsSystem || role.Name == "Administrator") return false;
        var api = ApiClientService.Instance; var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed"); var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        var attempt = _assign.Get(user.Id, () => new CatalogEditorAttempt<UserRow, RoleAssignmentDraft>(user,
            _ => new() { RoleId = role.Id, Remove = remove }, r => r.Revision, d => d.RoleId == 0 ? "Выберите роль." : null,
            (d, revision, id, cancellation) => d.Remove
                ? ApplicationMutationClient.ExecuteAsync("users.remove-role", new RemoveUserRoleCommand(user.Id, d.RoleId, revision), id, origin, token, cancellation)
                : ApplicationMutationClient.ExecuteAsync("users.assign-role", new AssignUserRoleCommand(user.Id, d.RoleId, revision), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, UserRow>("users.get", new(user.Id), cancellation), user.Id, failureMessage: UserEditorService.Failure));
        var choice = new NumericUpDown { Minimum = 1, Maximum = uint.MaxValue, IsEnabled = false }; AutomationProperties.SetName(choice, "ID роли (сохранённый выбор)");
        var action = new CheckBox { Content = "Удалить назначение роли", IsEnabled = false };
        return await CatalogEditorDialog.ShowAsync(host, $"Роль пользователя {user.Login}", attempt, [new TextBlock { Text = $"Выбрана роль {role.Name}. При неизвестном результате сохраняется прежний выбор, а не новая операция." }, choice, action],
            _ => { }, d => { choice.Value = d.RoleId; action.IsChecked = d.Remove; }, canSubmit, ct);
    }
    public async Task<bool> ShowActivationAsync(UserRow user, Func<bool> canSubmit, CancellationToken ct)
    {
        var api = ApiClientService.Instance; var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed"); var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        var active = new CheckBox { Content = "Пользователь активен" }; AutomationProperties.SetName(active, "Пользователь активен");
        var attempt = _active.Get(user.Id, () => new CatalogEditorAttempt<UserRow, UserActivationDraft>(user, r => new() { Active = r?.IsActive ?? true }, r => r.Revision, _ => null,
            (d, revision, id, cancellation) => ApplicationMutationClient.ExecuteAsync("users.set-active", new SetUserActiveCommand(user.Id, d.Active, revision), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, UserRow>("users.get", new(user.Id), cancellation), user.Id, failureMessage: UserEditorService.Failure));
        return await CatalogEditorDialog.ShowAsync(host, $"Активация пользователя {user.Login}", attempt, [active], d => d.Active = active.IsChecked == true, d => active.IsChecked = d.Active, canSubmit, ct,
            "Сервер запрещает отключать себя или последнего активного администратора.");
    }
}
