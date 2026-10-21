using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Automation;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

/// <summary>Desktop/MAUI presentation boundary for the existing two-field job editor.</summary>
public sealed class JobEditorService(ControlDialogHost host)
{
    public async Task<bool> ShowAsync(JobRow? original, Func<bool> canSubmit)
    {
        var api = ApiClientService.Instance;
        var origin = api.CurrentBaseUrl ?? throw new ApplicationCommandException("session_changed");
        var token = api.AuthToken ?? throw new ApplicationCommandException("unauthenticated");
        var operationId = Guid.NewGuid().ToString("N");
        var committed = false;
        var blocked = false;
        var draftRevision = original?.Revision;
        using var closed = new CancellationTokenSource();
        var inFlight = false;
        var ended = false;
        var title = new TextBox { Text = original?.JobTitle, PlaceholderText = "Название должности", MaxLength = 200 };
        var internship = new TextBox { Text = original?.Internship, PlaceholderText = "Требования к стажировке", MaxLength = 2000 };
        AutomationProperties.SetName(title, "Название должности"); AutomationProperties.SetName(internship, "Требования к стажировке");
        var error = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        var save = new Button { Content = original == null ? "Добавить" : "Сохранить", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        var reconcile = new Button { Content = "Проверить результат", IsVisible = false };
        var reload = new Button { Content = "Загрузить актуальные данные", IsVisible = false };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(10) };
        Control[] controls = [title, internship, error, save, reconcile, reload];
        for (var i = 0; i < controls.Length; i++) { grid.Children.Add(controls[i]); Grid.SetRow(controls[i], i); }
        var content = new ScrollViewer { Content = grid };
        bool OwnsSession() => api.AuthToken == token && api.CurrentBaseUrl == origin;
        void SessionChanged(object? sender, string? value) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!ended && !OwnsSession()) closed.Cancel(); });
        api.OnAuthTokenChanged += SessionChanged;
        content.AttachedToVisualTree += (_, _) => title.Focus();
        save.Click += async (_, _) =>
        {
            if (blocked || !OwnsSession() || !canSubmit()) { error.Text = "Дождитесь синхронизации и проверьте права доступа. Черновик сохранён."; return; }
            if (string.IsNullOrWhiteSpace(title.Text)) { error.Text = "Название должности обязательно."; title.Focus(); return; }
            save.IsEnabled = false; inFlight = true;
            try
            {
                if (original == null)
                    await ApplicationMutationClient.ExecuteAsync("jobs.create", new CreateJobCommand(title.Text, internship.Text ?? ""), operationId, origin, token, closed.Token);
                else
                    await ApplicationMutationClient.ExecuteAsync("jobs.update", new UpdateJobCommand(original.JobId, draftRevision, title.Text, internship.Text ?? ""), operationId, origin, token, closed.Token);
                if (OwnsSession() && !closed.IsCancellationRequested) { committed = true; closed.Cancel(); }
            }
            catch (Exception exception)
            {
                if (closed.IsCancellationRequested) return;
                var code = exception is ApplicationCommandException failure ? failure.Code : "commit_outcome_unknown";
                Serilog.Log.Warning("Job editor operation {OperationId}: {Code}", operationId, code);
                blocked = BlocksRetry(original, code);
                reconcile.IsVisible = code == "commit_outcome_unknown";
                reload.IsVisible = original != null && code == "conflict";
                error.Text = code == "conflict" ? "Данные изменились или изменение отклонено. Черновик сохранён; можно загрузить актуальные данные."
                    : code == "commit_outcome_unknown" ? "Результат неизвестен. Проверьте его перед повтором; черновик сохранён."
                    : "Изменение отклонено. Проверьте поля и права доступа.";
                save.IsEnabled = !blocked;
            }
            finally { inFlight = false; }
        };
        reconcile.Click += async (_, _) =>
        {
            if (!OwnsSession()) return;
            reconcile.IsEnabled = false;
            try
            {
                var receipt = await ApplicationQueryClient.ReadAsync<ReceiptRequest, MutationResult?>("mutations.receipt", new(operationId), closed.Token);
                if (!OwnsSession() || closed.IsCancellationRequested) return;
                if (receipt != null) { committed = true; closed.Cancel(); }
                else error.Text = "Подтверждение пока не найдено. Это не означает отмену операции. Обновите список или проверьте снова.";
            }
            catch (Exception exception) { error.Text = "Не удалось проверить результат. Повторите проверку."; Serilog.Log.Warning("Job receipt lookup failed: {ErrorType}", exception.GetType().Name); }
            finally { reconcile.IsEnabled = true; }
        };
        reload.Click += async (_, _) =>
        {
            if (original == null || !OwnsSession()) return;
            reload.IsEnabled = false;
            try
            {
                var current = await ApplicationQueryClient.ReadAsync<EntityRequest, JobRow>("jobs.get", new(original.JobId), closed.Token);
                if (!OwnsSession() || closed.IsCancellationRequested) return;
                // A separate explicit action replaces the draft; loading alone never discards it.
                error.Text = $"Сейчас на сервере: {current.JobTitle} / {current.Internship}. Ваш черновик остаётся в полях.";
                var replace = new Button { Content = "Заменить черновик данными сервера" };
                replace.Click += (_, _) =>
                {
                    if (!OwnsSession() || closed.IsCancellationRequested) return;
                    title.Text = current.JobTitle; internship.Text = current.Internship;
                    draftRevision = current.Revision; operationId = Guid.NewGuid().ToString("N");
                    blocked = false; save.IsEnabled = true; reload.IsVisible = false;
                    grid.Children.Remove(replace); error.Text = "Актуальные данные загружены. Проверьте изменения перед сохранением.";
                };
                grid.Children.Add(replace); Grid.SetRow(replace, 5); reload.IsVisible = false;
            }
            catch (Exception exception)
            {
                if (!closed.IsCancellationRequested) error.Text = "Не удалось загрузить актуальную запись. Черновик сохранён.";
                Serilog.Log.Warning("Job conflict reload failed: {ErrorType}", exception.GetType().Name);
            }
            finally { reload.IsEnabled = true; }
        };
        try { await host.ShowAsync(original == null ? "Добавить должность" : "Редактировать должность", content, closed.Token, () => !inFlight); return committed; }
        finally { ended = true; closed.Cancel(); api.OnAuthTokenChanged -= SessionChanged; }
    }
    private static bool BlocksRetry(JobRow? original, string code) =>
        code is not ("validation_failed" or "forbidden") && !(original == null && code == "conflict");
}
