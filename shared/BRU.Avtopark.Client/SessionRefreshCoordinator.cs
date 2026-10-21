namespace BRU.Avtopark.Client;

public interface ISessionRefresher
{
    /// <summary>Rotate once, persist the replacement, and return the new access token. Never retry a refresh request.</summary>
    Task<string?> RefreshAsync(string apiBase, string expiredAccessToken, CancellationToken cancellationToken = default);
}

/// <summary>One refresh flight across callers, with cached outcomes for late arrivals and a failure cooldown.</summary>
public sealed class SessionRefreshCoordinator(ISessionRefresher refresher, TimeProvider clock)
{
    private readonly object gate = new();
    private Task<string?>? flight;
    private string? origin, expired, replacement;
    private DateTimeOffset failedUntil;
    private bool notified;
    public event Action? SessionExpired;
    public bool IsReplacement(string apiBase, string oldToken, string? current)
    { lock (gate) return origin == apiBase && expired == oldToken && replacement != null && replacement == current; }
    public Task<string?> RefreshAsync(string apiBase, string token, CancellationToken cancellationToken = default)
    {
        Task<string?> current;
        lock (gate)
        {
            if (origin == apiBase && expired == token && flight != null && (!flight.IsCompleted || replacement != null || clock.GetUtcNow() < failedUntil)) current = flight;
            else if (flight is { IsCompleted: false }) current = WaitForPreviousAsync(flight, apiBase, token);
            else
            {
                if (origin != apiBase || expired != token) notified = false;
                origin = apiBase; expired = token; replacement = null;
                current = flight = RunAsync(apiBase, token);
            }
        }
        return current.WaitAsync(cancellationToken); // One caller's cancellation cannot cancel/duplicate a shared rotation.
    }
    private async Task<string?> WaitForPreviousAsync(Task<string?> previous, string apiBase, string token)
    { await previous.ConfigureAwait(false); return await RefreshAsync(apiBase, token).ConfigureAwait(false); }
    private async Task<string?> RunAsync(string apiBase, string token)
    {
        await Task.Yield(); // Publish the flight before a synchronously completing fake/refresher can finish.
        string? result;
        try { result = await refresher.RefreshAsync(apiBase, token, CancellationToken.None).ConfigureAwait(false); }
        catch { result = null; }
        Action? notify = null;
        lock (gate)
        {
            replacement = string.IsNullOrEmpty(result) ? null : result;
            if (replacement == null)
            {
                failedUntil = clock.GetUtcNow().AddSeconds(30);
                if (!notified) { notified = true; notify = SessionExpired; }
            }
        }
        try { notify?.Invoke(); } catch { } // UI notifications never change a completed rotation outcome.
        return replacement;
    }
    public async Task<T> ExecuteAsync<T>(string apiBase, string token, bool mutation,
        Func<string, CancellationToken, Task<T>> execute, CancellationToken cancellationToken = default)
    {
        try { return await execute(token, cancellationToken).ConfigureAwait(false); }
        catch (ApplicationCommandException ex) when (!mutation && ex.Code == "session_expired")
        {
            var refreshed = await RefreshAsync(apiBase, token, cancellationToken).ConfigureAwait(false);
            if (refreshed == null) throw;
            // A read is retried once only. Mutations never enter this path, including uncertain writes.
            try { return await execute(refreshed, cancellationToken).ConfigureAwait(false); }
            catch (ApplicationCommandException retry) when (retry.Code == "session_expired") { SignalExpired(); throw; }
        }
    }
    public async IAsyncEnumerable<QueryUpdate<T>> WatchAsync<T>(string apiBase, string token,
        Func<string, CancellationToken, IAsyncEnumerable<QueryUpdate<T>>> watch,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var refreshesWithoutSnapshot = 0;
        while (true)
        {
            ApplicationCommandException? failure = null;
            await using (var iterator = watch(token, cancellationToken).GetAsyncEnumerator(cancellationToken))
            {
                while (true)
                {
                    bool next;
                    try { next = await iterator.MoveNextAsync().ConfigureAwait(false); }
                    catch (ApplicationCommandException ex) when (ex.Code is "session_expired" or "session_changed") { failure = ex; break; }
                    if (!next) yield break;
                    if (iterator.Current.IsSynchronized) refreshesWithoutSnapshot = 0;
                    yield return iterator.Current;
                }
            }
            if (failure == null) yield break;
            string? nextToken;
            lock (gate) nextToken = origin == apiBase && expired == token ? replacement : null;
            if (failure.Code == "session_expired" && nextToken == null && refreshesWithoutSnapshot++ == 0)
                nextToken = await RefreshAsync(apiBase, token, cancellationToken).ConfigureAwait(false);
            if (nextToken == null) { if (failure.Code == "session_expired") SignalExpired(); throw failure; }
            token = nextToken;
            yield return new(default, false, "session_changed");
        }
    }
    private void SignalExpired()
    {
        Action? notify = null;
        lock (gate) { if (!notified) { notified = true; notify = SessionExpired; } }
        try { notify?.Invoke(); } catch { }
    }
}
