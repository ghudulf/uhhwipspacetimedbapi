using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

public interface IQrScanner { Task<string?> ScanAsync(CancellationToken cancellationToken); }
public static class QrScanner
{
    public static IQrScanner Current { get; set; } = new ManualQrScanner();
}
/// <summary>Working keyboard/paste fallback. Hosts may register a camera scanner; this implementation never accesses a camera.</summary>
public sealed class ManualQrScanner : IQrScanner
{
    public async Task<string?> ScanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = new TextBox { Watermark = "Вставьте QR-текст bruqr://…", MaxLength = 2048 };
        AutomationProperties.SetName(input, "QR-текст для подтверждения входа");
        var submit = new Button { Content = "Открыть QR", IsDefault = true };
        AutomationProperties.SetName(submit, "Открыть вставленный QR-код");
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        AutomationProperties.SetName(cancel, "Отменить ввод QR-кода");
        var instruction = new TextBlock { Text = "Камера не подключена. Вставьте или введите QR-текст.", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        AutomationProperties.SetName(instruction, "Инструкция ручного ввода QR-кода");
        var window = new Window { Title = "Подтвердить вход по QR", Width = 480, Height = 230, CanResize = false,
            Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = { instruction, input,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { submit, cancel } } } } };
        AutomationProperties.SetName(window, "Ручной ввод QR-кода");
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        submit.Click += (_, _) => { completion.TrySetResult(input.Text); window.Close(); };
        cancel.Click += (_, _) => window.Close();
        window.Closed += (_, _) => completion.TrySetResult(null);
        window.Opened += (_, _) => input.Focus();
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => window.Close()));
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }) window.Show(owner);
        else window.Show();
        return await completion.Task;
    }
}
