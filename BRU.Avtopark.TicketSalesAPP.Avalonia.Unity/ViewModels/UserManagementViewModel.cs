using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public sealed class UserDisplayModel(UserRow row) : ReactiveObject
{
    public UserRow Row { get; private set; } = row;
    public uint LegacyUserId => Row.Id;
    public string Login => Row.Login;
    public string? Email => Row.Email;
    public string? PhoneNumber => Row.PhoneNumber;
    public bool IsActive => Row.IsActive;
    public string[] Roles => Row.Roles;
    public string CreatedAtDisplay => Row.CreatedAt?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "Не указано";
    public string LastLoginAtDisplay => Row.LastLoginAt?.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "Никогда";
    public void Update(UserRow value) { Row = value; foreach (var name in new[] { nameof(Row), nameof(Login), nameof(Email), nameof(PhoneNumber), nameof(IsActive), nameof(Roles), nameof(CreatedAtDisplay), nameof(LastLoginAtDisplay) }) this.RaisePropertyChanged(name); }
}
public sealed class RoleDisplayModel(RoleRow row) : ReactiveObject
{
    public RoleRow Row { get; private set; } = row;
    public uint RoleId => Row.Id;
    public int LegacyRoleId => Row.LegacyRoleId;
    public string Name => Row.Name;
    public string? Description => Row.Description;
    public bool IsSystem => Row.IsSystem;
    public bool IsActive => Row.IsActive;
    public uint Priority => Row.Priority;
    public bool IsReadOnly => IsSystem || Name == "Administrator";
    public string[] Permissions => Row.Permissions;
    public void Update(RoleRow value) { Row = value; foreach (var name in new[] { nameof(Row), nameof(LegacyRoleId), nameof(Name), nameof(Description), nameof(IsSystem), nameof(IsActive), nameof(Priority), nameof(IsReadOnly), nameof(Permissions) }) this.RaisePropertyChanged(name); }
}
public sealed class PermissionDisplayModel(PermissionRow row) : ReactiveObject
{
    public PermissionRow Row { get; private set; } = row;
    public uint PermissionId => Row.Id;
    public string Name => Row.Name;
    public string? Description => Row.Description;
    public string? Category => Row.Category;
    public bool IsActive => Row.IsActive;
    public void Update(PermissionRow value) { Row = value; foreach (var name in new[] { nameof(Row), nameof(Name), nameof(Description), nameof(Category), nameof(IsActive) }) this.RaisePropertyChanged(name); }
}

