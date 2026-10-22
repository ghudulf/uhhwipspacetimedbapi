using Avalonia;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers
{
    /// <summary>
    /// Ensures AttachDeveloperTools is only called once per application lifetime.
    /// Calling AttachOnce multiple times is safe; subsequent calls are no-op and do not throw.
    /// </summary>
    internal static class DevToolsHelper
    {
#if DEBUG
        private static bool _attached = false;
        private static readonly object _lock = new();

#endif

        public static void AttachOnce()
        {
#if DEBUG
            lock (_lock)
            {
                if (_attached) return;
                if (Application.Current == null) return;
                Application.Current.AttachDeveloperTools();
                _attached = true;
            }
#endif
        }
    }
}