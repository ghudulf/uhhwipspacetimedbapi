using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

public sealed class SaleDraft
{
    public uint TicketId { get; set; }
    public string BuyerName { get; set; } = "";
    public string BuyerPhone { get; set; } = "";
}

public sealed class SaleEditorService(ControlDialogHost host)
{
    private readonly CatalogEditorDrafts<SaleRow, SaleDraft> _drafts = new();
    public static SaleDraft DraftFrom(SaleRow? row) => new() { TicketId = row?.TicketId ?? 0, BuyerName = row?.BuyerName ?? "", BuyerPhone = row?.BuyerPhone ?? "" };
    public static string? Validate(SaleDraft d)
    {
        if (d.TicketId == 0) return "Выберите билет. Если список пуст, проверьте право просмотра билетов.";
        if (d.BuyerName.Length > 200 || d.BuyerName.Length > 0 && string.IsNullOrWhiteSpace(d.BuyerName)) return "Имя покупателя: до 200 символов. Оставьте пустым для значения по умолчанию.";
        if (d.BuyerPhone.Length > 50 || d.BuyerPhone.Length > 0 && string.IsNullOrWhiteSpace(d.BuyerPhone)) return "Телефон: до 50 символов; пустое значение допустимо.";
        return null;
    }
    public async Task<bool> ShowAsync(SaleRow? original, Func<bool> canSubmit, CancellationToken ct)
    {
        var api = ApiClientService.Instance; var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed");
        var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        List<TicketRow> tickets = []; var notice = "Дата продажи задаётся сервером и не изменяется. Пустое имя: значение по умолчанию при создании, без изменения при редактировании. Телефон может быть пустым.";
        if (original == null)
        {
            try { tickets = await CatalogEditorLookups.CatalogAsync<TicketRow>("tickets.list", ct); }
            catch (ApplicationCommandException ex) when (ex.Code == "forbidden") { notice += " Нет права просмотра билетов."; }
            if (tickets.Count == 0) notice += " Нет доступных билетов.";
        }
        ct.ThrowIfCancellationRequested();
        if (api.AuthToken != token || api.CurrentBaseUrl != origin) throw new ApplicationCommandException("session_changed");
        var ticket = new ComboBox { ItemsSource = tickets, IsVisible = original == null, ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<TicketRow>((r, _) => new TextBlock { Text = $"Билет {r?.Id}: {r?.RouteLabel}, место {r?.SeatNumber}" }) };
        AutomationProperties.SetName(ticket, "Билет");
        var immutable = new TextBlock { Text = original == null ? "Дата продажи: задаётся сервером" : $"Билет {original.TicketId}; дата продажи: {original.SaleDate?.ToString("O") ?? "Не указано"} (не изменяется)" };
        var name = CatalogEditorDialog.Text("Покупатель", 200); var phone = CatalogEditorDialog.Text("Телефон покупателя", 50);
        var attempt = _drafts.Get(original?.SaleId ?? 0, () => new CatalogEditorAttempt<SaleRow, SaleDraft>(original, DraftFrom, r => r.Revision, Validate,
            (d, revision, id, cancellation) => original == null
                ? ApplicationMutationClient.ExecuteAsync("sales.create", new CreateSaleCommand(d.TicketId, d.BuyerName.Length == 0 ? null : d.BuyerName, d.BuyerPhone), id, origin, token, cancellation)
                : ApplicationMutationClient.ExecuteAsync("sales.update", new UpdateSaleCommand(original.SaleId, revision, d.BuyerName.Length == 0 ? null : d.BuyerName, d.BuyerPhone), id, origin, token, cancellation),
            (id, cancellation) => ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(id), cancellation),
            cancellation => ApplicationQueryClient.ReadAsync<EntityRequest, SaleRow>("sales.get", new(original!.SaleId), cancellation), original?.SaleId,
            "Продажа изменена или билет недоступен. Черновик сохранён; загрузите актуальную запись."));
        return await CatalogEditorDialog.ShowAsync(host, original == null ? "Добавить продажу" : "Редактировать покупателя", attempt, [immutable, ticket, name, phone],
            d => { d.TicketId = original?.TicketId ?? (ticket.SelectedItem as TicketRow)?.Id ?? 0; d.BuyerName = name.Text ?? ""; d.BuyerPhone = phone.Text ?? ""; },
            d => { ticket.SelectedItem = tickets.Find(r => r.Id == d.TicketId); name.Text = d.BuyerName; phone.Text = d.BuyerPhone; }, canSubmit, ct, notice);
    }
}
