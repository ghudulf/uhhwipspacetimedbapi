using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

internal static class EntraAuthenticationTests
{
    internal const string Tenant = "11111111-1111-1111-1111-111111111111";
    internal const string ClientId = "22222222-2222-2222-2222-222222222222";
    internal const string Subject = "33333333-3333-3333-3333-333333333333";
    internal const string Issuer = "https://login.microsoftonline.com/" + Tenant + "/v2.0";
    private static readonly Identity Actor = Identity.FromHexString(new string('1', 64));
    private static void Check(bool value, string message) { if (!value) throw new Exception("Entra: " + message); }
    internal static EntraOptions OptionsValue() => new() { Enabled = true, TenantId = Tenant, ClientId = ClientId };
    public static async Task RunAsync()
    {
        var validator = new EntraOptionsValidator(new QrLoginTests.EnvironmentStub());
        foreach (var tenant in new[] { "common", "organizations", "consumers", "not-a-tenant" })
            Check(validator.Validate(null, new() { Enabled = true, TenantId = tenant, ClientId = ClientId }).Failed, "unrestricted tenant accepted");
        Check(validator.Validate(null, new() { Enabled = true, TenantId = "common", ClientId = ClientId, AllowMultiTenant = true, AllowedTenants = [Tenant] }).Succeeded, "explicit tenant set rejected");
        foreach (var redirect in new[] { "http://api.test/callback", "https://user@api.test/callback", "https://api.test/callback?x=1", "https://api.test/callback#x" })
            Check(validator.Validate(null, new() { Enabled = true, TenantId = Tenant, ClientId = ClientId, RedirectUri = redirect }).Failed, "unsafe redirect accepted");
        Check(validator.Validate(null, new() { Enabled = true, TenantId = Tenant, ClientId = ClientId, Authority = "http://idp.test" }).Failed, "HTTP authority accepted");
        using var idp = new FakeIdp();
        foreach (var variant in new[] { "happy", "rotation", "unknown_kid", "signature", "aud", "issuer", "tenant", "nonce", "expired", "future", "oid", "tid", "none", "exchange", "redirect", "oversized", "error" })
        {
            idp.Variant = "happy"; idp.PublishRotatedKey = false;
            var broker = Broker(); var service = Service(broker, idp);
            var flow = await service.StartAsync("login", null, "ip", "https://api.test/api/auth/entra/callback", default);
            var query = QueryHelpers.ParseQuery(new Uri(flow.AuthorizationUrl).Query);
            Check(query["scope"] == "openid profile email" && query["code_challenge_method"] == "S256" && query["prompt"] == "select_account" && query["response_mode"] == "query", "authorization parameters");
            idp.Nonce = query["nonce"]!; idp.Challenge = query["code_challenge"]!; idp.Variant = variant;
            Check(!await service.CallbackAsync("synthetic-code", "unknown", null, default), "unknown state accepted");
            var state = query["state"].ToString();
            var result = await service.CallbackAsync("synthetic-code", state, variant == "error" ? "untrusted-text" : null, default);
            var success = variant is "happy" or "rotation";
            Check(result == success, "token verification matrix: " + variant);
            Check(!await service.CallbackAsync("synthetic-code", state, null, default), "state replay accepted");
            var status = broker.Complete(flow.FlowId, "entra", flow.FlowSecret, out var proof);
            Check(status == (success ? "completed" : "failed"), "terminal status: " + variant);
            if (proof != null) Check(proof.ExternalIdentity == new ProviderIdentity(Issuer, Subject), "email/sub substituted for oid identity");
        }
        var clock = new Clock(); var expiredBroker = Broker(clock); var expiredService = Service(expiredBroker, idp);
        var expired = await expiredService.StartAsync("login", null, "ip", "https://api.test/callback", default);
        clock.Advance(301);
        Check(!await expiredService.CallbackAsync("code", QueryHelpers.ParseQuery(new Uri(expired.AuthorizationUrl).Query)["state"], null, default), "expired state accepted");
        Console.WriteLine("PASS Entra fake IdP/real JWT: discovery/JWKS, code+S256 PKCE, oid/tid identity, unknown/replayed/expired state, signature/aud/issuer/tenant/nonce/lifetime/claims/alg rejection, exchange/error/redirect/size failures and configuration validation");
        await ControllerAsync(idp);
        await EntraHttpPipelineTests.RunAsync();
    }
    private static ProviderFlowBroker Broker(TimeProvider? clock = null) => new(clock ?? TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new ProviderFlowOptions()));
    private static EntraAuthenticationService Service(ProviderFlowBroker broker, FakeIdp idp, EntraOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? OptionsValue()), broker, new Clients(idp));
    private static async Task ControllerAsync(FakeIdp idp)
    {
        var broker = Broker(); var options = OptionsValue(); var service = Service(broker, idp);
        var active = true; var rejectLink = false; var linked = true; var failAuth = false; var linkCalls = 0;
        var db = Stub.Create<ISpacetimeDBService>((method, args) =>
        {
            if (method.Name == "ReadAsync") return method.GetGenericArguments()[0] == typeof(UserAccessSnapshot)
                ? Task.FromResult(new UserAccessSnapshot(active, false, [], [])) : Task.FromResult(linked);
            if (method.Name == "ExecuteReducerAsync")
            {
                if (args![0] is Reducer.LinkOwnExternalAccount link)
                {
                    Check(link.ActingUser == Actor && link.Provider == "entra" && link.Issuer == Issuer && link.Subject == Subject, "unbound reducer arguments");
                    linkCalls++;
                    if (rejectLink) return Task.FromException<bool>(new ReducerRejectedException("synthetic"));
                }
                else Check(args[0] is Reducer.UnlinkOwnExternalAccount { Provider: "entra" } unlink && unlink.ActingUser == Actor, "own unlink arguments");
                return Task.FromResult(true);
            }
            throw new Exception("Unexpected database call");
        });
        var tokens = Stub.Create<IAuthWebSocketTokenValidator>((_, _) => Task.FromResult<Dictionary<string, object>?>(new() { ["identity"] = Actor.ToString() }));
        var auth = Stub.Create<IAuthOrchestrationService>((method, args) =>
        {
            Check(method.Name == "LoginExternalAsync" && (string)args![0]! == "entra" && (string)args[1]! == Issuer && (string)args[2]! == Subject, "wrong login policy/identity");
            if (failAuth) return Task.FromException<LoginResult>(new InvalidOperationException("do-not-log"));
            return Task.FromResult(linked ? LoginResult.RequiresTwoFactorAuth("challenge", true, false) : LoginResult.Failed("sensitive-account-hint"));
        });
        EntraAuthenticationController Controller() => new(Microsoft.Extensions.Options.Options.Create(options), broker, service, tokens, new(db), db, auth, new QrLoginTests.EnvironmentStub())
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var c = Controller(); c.Request.Scheme = "https"; c.Request.Host = new("api.test");
        options.Enabled = false;
        foreach (var result in new[] { await c.Start(new("login")), await c.Callback(null, null, null), await c.Complete("id", new(null)), await c.Cancel("id", new(null)), await c.LinkStatus(), await c.Unlink() })
            Check(QrLoginTests.Code(result) == 404, "disabled route exposed");
        options.Enabled = true;
        c.Request.Scheme = "http"; Check(QrLoginTests.Code(await c.Start(new("login"))) == 400, "HTTPS gate"); c.Request.Scheme = "https";
        Check(QrLoginTests.Code(await c.Start(new("link"))) == 401, "link without bearer");
        c.Request.Headers.Authorization = "Bearer session";
        active = false; Check(QrLoginTests.Code(await c.Start(new("link"))) == 401, "inactive actor at start"); active = true;
        async Task<EntraStartResponse> Authenticated(string purpose)
        {
            var start = (EntraStartResponse)((JsonResult)await c.Start(new(purpose))).Value!;
            var query = QueryHelpers.ParseQuery(new Uri(start.AuthorizationUrl).Query);
            idp.Nonce = query["nonce"]!; idp.Challenge = query["code_challenge"]!; idp.Variant = "happy";
            var callback = (ContentResult)await c.Callback("code", query["state"], null);
            Check(callback.Content!.Contains("Вход выполнен", StringComparison.Ordinal) && !callback.Content.Contains("code", StringComparison.Ordinal), "static browser completion");
            Check(c.Response.Headers.ContentSecurityPolicy.ToString().Contains("default-src 'none'", StringComparison.Ordinal) && c.Response.Headers["Referrer-Policy"] == "no-referrer" && c.Response.Headers.CacheControl == "no-store", "callback headers");
            return start;
        }
        var loginFlow = await Authenticated("login");
        Check(QrLoginTests.Code(await c.Complete(loginFlow.FlowId, new(null))) == 404 && QrLoginTests.Code(await c.Complete(loginFlow.FlowId, new("wrong"))) == 404, "wrong/missing secret");
        var login = Json(await c.Complete(loginFlow.FlowId, new(loginFlow.FlowSecret)));
        Check(login.Contains("\"requiresTwoFactor\":true", StringComparison.Ordinal), "2FA passthrough");
        linked = false; var unlinked = await Authenticated("login");
        Check(Json(await c.Complete(unlinked.FlowId, new(unlinked.FlowSecret))) == "{\"state\":\"failed\",\"code\":\"not_linked\"}", "account hint exposed"); linked = true;
        var linkFlow = await Authenticated("link");
        Check(Json(await c.Complete(linkFlow.FlowId, new(linkFlow.FlowSecret))) == "{\"state\":\"completed\",\"linked\":true}" && linkCalls == 1, "link commitment");
        var inactive = await Authenticated("link"); active = false;
        Check(Json(await c.Complete(inactive.FlowId, new(inactive.FlowSecret))).Contains("unauthorized", StringComparison.Ordinal) && linkCalls == 1, "actor not revalidated"); active = true;
        var duplicate = await Authenticated("link"); rejectLink = true;
        Check(Json(await c.Complete(duplicate.FlowId, new(duplicate.FlowSecret))).Contains("already_linked", StringComparison.Ordinal), "duplicate not generic");
        var failed = await Authenticated("login"); failAuth = true;
        Check(QrLoginTests.Code(await c.Complete(failed.FlowId, new(failed.FlowSecret))) == 503 && broker.Complete(failed.FlowId, "entra", failed.FlowSecret, out _) == "consumed", "issuance failure not burned");
        Check(Json(await c.LinkStatus()) == "{\"state\":\"linked\"}" && QrLoginTests.Code(await c.Unlink()) == 200, "own management");
        var bad = (ContentResult)await c.Callback("malicious-code", "unknown", "<script>evil</script>");
        var replay = (ContentResult)await c.Callback(null, "unknown", null);
        Check(bad.Content == replay.Content && !bad.Content!.Contains("script", StringComparison.Ordinal), "failure reflection/oracle");
        Console.WriteLine("PASS Entra controller: disabled/HTTPS/full-bearer boundaries, real browser callback, no-store/CSP, secret-bound login+2FA/link, inactive actor recheck, duplicate/unlinked generic errors, own status/unlink and burned issuance failure");
    }
    private static string Json(IActionResult result) => JsonSerializer.Serialize(((JsonResult)result).Value, ApiProtocol.Json);
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
    internal sealed class Clients(FakeIdp handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { Check(name == "entra", "unnamed client"); return new(handler, false); }
    }
    internal sealed class FakeIdp : HttpMessageHandler
    {
        private readonly RSA key = RSA.Create(2048);
        private readonly RSA rotatedKey = RSA.Create(2048);
        public bool PublishRotatedKey;
        public string Nonce = "", Challenge = "", Variant = "happy";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check(request.RequestUri!.Scheme == "https", "HTTP IdP request");
            if (request.RequestUri.AbsolutePath.EndsWith("openid-configuration", StringComparison.Ordinal))
                return Response(JsonSerializer.Serialize(new { issuer = Issuer, authorization_endpoint = Issuer + "/authorize", token_endpoint = Issuer + "/token", jwks_uri = Issuer + "/keys" }));
            if (request.RequestUri.AbsolutePath.EndsWith("/keys", StringComparison.Ordinal))
            {
                var parameters = (PublishRotatedKey ? rotatedKey : key).ExportParameters(false);
                return Response(JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", use = "sig", kid = PublishRotatedKey ? "rotated" : "test", alg = "RS256", n = Base64UrlEncoder.Encode(parameters.Modulus), e = Base64UrlEncoder.Encode(parameters.Exponent) } } }));
            }
            Check(request.RequestUri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal), "unexpected endpoint");
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            Check(form["grant_type"] == "authorization_code" && form["client_id"] == ClientId &&
                Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!))) == Challenge, "code exchange PKCE");
            if (Variant == "exchange") return new(HttpStatusCode.BadRequest);
            if (Variant == "redirect") return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.test") } };
            if (Variant == "oversized") return Response(new string('x', 65537));
            var claims = new List<Claim> { new("nonce", Variant == "nonce" ? "wrong" : Nonce), new("sub", "not-the-identity"), new("email", "synthetic@example.test") };
            if (Variant != "oid") claims.Add(new("oid", Subject));
            if (Variant != "tid") claims.Add(new("tid", Variant == "tenant" ? ClientId : Tenant));
            using var other = RSA.Create(2048);
            if (Variant == "rotation") PublishRotatedKey = true;
            var securityKey = new RsaSecurityKey(Variant is "rotation" or "unknown_kid" ? rotatedKey : Variant == "signature" ? other : key) { KeyId = Variant is "rotation" or "unknown_kid" ? "rotated" : "test" };
            var token = new JwtSecurityToken(Variant == "issuer" ? "https://evil.test" : Issuer, Variant == "aud" ? "wrong" : ClientId, claims,
                DateTime.UtcNow.AddMinutes(Variant == "expired" ? -30 : Variant == "future" ? 10 : -1), DateTime.UtcNow.AddMinutes(Variant == "expired" ? -10 : 30),
                Variant == "none" ? null : new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256));
            return Response(JsonSerializer.Serialize(new { id_token = new JwtSecurityTokenHandler().WriteToken(token) }));
        }
        private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        protected override void Dispose(bool disposing) { if (disposing) { key.Dispose(); rotatedKey.Dispose(); } base.Dispose(disposing); }
    }
}
