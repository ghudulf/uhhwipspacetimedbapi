using System;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

/// <summary>One retained draft; an uncertain attempt is never replayed, including after a missing receipt.</summary>
public sealed class CatalogEditorAttempt<TRow, TDraft> where TRow : class where TDraft : class
{
    private readonly bool _create;
    private readonly Func<TDraft, string?> _validate;
    private readonly Func<TDraft, ulong?, string, CancellationToken, Task<MutationResult>> _execute;
    private readonly Func<string, CancellationToken, Task<MutationResult?>> _receipt;
    private readonly Func<CancellationToken, Task<TRow>> _readCurrent;
    private readonly Func<TRow, TDraft> _draft;
    private readonly Func<TRow, ulong> _revision;
    private bool _newAttempt;
    private readonly uint? _entityId;
    private readonly string? _conflictMessage;
    private readonly Func<string, string>? _failureMessage;
    public TDraft Draft { get; private set; }
    public TRow? Current { get; private set; }
    public ulong? ExpectedRevision { get; private set; }
    public string OperationId { get; private set; } = Guid.NewGuid().ToString("N");
    public string Message { get; private set; } = "";
    public bool IsBlocked { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsCommitted { get; private set; }
    public bool CanReconcile { get; private set; }
    public bool CanReload { get; private set; }
    public event Action? Changed;

    public CatalogEditorAttempt(TRow? original, Func<TRow?, TDraft> draft, Func<TRow, ulong> revision,
        Func<TDraft, string?> validate, Func<TDraft, ulong?, string, CancellationToken, Task<MutationResult>> execute,
        Func<string, CancellationToken, Task<MutationResult?>> receipt, Func<CancellationToken, Task<TRow>> readCurrent, uint? entityId = null, string? conflictMessage = null, Func<string, string>? failureMessage = null)
    {
        _create = original == null; _draft = row => draft(row); _revision = revision; _validate = validate;
        _execute = execute; _receipt = receipt; _readCurrent = readCurrent;
        _entityId = entityId; _conflictMessage = conflictMessage;
        _failureMessage = failureMessage;
        Draft = draft(original); ExpectedRevision = original == null ? null : revision(original);
    }
    public async Task SubmitAsync(Func<bool> canSubmit, CancellationToken ct)
    {
        if (IsBusy || IsBlocked || IsCommitted || ct.IsCancellationRequested) return;
        if (!canSubmit()) { Message = "Дождитесь синхронизации и проверьте права доступа. Черновик сохранён."; return; }
        if (_validate(Draft) is { } validation) { Message = validation; return; }
        if (_newAttempt) { OperationId = Guid.NewGuid().ToString("N"); _newAttempt = false; }
        IsBusy = true;
        try
        {
            var result = await _execute(Draft, ExpectedRevision, OperationId, ct);
            IsCommitted = true; IsBlocked = true;
            Log.Information("Catalog editor {RowType} operation {OperationId}: {Code}, entity {EntityId}, revision {Revision}", typeof(TRow).Name, OperationId, "ok", result.EntityId, result.Revision);
        }
        catch (Exception exception)
        {
            var code = exception is ApplicationCommandException failure ? failure.Code : "commit_outcome_unknown";
            Log.Information("Catalog editor {RowType} operation {OperationId}: {Code}, entity {EntityId}, revision {Revision}", typeof(TRow).Name, OperationId, code, _entityId, ExpectedRevision);
            CanReconcile = code == "commit_outcome_unknown";
            CanReload = !_create && code == "conflict";
            IsBlocked = code is not ("validation_failed" or "forbidden") && !(_create && code == "conflict");
            _newAttempt = !CanReconcile;
            Message = code == "conflict" ? _conflictMessage ?? "Данные изменились или изменение отклонено. Черновик сохранён; можно загрузить актуальные данные."
                : CanReconcile ? "Результат неизвестен. Проверьте его перед повтором; черновик сохранён."
                 : "Изменение отклонено. Проверьте поля и права доступа. Черновик сохранён.";
            if (_failureMessage != null && !CanReconcile) Message = _failureMessage(code);
        }
        finally { IsBusy = false; Changed?.Invoke(); }
    }
    public async Task ReconcileAsync(CancellationToken ct)
    {
        if (!CanReconcile || IsBusy || ct.IsCancellationRequested) return;
        IsBusy = true;
        try
        {
            var receipt = await _receipt(OperationId, ct);
            if (receipt != null)
            {
                IsCommitted = true; CanReconcile = false;
                Log.Information("Catalog editor {RowType} receipt {OperationId}: {Code}, entity {EntityId}, revision {Revision}", typeof(TRow).Name, OperationId, "ok", receipt.EntityId, receipt.Revision);
            }
            else Message = "Подтверждение пока не найдено. Это не означает отмену операции. Проверьте снова; повторная отправка заблокирована.";
        }
        catch (Exception exception)
        {
            Message = "Не удалось проверить результат. Черновик сохранён; повторите проверку.";
            Log.Debug("Catalog receipt {OperationId}: {Code}", OperationId, exception is ApplicationCommandException failure ? failure.Code : exception.GetType().Name);
        }
        finally { IsBusy = false; Changed?.Invoke(); }
    }
    public async Task ReloadAsync(CancellationToken ct)
    {
        if (!CanReload || IsBusy || ct.IsCancellationRequested) return;
        IsBusy = true;
        try
        {
            Current = await _readCurrent(ct);
            Message = $"Сейчас на сервере: {Current}. Черновик остаётся в полях; замените его только явным действием.";
            Log.Debug("Catalog conflict read {OperationId}: {Code}, revision {Revision}, row {@Row}", OperationId, "ok", _revision(Current), Current);
        }
        catch (Exception exception)
        {
            Message = "Не удалось загрузить актуальную запись. Черновик сохранён.";
            Log.Debug("Catalog conflict read {OperationId}: {Code}", OperationId, exception is ApplicationCommandException failure ? failure.Code : exception.GetType().Name);
        }
        finally { IsBusy = false; Changed?.Invoke(); }
    }
    public void ReplaceWithCurrent()
    {
        if (Current == null || IsBusy || !CanReload) return;
        Draft = _draft(Current); ExpectedRevision = _revision(Current); Current = null;
        CanReload = false; IsBlocked = false; _newAttempt = true;
        Message = "Актуальные данные загружены. Проверьте изменения перед сохранением.";
        Changed?.Invoke();
    }
}
