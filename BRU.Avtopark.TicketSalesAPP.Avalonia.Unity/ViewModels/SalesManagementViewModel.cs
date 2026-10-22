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
using ReactiveUI;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

public sealed class SaleDisplayModel(SaleRow row) : ReactiveObject
{
    public SaleRow Row { get; private set; } = row;
    public uint SaleId => Row.SaleId;
    public uint TicketId => Row.TicketId;
    public string TicketSoldToUser => Row.BuyerName;
    public string TicketSoldToUserPhone => Row.BuyerPhone;
    public string SellerId => Row.SellerLabel ?? Row.SellerId ?? "—";
    public string SaleDateDisplay => Row.SaleDate?.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "Не указано";
    public decimal? RecordedAmount => Row.RecordedAmount;
    public string RecordedAmountDisplay => RecordedAmount?.ToString("C", CultureInfo.CurrentCulture) ?? "Неизвестно";
    public void Update(SaleRow value)
    {
        Row = value;
        foreach (var name in new[] { nameof(TicketId), nameof(TicketSoldToUser), nameof(TicketSoldToUserPhone), nameof(SellerId), nameof(SaleDateDisplay), nameof(RecordedAmount), nameof(RecordedAmountDisplay) }) this.RaisePropertyChanged(name);
    }
}

public sealed class SalesManagementViewModel : EditableCatalogViewModel<SaleRow, SaleDisplayModel>
{
    private DateTimeOffset _start = DateTimeOffset.Now.AddMonths(-1), _end = DateTimeOffset.Now;
    private decimal? _minimum, _maximum, _knownTotal;
    private int? _unknown;
    public ObservableCollection<SaleDisplayModel> Sales => Items;
    public SaleDisplayModel? SelectedSale { get => SelectedItem; set => SelectedItem = value; }
    public DateTimeOffset StartDate { get => _start; set { if (_start == value) return; this.RaiseAndSetIfChanged(ref _start, value); QueryChanged(); } }
    public DateTimeOffset EndDate { get => _end; set { if (_end == value) return; this.RaiseAndSetIfChanged(ref _end, value); QueryChanged(); } }
    public decimal? MinPrice { get => _minimum; set { if (_minimum == value) return; this.RaiseAndSetIfChanged(ref _minimum, value); QueryChanged(); } }
    public decimal? MaxPrice { get => _maximum; set { if (_maximum == value) return; this.RaiseAndSetIfChanged(ref _maximum, value); QueryChanged(); } }
    public decimal? TotalIncome => _unknown == 0 ? _knownTotal : null;
    public string TotalIncomeDisplay => TotalIncome.HasValue ? $"Общая сумма: {TotalIncome:C}" : $"Известная сумма: {_knownTotal?.ToString("C") ?? "неизвестно"}; продажи с неизвестной суммой: {_unknown?.ToString() ?? "неизвестно"}";
    public string StatusMessage => !IsSynchronized ? SynchronizationMessage : SelectedSale is { } sale
        ? $"Сумма продажи: {sale.RecordedAmountDisplay}. Enter / двойной щелчок: изменить покупателя. Дата продажи и билет не изменяются."
        : "Enter / двойной щелчок по продаже: изменить покупателя. Дата продажи и билет не изменяются.";
    public SalesManagementViewModel() : this(null) { }
    public SalesManagementViewModel(Func<PageRequest, CancellationToken, Task<CatalogPageResult<SaleRow>>>? read,
        Func<PageRequest, CancellationToken, IAsyncEnumerable<QueryUpdate<CatalogPageResult<SaleRow>>>>? watch = null,
        Func<SaleRow?, Func<bool>, CancellationToken, Task<bool>>? edit = null, Func<SaleRow, CancellationToken, Task<bool>>? confirmDelete = null,
        Func<SaleRow, string, CancellationToken, Task<MutationResult>>? delete = null)
        : base("sales.list", r => r.SaleId, item => item.SaleId, r => new(r), (item, r) => item.Update(r), item => item.Row, r => r.Revision,
            delete ?? DeleteAsync, read, watch, edit, confirmDelete) { }
    protected override PageRequest BuildRequest() => base.BuildRequest() with { StartDate = StartDate, EndDate = EndDate, MinPrice = MinPrice, MaxPrice = MaxPrice };
    protected override void SnapshotApplied(CatalogPageResult<SaleRow> page)
    {
        base.SnapshotApplied(page);
        _knownTotal = page.KnownTotal ?? (page.TotalCount == page.Items.Length ? page.Items.Sum(r => r.RecordedAmount ?? 0) : null);
        _unknown = page.UnknownAmountCount ?? (page.TotalCount == page.Items.Length ? page.Items.Count(r => !r.RecordedAmount.HasValue) : null);
        this.RaisePropertyChanged(nameof(TotalIncome)); this.RaisePropertyChanged(nameof(TotalIncomeDisplay)); this.RaisePropertyChanged(nameof(StatusMessage));
    }
    protected override void SelectionChanged() { this.RaisePropertyChanged(nameof(SelectedSale)); this.RaisePropertyChanged(nameof(StatusMessage)); }
    protected override void EligibilityChanged() { base.EligibilityChanged(); this.RaisePropertyChanged(nameof(StatusMessage)); }
    private static Task<MutationResult> DeleteAsync(SaleRow row, string id, CancellationToken ct)
    { var api = ApiClientService.Instance; return ApplicationMutationClient.ExecuteAsync("sales.delete", new DeleteSaleCommand(row.SaleId, row.Revision), id, api.CurrentBaseUrl!, api.AuthToken!, ct); }
}
