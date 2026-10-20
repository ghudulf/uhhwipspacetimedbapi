using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

public sealed class TwoFactorService(ISpacetimeDBService database, ILogger<TwoFactorService> logger) : ITwoFactorService
{
    public async Task<string> CreateTempTokenAsync(Identity userId, string tokenType, string? deviceInfo = null, string? ipAddress = null)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = (ulong)DateTimeOffset.UtcNow.AddMinutes(9).ToUnixTimeMilliseconds();
        await database.ExecuteReducerAsync(new Reducer.CreateTwoFactorToken(userId, token, false, expires, deviceInfo, ipAddress), _ => true);
        logger.LogInformation("Second-factor challenge committed; type {ChallengeType}", tokenType);
        return token;
    }

    public Task<(bool isValid, Identity? userId)> ValidateTempTokenAsync(string token) => database.ReadAsync(connection =>
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128) return (false, (Identity?)null);
        var row = connection.Db.TwoFactorToken.Iter().FirstOrDefault(t => t.Token == token);
        var valid = row != null && !row.IsUsed && row.ExpiresAt > (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            && connection.Db.UserProfile.UserId.Find(row.UserId)?.IsActive == true;
        logger.LogDebug("Second-factor challenge validation: {Valid}", valid);
        return (valid, valid ? (Identity?)row!.UserId : null);
    });

    public async Task<bool> MarkTokenAsUsedAsync(string token)
    {
        var row = await database.ReadAsync(connection => connection.Db.TwoFactorToken.Iter().FirstOrDefault(t => t.Token == token));
        if (row == null) return false;
        try
        {
            await database.ExecuteReducerAsync(new Reducer.UpdateTwoFactorToken(row.Id, row.UserId, row.Token, true, row.ExpiresAt), _ => true);
            logger.LogInformation("Second-factor challenge consumed at commit");
            return true;
        }
        catch (ReducerRejectedException)
        {
            logger.LogWarning("Second-factor challenge consumption rejected (expired, replayed or disabled account)");
            return false;
        }
        // Uncertain commit outcomes propagate: callers must not issue a session without confirmation.
    }

    public async Task<int> CleanupExpiredTokensAsync()
    {
        var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ids = await database.ReadAsync(connection => connection.Db.TwoFactorToken.Iter()
            .Where(t => t.ExpiresAt <= now).Take(128).Select(t => t.Id).ToArray());
        var removed = 0;
        foreach (var id in ids)
        {
            try { await database.ExecuteReducerAsync(new Reducer.DeleteTwoFactorToken(id), _ => true); removed++; }
            catch (ReducerRejectedException) { /* A concurrent cleanup may already have removed this row. */ }
        }
        logger.LogInformation("Second-factor cleanup committed {Count} removals", removed);
        return removed;
    }
}
