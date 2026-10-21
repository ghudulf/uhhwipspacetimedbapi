using BRU.Avtopark.Client;

internal static class SessionRefreshCoordinatorTests
{
    public static async Task RunAsync()
    {
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var refresh = new Refresher(async () => { Interlocked.Increment(ref calls); return await release.Task; });
        var coordinator = new SessionRefreshCoordinator(refresh, TimeProvider.System);
        var retries = 0;
        var work = Enumerable.Range(0, 50).Select(_ => coordinator.ExecuteAsync("https://api.test", "old", false, (token, _) =>
        {
            if (token == "old") throw new ApplicationCommandException("session_expired");
            Interlocked.Increment(ref retries); return Task.FromResult(token);
        })).ToArray();
        release.SetResult("new");
        Check((await Task.WhenAll(work)).All(token => token == "new") && calls == 1 && retries == 50 && coordinator.IsReplacement("https://api.test", "old", "new"), "50 expiries did not share one rotation");
        var mutationCalls = 0;
        try { await coordinator.ExecuteAsync<string>("https://api.test", "new", true, (_, _) => { mutationCalls++; throw new ApplicationCommandException("session_expired"); }); }
        catch (ApplicationCommandException) { }
        Check(mutationCalls == 1 && calls == 1, "mutation refreshed/retried");
        var failureCalls = 0; var events = 0;
        var failed = new SessionRefreshCoordinator(new Refresher(() => { failureCalls++; throw new InvalidOperationException("private-token-sentinel"); }), TimeProvider.System);
        failed.SessionExpired += () => events++;
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => failed.RefreshAsync("https://api.test", "old")));
        Check(await failed.RefreshAsync("https://api.test", "old") == null && failureCalls == 1 && events == 1, "failure cooldown/storm/event coalescing");
        var acquisitions = new List<string>();
        async IAsyncEnumerable<QueryUpdate<string>> Watch(string token, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); await Task.Yield(); acquisitions.Add(token);
            if (token == "old") { yield return new("old snapshot", true); throw new ApplicationCommandException("session_expired"); }
            yield return new("fresh snapshot", true);
        }
        var updates = new List<QueryUpdate<string>>();
        await foreach (var item in coordinator.WatchAsync("https://api.test", "old", Watch)) updates.Add(item);
        Check(acquisitions.SequenceEqual(["old", "new"]) && calls == 1 && updates is [{ IsSynchronized: true }, { IsSynchronized: false, Reason: "session_changed" }, { Data: "fresh snapshot", IsSynchronized: true }], "subscription reacquisition refreshed twice or missed invalidation");
        Console.WriteLine("PASS shared session refresh coordinator fakes: 50 concurrent read expiries share one rotation, one read retry, mutation never refreshed/retried, failure cooldown, one SessionExpired notification and cached replacement for subscription reacquisition");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Session coordinator: " + message); }
    private sealed class Refresher(Func<Task<string?>> refresh) : ISessionRefresher
    { public Task<string?> RefreshAsync(string apiBase, string expiredAccessToken, CancellationToken cancellationToken = default) => refresh(); }
}
