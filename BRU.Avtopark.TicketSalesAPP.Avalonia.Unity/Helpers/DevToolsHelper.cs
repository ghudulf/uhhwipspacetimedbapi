using Avalonia;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Helpers
{
    /// <summary>
    /// Ensures AttachDeveloperTools is only called once per application lifetime.
    /// Calling it more than once throws InvalidOperationException.
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
                Application.Current?.AttachDeveloperTools();
                _attached = true;
            }
        }
    }
}
