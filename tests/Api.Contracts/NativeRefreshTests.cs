using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using BRU_AVTOPARK.Services.Implementations;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

internal static class NativeRefreshTests
{
    private static readonly Identity Actor = Identity.FromHexString(new string('1', 64));
    public static async Task RunAsync()
    {
        var context = new DefaultHttpContext(); context.Request.Scheme = "https";
        var store = new Store(); var logs = new Capture();
        var native = Native(store, context); var service = Service(store, native, logs);
        var ordinary = await service.CompleteQrLoginAsync(Actor, true);
        Check(ordinary.Success && ordinary.RefreshToken == null && store.Issued == 0 && !JsonSerializer.Serialize(ordinary).Contains("RefreshToken", StringComparison.Ordinal), "browser refresh issuance");
        context.Request.Headers["X-BRU-Native-Login"] = "1"; context.Request.Headers.Origin = "https://browser.test";
        Check((await service.CompleteQrLoginAsync(Actor, true)).RefreshToken == null, "Origin-bearing native opt-in accepted");
        context.Request.Headers.Remove("Origin"); context.Request.Headers["X-BRU-Device"] = "Device\n" + new string('x', 100);
        var login = await service.CompleteQrLoginAsync(Actor, true);
        Check(login.RefreshToken is { Length: 43 } && store.Root!.TokenHash == NativeRefreshTokens.Hash(login.RefreshToken) && store.Root.DeviceLabel.Length == 64 && !store.Root.DeviceLabel.Any(char.IsControl), "native opaque/hash/label finalization");
        var originalExpiry = store.Root.ExpiresAt;
        var refreshed = await service.RefreshTokenAsync(login.RefreshToken!);
        Check(refreshed.Success && refreshed.RefreshToken is { Length: 43 } && refreshed.RefreshToken != login.RefreshToken && store.Next!.ExpiresAt == originalExpiry &&
            store.Next.TokenHash == NativeRefreshTokens.Hash(refreshed.RefreshToken) && refreshed.Token != null && refreshed.ExpiresAt > DateTime.UtcNow, "real orchestration rotation/mint/absolute family expiry");
        store.RecordReads = 0;
        var replay = await service.RefreshTokenAsync(login.RefreshToken!);
        Check(!replay.Success && replay.ErrorMessage == "invalid_refresh_token" && store.Root.RevokedAt != null && store.LastSuccessor!.RevokedAt != null, "committed replay denial mapped to success");
        foreach (var variant in new[] { "missing", "different_family", "inactive", "rejected", "revoked" })
        {
            var s = new Store(); var n = Native(s, context); var auth = Service(s, n, logs);
            var issued = await auth.CompleteQrLoginAsync(Actor, true);
            s.Variant = variant;
            var denied = await auth.RefreshTokenAsync(issued.RefreshToken!);
            Check(!denied.Success && denied.ErrorMessage == "invalid_refresh_token", "uniform denial: " + variant);
        }
        Check(!(await service.RefreshTokenAsync("bad")).Success, "invalid format");
        var twoFactorStore = new Store { Totp = true }; var twoFactor = Service(twoFactorStore, Native(twoFactorStore, context), logs);
        var challenge = await twoFactor.LoginAsync("synthetic", "password");
        Check(challenge.RequiresTwoFactor && challenge.RefreshToken == null && twoFactorStore.Issued == 0, "refresh issued before second factor");
        var complete = await twoFactor.ValidateTotpAsync("temp", "123456");
        Check(complete.Success && complete.RefreshToken != null && twoFactorStore.Issued == 1, "second-factor finalization missing refresh");
        var logoutStore = new Store(); var logout = Service(logoutStore, Native(logoutStore, context), logs);
        var session = await logout.LoginAsync("synthetic", "password");
        Check((await logout.LogoutAsync(Actor, session.RefreshToken)).Success && logoutStore.Root!.RevokedAt != null, "logout did not commit family revocation");
        var clock = new Clock(); var budget = new RefreshRateBudget(clock);
        for (var i = 0; i < 30; i++) Check(budget.Allow("ip", 30), "IP budget exhausted early");
        Check(!budget.Allow("ip", 30), "IP budget missing");
        for (var i = 0; i < 10; i++) Check(budget.Allow("family", 10), "family budget exhausted early");
        Check(!budget.Allow("family", 10), "family budget missing"); clock.Advance(); Check(budget.Allow("ip", 30), "sliding expiry");
        var text = string.Join('\n', logs.Messages);
        Check(!text.Contains(login.RefreshToken!, StringComparison.Ordinal) && !text.Contains(refreshed.RefreshToken!, StringComparison.Ordinal) && !text.Contains("private reducer payload", StringComparison.Ordinal), "token/exception in capture sink");
        Console.WriteLine("PASS production native refresh orchestration: opt-in/no-Origin, opaque hashes/labels, committed rotation and replay-family denial, uniform missing/cross-family/inactive/rejected/revoked errors, access minting, second-factor-only issuance, logout, sliding budgets and token-free capture sink");
    }
    private static NativeRefreshTokens Native(Store store, HttpContext context) => new(store.Database, new HttpContextAccessor { HttpContext = context },
        new ConfigurationBuilder().Build(), new QrLoginTests.EnvironmentStub(), new RefreshRateBudget(TimeProvider.System), TimeProvider.System);
    private static AuthOrchestrationService Service(Store store, NativeRefreshTokens native, ILogger<AuthOrchestrationService> logger)
    {
        var auth = Stub.Create<IAuthenticationService>((method, _) => method.Name == "AuthenticateAsync" ? Task.FromResult<UserProfile?>(store.User) : 0);
        var token = Stub.Create<ITokenService>((method, _) => method.Name switch
        {
            "GenerateToken" => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("https://api.test", "native", [new Claim("identity", Actor.ToString())], DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(15))),
            "ExtractTokenClaims" => new Dictionary<string, object>(),
            _ => throw new Exception("Unexpected token call")
        });
        var factors = Stub.Create<ITwoFactorService>((method, _) => method.Name switch
        { "CreateTempTokenAsync" => Task.FromResult("temp"), "ValidateTempTokenAsync" => Task.FromResult<(bool, Identity?)>((true, Actor)), "MarkTokenAsUsedAsync" => Task.FromResult(true), _ => throw new Exception("Unexpected second factor") });
        var settings = Stub.Create<ISettingsService>((_, _) => Task.FromResult(new UserSettings { UserId = Actor, TotpEnabled = store.Totp }));
        var totp = Stub.Create<ITotpService>((_, _) => Task.FromResult<(bool, string?)>((true, null)));
        return new(auth, Stub.Create<IUserService>(), store.Database, token, factors, settings, totp, Stub.Create<IWebAuthnService>(),
            Stub.Create<IMagicLinkService>(), Stub.Create<IQRAuthenticationService>(), Stub.Create<IProfileService>(), Stub.Create<IOpenIdConnectService>(), logger, native);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Native refresh: " + message); }
    private sealed class Clock : TimeProvider { private DateTimeOffset now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => now; public void Advance() => now = now.AddMinutes(2); }
    private sealed class Capture : ILogger<AuthOrchestrationService>
    {
        public readonly List<string> Messages = new(); public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null; public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
    private sealed class Store
    {
        public UserProfile User = new() { UserId = Actor, Login = "synthetic", IsActive = true };
        public RefreshTokenRecord? Root, Next, LastSuccessor;
        public int Issued, RecordReads; public bool Totp; public string Variant = "";
        public ISpacetimeDBService Database { get; }
        public Store()
        {
            Database = Stub.Create<ISpacetimeDBService>((method, args) =>
            {
                if (method.Name == "ReadAsync")
                {
                    if (method.GetGenericArguments()[0] == typeof(UserProfile)) return Task.FromResult<UserProfile?>(Variant == "inactive" ? null : User);
                    if (method.GetGenericArguments()[0] == typeof(RefreshTokenRecord)) return Task.FromResult(RecordReads++ == 0 ? Root : Next);
                    throw new Exception("Unexpected refresh read");
                }
                if (method.Name == "ExecuteReducerAsync")
                {
                    switch (args![0])
                    {
                        case Reducer.IssueRefreshToken issue:
                            Issued++; Root = new() { TokenHash = issue.TokenHash, FamilyId = issue.TokenHash, UserId = issue.ActingUser, DeviceLabel = issue.DeviceLabel,
                                IssuedAt = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ExpiresAt = (ulong)DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds() }; break;
                        case Reducer.RotateRefreshToken rotate:
                            if (Variant == "rejected") return Task.FromException<bool>(new ReducerRejectedException("private reducer payload"));
                            if (Root!.UsedAt != null) { Root.RevokedAt = 1; LastSuccessor!.RevokedAt = 1; Next = null; break; }
                            Root.UsedAt = 1; Next = Variant == "missing" ? null : new() { TokenHash = rotate.NewHash, FamilyId = Variant == "different_family" ? "wrong" : Root.FamilyId,
                                UserId = Actor, ExpiresAt = Root.ExpiresAt, RevokedAt = Variant == "revoked" ? 1UL : null }; LastSuccessor = Next; break;
                        case Reducer.RevokeRefreshFamily: Root!.RevokedAt = 1; break;
                        default: throw new Exception("Unexpected refresh reducer");
                    }
                    return Task.FromResult(true);
                }
                throw new Exception("Unexpected refresh database call");
            });
        }
    }
}
