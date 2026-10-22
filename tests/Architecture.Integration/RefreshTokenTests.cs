using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

internal static class RefreshTokenTests
{
    private static string Hash() => Base64UrlEncoder.Encode(SHA256.HashData(Guid.NewGuid().ToByteArray()));
    public static async Task RunAsync(ISpacetimeDBService db, Identity owner, string ownerToken)
    {
        using var http = new HttpClient(); http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        async Task Fixture(string reducer, object args)
        {
            using var content = new ByteArrayContent(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(args));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await http.PostAsync("http://127.0.0.1:3000/v1/database/bru-architecture-integration/call/" + reducer, content);
            response.EnsureSuccessStatusCode();
        }
        var actor = Identity.FromHexString(new string('c', 64));
        await Fixture("prepare_refresh_token_test", new[] { actor.ToString() });
        async Task<string> Issue(uint seconds = 3600)
        { var hash = Hash(); await db.ExecuteReducerAsync(new Reducer.IssueRefreshToken(hash, actor, "synthetic", seconds), _ => true); return hash; }
        var first = await Issue(1); var next = Hash();
        var original = await db.ReadAsync(c => c.Db.RefreshTokenRecord.TokenHash.Find(first)!);
        Check(original.ExpiresAt - original.IssuedAt == 3600000, "minimum lifetime clamp");
        await db.ExecuteReducerAsync(new Reducer.RotateRefreshToken(first, next), _ => true);
        Check(await db.ReadAsync(c => c.Db.RefreshTokenRecord.TokenHash.Find(first)!.UsedAt != null &&
            c.Db.RefreshTokenRecord.TokenHash.Find(next) is { } successor && successor.FamilyId == first && successor.ExpiresAt == original.ExpiresAt), "rotation not atomic or absolute expiry extended");
        var forbiddenSuccessor = Hash();
        await db.ExecuteReducerAsync(new Reducer.RotateRefreshToken(first, forbiddenSuccessor), _ => true);
        Check(await db.ReadAsync(c => c.Db.RefreshTokenRecord.TokenHash.Find(forbiddenSuccessor) == null &&
            c.Db.RefreshTokenRecord.UserId.Filter(actor).Where(row => row.FamilyId == first).Count() == 2 &&
            c.Db.RefreshTokenRecord.UserId.Filter(actor).Where(row => row.FamilyId == first).All(row => row.RevokedAt != null)), "reuse revocation rolled back or issued successor");
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RotateRefreshToken(next, Hash()), _ => true));
        var race = await Issue();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        { try { await db.ExecuteReducerAsync(new Reducer.RotateRefreshToken(race, Hash()), _ => true); } catch (ReducerRejectedException) { } }));
        Check(await db.ReadAsync(c => c.Db.RefreshTokenRecord.UserId.Filter(actor).Where(row => row.FamilyId == race).Count() == 2 &&
            c.Db.RefreshTokenRecord.UserId.Filter(actor).Where(row => row.FamilyId == race).All(row => row.RevokedAt != null)), "concurrent rotations issued multiple successors or failed to burn family");
        var expired = await Issue();
        await Fixture("expire_refresh_token_for_test", new[] { expired });
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RotateRefreshToken(expired, Hash()), _ => true));
        var revoke = await Issue(); await db.ExecuteReducerAsync(new Reducer.RevokeRefreshFamily(revoke), _ => true);
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RotateRefreshToken(revoke, Hash()), _ => true));
        await Issue(); await db.ExecuteReducerAsync(new Reducer.RevokeAllRefreshTokens(actor), _ => true);
        Check(await db.ReadAsync(c => c.Db.RefreshTokenRecord.UserId.Filter(actor).All(row => row.RevokedAt != null || row.ExpiresAt <= (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())), "revoke all");
        var hashes = new List<string>(); for (var i = 0; i < 11; i++) hashes.Add(await Issue(uint.MaxValue));
        Check(await db.ReadAsync(c => c.Db.RefreshTokenRecord.UserId.Filter(actor).Count(row => row.RevokedAt == null && row.UsedAt == null) == 10), "family cap");
        Check(await db.ReadAsync(c => c.Db.RefreshTokenRecord.TokenHash.Find(hashes[0]) is not { RevokedAt: null }), "oldest family survived cap eviction");
        var latest = await db.ReadAsync(c => c.Db.RefreshTokenRecord.TokenHash.Find(hashes[^1])!);
        Check(latest.ExpiresAt - latest.IssuedAt == 90UL * 86400 * 1000, "maximum lifetime clamp");
        await db.ExecuteReducerAsync(new Reducer.UpdateUser(actor, null, "synthetic-reset-password", null, null, null, null, owner), _ => true);
        Check(await db.ReadAsync(c => c.Db.RefreshTokenRecord.UserId.Filter(actor).All(row => row.RevokedAt != null)), "password reset did not revoke");
        var changed = await Issue();
        await Reject(() => db.ExecuteReducerAsync(new Reducer.ChangePassword(actor, "incorrect-password", "synthetic-new-password", actor), _ => true));
        Check(await db.ReadAsync(c => c.Db.RefreshTokenRecord.TokenHash.Find(changed) is { RevokedAt: null }), "failed password change revoked family");
        await db.ExecuteReducerAsync(new Reducer.ChangePassword(actor, "synthetic-reset-password", "synthetic-new-password", actor), _ => true);
        Check(await db.ReadAsync(c => c.Db.RefreshTokenRecord.TokenHash.Find(changed) is { RevokedAt: not null }), "own password change did not revoke");
        var inactive = await Issue(); await db.ExecuteReducerAsync(new Reducer.DeactivateUser(actor, owner), _ => true);
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RotateRefreshToken(inactive, Hash()), _ => true));
        Check(await db.ReadAsync(c => c.Db.RefreshTokenRecord.UserId.Filter(actor).All(row => row.RevokedAt != null)), "deactivation did not revoke");
        await Reject(() => db.ExecuteReducerAsync(new Reducer.IssueRefreshToken(Hash(), actor, "native", 3600), _ => true));
        Console.WriteLine("PASS refresh real reducers: issue/absolute-expiry rotation, committed reuse-family burn without successor, 20 concurrent rotations issue one successor then revoke all, expiry, family/all revocation, oldest eviction/10-family cap/lifetime clamps, password-reset/own-change/deactivation hooks, rejected change rollback and inactive rejection");
    }
    public static async Task OutsiderAsync(ISpacetimeDBService db, Identity owner)
    {
        Check(await db.ReadAsync(c => !c.Db.RefreshTokenRecord.Iter().Any()), "refresh RLS");
        await Reject(() => db.ExecuteReducerAsync(new Reducer.IssueRefreshToken(Hash(), owner, "native", 3600), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RotateRefreshToken(Hash(), Hash()), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RevokeRefreshFamily(Hash()), _ => true));
        await Reject(() => db.ExecuteReducerAsync(new Reducer.RevokeAllRefreshTokens(owner), _ => true));
        Console.WriteLine("PASS refresh RLS hides records and every refresh reducer rejects untrusted callers");
    }
    private static async Task Reject(Func<Task> action) { try { await action(); } catch (ReducerRejectedException) { return; } throw new Exception("Refresh invalid mutation committed"); }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Refresh tokens: " + message); }
}
