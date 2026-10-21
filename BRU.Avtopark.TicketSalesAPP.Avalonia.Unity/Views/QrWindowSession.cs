using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Views;

internal sealed class QrWindowSession
{
    private readonly CancellationTokenSource lifetime = new();
    private QrViewModel? model;
    public string Bearer { get; } = ApiClientService.Instance.AuthToken ?? "";
    public CancellationToken Token => lifetime.Token;
    public QrWindowSession(Window window)
    {
        var api = ApiClientService.Instance;
        void Changed(object? sender, string? token)
        {
            if (token != Bearer) Dispatcher.UIThread.Post(() => { model?.SessionChanged(); lifetime.Cancel(); window.Close(); });
        }
        api.OnAuthTokenChanged += Changed;
        window.Closed += async (_, _) =>
        {
            api.OnAuthTokenChanged -= Changed;
            lifetime.Cancel();
            if (model != null) await model.DisposeAsync();
        };
    }
    public void Bind(QrViewModel value) => model = value;
}
