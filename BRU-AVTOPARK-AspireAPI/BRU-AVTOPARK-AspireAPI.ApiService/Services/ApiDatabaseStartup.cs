using Microsoft.Extensions.Diagnostics.HealthChecks;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Services;

/// <summary>The API makes its own first connection. Optional owner-authorized startup grants that exact identity.</summary>
public sealed class ApiDatabaseStartup(ISpacetimeDBService database, IConfiguration configuration, ILogger<ApiDatabaseStartup> logger) : BackgroundService
{
    private string _status = "connecting";
    public string Status => Volatile.Read(ref _status);
    public bool IsReady => Status == "ready" && database.IsSubscriptionReady();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempted = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!database.IsSubscriptionReady() || database.GetLocalIdentity() is not { } identity) SetStatus("connecting");
                    else if (await database.ReadAsync(c => c.Db.TrustedApiIdentity.Identity.Find(identity) != null, stoppingToken))
                    {
                        attempted = true; // Revocation must never trigger automatic re-provisioning.
                        SetStatus("ready");
                    }
                    else
                    {
                        SetStatus("awaiting_trust");
                        if (!attempted && configuration.GetValue<bool>("SpacetimeDB:Bootstrap:Enabled"))
                        {
                            attempted = true;
                            SetStatus("provisioning");
                            var ownerFile = configuration["SpacetimeDB:Bootstrap:OwnerTokenFile"];
                            if (string.IsNullOrWhiteSpace(ownerFile)) throw new IOException("Bootstrap owner credential file is required.");
                            var passwordFile = configuration["SpacetimeDB:Bootstrap:InitialAdminPasswordFile"];
                            var password = string.IsNullOrWhiteSpace(passwordFile) ? null : DatabaseInstallationProvisioner.ReadPrivateSecret(passwordFile);
                            await DatabaseInstallationProvisioner.ProvisionAsync(configuration["SpacetimeDB:Host"] ?? "http://localhost:3000",
                                configuration["SpacetimeDB:DatabaseName"] ?? "avtopark", DatabaseInstallationProvisioner.ReadOwnerToken(ownerFile), identity,
                                password == null ? null : configuration["SpacetimeDB:Bootstrap:AdminLogin"] ?? "admin", password, stoppingToken, Security.HttpSecurityPolicy.Mode(configuration));
                            logger.LogInformation("Owner-authorized API startup provisioning committed for API identity {ApiIdentity}. Remove bootstrap secrets/configuration after initial setup.", identity);
                            // Recheck the API's own subscribed trust row before claiming readiness.
                            SetStatus("awaiting_trust");
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    SetStatus("provisioning_failed");
                    logger.LogError("Database startup not ready: {ErrorType}. Check owner authority, private credential files and existing admin-password state. No automatic retry of provisioning.", exception.GetType().Name);
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { SetStatus("stopped"); }
    }
    private void SetStatus(string value)
    {
        if (Status == value) return;
        Volatile.Write(ref _status, value);
        logger.LogInformation("API database startup: {Status}", value);
        if (value == "awaiting_trust") logger.LogWarning("API is connected but not provisioned. Provision its logged identity as deployment owner, or configure SpacetimeDB:Bootstrap with private owner/password files and restart.");
    }
}

public sealed class ApiDatabaseStartupHealthCheck(ApiDatabaseStartup startup) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(startup.IsReady ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("database_" + startup.Status));
}
