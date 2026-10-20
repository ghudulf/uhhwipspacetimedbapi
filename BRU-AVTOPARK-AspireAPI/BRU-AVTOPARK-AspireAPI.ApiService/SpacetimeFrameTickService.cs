using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Services;

/// <summary>Owns database connection attempts, frame processing and orderly shutdown.</summary>
public sealed class SpacetimeFrameTickService(ISpacetimeDBService database, ILogger<SpacetimeFrameTickService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryAt = DateTimeOffset.MinValue;
        var attempt = 0;
        var connectingSince = DateTimeOffset.MinValue;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    if ((database.State is DatabaseConnectionState.Connecting or DatabaseConnectionState.Subscribing) && now - connectingSince > TimeSpan.FromSeconds(20))
                        database.Disconnect();
                    if (database.State is DatabaseConnectionState.Disconnected or DatabaseConnectionState.Degraded && now >= retryAt)
                    {
                        connectingSince = now;
                        retryAt = now.AddSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt++, 5))) + Random.Shared.NextDouble());
                        database.Connect();
                    }
                    // Connect/subscription callbacks also require frames while not yet ready.
                    database.ProcessFrameTick();
                    if (database.State == DatabaseConnectionState.Ready) attempt = 0;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Database coordinator iteration failed");
                    database.Disconnect();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { database.Disconnect(); }
    }
}
