using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

internal static class CatalogEditorDialog
{
    internal static TextBox Text(string label, int maxLength)
    {
        var text = new TextBox { PlaceholderText = label, MaxLength = maxLength };
        AutomationProperties.SetName(text, label);
        return text;
    }
    internal static async Task<bool> ShowAsync<TRow, TDraft>(ControlDialogHost host, string title,
        CatalogEditorAttempt<TRow, TDraft> attempt, Control[] fields, Action<TDraft> readFields, Action<TDraft> writeFields,
        Func<bool> canSubmit, CancellationToken cancellationToken) where TRow : class where TDraft : class
    {
        var api = ApiClientService.Instance;
        var token = api.AuthToken; var origin = api.CurrentBaseUrl;
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool OwnsSession() => token == api.AuthToken && origin == api.CurrentBaseUrl && !closed.IsCancellationRequested;
        void SessionChanged(object? sender, string? value) { if (token != api.AuthToken || origin != api.CurrentBaseUrl) closed.Cancel(); }
        api.OnAuthTokenChanged += SessionChanged;
        var error = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        var save = new Button { Content = "Сохранить", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Center };
        var reconcile = new Button { Content = "Проверить результат", IsVisible = false };
        var reload = new Button { Content = "Загрузить актуальные данные", IsVisible = false };
        var replace = new Button { Content = "Заменить черновик данными сервера", IsVisible = false };
        var panel = new StackPanel { Margin = new Thickness(10), Spacing = 8 };
        foreach (var field in fields) panel.Children.Add(field);
        foreach (var field in new Control[] { error, save, reconcile, reload, replace }) panel.Children.Add(field);
        void Update()
        {
            error.Text = attempt.Message;
            save.IsEnabled = !attempt.IsBlocked && !attempt.IsBusy;
            reconcile.IsVisible = attempt.CanReconcile; reconcile.IsEnabled = !attempt.IsBusy;
            reload.IsVisible = attempt.CanReload; reload.IsEnabled = !attempt.IsBusy;
            replace.IsVisible = attempt.Current != null; replace.IsEnabled = !attempt.IsBusy;
            if (attempt.IsCommitted) closed.Cancel();
        }
        async Task Run(Func<Task> action)
        {
            if (!OwnsSession() || attempt.IsBusy) return;
            save.IsEnabled = reconcile.IsEnabled = reload.IsEnabled = replace.IsEnabled = false;
            await action();
            if (OwnsSession()) Update();
        }
        save.Click += async (_, _) => await Run(async () => { readFields(attempt.Draft); await attempt.SubmitAsync(() => OwnsSession() && canSubmit(), closed.Token); });
        reconcile.Click += async (_, _) => await Run(() => attempt.ReconcileAsync(closed.Token));
        reload.Click += async (_, _) => await Run(() => attempt.ReloadAsync(closed.Token));
        replace.Click += (_, _) => { if (!OwnsSession()) return; attempt.ReplaceWithCurrent(); writeFields(attempt.Draft); Update(); };
        writeFields(attempt.Draft);
        try
        {
            await host.ShowAsync(title, new ScrollViewer { Content = panel }, closed.Token, () => !attempt.IsBusy);
            return attempt.IsCommitted && token == api.AuthToken && origin == api.CurrentBaseUrl && !cancellationToken.IsCancellationRequested;
        }
        finally { closed.Cancel(); api.OnAuthTokenChanged -= SessionChanged; }
    }
}
