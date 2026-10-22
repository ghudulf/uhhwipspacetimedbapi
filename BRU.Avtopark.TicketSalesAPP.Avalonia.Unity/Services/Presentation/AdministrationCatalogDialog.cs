using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

public static class AdministrationCatalogDialog
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ControlDialogHost, Browsers> Sessions = new();
    public static CatalogEditorDrafts<BRU.Avtopark.Contracts.RoleRow, RoleDraft> RoleDrafts(ControlDialogHost host) => Sessions.GetValue(host, _ => new Browsers()).RoleDrafts;
    public static Task ShowRolesAsync(ControlDialogHost host, CancellationToken ct) => Sessions.GetValue(host, _ => new Browsers()).ShowRolesAsync(host, ct);
    public static Task ShowPermissionsAsync(ControlDialogHost host, CancellationToken ct) => Sessions.GetValue(host, _ => new Browsers()).ShowPermissionsAsync(host, ct);
    // One browser/editor set per owning screen: closing the browser cannot discard an uncertain draft.
    private sealed class Browsers
    {
        public CatalogEditorDrafts<BRU.Avtopark.Contracts.RoleRow, RoleDraft> RoleDrafts { get; } = new();
        private readonly UserControl _roleOwner = new(), _permissionOwner = new();
        private readonly RoleManagementViewModel _roles;
        private readonly PermissionManagementViewModel _permissions;
        public Browsers()
        {
            var roleDialogs = new ControlDialogHost(_roleOwner);
            var roleEditor = new RoleEditorService(roleDialogs, () => _roles?.Access is { IsActive: true } a && Array.IndexOf(a.Permissions, "grant_permissions") >= 0, RoleDrafts);
            _roles = new RoleManagementViewModel(null, edit: roleEditor.ShowAsync, confirmDelete: (r, ct) => roleDialogs.ConfirmAsync("Удаление роли", $"Удалить роль {r.Name}? Назначенные и системные роли не удаляются.", ct));
            var permissionDialogs = new ControlDialogHost(_permissionOwner); var permissionEditor = new PermissionEditorService(permissionDialogs);
            _permissions = new PermissionManagementViewModel(null, edit: permissionEditor.ShowAsync, confirmDelete: (r, ct) => permissionDialogs.ConfirmAsync("Удаление разрешения", $"Удалить разрешение {r.Name}?", ct));
        }
        public Task ShowRolesAsync(ControlDialogHost host, CancellationToken ct) => ShowAsync(host, _roleOwner, "Роли (100 строк на страницу)", _roles, [nameof(RoleDisplayModel.Name), nameof(RoleDisplayModel.Description), nameof(RoleDisplayModel.LegacyRoleId), nameof(RoleDisplayModel.Priority), nameof(RoleDisplayModel.IsSystem)], ct);
        public Task ShowPermissionsAsync(ControlDialogHost host, CancellationToken ct) => ShowAsync(host, _permissionOwner, "Разрешения (100 строк на страницу)", _permissions, [nameof(PermissionDisplayModel.Name), nameof(PermissionDisplayModel.Description), nameof(PermissionDisplayModel.Category), nameof(PermissionDisplayModel.IsActive)], ct);
    }
    private static async Task ShowAsync<TRow, TItem>(ControlDialogHost host, UserControl owner, string title, EditableCatalogViewModel<TRow, TItem> model, string[] columns, CancellationToken ct) where TItem : class
    {
        var panel = new StackPanel { Spacing = 8 };
        var search = CatalogEditorDialog.Text("Поиск", 200); search.Bind(TextBox.TextProperty, new Binding(nameof(model.SearchText)) { Mode = BindingMode.TwoWay }); panel.Children.Add(search);
        var grid = new DataGrid { ItemsSource = model.Items, AutoGenerateColumns = false, IsReadOnly = true, Height = 240 };
        AutomationProperties.SetName(grid, title); grid.Bind(DataGrid.SelectedItemProperty, new Binding(nameof(model.SelectedItem)) { Mode = BindingMode.TwoWay });
        foreach (var column in columns) grid.Columns.Add(new DataGridTextColumn { Header = column, Binding = new Binding(column) }); panel.Children.Add(grid);
        var error = new TextBlock(); error.Bind(TextBlock.TextProperty, new Binding(nameof(model.ErrorMessage))); AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite); panel.Children.Add(error);
        var sync = new TextBlock(); sync.Bind(TextBlock.TextProperty, new Binding(nameof(model.SynchronizationMessage))); AutomationProperties.SetLiveSetting(sync, AutomationLiveSetting.Polite); panel.Children.Add(sync);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var action in new[] { ("Добавить", model.AddCommand), ("Редактировать", model.EditCommand), ("Удалить", model.DeleteCommand) }) actions.Children.Add(new Button { Content = action.Item1, Command = action.Item2 });
        panel.Children.Add(actions);
        var pager = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        pager.Children.Add(new Button { Content = "Назад", Command = model.PreviousPageCommand });
        var info = new TextBlock { VerticalAlignment = VerticalAlignment.Center }; info.Bind(TextBlock.TextProperty, new Binding(nameof(model.PageInfo))); pager.Children.Add(info);
        pager.Children.Add(new Button { Content = "Далее", Command = model.NextPageCommand }); pager.Children.Add(new Button { Content = "Обновить", Command = model.LoadDataCommand });
        var progress = new ProgressBar { IsIndeterminate = true, Width = 70 }; progress.Bind(ProgressBar.IsVisibleProperty, new Binding(nameof(model.IsBusy))); pager.Children.Add(progress); panel.Children.Add(pager);
        owner.DataContext = model; owner.Content = panel;
        try { await model.ActivateAsync(ct); await host.ShowAsync(title, owner, ct); }
        finally { model.Deactivate(); }
    }
}
