namespace BRU_AVTOPARK_AspireAPI.ApiService.Services;

/// <summary>Owns the one-shot startup operation and cancels it with the host.</summary>
public sealed class OAuthClientRegistrationService(Func<CancellationToken, Task> register, ILogger<OAuthClientRegistrationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await register(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError("Default OAuth client setup failed: {ErrorType}", ex.GetType().Name); }
    }
}
