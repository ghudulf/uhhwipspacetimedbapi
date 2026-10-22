using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Threading;
using CommunityToolkit.Mvvm.Input;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views.ManagementToolWindowsViews
{
    public partial class UserManagementToolWindow : UserControl
    {
        public UserManagementToolWindow()
        {
            InitializeComponent();
            var dialogs = new ControlDialogHost(this); var editor = new UserEditorService(dialogs); var assignment = new RoleAssignmentService(dialogs);
            RoleManagementViewModel? roles = null;
            var roleEditor = new RoleEditorService(dialogs, () => roles?.Access is { IsActive: true } access && System.Array.IndexOf(access.Permissions, "grant_permissions") >= 0, AdministrationCatalogDialog.RoleDrafts(dialogs));
            roles = new RoleManagementViewModel(null, edit: roleEditor.ShowAsync, confirmDelete: (r, ct) => dialogs.ConfirmAsync("Удаление роли", $"Удалить роль {r.Name}?", ct));
            DataContext = new UserManagementViewModel(null, edit: editor.ShowAsync, confirmDelete: (r, ct) => dialogs.ConfirmAsync("Удаление пользователя", $"Удалить пользователя {r.Login}? Нельзя удалить себя или последнего активного администратора.", ct),
                roles: roles, assign: assignment.ShowAsync, activate: assignment.ShowActivationAsync);
            var usersGrid = this.FindControl<DataGrid>("UsersGrid")!; var rolesGrid = this.FindControl<DataGrid>("RolesGrid")!;
            usersGrid.AddHandler(InputElement.KeyDownEvent, async (_, e) => { if (e.Key == Key.Enter && DataContext is UserManagementViewModel vm && vm.EditCommand.CanExecute(null)) { e.Handled = true; await vm.EditCommand.ExecuteAsync(null); } }, RoutingStrategies.Tunnel);
            usersGrid.ContextMenu = new ContextMenu(); rolesGrid.ContextMenu = new ContextMenu();
            usersGrid.ContextMenu.Opening += (_, _) =>
            {
                if (DataContext is not UserManagementViewModel vm) return;
                usersGrid.ContextMenu.ItemsSource = new[] { Item("Редактировать email и телефон (Enter)", vm.EditCommand), Item("Активировать / отключить", vm.SetActiveCommand), Item("Удалить пользователя", vm.DeleteCommand) };
            };
            rolesGrid.ContextMenu.Opening += (_, _) =>
            {
                if (DataContext is not UserManagementViewModel vm) return;
                rolesGrid.ContextMenu.ItemsSource = new[] { Item("Добавить роль", vm.RolesCatalog.AddCommand), Item("Редактировать роль", vm.RolesCatalog.EditCommand), Item("Удалить роль", vm.RolesCatalog.DeleteCommand),
                    Item("Просмотр роли (Administrator / системная — только чтение)", new AsyncRelayCommand(async () => { if (vm.SelectedRole is { } role) await roleEditor.ShowAsync(role.Row, () => false, _lifetime.Token); }, () => vm.SelectedRole != null && vm.RolesCatalog.IsSynchronized)),
                    Item("Все роли / страницы", new AsyncRelayCommand(async () => { await AdministrationCatalogDialog.ShowRolesAsync(dialogs, _lifetime.Token); await vm.RolesCatalog.LoadDataCommand.ExecuteAsync(null); })) };
            };
            foreach (var name in new[] { "UserPermissionsGrid", "RolePermissionsGrid" })
            {
                var grid = this.FindControl<DataGrid>(name)!;
                grid.ContextMenu = new ContextMenu { ItemsSource = new[] { Item("Все разрешения / создать / изменить", new AsyncRelayCommand(() => AdministrationCatalogDialog.ShowPermissionsAsync(dialogs, _lifetime.Token))) } };
            }
            AttachedToVisualTree += async (_, _) => { _lifetime.Dispose(); _lifetime = new(); if (DataContext is UserManagementViewModel model) await model.ActivateAsync(_lifetime.Token); };
            DetachedFromVisualTree += (_, _) => { _lifetime.Cancel(); if (DataContext is UserManagementViewModel model) model.Deactivate(); };
        }

        private CancellationTokenSource _lifetime = new();
        private static MenuItem Item(string label, IAsyncRelayCommand command) => new() { Header = label, Command = command };

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
