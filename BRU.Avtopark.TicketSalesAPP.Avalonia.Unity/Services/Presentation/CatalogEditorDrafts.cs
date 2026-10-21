using System;
using System.Collections.Generic;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

/// <summary>Closing/reopening a dialog cannot discard an uncertain operation or replay it with a new ID.</summary>
public sealed class CatalogEditorDrafts<TRow, TDraft> where TRow : class where TDraft : class
{
    private readonly Dictionary<uint, CatalogEditorAttempt<TRow, TDraft>> _attempts = new();
    private string? _token, _origin;
    public CatalogEditorAttempt<TRow, TDraft> Get(uint id, Func<CatalogEditorAttempt<TRow, TDraft>> create)
    {
        var api = ApiClientService.Instance;
        if (_token != api.AuthToken || _origin != api.CurrentBaseUrl)
        {
            _attempts.Clear(); _token = api.AuthToken; _origin = api.CurrentBaseUrl;
        }
        if (!_attempts.TryGetValue(id, out var attempt) || attempt.IsCommitted)
            _attempts[id] = attempt = create();
        return attempt;
    }
}
