namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.V2;

/// <summary>Process-wide admission and reconnect-resistant authentication budget. Forwarded IPs are configured by the host.</summary>
public sealed class ConnectionAdmission
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset Until, int Count)> _attempts = new(StringComparer.Ordinal);
    private int _total;

    public IDisposable? TryEnter(string address)
    {
        lock (_gate)
        {
            if (_total >= 128 || _connections.GetValueOrDefault(address) >= 4) return null;
            _connections[address] = _connections.GetValueOrDefault(address) + 1;
            _total++;
            return new Lease(this, address);
        }
    }
    public bool TryAuthenticate(string address)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var expired in _attempts.Where(p => p.Value.Until <= now).Select(p => p.Key).ToArray()) _attempts.Remove(expired);
            if (!_attempts.TryGetValue(address, out var budget))
            {
                // Fail closed at capacity instead of evicting an attacker's exhausted budget.
                if (_attempts.Count >= 4096) return false;
                budget = (now.AddMinutes(1), 0);
            }
            if (budget.Count >= 10) return false;
            _attempts[address] = (budget.Until, budget.Count + 1);
            return true;
        }
    }
    private sealed class Lease(ConnectionAdmission owner, string address) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._gate)
            {
                if (--owner._connections[address] == 0) owner._connections.Remove(address);
                owner._total--;
            }
        }
    }
}
