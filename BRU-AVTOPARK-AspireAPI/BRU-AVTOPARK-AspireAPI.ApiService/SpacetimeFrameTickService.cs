using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Services
{
    public class SpacetimeFrameTickService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpacetimeFrameTickService> _logger;
    private DateTime _lastConnectionAttempt = DateTime.MinValue;
    private readonly TimeSpan _connectionRetryDelay = TimeSpan.FromSeconds(5);
    private volatile bool _connectionInProgress = false;

    public SpacetimeFrameTickService(
        IServiceProvider serviceProvider,
        ILogger<SpacetimeFrameTickService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var spacetimeService = scope.ServiceProvider.GetRequiredService<ISpacetimeDBService>();

                if (!spacetimeService.IsConnected())
                {
                    // Only attempt to connect if we're not already trying
                    if (!_connectionInProgress)
                    {
                        var now = DateTime.Now;
                        if (now - _lastConnectionAttempt > _connectionRetryDelay)
                        {
                            _lastConnectionAttempt = now;
                            _connectionInProgress = true;
                            try
                            {
                                _logger.LogInformation("Attempting to connect to SpacetimeDB");
                                spacetimeService.Connect();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to connect to SpacetimeDB");
                                _connectionInProgress = false;
                            }
                        }
                    }
                }
                else
                {
                    _connectionInProgress = false;
                    spacetimeService.ProcessFrameTick();
                }

                await Task.Delay(100, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SpacetimeFrameTickService");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
}