public sealed class UserManagementViewModel : EditableCatalogViewModel<UserRow, UserDisplayModel>
{
    private readonly Func<UserRow, RoleRow?, bool, Func<bool>, CancellationToken, Task<bool>>? _assign;
    private readonly Func<UserRow, Func<bool>, CancellationToken, Task<bool>>? _activate;
    private AccessSnapshot? _access;
    public RoleManagementViewModel RolesCatalog { get; }
    public ObservableCollection<UserDisplayModel> Users => Items;
    public ObservableCollection<RoleDisplayModel> Roles => RolesCatalog.Items;
    public UserDisplayModel? SelectedUser { get => SelectedItem; set => SelectedItem = value; }
    public RoleDisplayModel? SelectedRole { get => RolesCatalog.SelectedItem; set { RolesCatalog.SelectedItem = value; DetailsChanged(); } }
    public PermissionDisplayModel[] SelectedUserPermissions => SelectedUser?.Row.PermissionDetails?.Select(p => new PermissionDisplayModel(p)).ToArray() ?? [];
    public PermissionDisplayModel[] SelectedRolePermissions => SelectedRole?.Row.PermissionDetails?.Select(p => new PermissionDisplayModel(p)).ToArray() ?? [];
    public bool CanAssignRole => CanMutate && SelectedUser != null && RolesCatalog.IsSynchronized && SelectedRole is { IsReadOnly: false } && Allows("assign_roles");
    public bool CanSetActive => CanEdit && Allows("users.edit");
    public IAsyncRelayCommand AssignRoleCommand { get; }
    public IAsyncRelayCommand RemoveRoleCommand { get; }
    public IAsyncRelayCommand SetActiveCommand { get; }
    public UserManagementViewModel() : this(null) { }
    public UserManagementViewModel(Func<PageRequest, CancellationToken, Task<CatalogPageResult<UserRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<UserRow>>>>? watch = null,
        Func<UserRow?, Func<bool>, CancellationToken, Task<bool>>? edit = null, Func<UserRow, CancellationToken, Task<bool>>? confirmDelete = null,
        Func<UserRow, string, CancellationToken, Task<MutationResult>>? delete = null, RoleManagementViewModel? roles = null,
        Func<UserRow, RoleRow?, bool, Func<bool>, CancellationToken, Task<bool>>? assign = null, Func<UserRow, Func<bool>, CancellationToken, Task<bool>>? activate = null)
        : base("users.list", r => r.Id, i => i.LegacyUserId, r => new(r), (i, r) => i.Update(r), i => i.Row, r => r.Revision,
            delete ?? DeleteAsync, read, watch, edit, confirmDelete)
    {
        RolesCatalog = roles ?? (read == null ? new RoleManagementViewModel(null) : new RoleManagementViewModel((_, _) => Task.FromResult(new CatalogPageResult<RoleRow>([], 1, 100, 0, false, false, false))));
        _assign = assign; _activate = activate;
        AssignRoleCommand = new AsyncRelayCommand(() => AssignAsync(false), () => CanAssignRole && _assign != null);
        RemoveRoleCommand = new AsyncRelayCommand(() => AssignAsync(true), () => CanAssignRole && _assign != null);
        SetActiveCommand = new AsyncRelayCommand(ActivateUserAsync, () => CanSetActive && _activate != null);
        RolesCatalog.Changed += DetailsChanged;
    }
    public override async Task ActivateAsync(CancellationToken ct = default) { await base.ActivateAsync(ct); if (!ActivityToken.IsCancellationRequested) await RolesCatalog.ActivateAsync(ct); }
    public override void Deactivate() { RolesCatalog.Deactivate(); base.Deactivate(); }
    private bool Allows(string permission) => _access is { IsActive: true } && _access.Permissions.Contains(permission, StringComparer.Ordinal);
    protected override void SnapshotApplied(CatalogPageResult<UserRow> page) { base.SnapshotApplied(page); _access = page.Access; DetailsChanged(); }
    protected override void SelectionChanged() { this.RaisePropertyChanged(nameof(SelectedUser)); DetailsChanged(); }
    protected override void EligibilityChanged() { base.EligibilityChanged(); AssignRoleCommand?.NotifyCanExecuteChanged(); RemoveRoleCommand?.NotifyCanExecuteChanged(); SetActiveCommand?.NotifyCanExecuteChanged(); this.RaisePropertyChanged(nameof(CanAssignRole)); this.RaisePropertyChanged(nameof(CanSetActive)); }
    private void DetailsChanged()
    {
        foreach (var name in new[] { nameof(SelectedRole), nameof(SelectedUserPermissions), nameof(SelectedRolePermissions) }) this.RaisePropertyChanged(name);
        EligibilityChanged();
        DataGridDiagnostics.DumpRows(nameof(UserManagementViewModel) + ".Roles", Roles);
        DataGridDiagnostics.DumpRows(nameof(UserManagementViewModel) + ".UserPermissions", SelectedUserPermissions);
        DataGridDiagnostics.DumpRows(nameof(UserManagementViewModel) + ".RolePermissions", SelectedRolePermissions);
    }
    private async Task AssignAsync(bool remove)
    {
        if (!CanAssignRole || _assign == null) return; var row = SelectedUser!.Row; var role = SelectedRole!.Row;
        bool Eligible() => CanAssignRole && SelectedUser?.Row.Id == row.Id && SelectedRole?.Row.Id == role.Id;
        if (await _assign(row, role, remove, Eligible, ActivityToken) && Eligible()) await LoadData();
    }
    private async Task ActivateUserAsync()
    {
        if (!CanSetActive || _activate == null) return; var row = SelectedUser!.Row;
        bool Eligible() => CanSetActive && SelectedUser?.Row.Id == row.Id;
        if (await _activate(row, Eligible, ActivityToken) && Eligible()) await LoadData();
    }
    protected override string DeleteConflictMessage => "[conflict] Нельзя удалить себя или последнего активного администратора; запись также могла измениться. Обновите данные.";
    private static Task<MutationResult> DeleteAsync(UserRow row, string id, CancellationToken ct) { var api = ApiClientService.Instance; return ApplicationMutationClient.ExecuteAsync("users.delete", new DeleteUserCommand(row.Id, row.Revision), id, api.CurrentBaseUrl!, api.AuthToken!, ct); }
}

