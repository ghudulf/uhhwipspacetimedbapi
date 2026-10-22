using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class QrDeviceTests
{
    public static async Task RunAsync(SpacetimeDBService database, Identity owner, string ownerToken)
    {
        var store = new QrLoginDeviceStore(database);
        foreach (var stale in (await store.ListAsync(owner)).Where(d => d.DeviceId.StartsWith("qr-", StringComparison.Ordinal)))
            await store.RevokeAsync(owner, stale.DeviceId);
        var suffix = Guid.NewGuid().ToString("N");
        var ids = Enumerable.Range(0, 6).Select(i => $"qr-{suffix}-{i}").ToArray();
        foreach (var id in ids.Take(5)) await store.RegisterAsync(owner, id, "synthetic-spki", "Device", "test");
        Check((await store.ListAsync(owner)).Count(d => ids.Contains(d.DeviceId)) == 5, "committed list");
        await Reject(() => store.RegisterAsync(owner, ids[0], "synthetic-spki", "Device", "test"));
        await Reject(() => store.RegisterAsync(owner, ids[5], "synthetic-spki", "Device", "test"));
        await store.TouchAsync(ids[0]);
        Check((await store.FindAsync(owner, ids[0]))?.LastUsedAt != null, "touch");
        await store.RevokeAsync(owner, ids[0]); await store.RevokeAsync(owner, ids[0]);
        Check(await store.FindAsync(owner, ids[0]) == null, "revoked key still usable");
        await store.TouchAsync(ids[0]);
        Check(await database.ReadAsync(c => c.Db.QrLoginDevice.DeviceId.Find(ids[0])!.RevokedAt != null), "revoked touch");
        await store.RegisterAsync(owner, ids[5], "synthetic-spki", "Device", "test");
        var other = Identity.FromHexString(new string('7', 64));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        async Task SetActive(bool active)
        {
            using var body = new StringContent(JsonSerializer.Serialize(new object[] { other.ToString(), active }), Encoding.UTF8, "application/json");
            body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await http.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/prepare_qr_device_test", body);
            response.EnsureSuccessStatusCode();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (await database.ReadAsync(c => c.Db.UserProfile.UserId.Find(other)?.IsActive != active, timeout.Token)) await Task.Delay(10, timeout.Token);
        }
        await SetActive(true);
        await Reject(() => store.RevokeAsync(other, ids[1]));
        var otherDevice = "qr-other-" + suffix;
        await store.RegisterAsync(other, otherDevice, "synthetic-spki", "Other", "test");
        Check(await store.FindAsync(owner, otherDevice) == null, "other user's device returned");
        await SetActive(false);
        Check(await store.FindAsync(other, otherDevice) == null && (await store.ListAsync(other)).Count == 0, "inactive user's device returned");
        await Reject(() => store.RegisterAsync(other, "qr-inactive-" + suffix, "synthetic-spki", "Other", "test"));
        await SetActive(true);
        await store.RevokeAsync(other, otherDevice);
        foreach (var id in ids.Skip(1)) await store.RevokeAsync(owner, id);
        Console.WriteLine("PASS QR DeviceReducersAndStore: committed Register/List/Find/Touch/Revoke, duplicate/five-device limits, owner isolation, revoked/inactive key rejection");
    }
    public static async Task OutsiderAsync(ISpacetimeDBService stranger, Identity owner)
    {
        Check(!await stranger.ReadAsync(c => c.Db.QrLoginDevice.Iter().Any()), "RLS leaked device keys");
        await Reject(() => stranger.ExecuteReducerAsync(new Reducer.RegisterQrLoginDevice("outsider", "key", "name", "test", owner), _ => true));
        await Reject(() => stranger.ExecuteReducerAsync(new Reducer.RevokeQrLoginDevice("outsider", owner), _ => true));
        await Reject(() => stranger.ExecuteReducerAsync(new Reducer.TouchQrLoginDevice("outsider"), _ => true));
        Console.WriteLine("PASS QR DeviceRlsAndTrustedApi: outsider sees no rows and all three reducers reject untrusted calls");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("QR devices: " + message); }
    private static async Task Reject(Func<Task> action)
    { try { await action(); } catch (ReducerRejectedException) { return; } throw new Exception("QR device reducer unexpectedly accepted mutation"); }
}
