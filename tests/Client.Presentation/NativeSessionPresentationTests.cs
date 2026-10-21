using System.Net;
using System.Text;
using System.Text.Json;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.ViewModels;
using Serilog;
using Serilog.Core;
using Serilog.Events;

internal static class NativeSessionPresentationTests
{
    private static readonly string OldProof = QrLoginProtocol.Base64Url(Enumerable.Repeat((byte)31, 32).ToArray());
    private static readonly string NewProof = QrLoginProtocol.Base64Url(Enumerable.Repeat((byte)32, 32).ToArray());
    public static async Task RunAsync()
    {
        var api = ApiClientService.Instance; var previousToken = api.AuthToken;
        var storage = new TokenStorageService(api.CurrentBaseUrl!);
        var previousStored = await storage.GetTokensAsync();
        var sink = new Sink(); var previousLogger = Log.Logger;
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger(); Log.Logger = logger;
        try
        {
            foreach (var provider in new[] { "Windows", "QR", "Microsoft", "Passkey" })
            {
                var vm = new AuthViewModel();
                await vm.ApplyExternalLoginAsync(JsonSerializer.Serialize(new { token = "old-access-sentinel", refreshToken = OldProof }), description: provider);
                Check(vm.IsAuthenticated && (await storage.GetTokensAsync()) is { SessionKind: "native", RefreshToken: not null } stored && stored.RefreshToken == OldProof, "provider refresh storage: " + provider);
            }
            var dispatches = 0;
            using var http = new HttpClient(new Handler(async request =>
            {
                dispatches++;
                Check(request.Headers.GetValues("X-BRU-Native-Login").Single() == "1" && !request.Headers.Contains("Origin") &&
                    (await request.Content!.ReadAsStringAsync()).Contains(OldProof, StringComparison.Ordinal), "native refresh request");
                return Response("new-access-sentinel", NewProof);
            }));
            var coordinator = new SessionRefreshCoordinator(new NativeSessionRefresher(http), TimeProvider.System);
            var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => coordinator.RefreshAsync(api.CurrentBaseUrl!, "old-access-sentinel")));
            Check(dispatches == 1 && results.All(token => token == "new-access-sentinel") && api.AuthToken == "new-access-sentinel" &&
                (await storage.GetTokensAsync())?.RefreshToken == NewProof, "native single-flight/store/token synchronization");
            await NativeSessionRefresher.StoreLoginAsync("old-access-sentinel", OldProof);
            using var failedHttp = new HttpClient(new Handler(_ => throw new HttpRequestException("private-refresh-proof:" + OldProof)));
            var failed = new SessionRefreshCoordinator(new NativeSessionRefresher(failedHttp), TimeProvider.System); var expiredEvents = 0;
            failed.SessionExpired += () => expiredEvents++;
            await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => failed.RefreshAsync(api.CurrentBaseUrl!, "old-access-sentinel")));
            Check(expiredEvents == 1 && await storage.GetTokensAsync() == null, "uncertain refresh proof retained or repeated SessionExpired");
            await NativeSessionRefresher.StoreLoginAsync("old-access-sentinel", OldProof);
            var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var racingHttp = new HttpClient(new Handler(_ => { entered.TrySetResult(); return pending.Task; }));
            var racing = new NativeSessionRefresher(racingHttp).RefreshAsync(api.CurrentBaseUrl!, "old-access-sentinel");
            await entered.Task;
            await NativeSessionRefresher.StoreLoginAsync("other-account-access", NewProof);
            pending.SetResult(Response("must-not-apply", OldProof));
            Check(await racing == null && api.AuthToken == "other-account-access" && (await storage.GetTokensAsync())?.AccessToken == "other-account-access", "late refresh overwrote another account");
            Check(!await storage.ReplaceIfAccessMatchesAsync("wrong", new OAuthTokenResponse { AccessToken = "must-not-apply" }), "conditional storage overwrite");
            await PasswordAndTotpAsync(storage);
            var messages = string.Join('\n', sink.Messages);
            foreach (var secret in new[] { OldProof, NewProof, "old-access-sentinel", "new-access-sentinel", "private-refresh-proof" })
                Check(!messages.Contains(secret, StringComparison.Ordinal), "credential in Serilog sink");
        }
        finally
        {
            Log.Logger = previousLogger;
            if (previousStored == null) await storage.ClearTokensAsync(); else await storage.SaveTokensAsync(previousStored);
            api.AuthToken = previousToken;
        }
        Console.WriteLine("PASS native Avalonia/session storage: all provider results persist refresh tokens, 50 expiries dispatch once, rotated access/proof synchronized, uncertain proof cleared, SessionExpired once, stale-account response rejected, atomic storage compare, password/TOTP envelope persistence and token-free Serilog capture");
    }
    private static async Task PasswordAndTotpAsync(TokenStorageService storage)
    {
        var access = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            "https://api.test", "native", [new System.Security.Claims.Claim("role", "0")], DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(15)));
        using var http = new HttpClient(new Handler(request =>
        {
            Check(request.Headers.GetValues("X-BRU-Native-Login").Single() == "1", "password/TOTP native opt-in");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            { success = true, data = new { token = access, refreshToken = NewProof, user = new { id = 1, username = "synthetic", role = 0 } } }), Encoding.UTF8, "application/json") });
        })) { BaseAddress = new Uri(ApiClientService.Instance.CurrentBaseUrl!) };
        var password = new AuthViewModel(http) { Username = "synthetic", Password = "synthetic-password" };
        await password.TriggerLoginAndAdvanceCommand.ExecuteAsync(null);
        Check(password.IsAuthenticated && (await storage.GetTokensAsync())?.RefreshToken == NewProof, "password token pair not stored");
        var totp = new AuthViewModel(http);
        await totp.ApplyExternalLoginAsync("{\"requiresTwoFactor\":true,\"twoFactorType\":\"totp\",\"tempToken\":\"temp\"}");
        totp.TotpCode = "123456";
        await totp.VerifyTotpCommand.ExecuteAsync(null);
        Check(totp.IsAuthenticated && (await storage.GetTokensAsync())?.RefreshToken == NewProof, "TOTP wrapped response token pair not stored");
    }
    private static HttpResponseMessage Response(string access, string proof) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
    { success = true, data = new { token = access, refreshToken = proof, expiresAt = DateTime.UtcNow.AddMinutes(15) } }), Encoding.UTF8, "application/json") };
    private static void Check(bool value, string message) { if (!value) throw new Exception("Native session UI: " + message); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }
    private sealed class Sink : ILogEventSink { public readonly List<string> Messages = new(); public void Emit(LogEvent entry) => Messages.Add(entry.RenderMessage()); }
}
