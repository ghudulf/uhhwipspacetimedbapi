using Avalonia;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers
{
    /// <summary>
    /// Ensures AttachDeveloperTools is only called once per application lifetime.
    /// Calling AttachOnce multiple times is safe; subsequent calls are no-op and do not throw.
    /// </summary>
    internal static class DevToolsHelper
    {
        private static bool _attached = false;
        private static readonly object _lock = new();

        public static void AttachOnce()
        {
            lock (_lock)
            {
                if (_attached) return;
                if (Application.Current == null) return;
                Application.Current.AttachDeveloperTools();
                _attached = true;
            }
        }
    }
}