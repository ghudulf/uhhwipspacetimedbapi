using SharpOpenNat;
using Microsoft.Extensions.Options;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Services;

/// <summary>
/// Hosted service that registers a UPnP port mapping on startup and removes it on shutdown.
/// Allows the API server to be reachable from the LAN without manual router configuration.
/// Non-fatal: if the router doesn't support UPnP or the mapping fails, the server still starts normally.
/// </summary>
public sealed class UPnPService : IHostedService
{
    private readonly ILogger<UPnPService> _logger;
    private readonly UPnPOptions _options;

    private NatDevice? _device;
    private Mapping? _tcpMapping;

    public UPnPService(ILogger<UPnPService> logger, IOptions<UPnPOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("[UPnP] Disabled via configuration — skipping port mapping");
            return;
        }

        try
        {
            _logger.LogInformation("[UPnP] Discovering NAT device (timeout: {Timeout}s)...", _options.DiscoveryTimeoutSeconds);

            using var discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            discoveryCts.CancelAfter(TimeSpan.FromSeconds(_options.DiscoveryTimeoutSeconds));

            _device = await NatDiscoverer.DiscoverDeviceAsync(PortMapper.Upnp, discoveryCts);

            var externalIp = await _device.GetExternalIPAsync();
            _logger.LogInformation("[UPnP] Found NAT device. External IP: {ExternalIp}", externalIp);

            // TCP mapping for HTTP
            _tcpMapping = new Mapping(
                Protocol.Tcp,
                _options.InternalPort,
                _options.ExternalPort,
                _options.MappingDescription);

            await _device.CreatePortMapAsync(_tcpMapping);

            _logger.LogInformation(
                "[UPnP] Port mapping created: {ExternalIp}:{ExternalPort} → :{InternalPort}/TCP (desc: {Desc})",
                externalIp, _options.ExternalPort, _options.InternalPort, _options.MappingDescription);
        }
        catch (NatDeviceNotFoundException)
        {
            _logger.LogWarning("[UPnP] No UPnP-capable NAT device found — running without port mapping");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[UPnP] Discovery timed out after {Timeout}s — running without port mapping",
                _options.DiscoveryTimeoutSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UPnP] Port mapping failed — server will still start normally");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_device == null || _tcpMapping == null)
            return;

        try
        {
            await _device.DeletePortMapAsync(_tcpMapping);
            _logger.LogInformation("[UPnP] Port mapping removed: :{ExternalPort}/TCP", _options.ExternalPort);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UPnP] Failed to remove port mapping on shutdown (router may clean it up automatically)");
        }
    }
}

/// <summary>Configuration options for the UPnP port mapping service.</summary>
public sealed class UPnPOptions
{
    public const string SectionName = "UPnP";

    /// <summary>Whether UPnP port mapping is enabled. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The port the server listens on internally. Default: 5000.</summary>
    public int InternalPort { get; set; } = 5000;

    /// <summary>The external port to request on the router. Default: 5000.</summary>
    public int ExternalPort { get; set; } = 5000;

    /// <summary>Lease duration in seconds. 0 = permanent (router-dependent). Default: 3600.</summary>
    public int LeaseDurationSeconds { get; set; } = 3600;

    /// <summary>Description shown in the router's port mapping table.</summary>
    public string MappingDescription { get; set; } = "BRU-AVTOPARK API";

    /// <summary>How long to wait for a UPnP device to respond. Default: 5 seconds.</summary>
    public int DiscoveryTimeoutSeconds { get; set; } = 5;
}
