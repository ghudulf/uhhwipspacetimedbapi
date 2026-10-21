using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

internal static class WebAuthnStorageTests
{
    public static async Task RunAsync(ISpacetimeDBService db, Identity actor)
    {
        var other = await db.ReadAsync(c => c.Db.UserProfile.Iter().First(user => user.IsActive && user.UserId != actor).UserId);
        await db.ExecuteReducerAsync(new Reducer.EnsureWebAuthnUserHandle(actor), _ => true);
        var first = await db.ReadAsync(c => c.Db.WebAuthnUserHandle.UserId.Find(actor)!.Handle.ToArray());
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => db.ExecuteReducerAsync(new Reducer.EnsureWebAuthnUserHandle(actor), _ => true)));
        await db.ExecuteReducerAsync(new Reducer.EnsureWebAuthnUserHandle(other), _ => true);
        Check(first.Length == 32 && await db.ReadAsync(c => c.Db.WebAuthnUserHandle.UserId.Find(actor)!.Handle.SequenceEqual(first) &&
            !c.Db.WebAuthnUserHandle.UserId.Find(other)!.Handle.SequenceEqual(first)), "handle length/idempotency/separation");
        var id = Guid.NewGuid().ToByteArray().ToList();
        await db.ExecuteReducerAsync(new Reducer.RegisterWebAuthnCredential(other, id, "AQID", 0, "synthetic"), _ => true);
        var row = await db.ReadAsync(c => c.Db.WebAuthnCredential.Iter().Single(credential => credential.CredentialId.SequenceEqual(id)));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RegisterWebAuthnCredential(actor, id, "AQID", 0, null), _ => true));
        await db.ExecuteReducerAsync(new Reducer.UpdateWebAuthnCounter(id, 0), _ => true);
        await db.ExecuteReducerAsync(new Reducer.UpdateWebAuthnCounter(id, 1), _ => true);
        await Reject(() => db.ExecuteReducerAsync(new Reducer.UpdateWebAuthnCounter(id, 1), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.UpdateWebAuthnCounter(id, 0), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.UpdateWebAuthnCredentialCounter(row.Id, 1), _ => true));
        await db.ExecuteReducerAsync(new Reducer.UpdateWebAuthnCredentialCounter(row.Id, 2), _ => true);
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RemoveOwnWebAuthnCredential(row.Id, actor), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.DeactivateWebAuthnCredential(row.Id, actor), _ => true));
        Check(await db.ReadAsync(c => c.Db.WebAuthnCredential.Id.Find(row.Id) is { IsActive: true, Counter: 2 }), "cross-account removal changed credential");
        await db.ExecuteReducerAsync(new Reducer.RemoveOwnWebAuthnCredential(row.Id, other), _ => true);
        Check(await db.ReadAsync(c => c.Db.WebAuthnCredential.Id.Find(row.Id)?.IsActive == false), "own removal not committed");
        await Reject(() => db.ExecuteReducerAsync(new Reducer.UpdateWebAuthnCounter(id, 3), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RemoveOwnWebAuthnCredential(row.Id, other), _ => true));
        Console.WriteLine("PASS WebAuthn real reducers: random 32-byte per-user handles/concurrent idempotency, duplicate credential rejection, zero/monotonic counter policy, legacy counter parity, own-only committed removal and inactive credential rejection");
    }
    public static async Task OutsiderAsync(ISpacetimeDBService db, Identity actor)
    {
        Check(await db.ReadAsync(c => !c.Db.WebAuthnUserHandle.Iter().Any()), "RLS exposed handles");
        await Reject(() => db.ExecuteReducerAsync(new Reducer.EnsureWebAuthnUserHandle(actor), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RemoveOwnWebAuthnCredential(0, actor), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.UpdateWebAuthnCounter([1], 1), _ => true));
        Console.WriteLine("PASS WebAuthn handle RLS and untrusted ensure/removal/counter reducer rejection");
    }
    private static async Task Reject(Func<Task> action)
    { try { await action(); } catch (ReducerRejectedException) { return; } throw new Exception("WebAuthn invalid mutation committed"); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("WebAuthn storage: " + message); }
}
