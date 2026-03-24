using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services
{
    public class ApiClientService
    {
        private static ApiClientService? _instance;
        private static readonly object _lock = new();
        private string? _authToken;
        private bool? _isAdmin;
        private int? _userRole;
        private string? _roleName;

        // Cached discovered base URL (null = not yet discovered)
        private string? _discoveredBaseUrl;
        private static readonly SemaphoreSlim _discoveryLock = new(1, 1);

        // Shared HttpClient for discovery probes — avoids socket exhaustion from per-probe HttpClient creation
        private static readonly HttpClient _probeClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(ProbeTimeoutMs) };

        // IP range to scan: 192.168.0.100 – 192.168.0.249
        private const int ScanRangeStart = 100;
        private const int ScanRangeEnd = 249;
        private const int ApiPort = 5000;
        private const string PingPath = "api/discovery/ping";
        private const int ProbeTimeoutMs = 500;

        public static ApiClientService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ApiClientService();
                    }
                }
                return _instance;
            }
        }

        private ApiClientService()
        {
        }

        public string? AuthToken
        {
            get => _authToken;
            set
            {
                if (_authToken != value)
                {
                    Serilog.Log.Information("ApiClientService.AuthToken: Setting new token");
                    if (value != null)
                    {
                       Serilog.Log.Debug("ApiClientService.AuthToken: New token length: {Length}, preview: {Preview}...",
                            value.Length, value.Length > 20 ? value.Substring(0, 20) : value);
                    }
                    else
                    {
                        Serilog.Log.Information("ApiClientService.AuthToken: Token cleared (set to null)");
                    }
                    _authToken = value;
                    OnAuthTokenChanged?.Invoke(this, value);
                }
                else
                {
                    Serilog.Log.Debug("ApiClientService.AuthToken: Token value unchanged, skipping event");
                }
            }
        }

        public bool? IsAdmin
        {
            get => _isAdmin;
            set
            {
                _isAdmin = value;
                OnAdminStatusChanged?.Invoke(this, value);
            }
        }

        public int? UserRole
        {
            get => _userRole;
            set
            {
                _userRole = value;
                OnUserRoleChanged?.Invoke(this, value);
                RoleName = GetRussianRoleName(value);
            }
        }

        public string? RoleName
        {
            get => _roleName;
            private set
            {
                _roleName = value;
                OnRoleNameChanged?.Invoke(this, value);
            }
        }

        public event EventHandler<string?> OnAuthTokenChanged;
        public event EventHandler<bool?> OnAdminStatusChanged;
        public event EventHandler<int?> OnUserRoleChanged;
        public event EventHandler<string?> OnRoleNameChanged;

        /// <summary>
        /// Returns the currently cached base URL, or null if discovery hasn't run yet.
        /// </summary>
        public string? CurrentBaseUrl => _discoveredBaseUrl ?? $"http://localhost:{ApiPort}/api/";

        /// <summary>
        /// Probes localhost first, then scans 192.168.0.100–249 in parallel.
        /// Caches the first responding host and returns it.
        /// </summary>
        public async Task<string> DiscoverApiBaseUrlAsync(CancellationToken cancellationToken = default)
        {
            // Return cached result if already discovered
            if (_discoveredBaseUrl != null)
                return _discoveredBaseUrl;

            await _discoveryLock.WaitAsync(cancellationToken);
            try
            {
                // Double-check after acquiring lock
                if (_discoveredBaseUrl != null)
                    return _discoveredBaseUrl;

                Serilog.Log.Information("ApiClientService: Starting API server discovery...");

                // 1. Try localhost first (fastest path for the dev machine)
                var localhostUrl = $"http://localhost:{ApiPort}/";
                if (await PingApiAsync(localhostUrl, cancellationToken))
                {
                    Serilog.Log.Information("ApiClientService: API found at localhost");
                    _discoveredBaseUrl = $"{localhostUrl}api/";
                    return _discoveredBaseUrl;
                }

                // 2. Scan LAN range in parallel — subnet from config or auto-detected local /24
                var subnet = GetDiscoverySubnet();
                Serilog.Log.Information("ApiClientService: Scanning LAN range {Subnet}.{Start}-{End}...",
                    subnet, ScanRangeStart, ScanRangeEnd);

                var candidates = Enumerable.Range(ScanRangeStart, ScanRangeEnd - ScanRangeStart + 1)
                    .Select(i => $"http://{subnet}.{i}:{ApiPort}/");

                var found = await FindFirstRespondingHostAsync(candidates, cancellationToken);
                if (found != null)
                {
                    Serilog.Log.Information("ApiClientService: API found at {Url}", found);
                    _discoveredBaseUrl = $"{found}api/";
                    return _discoveredBaseUrl;
                }

                // 3. Fall back to localhost even if it didn't respond (offline/dev scenario)
                Serilog.Log.Warning("ApiClientService: No API server found on LAN, falling back to localhost");
                _discoveredBaseUrl = $"http://localhost:{ApiPort}/api/";
                return _discoveredBaseUrl;
            }
            finally
            {
                _discoveryLock.Release();
            }
        }

        /// <summary>
        /// Resets the cached discovery result so the next call to DiscoverApiBaseUrlAsync re-scans.
        /// </summary>
        public void ResetDiscovery()
        {
            _discoveredBaseUrl = null;
            Serilog.Log.Information("ApiClientService: Discovery cache cleared");
        }

        /// <summary>
        /// Returns the /24 subnet prefix to scan (e.g. "192.168.0").
        /// Reads DiscoverySubnet from app config if available, otherwise auto-detects
        /// the local machine's first non-loopback IPv4 /24.
        /// </summary>
        private static string GetDiscoverySubnet()
        {
            // Check environment variable override first
            var envSubnet = Environment.GetEnvironmentVariable("BRU_DISCOVERY_SUBNET");
            if (!string.IsNullOrWhiteSpace(envSubnet))
                return envSubnet.TrimEnd('.');

            // Auto-detect: find first non-loopback IPv4 address and use its /24
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var addr in host.AddressList)
                {
                    if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        continue;
                    var bytes = addr.GetAddressBytes();
                    if (bytes[0] == 127) continue; // skip loopback
                    return $"{bytes[0]}.{bytes[1]}.{bytes[2]}";
                }
            }
            catch { /* fall through to default */ }

            return "192.168.0";
        }

        public HttpClient CreateClient()
        {
            var baseUrl = _discoveredBaseUrl ?? $"http://localhost:{ApiPort}/api/";
            var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl)
            };

            if (!string.IsNullOrEmpty(_authToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _authToken);
                Serilog.Log.Debug("ApiClientService.CreateClient: Authorization header added with token (length: {Length})", _authToken.Length);
            }
            else
            {
                Serilog.Log.Warning("ApiClientService.CreateClient: No auth token available, Authorization header NOT added");
                Serilog.Log.Warning("ApiClientService.CreateClient: _authToken is null or empty. Current value: {TokenValue}",
                    _authToken ?? "(null)");
            }

            return client;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static async Task<bool> PingApiAsync(string baseUrl, CancellationToken cancellationToken)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(ProbeTimeoutMs);

                var response = await _probeClient.GetAsync($"{baseUrl.TrimEnd('/')}/{PingPath}", cts.Token);
                if (!response.IsSuccessStatusCode) return false;

                var body = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("status", out var status)
                    && status.GetString() == "ok";
            }
            catch
            {
                return false;
            }
        }

        private static async Task<string?> FindFirstRespondingHostAsync(
            IEnumerable<string> candidates,
            CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var tasks = candidates
                .Select(url => Task.Run(async () =>
                {
                    var ok = await PingApiAsync(url, cts.Token);
                    return ok ? url : null;
                }, cts.Token))
                .ToList();

            while (tasks.Count > 0)
            {
                var completed = await Task.WhenAny(tasks);
                tasks.Remove(completed);

                try
                {
                    var result = await completed;
                    if (result != null)
                    {
                        cts.Cancel(); // stop remaining probes
                        return result;
                    }
                }
                catch
                {
                    // probe failed, continue
                }
            }

            return null;
        }

        private string? GetRussianRoleName(int? role)
        {
            return role switch
            {
                0 => "Пользователь",
                1 => "Администратор",
                2 => "Менеджер",
                3 => "Диспетчер",
                4 => "Кассир",
                5 => "Водитель",
                6 => "Кондуктор",
                7 => "Механик",
                8 => "Инженер",
                9 => "Контролер",
                10 => "Инспектор",
                _ => null
            };
        }
    }
}
