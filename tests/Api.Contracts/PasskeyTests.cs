using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Authentication;
using Fido2NetLib;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

internal static class PasskeyTests
{
    internal const string Origin = "https://api.example.test";
    internal const string Rp = "api.example.test";
    internal static readonly Identity Actor = Identity.FromHexString(new string('1', 64));
    public static async Task RunAsync()
    {
        ValidateConfiguration();
        foreach (var tamper in new[] { "none", "challenge", "origin", "rp", "uv" })
        {
            var store = new Store(); using var authenticator = new SoftwareAuthenticator(); var service = Service(store);
            var options = await service.CreateRegistrationOptionsAsync(Actor);
            Check(options.AuthenticatorSelection.UserVerification == Fido2NetLib.Objects.UserVerificationRequirement.Required && options.Attestation == Fido2NetLib.Objects.AttestationConveyancePreference.None && options.User.Id.SequenceEqual(store.Handle), "registration options/handle");
            var response = authenticator.Register(options, tamper == "origin" ? "https://evil.test" : Origin, tamper == "rp" ? "evil.test" : null, tamper != "uv", tamper == "challenge" ? new byte[32] : null);
            if (tamper == "none")
            {
                await service.VerifyRegistrationAsync(Actor, options, response);
                Check(store.Credentials.Count == 1 && store.Writes.Contains("RegisterWebAuthnCredential"), "registration did not commit");
            }
            else { await Reject(() => service.VerifyRegistrationAsync(Actor, options, response)); Check(store.Credentials.Count == 0, "bad registration persisted"); }
        }
        var registered = new Store(); var logs = new Capture(); var verifier = Service(registered, logs); using var key = new SoftwareAuthenticator();
        var create = await verifier.CreateRegistrationOptionsAsync(Actor); await verifier.VerifyRegistrationAsync(Actor, create, key.Register(create, Origin));
        foreach (var tamper in new[] { "challenge", "origin", "rp", "uv", "handle", "unknown", "inactive" })
        {
            var options = await verifier.CreateAssertionOptionsAsync(null);
            var response = key.Assert(options, tamper == "origin" ? "https://evil.test" : Origin, tamper == "rp" ? "evil.test" : Rp,
                uv: tamper != "uv", challenge: tamper == "challenge" ? new byte[32] : null, handle: tamper == "handle" ? new byte[32] : null);
            registered.Unknown = tamper == "unknown"; registered.Active = tamper != "inactive"; registered.OwnsHandle = tamper != "handle";
            await Reject(() => verifier.VerifyAssertionAsync(options, response));
            registered.Unknown = false; registered.Active = true; registered.OwnsHandle = true;
        }
        for (var i = 0; i < 2; i++)
        {
            var options = await verifier.CreateAssertionOptionsAsync(null);
            Check((await verifier.VerifyAssertionAsync(options, key.Assert(options, Origin, Rp, counter: 0))).UserId == Actor, "synced zero counter rejected");
        }
        var assertion = await verifier.CreateAssertionOptionsAsync(null);
        using (var imposter = new SoftwareAuthenticator())
        {
            key.Id.CopyTo(imposter.Id, 0); imposter.Handle = key.Handle;
            await Reject(() => verifier.VerifyAssertionAsync(assertion, imposter.Assert(assertion, Origin, Rp)));
        }
        await verifier.VerifyAssertionAsync(assertion, key.Assert(assertion, Origin, Rp));
        var replay = await verifier.CreateAssertionOptionsAsync(null); await Reject(() => verifier.VerifyAssertionAsync(replay, key.Assert(replay, Origin, Rp)));
        await verifier.RemoveCredentialAsync(Actor, Convert.ToBase64String(key.Id));
        Check(!registered.Credentials.Single().IsActive && registered.Writes.Last() == "RemoveOwnWebAuthnCredential", "management removal not committed");
        var empty = new Store(); var noCredentials = Service(empty);
        var discoverable = await noCredentials.CreateAssertionOptionsAsync(null); var hinted = await noCredentials.CreateAssertionOptionsAsync("unknown");
        Check(!discoverable.AllowCredentials.Any() && !hinted.AllowCredentials.Any() && discoverable.UserVerification == hinted.UserVerification, "unknown/no-credential options enumerate");
        // Legacy endpoints exercise actual stored Base64 challenges with the same real verifier.
        var legacy = new Store(); var legacyService = Service(legacy); using var legacyKey = new SoftwareAuthenticator();
        var legacyOptions = await legacyService.GetCredentialCreateOptionsAsync(Actor, "private-user");
        Check((await legacyService.CompleteRegistrationAsync(Actor, "private-user", legacyKey.Register(legacyOptions.options!, Origin))).success, "legacy Base64 registration challenge reconstruction");
        var legacyAssertion = await legacyService.GetAssertionOptionsAsync("private-user");
        Check((await legacyService.CompleteAssertionAsync("private-user", legacyKey.Assert(legacyAssertion.options!, Origin, Rp))).success, "legacy Base64 assertion reconstruction");
        foreach (var secret in new[] { "private-user", Actor.ToString(), Convert.ToBase64String(key.Id), Convert.ToBase64String(registered.Handle), Convert.ToBase64String(assertion.Challenge), registered.Credentials.Single().PublicKey })
            Check(!string.Join('\n', logs.Messages).Contains(secret, StringComparison.Ordinal), "sensitive verifier logging");
        Console.WriteLine("PASS passkey software ES256/CBOR authenticator with real Fido2: none registration, challenge/origin/RP/UV rejection, signed login, user-handle/unknown/inactive wrapper checks, zero counters, replay/clone rejection, committed removal, nonenumerating options and legacy Base64 challenge parity");
        await ControllerAsync();
        await PasskeyHttpPipelineTests.RunAsync();
    }
    internal static WebAuthnService Service(Store store, ILogger<WebAuthnService>? logger = null) => new(store.Database, new Fido2(new Fido2Configuration { ServerDomain = Rp, ServerName = "Tests", Origins = new HashSet<string> { Origin } }), logger ?? NullLogger<WebAuthnService>.Instance);
    private static void ValidateConfiguration()
    {
        var good = new Dictionary<string, string?> { ["Fido2:ServerDomain"] = Rp, ["Fido2:Origins:0"] = Origin };
        var validator = new PasskeyOptionsValidator(new ConfigurationBuilder().AddInMemoryCollection(good).Build(), new QrLoginTests.EnvironmentStub());
        Check(validator.Validate(null, new() { Enabled = true, PublicOrigin = Origin }).Succeeded, "valid startup configuration");
        foreach (var origin in new[] { "", "http://api.example.test", "https://other.test", Origin + "/path", "https://user@api.example.test", Origin + "?secret=1" })
            Check(validator.Validate(null, new() { Enabled = true, PublicOrigin = origin }).Failed, "invalid startup origin accepted");
        Check(new PasskeyOptionsValidator(new ConfigurationBuilder().Build(), new QrLoginTests.EnvironmentStub()).Validate(null, new() { Enabled = true, PublicOrigin = Origin }).Failed, "missing RP/origin accepted");
        var development = new PasskeyOptionsValidator(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Fido2:ServerDomain"] = "localhost", ["Fido2:Origins:0"] = "http://localhost:5000" }).Build(), new QrLoginTests.EnvironmentStub { EnvironmentName = "Development" });
        Check(development.Validate(null, new() { Enabled = true, PublicOrigin = "http://localhost:5000" }).Succeeded, "Development loopback exemption");
    }
    private static async Task ControllerAsync()
    {
        var store = new Store(); var webauthn = Service(store); var broker = new ProviderFlowBroker(TimeProvider.System, Options.Create(new ProviderFlowOptions()));
        var config = new PasskeyOptions { Enabled = true, PublicOrigin = Origin };
        var tokens = Stub.Create<IAuthWebSocketTokenValidator>((_, _) => Task.FromResult<Dictionary<string, object>?>(new() { ["identity"] = Actor.ToString() }));
        var primaryPolicies = new List<bool>();
        var auth = Stub.Create<IAuthOrchestrationService>((method, args) =>
        { Check(method.Name == "CompleteQrLoginAsync" && (Identity)args![0]! == Actor, "primary policy routing"); primaryPolicies.Add((bool)args![1]!); return Task.FromResult(LoginResult.RequiresTwoFactorAuth("temp", true, true)); });
        var c = new PasskeyAuthenticationController(Options.Create(config), broker, webauthn, tokens, new(store.Database), store.Database, auth, new QrLoginTests.EnvironmentStub())
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        c.Request.Scheme = "https"; c.Request.Host = new(Rp);
        Check(QrLoginTests.Code(await c.Start(new("register"))) == 401, "registration without bearer"); c.Request.Headers.Authorization = "Bearer session";
        using var authenticator = new SoftwareAuthenticator();
        async Task<(PasskeyStartResponse Flow, string Browser)> Start(string purpose)
        {
            var flow = (PasskeyStartResponse)((JsonResult)await c.Start(new(purpose))).Value!;
            var b = QueryHelpers.ParseQuery(new Uri(flow.BrowserUrl).Query)["b"].ToString();
            Check(b != flow.FlowSecret, "browser/redemption secrets not split");
            return (flow, b);
        }
        var registration = await Start("register");
        var page = (ContentResult)await c.Browser(registration.Flow.FlowId, registration.Browser);
        Check(page.Content!.Contains("history.replaceState", StringComparison.Ordinal) && c.Response.Headers.ContentSecurityPolicy.ToString().Contains("script-src 'nonce-", StringComparison.Ordinal) && !page.Content.Contains("onclick", StringComparison.OrdinalIgnoreCase), "browser page safety");
        Check(QrLoginTests.Code(await c.Browser(registration.Flow.FlowId, registration.Browser)) == 404, "landing browser token replay");
        Check(QrLoginTests.Code(await c.Complete(registration.Flow.FlowId, new(registration.Browser))) == 404, "browser token redeemed flow");
        var registrationOptions = JsonSerializer.SerializeToElement(((JsonResult)await c.Options(registration.Flow.FlowId, new(registration.Browser))).Value, ApiProtocol.Json);
        Check(registrationOptions.GetProperty("publicKey").GetProperty("authenticatorSelection").GetProperty("residentKey").GetString() == "preferred", "residentKey preferred adapter");
        var create = CredentialCreateOptions.FromJson(registrationOptions.GetProperty("publicKey").GetRawText());
        var registered = await c.Assert(registration.Flow.FlowId, new(registration.Browser, JsonSerializer.SerializeToElement(authenticator.Register(create, Origin))));
        Check(QrLoginTests.Code(registered) == 200 && Json(await c.Complete(registration.Flow.FlowId, new(registration.Flow.FlowSecret))).Contains("\"registered\":true", StringComparison.Ordinal), "browser register/complete");
        Check(QrLoginTests.Code(await c.Assert(registration.Flow.FlowId, new(registration.Browser, default))) == 404, "assertion replay");
        var login = await Start("login");
        var loginOptions = JsonSerializer.SerializeToElement(((JsonResult)await c.Options(login.Flow.FlowId, new(login.Browser))).Value, ApiProtocol.Json);
        var assertionOptions = AssertionOptions.FromJson(loginOptions.GetProperty("publicKey").GetRawText());
        Check(QrLoginTests.Code(await c.Assert(login.Flow.FlowId, new(login.Browser, JsonSerializer.SerializeToElement(authenticator.Assert(assertionOptions, Origin, Rp))))) == 200, "browser login verification");
        Check(Json(await c.Complete(login.Flow.FlowId, new(login.Flow.FlowSecret))).Contains("\"requiresTwoFactor\":true", StringComparison.Ordinal) && primaryPolicies.SequenceEqual([false]), "UV bypassed second factor");
        Check(Json(await c.Complete(login.Flow.FlowId, new(login.Flow.FlowSecret))) == "{\"state\":\"consumed\"}", "flow replay");
        var failures = await Start("login");
        for (var i = 0; i < 3; i++) Check(QrLoginTests.Code(await c.Options(failures.Flow.FlowId, new("wrong"))) == 404, "bad browser token");
        Check(Json(await c.Complete(failures.Flow.FlowId, new(failures.Flow.FlowSecret))) == "{\"state\":\"failed\"}", "three-failure burn");
        var reused = await Start("login"); await c.Options(reused.Flow.FlowId, new(reused.Browser));
        Check(QrLoginTests.Code(await c.Options(reused.Flow.FlowId, new(reused.Browser))) == 404, "options browser token reuse");
        config.Enabled = false;
        Check(QrLoginTests.Code(await c.Start(new("login"))) == 404 && QrLoginTests.Code(await c.Browser("id", "secret")) == 404, "disabled routes");
        Console.WriteLine("PASS passkey controller with real software authenticator: split secrets, browser/options/assert/register/login, residentKey adapter, 2FA primary policy, flow/browser-token replay, three-failure burn and disabled boundaries");
    }
    private static string Json(IActionResult result) => JsonSerializer.Serialize(((JsonResult)result).Value, ApiProtocol.Json);
    private static async Task Reject(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("Passkey invalid proof accepted"); }
    private sealed class Capture : ILogger<WebAuthnService>
    {
        public readonly List<string> Messages = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
    internal static void Check(bool condition, string message) { if (!condition) throw new Exception("Passkey: " + message); }
    internal sealed class Store
    {
        public readonly byte[] Handle = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        public readonly List<WebAuthnCredential> Credentials = new();
        public readonly List<string> Writes = new();
        public bool Active = true, Unknown, OwnsHandle = true;
        private WebAuthnChallenge? challenge;
        public ISpacetimeDBService Database { get; }
        public Store()
        {
            Database = Stub.Create<ISpacetimeDBService>((method, args) =>
            {
                if (method.Name == "ReadAsync")
                {
                    var type = method.GetGenericArguments()[0];
                    if (type == typeof(UserProfile)) return Task.FromResult<UserProfile?>(new() { UserId = Actor, Login = "private-user", IsActive = Active });
                    if (type == typeof(byte[])) return Task.FromResult(Handle.ToArray());
                    if (type == typeof(List<WebAuthnCredential>)) return Task.FromResult(Credentials.Where(c => c.IsActive).ToList());
                    if (type == typeof(WebAuthnCredential)) return Task.FromResult(Unknown ? null : Credentials.FirstOrDefault(c => c.IsActive));
                    if (type == typeof(WebAuthnChallenge)) return Task.FromResult(challenge);
                    if (type == typeof(bool)) return Task.FromResult(OwnsHandle && Credentials.Any(c => c.IsActive));
                    if (type == typeof(UserAccessSnapshot)) return Task.FromResult(new UserAccessSnapshot(Active, false, [], []));
                    throw new Exception("Unexpected WebAuthn read type: " + type.Name);
                }
                if (method.Name == "ExecuteReducerAsync")
                {
                    var reducer = args![0]!; Writes.Add(reducer.GetType().Name);
                    switch (reducer)
                    {
                        case Reducer.RegisterWebAuthnCredential add:
                            Credentials.Add(new() { Id = 1, UserId = add.UserId, CredentialId = add.CredentialId, PublicKey = add.PublicKey, Counter = add.Counter, IsActive = true }); break;
                        case Reducer.UpdateWebAuthnCounter update:
                            var credential = Credentials.Single(c => c.CredentialId.SequenceEqual(update.CredentialId));
                            if ((credential.Counter != 0 || update.Counter != 0) && update.Counter <= credential.Counter) return Task.FromException<bool>(new ReducerRejectedException("counter replay"));
                            credential.Counter = update.Counter; break;
                        case Reducer.RemoveOwnWebAuthnCredential remove: Credentials.Single(c => c.Id == remove.Id && c.UserId == remove.ActingUser).IsActive = false; break;
                        case Reducer.StoreWebAuthnChallenge store: challenge = new() { Id = 1, UserId = store.UserId, Challenge = store.Challenge, ExpiresAt = store.ExpiresAt }; break;
                        case Reducer.DeleteWebAuthnChallenge: challenge = null; break;
                        case Reducer.EnsureWebAuthnUserHandle or Reducer.EnableWebAuthn: break;
                        default: throw new Exception("Unexpected WebAuthn reducer");
                    }
                    return Task.FromResult(true);
                }
                throw new Exception("WebAuthn bypassed committed service path: " + method.Name);
            });
        }
    }
}
