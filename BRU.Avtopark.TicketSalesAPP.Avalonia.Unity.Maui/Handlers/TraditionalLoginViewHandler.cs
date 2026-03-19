using Avalonia.Controls;
using Avalonia.Controls.Maui.Handlers;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Controls;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Views;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Handlers;

/// <summary>
/// Thin UserControl wrapper so TControl resolves to a type defined in this
/// compilation unit, avoiding cross-project generic constraint resolution issues.
/// </summary>
public sealed class TraditionalLoginControlWrapper : UserControl
{
    public TraditionalLoginControl Inner { get; }

    public TraditionalLoginControlWrapper()
    {
        Inner = new TraditionalLoginControl();
        Content = Inner;
    }
}

/// <summary>
/// MAUI handler that bridges <see cref="TraditionalLoginView"/> (MAUI) to
/// <see cref="TraditionalLoginControl"/> (Avalonia).
/// Wires the Avalonia control's AuthCompleted event to Shell navigation.
/// </summary>
public class TraditionalLoginViewHandler : AvaloniaControlHandler<TraditionalLoginView, TraditionalLoginControlWrapper>
{
    public static new readonly IPropertyMapper<TraditionalLoginView, TraditionalLoginViewHandler> Mapper =
        new PropertyMapper<TraditionalLoginView, TraditionalLoginViewHandler>(
            AvaloniaControlHandler<TraditionalLoginView, TraditionalLoginControlWrapper>.Mapper);

    public TraditionalLoginViewHandler() : base(Mapper) { }

    // ── Avalonia control creation ────────────────────────────────────────

    protected override TraditionalLoginControlWrapper CreateAvaloniaControl()
    {
        Console.WriteLine("[TraditionalLoginViewHandler] CreateAvaloniaControl");
        var wrapper = new TraditionalLoginControlWrapper();
        wrapper.Inner.AuthCompleted += OnAvaloniaAuthCompleted;
        return wrapper;
    }

    // ── Avalonia → MAUI event bridge ─────────────────────────────────────

    private void OnAvaloniaAuthCompleted(object? sender, bool success)
    {
        Console.WriteLine($"[TraditionalLoginViewHandler] AuthCompleted: success={success}");

        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            if (VirtualView is not { } view) return;

            view.RaiseAuthCompleted(success);
        });
    }
}