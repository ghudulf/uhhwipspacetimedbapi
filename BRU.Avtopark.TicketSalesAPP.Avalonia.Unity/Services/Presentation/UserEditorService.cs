using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

// Credentials deliberately have no place in retained drafts or row projections.
public sealed class UserDraft
{
    public string Login { get; set; } = "";
    public string Email { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public uint? RoleId { get; set; }
}
public sealed class UserEditorService(ControlDialogHost host)
{
    private readonly CatalogEditorDrafts<UserRow, UserDraft> _drafts = new();
    private string _initialPassword = "";
    public static UserDraft DraftFrom(UserRow? row) => new() { Login = row?.Login ?? "", Email = row?.Email ?? "", PhoneNumber = row?.PhoneNumber ?? "" };
    public static string? Validate(UserDraft d) => string.IsNullOrWhiteSpace(d.Login) || d.Login.Length > 200 ? "Укажите логин (до 200 символов)."
        : d.Email.Length > 320 || d.Email.Length > 0 && string.IsNullOrWhiteSpace(d.Email) || d.PhoneNumber.Length > 50 || d.PhoneNumber.Length > 0 && string.IsNullOrWhiteSpace(d.PhoneNumber) ? "Проверьте email (до 320 символов) и телефон (до 50 символов)." : null;
    public static string Failure(string code) => $"[{code}] " + (code == "validation_failed" ? "Проверьте логин, пароль (6–1024 символа), email и телефон."
        : code == "conflict" ? "Логин уже занят, запись изменена, либо сервер запретил изменение: нельзя удалить/отключить себя или последнего активного администратора, назначать Administrator или права сверх своих."
        : "Недостаточно прав или сервер запретил изменение. Назначение Administrator и выдача чужих прав ограничены.") + " Черновик сохранён.";
    public async Task<bool> ShowAsync(UserRow? original, Func<bool> canSubmit, CancellationToken ct)
    {
        var api = ApiClientService.Instance; var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed"); var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        List<RoleRow> roles = []; var notice = "Редактируются только email и телефон. Логин и пароль существующей записи не изменяются. Активация и роли — отдельные операции.";
        if (original == null)
        {
            try { roles = await CatalogEditorLookups.CatalogAsync<RoleRow>("roles.list", ct); }
            catch (ApplicationCommandException ex) when (ex.Code == "forbidden") { notice += " Нет права просмотра ролей; можно создать пользователя без роли."; }
        }
        ct.ThrowIfCancellationRequested(); if (api.AuthToken != token || api.CurrentBaseUrl != origin) throw new ApplicationCommandException("session_changed");
        var login = CatalogEditorDialog.Text("Логин", 200); login.IsReadOnly = original != null;
        var password = CatalogEditorDialog.Text("Начальный пароль", 1024); password.PasswordChar = '*'; password.IsVisible = original == null;
        var email = CatalogEditorDialog.Text("Email", 320); var phone = CatalogEditorDialog.Text("Телефон", 50);
        var role = new ComboBox { ItemsSource = roles, IsVisible = original == null, ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<RoleRow>((r, _) => new TextBlock { Text = r?.Name }) };
        AutomationProperties.SetName(role, "Начальная роль (необязательно)");
        var attempt = _drafts.Get(original?.Id ?? 0, () => new CatalogEditorAttempt<UserRow, UserDraft>(original, DraftFrom, r => r.Revision,
            d => Validate(d) ?? (original == null && (string.IsNullOrWhiteSpace(_initialPassword) || _initialPassword.Length is < 6 or > 1024) ? "Введите начальный пароль: 6–1024 символа." : null),
            (d, revision, id, cancellation) => original == null
                ? ApplicationMutationClient.ExecuteAsync("users.create", new CreateUserCommand(d.Login, _initialPassword, d.Email, d.PhoneNumber, d.RoleId), id, origin, token, cancellation)
                : ApplicationMutationClient.ExecuteAsync("users.update", new UpdateUserCommand(original.Id, d.Email, d.PhoneNumber, revision), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, UserRow>("users.get", new(original!.Id), cancellation), original?.Id, failureMessage: Failure));
        try
        {
            return await CatalogEditorDialog.ShowAsync(host, original == null ? "Добавить пользователя" : "Редактировать пользователя", attempt, [login, password, email, phone, role],
                d => { d.Login = login.Text ?? ""; d.Email = email.Text ?? ""; d.PhoneNumber = phone.Text ?? ""; d.RoleId = (role.SelectedItem as RoleRow)?.Id; _initialPassword = password.Text ?? ""; },
                d => { login.Text = d.Login; email.Text = d.Email; phone.Text = d.PhoneNumber; role.SelectedItem = roles.Find(r => r.Id == d.RoleId); password.Text = ""; }, canSubmit, ct, notice);
        }
        finally { password.Text = ""; _initialPassword = ""; }
    }
}
