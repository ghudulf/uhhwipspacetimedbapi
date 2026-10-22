using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

public sealed class PermissionDraft
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public sealed class PermissionEditorService(ControlDialogHost host)
{
    private readonly CatalogEditorDrafts<PermissionRow, PermissionDraft> _drafts = new();
    public static PermissionDraft DraftFrom(PermissionRow? row) => new() { Name = row?.Name ?? "", Description = row?.Description ?? "", Category = row?.Category ?? "", IsActive = row?.IsActive ?? true };
    public static string? Validate(PermissionDraft d) => string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 200 || d.Category.Length > 200 || d.Category.Length > 0 && string.IsNullOrWhiteSpace(d.Category) || d.Description.Length > 2000 || d.Description.Length > 0 && string.IsNullOrWhiteSpace(d.Description) ? "Укажите название (до 200 символов), категорию (до 200) и описание (до 2000)." : null;
    public async Task<bool> ShowAsync(PermissionRow? original, Func<bool> canSubmit, CancellationToken ct)
    {
        var api = ApiClientService.Instance; var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed"); var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        var name = CatalogEditorDialog.Text("Название разрешения", 200); var description = CatalogEditorDialog.Text("Описание разрешения", 2000); var category = CatalogEditorDialog.Text("Категория разрешения", 200);
        var active = new CheckBox { Content = "Разрешение активно", IsVisible = original != null };
        var attempt = _drafts.Get(original?.Id ?? 0, () => new CatalogEditorAttempt<PermissionRow, PermissionDraft>(original, DraftFrom, r => r.Revision, Validate,
            (d, revision, id, cancellation) => original == null
                ? ApplicationMutationClient.ExecuteAsync("permissions.create", new CreatePermissionCommand(d.Name, d.Description, d.Category), id, origin, token, cancellation)
                : ApplicationMutationClient.ExecuteAsync("permissions.update", new UpdatePermissionCommand(original.Id, d.Name, d.Description, d.Category, d.IsActive, revision), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, PermissionRow>("permissions.get", new(original!.Id), cancellation), original?.Id, failureMessage: UserEditorService.Failure));
        return await CatalogEditorDialog.ShowAsync(host, original == null ? "Добавить разрешение" : "Редактировать разрешение", attempt, [name, description, category, active],
            d => { d.Name = name.Text ?? ""; d.Description = description.Text ?? ""; d.Category = category.Text ?? ""; d.IsActive = active.IsChecked == true; },
            d => { name.Text = d.Name; description.Text = d.Description; category.Text = d.Category; active.IsChecked = d.IsActive; }, canSubmit, ct);
    }
}
