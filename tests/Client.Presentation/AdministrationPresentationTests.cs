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
using System.Threading.Channels;

internal static class AdministrationPresentationTests
{
    internal static readonly PermissionRow Permission = new(7, "users.view", "Просмотр", "Пользователи", true, 1);
    internal static readonly RoleRow Role = new(8, 2, "Диспетчер", "Описание", false, 4, true, [7], ["users.view"], 0, 1, [Permission]);
    internal static readonly UserRow User = new(9, "synthetic-identity", "VisibleLogin", "mail@example.test", "", true, null, null, ["Диспетчер"], 1, [8], [Permission]);
    internal static readonly AccessSnapshot Access = new(true, false, ["Synthetic"], ["assign_roles", "grant_permissions", "users.edit", "roles.edit", "permissions.edit"]);
    public static async Task RunAsync(CancellationToken stop)
    {
        await EmployeeMaintenancePresentationTests.ScreenAsync<UserRow, UserDisplayModel>((r, w, e, c, d) => new UserManagementViewModel(r, w, e, c, d), User, User with { Login = "Обновлено", Revision = 2 }, i => i.Login, "Обновлено", stop);
        await EmployeeMaintenancePresentationTests.ScreenAsync<RoleRow, RoleDisplayModel>((r, w, e, c, d) => new RoleManagementViewModel(r, w, e, c, d), Role, Role with { Name = "Обновлено", Revision = 2 }, i => i.Name, "Обновлено", stop);
        await EmployeeMaintenancePresentationTests.ScreenAsync<PermissionRow, PermissionDisplayModel>((r, w, e, c, d) => new PermissionManagementViewModel(r, w, e, c, d), Permission, Permission with { Name = "Обновлено", Revision = 2 }, i => i.Name, "Обновлено", stop);
        await EmployeeMaintenancePresentationTests.EditorAsync(User, User with { Revision = 7 }, UserEditorService.DraftFrom, r => r.Revision, UserEditorService.Validate, d => d.Email = "draft@example.test", d => d.Email, stop);
        await EmployeeMaintenancePresentationTests.EditorAsync(Role, Role with { Revision = 7 }, RoleEditorService.DraftFrom, r => r.Revision, RoleEditorService.Validate, d => d.Description = "Черновик", d => d.Description, stop);
        await EmployeeMaintenancePresentationTests.EditorAsync(Permission, Permission with { Revision = 7 }, PermissionEditorService.DraftFrom, r => r.Revision, PermissionEditorService.Validate, d => d.Description = "Черновик", d => d.Description, stop);
        foreach (var invalid in new Action<UserDraft>[] { d => d.Login = " ", d => d.Login = new('x', 201), d => d.Email = new('x', 321), d => d.PhoneNumber = new('x', 51) })
        { var d = UserEditorService.DraftFrom(User); invalid(d); Check(UserEditorService.Validate(d) != null, "invalid profile accepted"); }
        foreach (var invalid in new Action<RoleDraft>[] { d => d.Name = " ", d => d.Name = new('x', 201), d => d.Description = new('x', 2001), d => d.PermissionIds = [7, 7], d => d.PermissionIds = [0], d => d.IntegralCounts = false })
        { var d = RoleEditorService.DraftFrom(Role); invalid(d); Check(RoleEditorService.Validate(d) != null, "invalid role accepted"); }
        foreach (var invalid in new Action<PermissionDraft>[] { d => d.Name = " ", d => d.Name = new('x', 201), d => d.Description = new('x', 2001), d => d.Category = new('x', 201) })
        { var d = PermissionEditorService.DraftFrom(Permission); invalid(d); Check(PermissionEditorService.Validate(d) != null, "invalid permission accepted"); }
        Check(new UserDisplayModel(User).CreatedAtDisplay == "Не указано" && new UserDisplayModel(User).LastLoginAtDisplay == "Никогда", "missing user dates fabricated");
        Check(!typeof(UserDraft).GetProperties().Any(p => DebugPayload.IsCredential(p.Name)) && !typeof(UserRow).GetProperties().Any(p => DebugPayload.IsCredential(p.Name)), "credential stored in row/retained draft");
        var protectedRoles = new[] { Role with { IsSystem = true }, Role with { Name = "Administrator" } };
        foreach (var role in protectedRoles)
        {
            using var locked = new RoleManagementViewModel((_, _) => Task.FromResult(new CatalogPageResult<RoleRow>([role], 1, 100, 1, true, true, true)), edit: (_, _, _) => Task.FromResult(false), confirmDelete: (_, _) => Task.FromResult(true));
            await locked.ActivateAsync(); locked.SelectedItem = locked.Items[0]; Check(!locked.CanEdit && !locked.CanDelete && !locked.EditCommand.CanExecute(null) && !locked.DeleteCommand.CanExecute(null), "protected role editable/deletable");
        }
        var updates = Channel.CreateUnbounded<BRU.Avtopark.Client.QueryUpdate<CatalogPageResult<UserRow>>>();
        using var roles = new RoleManagementViewModel((_, _) => Task.FromResult(new CatalogPageResult<RoleRow>([Role], 1, 100, 1, true, true, true) { Access = Access }));
        var assignments = 0;
        using var users = new UserManagementViewModel((_, _) => Task.FromResult(Page(Access)), (_, ct) => updates.Reader.ReadAllAsync(ct), roles: roles,
            assign: (_, _, _, eligible, _) => { Check(eligible(), "assignment lost current gate"); assignments++; return Task.FromResult(false); });
        await users.ActivateAsync(); updates.Writer.TryWrite(new(Page(Access), true)); await Until(() => users.IsSynchronized);
        users.SelectedUser = users.Users[0]; users.SelectedRole = users.Roles[0]; await users.AssignRoleCommand.ExecuteAsync(null); Check(assignments == 1, "snapshot permission ignored");
        updates.Writer.TryWrite(new(Page(Access with { Permissions = [] }), true)); await Until(() => !users.CanAssignRole);
        Check(!users.AssignRoleCommand.CanExecute(null) && users.SelectedUserPermissions.Single().Name == Permission.Name && users.SelectedRolePermissions.Single().Name == Permission.Name, "revoked assignment or permission details incorrect");
        users.Deactivate();
        var previous = Environment.GetEnvironmentVariable("BRU_DEBUG_TRAFFIC");
        try
        {
            Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", "full");
            var dump = DataGridDiagnostics.DumpRows("UserManagementViewModel", [new UserDisplayModel(User)]);
            var roleDump = DataGridDiagnostics.DumpRows("RoleManagementViewModel", [new RoleDisplayModel(Role)]);
            Check(dump.Contains("VisibleLogin") && !dump.Contains("Password") && roleDump.Contains("users.view"), "credential-free row/role diagnostics lost useful business data");
            File.WriteAllText("/tmp/opencode/h16-users-grid.json", dump); File.WriteAllText("/tmp/opencode/h16-roles-grid.json", roleDump);
        }
        finally { Environment.SetEnvironmentVariable("BRU_DEBUG_TRAFFIC", previous); }
        var edits = 0;
        using var runtime = new UserManagementViewModel((_, _) => Task.FromResult(new CatalogPageResult<UserRow>([User], 1, 100, 101, true, true, true)), edit: (_, _, _) => { edits++; return Task.FromResult(false); });
        var theme = new FluentTheme(); Application.Current!.Styles.Add(theme);
        var view = new BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews.UserManagementToolWindow { DataContext = runtime };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        try
        {
            window.Show(); await Until(() => runtime.IsSynchronized); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); window.UpdateLayout();
            foreach (var label in new[] { "Назад", "Далее", "Обновить" })
            {
                var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == label);
                Check(ControlAutomationPeer.CreatePeerForElement(button)?.GetName() == label && button.Focusable && button.IsVisible, "user pager accessibility lost");
                if (button.IsEffectivelyEnabled) Check(button.Focus(), "user pager focus failed");
            }
            runtime.SelectedUser = runtime.Users[0]; var grid = view.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "UsersGrid");
            grid.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Check(edits == 1, "user keyboard editor unreachable");
        }
        finally { window.Close(); Application.Current.Styles.Remove(theme); }
        Check(!runtime.IsSynchronized, "detached administration screen stayed live");
        Console.WriteLine("PASS users/roles/permissions shared lifecycle/editor matrices: paging/search/cancellation, stable selection, permission revocation, retained drafts/IDs/receipts, uncertain deletes, protected roles, query-coherent assignment access, credential-free dumps and runtime user pager/Enter focus");
        CatalogPageResult<UserRow> Page(AccessSnapshot access) => new([User], 1, 100, 1, true, true, true) { Access = access };
        async Task Until(Func<bool> predicate) { while (!predicate()) await Task.Delay(1, stop); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