public sealed class RoleManagementViewModel : EditableCatalogViewModel<RoleRow, RoleDisplayModel>
{
    public event Action? Changed;
    public AccessSnapshot? Access { get; private set; }
    public RoleManagementViewModel(Func<PageRequest, CancellationToken, Task<CatalogPageResult<RoleRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<RoleRow>>>>? watch = null,
        Func<RoleRow?, Func<bool>, CancellationToken, Task<bool>>? edit = null, Func<RoleRow, CancellationToken, Task<bool>>? confirmDelete = null,
        Func<RoleRow, string, CancellationToken, Task<MutationResult>>? delete = null)
        : base("roles.list", r => r.Id, i => i.RoleId, r => new(r), (i, r) => i.Update(r), i => i.Row, r => r.Revision, delete ?? DeleteAsync, read, watch, edit, confirmDelete) { }
    protected override bool CanEditItem(RoleDisplayModel item) => !item.IsReadOnly;
    protected override bool CanDeleteItem(RoleDisplayModel item) => !item.IsReadOnly;
    protected override void SelectionChanged() => Changed?.Invoke();
    protected override void SnapshotApplied(CatalogPageResult<RoleRow> page) { base.SnapshotApplied(page); Access = page.Access; Changed?.Invoke(); }
    protected override void EligibilityChanged() { base.EligibilityChanged(); Changed?.Invoke(); }
    protected override string DeleteConflictMessage => "[conflict] Системная роль, Administrator или назначенная пользователям роль не удаляется. Обновите данные.";
    private static Task<MutationResult> DeleteAsync(RoleRow row, string id, CancellationToken ct) { var api = ApiClientService.Instance; return ApplicationMutationClient.ExecuteAsync("roles.delete", new DeleteRoleCommand(row.Id, row.Revision), id, api.CurrentBaseUrl!, api.AuthToken!, ct); }
}

public sealed class PermissionManagementViewModel : EditableCatalogViewModel<PermissionRow, PermissionDisplayModel>
{
    public PermissionManagementViewModel(Func<PageRequest, CancellationToken, Task<CatalogPageResult<PermissionRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<PermissionRow>>>>? watch = null,
        Func<PermissionRow?, Func<bool>, CancellationToken, Task<bool>>? edit = null, Func<PermissionRow, CancellationToken, Task<bool>>? confirmDelete = null,
        Func<PermissionRow, string, CancellationToken, Task<MutationResult>>? delete = null)
        : base("permissions.list", r => r.Id, i => i.PermissionId, r => new(r), (i, r) => i.Update(r), i => i.Row, r => r.Revision, delete ?? DeleteAsync, read, watch, edit, confirmDelete) { }
    private static Task<MutationResult> DeleteAsync(PermissionRow row, string id, CancellationToken ct) { var api = ApiClientService.Instance; return ApplicationMutationClient.ExecuteAsync("permissions.delete", new DeletePermissionCommand(row.Id, row.Revision), id, api.CurrentBaseUrl!, api.AuthToken!, ct); }
}
