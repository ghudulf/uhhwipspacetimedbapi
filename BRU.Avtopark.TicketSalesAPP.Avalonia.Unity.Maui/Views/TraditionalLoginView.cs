using Microsoft.Maui.Controls;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui.Views;

/// <summary>
/// MAUI View that wraps the Avalonia <c>TraditionalLoginControl</c>.
/// The corresponding <see cref="Handlers.TraditionalLoginViewHandler"/> creates the
/// Avalonia control and bridges the <c>AuthCompleted</c> event back to MAUI Shell navigation.
/// </summary>
public class TraditionalLoginView : View
{
    // ── Events ───────────────────────────────────────────────────────────

    /// <summary>Raised when the traditional login wizard completes (success=true) or is cancelled.</summary>
    public event EventHandler<bool>? AuthCompleted;

    internal void RaiseAuthCompleted(bool success) =>
        AuthCompleted?.Invoke(this, success);
}
