using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services.Presentation;

/// <summary>Uses a native desktop dialog or an in-view modal for an embedded MAUI/single-view host.</summary>
public sealed class ControlDialogHost(UserControl owner)
{
    private bool _showing;
    public async Task ShowAsync(string title, Control content, CancellationToken cancellationToken, Func<bool>? canClose = null)
    {
        if (_showing) throw new InvalidOperationException("A dialog is already open for this screen.");
        _showing = true;
        try
        {
            if (TopLevel.GetTopLevel(owner) is Window window)
            {
                var dialog = new Window { Title = title, Width = 400, Height = 350, Content = content, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                dialog.Closing += (_, e) => { if (!cancellationToken.IsCancellationRequested && canClose?.Invoke() == false) e.Cancel = true; };
                using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.Close));
                try { if (!cancellationToken.IsCancellationRequested) await dialog.ShowDialog(window); }
                finally { dialog.Content = null; }
                return;
            }
            var previous = owner.Content;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var close = new Button { Content = "Закрыть", HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (_, _) => { if (canClose?.Invoke() != false) completion.TrySetResult(); };
            var modal = new Border { Padding = new Thickness(16), MaxWidth = 500, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            modal.Bind(Border.BackgroundProperty, owner.GetResourceObservable("SystemControlBackgroundAltHighBrush"));
            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock { Text = title }); stack.Children.Add(content); stack.Children.Add(close);
            modal.Child = stack;
            KeyboardNavigation.SetTabNavigation(modal, KeyboardNavigationMode.Cycle);
            var grid = new Grid();
            owner.Content = null;
            grid.Children.Add(new ContentControl { Content = previous, IsEnabled = false });
            grid.Children.Add(modal);
            owner.Content = grid;
            void Detached(object? sender, VisualTreeAttachmentEventArgs e) => completion.TrySetResult();
            owner.DetachedFromVisualTree += Detached;
            using var registration2 = cancellationToken.Register(() => completion.TrySetResult());
            try
            {
                close.Focus();
                await completion.Task;
            }
            finally
            {
                owner.DetachedFromVisualTree -= Detached;
                stack.Children.Remove(content);
                ((ContentControl)grid.Children[0]).Content = null;
                owner.Content = previous;
                owner.Focus();
            }
        }
        finally { _showing = false; }
    }
    public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var accepted = false;
        using var close = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        var yes = new Button { Content = "Подтвердить" };
        yes.Click += (_, _) => { accepted = true; close.Cancel(); };
        panel.Children.Add(yes);
        await ShowAsync(title, panel, close.Token);
        return accepted;
    }
}
