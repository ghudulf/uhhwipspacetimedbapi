using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

public sealed record RefreshRotation(UserProfile User, string RefreshToken, DateTimeOffset FamilyExpiresAt);

/// <summary>Bounded, process-local sliding budgets. Keys contain only addresses or persisted family hashes.</summary>
public sealed class RefreshRateBudget(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> entries = new(StringComparer.Ordinal);
    public bool Allow(string key, int limit)
    {
        lock (gate)
        {
            var cutoff = clock.GetUtcNow().AddMinutes(-1);
            foreach (var entry in entries.ToArray())
            {
                while (entry.Value.TryPeek(out var old) && old <= cutoff) entry.Value.Dequeue();
                if (entry.Value.Count == 0) entries.Remove(entry.Key);
            }
            if (!entries.TryGetValue(key, out var queue))
            {
                if (entries.Count >= 20000 || key.Length > 160) return false;
                entries[key] = queue = new();
            }
            if (queue.Count >= limit) return false;
            queue.Enqueue(clock.GetUtcNow()); return true;
        }
    }
}

public sealed class NativeRefreshTokens(ISpacetimeDBService database, IHttpContextAccessor contexts, IConfiguration configuration,
    IHostEnvironment environment, RefreshRateBudget budget, TimeProvider clock)
{
    public static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
    public static bool ValidToken(string? token) => token is { Length: 43 } && token.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
    public bool IsNative
    {
        get
        {
            var context = contexts.HttpContext;
            _ = environment; // Retained constructor compatibility; transport policy is environment-independent.
            return context != null && context.Request.Headers["X-BRU-Native-Login"] == "1" && !context.Request.Headers.ContainsKey("Origin") &&
                Security.HttpSecurityPolicy.IsTransportAllowed(context, configuration);
        }
    }
    private string Address => contexts.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    public async Task<string?> IssueAsync(Identity actor)
    {
        if (!IsNative) return null;
        if (!budget.Allow("issue:" + Address, 20)) throw new InvalidOperationException("native_session_unavailable");
        var label = new string((contexts.HttpContext!.Request.Headers["X-BRU-Device"].FirstOrDefault() ?? "native").Trim().Where(c => !char.IsControl(c)).Take(64).ToArray());
        if (label.Length == 0) label = "native";
        var token = NewToken(); var hash = Hash(token);
        var seconds = Math.Clamp(configuration.GetValue("Authentication:RefreshTokens:LifetimeSeconds", 30 * 86400), 3600, 90 * 86400);
        await database.ExecuteReducerAsync(new Reducer.IssueRefreshToken(hash, actor, label, (uint)seconds), _ => true);
        return token;
    }
    public async Task<RefreshRotation?> RotateAsync(string token)
    {
        if (!IsNative || !ValidToken(token) || !budget.Allow("refresh:" + Address, 30)) return null;
        var hash = Hash(token);
        var original = await database.ReadAsync(c => c.Db.RefreshTokenRecord.TokenHash.Find(hash));
        if (original == null || !budget.Allow("family:" + original.FamilyId, 10)) return null;
        var next = NewToken(); var nextHash = Hash(next);
        await database.ExecuteReducerAsync(new Reducer.RotateRefreshToken(hash, nextHash), _ => true);
        // A replay is a committed denial. Never interpret a committed callback alone as refresh success.
        var successor = await database.ReadAsync(c => c.Db.RefreshTokenRecord.TokenHash.Find(nextHash));
        if (successor == null || successor.FamilyId != original.FamilyId || successor.UserId != original.UserId || successor.RevokedAt != null ||
            successor.UsedAt != null || successor.ExpiresAt <= (ulong)clock.GetUtcNow().ToUnixTimeMilliseconds()) return null;
        var user = await database.ReadAsync(c => c.Db.UserProfile.UserId.Find(successor.UserId));
        return user?.IsActive == true ? new(user, next, DateTimeOffset.FromUnixTimeMilliseconds((long)successor.ExpiresAt)) : null;
    }
    public async Task RevokeAsync(string token)
    {
        if (!IsNative || !ValidToken(token) || !budget.Allow("logout:" + Address, 30)) throw new InvalidOperationException("invalid_refresh_token");
        await database.ExecuteReducerAsync(new Reducer.RevokeRefreshFamily(Hash(token)), _ => true);
    }
}
