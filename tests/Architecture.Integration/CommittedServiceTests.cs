using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class CommittedServiceTests
{
    public static async Task SecurityAsync(ISpacetimeDBService db, Identity owner)
    {
        var settings = new SettingsService(db, NullLogger<SettingsService>.Instance);
        Check((await settings.GetOrCreateUserSettingsAsync(owner)).UserId == owner, "settings committed creation");
        Check(await settings.EnableTotpAsync(owner) && await db.ReadAsync(c => c.Db.UserSettings.Iter().Single(s => s.UserId == owner).TotpEnabled), "settings committed enable");
        Check(await settings.DisableTotpAsync(owner) && !await db.ReadAsync(c => c.Db.UserSettings.Iter().Single(s => s.UserId == owner).TotpEnabled), "settings committed disable");
        var missing = Identity.FromHexString(new string('d', 64));
        Check(!await settings.EnableTotpAsync(missing), "settings missing failure swallowed");
        var config = new ConfigurationBuilder().Build();
        var totp = new TotpService(db, config, UrlEncoder.Default, NullLogger<TotpService>.Instance);
        const string key = "JBSWY3DPEHPK3PXP";
        var code = Code();
        Check((await totp.EnableTotpAsync(owner, code, key)).success && await totp.IsTotpEnabledAsync(owner), "TOTP enable not committed");
        Check((await totp.DisableTotpAsync(owner)).success && !await totp.IsTotpEnabledAsync(owner) &&
            await db.ReadAsync(c => c.Db.TotpSecret.Iter().Where(s => s.UserId == owner).All(s => !s.IsActive)), "TOTP disable not committed");
        Check(!(await totp.EnableTotpAsync(missing, code, key)).success, "TOTP missing failure swallowed");
        var magic = new MagicLinkService(db, new UnusedEmail(), config, NullLogger<MagicLinkService>.Instance);
        Check(!await magic.MarkMagicLinkAsUsedAsync("missing-synthetic-proof"), "magic use rejection swallowed");
        var proof = Guid.NewGuid().ToString("N");
        await db.ExecuteReducerAsync(new Reducer.CreateMagicLinkToken(owner, proof, (ulong)DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), null, null), _ => true);
        Check(await magic.MarkMagicLinkAsUsedAsync(proof) && await db.ReadAsync(c => c.Db.MagicLinkToken.Token.Find(proof)!.IsUsed), "magic use returned before commit");
        Console.WriteLine("PASS migrated security services: committed settings create/enable/disable, TOTP enable/disable readback, missing-user failures and magic-link use rejection/committed state");
    }

    public static async Task SecurityOutsiderAsync(ISpacetimeDBService db, Identity owner)
    {
        var settings = new SettingsService(db, NullLogger<SettingsService>.Instance);
        try { await settings.GetOrCreateUserSettingsAsync(owner); throw new Exception("Untrusted settings creation reported success"); }
        catch (ReducerRejectedException) { }
        var totp = new TotpService(db, new ConfigurationBuilder().Build(), UrlEncoder.Default, NullLogger<TotpService>.Instance);
        Check(!(await totp.DisableTotpAsync(owner)).success, "untrusted TOTP mutation reported success");
        Console.WriteLine("PASS migrated settings fails closed on untrusted committed rejection instead of returning 2FA-disabled defaults");
    }

    private static string Code()
    {
        // RFC 4648 decoding of the fixed synthetic key above; no production credential.
        var bytes = Convert.FromHexString("48656c6c6f21deadbeef");
        using var hmac = new HMACSHA1(bytes);
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30).Reverse().ToArray();
        var hash = hmac.ComputeHash(counter); var offset = hash[^1] & 15;
        var value = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1000000).ToString("D6");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class UnusedEmail : IEmailService
    {
        public Task SendEmailAsync(string to, string subject, string body) => throw new NotSupportedException();
    }
}
