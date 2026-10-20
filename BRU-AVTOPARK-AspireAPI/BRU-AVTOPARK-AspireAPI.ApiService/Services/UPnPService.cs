using SharpOpenNat;
using Microsoft.Extensions.Options;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Services;

/// <summary>Renews and removes only the mapping created by this service instance.</summary>
public sealed class UPnPService(ILogger<UPnPService> logger, IOptions<UPnPOptions> options) : IHostedService, IDisposable
{
    private readonly UPnPOptions _options = options.Value;
    private readonly CancellationTokenSource _lifetime = new();
    private NatDevice? _device;
    private Mapping? _ownedMapping;
    private Task? _renewal;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) { logger.LogInformation("UPnP disabled by configuration"); return; }
        if (_options.InternalPort is < 1 or > 65535 || _options.ExternalPort is < 1 or > 65535 || _options.LeaseDurationSeconds < 0)
            throw new InvalidOperationException("Invalid UPnP port or lease configuration.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.DiscoveryTimeoutSeconds, 1, 60)));
            _device = await NatDiscoverer.DiscoverDeviceAsync(PortMapper.Upnp, timeout);
            if ((await _device.GetAllMappingsAsync().WaitAsync(timeout.Token)).Any(m => m.Protocol == Protocol.Tcp && m.PublicPort == _options.ExternalPort))
            {
                logger.LogInformation("UPnP requested port already mapped; preserving existing mapping");
                return;
            }
            var label = $"{_options.MappingDescription} [{Guid.NewGuid():N}]";
            var mapping = new Mapping(Protocol.Tcp, _options.InternalPort, _options.ExternalPort, _options.LeaseDurationSeconds, label);
            await _device.CreatePortMapAsync(mapping).WaitAsync(timeout.Token);
            _ownedMapping = mapping; // Ownership starts only after successful creation.
            logger.LogInformation("UPnP mapping created for TCP {Port}, lease {LeaseSeconds}s", mapping.PublicPort, _options.LeaseDurationSeconds);
            if (_options.LeaseDurationSeconds > 0) _renewal = RenewAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "UPnP mapping unavailable; API startup continues"); }
    }

    private async Task RenewAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.LeaseDurationSeconds / 2d)));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var mapping = _ownedMapping;
                if (mapping == null || _device == null) return;
                var current = await _device.GetSpecificMappingAsync(Protocol.Tcp, mapping.PublicPort).WaitAsync(timeout.Token);
                if (!SameMapping(current, mapping))
                {
                    _ownedMapping = null;
                    logger.LogWarning("UPnP mapping ownership changed; renewal stopped");
                    return;
                }
                await _device.CreatePortMapAsync(mapping).WaitAsync(timeout.Token);
                logger.LogDebug("UPnP lease renewed for TCP {Port}", mapping.PublicPort);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogWarning(ex, "UPnP renewal failed; lease may expire"); }
    }

    private static bool SameMapping(Mapping? current, Mapping owned) => current != null &&
        current.Description == owned.Description && current.PrivatePort == owned.PrivatePort && current.PublicPort == owned.PublicPort;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _lifetime.Cancel();
        if (_renewal != null) await _renewal.WaitAsync(cancellationToken);
        if (_device == null || _ownedMapping == null) return;
        try
        {
            var current = await _device.GetSpecificMappingAsync(Protocol.Tcp, _ownedMapping.PublicPort).WaitAsync(cancellationToken);
            if (SameMapping(current, _ownedMapping)) await _device.DeletePortMapAsync(_ownedMapping).WaitAsync(cancellationToken);
            _ownedMapping = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogWarning(ex, "UPnP mapping cleanup failed; lease may expire"); }
    }
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
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
