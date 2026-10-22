using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Models.Responses;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.AdminServer.Experimental.Services.Interfaces;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

internal static class QrLoginTests
{
    private static readonly Identity UserId = Identity.FromHexString(new string('1', 64));
    private const string Origin = "https://api.example.test/base";
    public static async Task RunAsync()
    {
        PayloadRejectionAndSigningVector();
        await BrokerStateMachineAndExactlyOnceAsync();
        BrokerExpiryAttemptsAndLimits();
        await ControllerProofAndPolicyAsync();
        await QrHttpPipelineTests.RunAsync();
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception("QR: " + message); }
    private static void PayloadRejectionAndSigningVector()
    {
        var id = QrLoginProtocol.Base64Url(new byte[16]);
        var secret = QrLoginProtocol.Base64Url(new byte[32]);
        var text = QrLoginProtocol.BuildPayload(id, secret, Origin);
        Assert(QrLoginProtocol.TryParsePayload(text, out var parsed) && parsed == new QrPayload(id, secret, Origin), "payload round trip");
        foreach (var invalid in new[] { text.Replace("bruqr:", "https:"), text.Replace("//v1/", "//v2/"), text.Replace("/session?", "/other?"), text + "&extra=1", text + "&s=" + id,
            text[..text.IndexOf("&o=", StringComparison.Ordinal)], text.Replace(id, "A"), text.Replace(secret, new string('+', 43)), text + "#fragment", text.Replace("v1/", "v1:99/"), new string('a', 2049) })
            Assert(!QrLoginProtocol.TryParsePayload(invalid, out _), "invalid payload accepted");
        foreach (var origin in new[] { "https://user:pass@host", "https://host/?query=1", "https://host/#fragment", "file:///host", "ftp://host", "relative", "https://host/" + new string('x', 512) })
            Assert(!QrLoginProtocol.TryParsePayload($"bruqr://v1/session?s={id}&k={secret}&o={Uri.EscapeDataString(origin)}", out _), "invalid origin accepted");
        var expected = "bru-qr/1\nsession\nsecret\nhttps://api.test/base\nalice\ndevice\n000123";
        Assert(Encoding.UTF8.GetString(QrLoginProtocol.SigningMessage("session", "secret", "https://api.test/base", "ALICE", "device", "000123")) == expected, "canonical signing vector");
        Assert(QrLoginProtocol.MaskIp("192.0.2.4") == "192.0.2.x" && QrLoginProtocol.MaskIp("2001:db8::1") == "2001:db8:0:x", "masked addresses");
        try { JsonSerializer.Deserialize<QrPollRequest>("{\"pollSecret\":\"x\",\"extra\":1}", ApiProtocol.Json); throw new Exception("unknown request member accepted"); }
        catch (JsonException) { }
        Console.WriteLine("PASS QR PayloadRejectionAndSigningVector: strict parser matrix, canonical vector, IP masking, unknown JSON members");
    }
    private static QrLoginBroker Broker(Clock? clock = null, int max = 2000, int perAddress = 5) => new(clock ?? new Clock(), Options.Create(new QrLoginOptions { MaxSessions = max, MaxSessionsPerAddress = perAddress }));
    private static QrCreateResponse Create(QrLoginBroker broker, string ip = "192.0.2.1", string? username = null, bool decoy = false) => broker.Create(new(" Desktop\n", " test\t", username), ip, Origin, decoy);
    private static void Reject(Action action, string code)
    { try { action(); throw new Exception("QR: expected rejection " + code); } catch (QrLoginException ex) { Assert(ex.Code == code, "wrong failure: " + ex.Code); } }
    private static async Task BrokerStateMachineAndExactlyOnceAsync()
    {
        var broker = Broker();
        var created = Create(broker);
        Assert(broker.Status(created.SessionId, created.PollSecret, out _) == "pending", "initial state");
        var peek = broker.Peek(created.SessionId, created.ApprovalSecret);
        Assert(peek.DeviceName == "Desktop" && peek.Platform == "test" && peek.RequestedFrom == "192.0.2.x", "peek sanitization");
        Assert(broker.Status(created.SessionId, created.PollSecret, out _) == "scanned", "scanned state");
        Reject(() => broker.Status(created.SessionId, "wrong", out _), "not_found");
        broker.Approve(created.SessionId, created.ApprovalSecret, created.VerificationCode, UserId, "alice");
        var completions = 0;
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            var state = broker.Status(created.SessionId, created.PollSecret, out var redemption);
            if (state == "completed") { Assert(redemption?.ApprovedByAuthenticatedSession == true, "Flow A policy"); Interlocked.Increment(ref completions); }
        })));
        Assert(completions == 1 && broker.Status(created.SessionId, created.PollSecret, out _) == "consumed", "exactly once");
        Reject(() => broker.Approve(created.SessionId, created.ApprovalSecret, created.VerificationCode, UserId, "alice"), "invalid_state");
        var bound = Create(broker, username: "ALICE");
        Reject(() => broker.Approve(bound.SessionId, bound.ApprovalSecret, bound.VerificationCode, UserId, "bob"), "wrong_account");
        Assert(broker.Complete(bound.SessionId, bound.ApprovalSecret, bound.VerificationCode, "device", UserId, true), "device complete");
        Assert(broker.Status(bound.SessionId, bound.PollSecret, out var proof) == "completed" && proof?.ApprovedByAuthenticatedSession == false, "Flow B policy");
        var decoy = Create(broker, username: "missing", decoy: true);
        Assert(decoy.GetType() == bound.GetType() && decoy.VerificationCode.Length == bound.VerificationCode.Length && decoy.QrPayload.Length == bound.QrPayload.Length, "decoy response shape");
        Assert(!broker.Complete(decoy.SessionId, decoy.ApprovalSecret, decoy.VerificationCode, "device", UserId, true), "decoy completed");
        Reject(() => broker.Approve(decoy.SessionId, decoy.ApprovalSecret, decoy.VerificationCode, UserId, "missing"), "qr_login_failed");
        var raw = (IDictionary)typeof(QrLoginBroker).GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(broker)!;
        foreach (var session in raw.Values)
            foreach (var field in session!.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                Assert(field.GetValue(session) is not string value || value != created.PollSecret && value != created.ApprovalSecret, "raw secret retained");
        Console.WriteLine("PASS QR BrokerStateMachineAndExactlyOnceAsync: both flows, 50 concurrent redeems, hashes only, decoys, bound-account mismatch");
    }
    private static void BrokerExpiryAttemptsAndLimits()
    {
        var clock = new Clock(); var broker = Broker(clock);
        var expired = Create(broker); clock.Advance(121);
        Assert(broker.Status(expired.SessionId, expired.PollSecret, out _) == "expired", "TTL");
        var burned = Create(broker);
        for (var i = 0; i < 3; i++) Reject(() => broker.Approve(burned.SessionId, burned.ApprovalSecret, "wrong", UserId, "alice"), "verification_failed");
        Assert(broker.Status(burned.SessionId, burned.PollSecret, out _) == "denied", "attempts exhausted");
        var failedProof = Create(broker, username: "alice");
        for (var i = 0; i < 3; i++) Assert(!broker.Complete(failedProof.SessionId, "wrong", "wrong", "device", UserId, true), "bad proof accepted");
        Assert(broker.Status(failedProof.SessionId, failedProof.PollSecret, out _) == "denied", "proof attempts exhausted");
        var cancel = Create(broker); broker.Cancel(cancel.SessionId, cancel.PollSecret); broker.Cancel(cancel.SessionId, cancel.PollSecret);
        Assert(broker.Status(cancel.SessionId, cancel.PollSecret, out _) == "cancelled", "cancel");
        var deny = Create(broker); broker.Deny(deny.SessionId, deny.ApprovalSecret);
        Assert(broker.Status(deny.SessionId, deny.PollSecret, out _) == "denied", "deny");
        var capacity = Broker(max: 1); Create(capacity); Reject(() => Create(capacity, "192.0.2.2"), "capacity");
        var address = Broker(perAddress: 1); Create(address); Reject(() => Create(address), "address_limit");
        var rate = Broker();
        for (var i = 0; i < 10; i++) { var s = Create(rate); rate.Cancel(s.SessionId, s.PollSecret); }
        Reject(() => Create(rate), "rate_limited");
        var usernameRate = Broker();
        for (var i = 0; i < 5; i++) Create(usernameRate, $"192.0.2.{i}", "alice");
        Reject(() => Create(usernameRate, "192.0.2.9", "ALICE"), "rate_limited");
        var polls = Broker(); var poll = Create(polls);
        for (var i = 0; i < 90; i++) polls.Status(poll.SessionId, poll.PollSecret, out _);
        Reject(() => polls.Status(poll.SessionId, poll.PollSecret, out _), "rate_limited");
        Assert(new QrLoginOptions { SessionSeconds = 1 }.SessionSeconds == 30 && new QrLoginOptions { SessionSeconds = 999 }.SessionSeconds == 300, "TTL clamping");
        Console.WriteLine("PASS QR BrokerExpiryAttemptsAndLimits: fake clock TTL, cancel/deny, attempts, capacity/address/username/create/poll limits");
    }
    private static async Task ControllerProofAndPolicyAsync()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = key.ExportSubjectPublicKeyInfo(); var deviceId = QrLoginProtocol.DeviceIdFor(spki);
        var user = new UserProfile { UserId = UserId, Login = "alice", IsActive = true };
        var device = new QrLoginDevice { DeviceId = deviceId, UserId = UserId, PublicKey = Convert.ToBase64String(spki) };
        bool enabled = true, tokenValid = true, active = true, enrolled = true;
        var count = 0; var touched = 0; var policies = new List<bool>();
        var database = Stub.Create<ISpacetimeDBService>((method, _) => method.GetGenericArguments()[0] == typeof(UserAccessSnapshot)
            ? Task.FromResult(new UserAccessSnapshot(active, false, [], [])) : Task.FromResult<UserProfile?>(active ? user : null));
        var store = Stub.Create<IQrLoginDeviceStore>((method, _) => method.Name switch
        {
            "FindUserAsync" => Task.FromResult<UserProfile?>(active ? user : null),
            "FindAsync" => Task.FromResult<QrLoginDevice?>(enrolled && active ? device : null),
            "TouchAsync" => Touch(),
            "ListAsync" => Task.FromResult<IReadOnlyList<QrLoginDevice>>(count == 0 ? [] : Enumerable.Repeat(device, count).ToArray()),
            "RegisterAsync" => Task.CompletedTask,
            _ => throw new Exception("Unexpected QR store call")
        });
        Task Touch() { touched++; return Task.CompletedTask; }
        var tokens = Stub.Create<IAuthWebSocketTokenValidator>((_, _) => Task.FromResult<Dictionary<string, object>?>(tokenValid ? new() { ["identity"] = UserId.ToString() } : null));
        var auth = Stub.Create<IAuthOrchestrationService>((method, args) =>
        { Assert(method.Name == "CompleteQrLoginAsync", "wrong orchestration method"); policies.Add((bool)args![1]!); return Task.FromResult(LoginResult.RequiresTwoFactorAuth("synthetic-temp", true, false)); });
        var flags = Stub.Create<IFeatureFlagService>((_, _) => Task.FromResult(enabled));
        var broker = Broker();
        QrLoginController Controller() => new(broker, store, tokens, new UserAccessService(database), database, auth, flags, new EnvironmentStub())
            { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var controller = Controller(); controller.Request.Scheme = "https"; controller.Request.Host = new("api.example.test"); controller.Request.Headers.Authorization = "Bearer synthetic";
        await NetworkBoundaryTests.CheckAsync(controller, "BoundaryAsync", "QRLoginGenerateEndpointEnabled");
        enabled = false; Assert(Code(await controller.Create(new("desktop", "test"))) == 503, "flag gate"); enabled = true;
        controller.Request.Scheme = "http"; Assert(Code(await controller.Create(new("desktop", "test"))) == 400, "HTTPS gate"); controller.Request.Scheme = "https";
        var created = Create(broker);
        controller.Request.Headers.Authorization = ""; Assert(Code(await controller.Approve(created.SessionId, new(created.ApprovalSecret, created.VerificationCode))) == 401, "missing bearer");
        controller.Request.Headers.Authorization = "Bearer synthetic"; tokenValid = false;
        Assert(Code(await controller.Approve(created.SessionId, new(created.ApprovalSecret, created.VerificationCode))) == 401, "invalid bearer"); tokenValid = true; active = false;
        Assert(Code(await controller.Approve(created.SessionId, new(created.ApprovalSecret, created.VerificationCode))) == 401, "inactive bearer"); active = true;
        var wrong = Create(broker, username: "bob"); Assert(Code(await controller.Approve(wrong.SessionId, new(wrong.ApprovalSecret, wrong.VerificationCode))) == 403, "wrong account");
        Assert(Code(await controller.Enroll(new("phone", "test", "bad"))) == 400, "bad enrollment key");
        using (var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384)) Assert(Code(await controller.Enroll(new("phone", "test", Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo())))) == 400, "wrong curve");
        Assert(Code(await controller.Enroll(new("phone", "test", Convert.ToBase64String(spki)))) == 201, "enrollment");
        count = 1; Assert(Code(await controller.Enroll(new("phone", "test", Convert.ToBase64String(spki)))) == 409, "duplicate");
        using (var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        { count = 5; Assert(Code(await controller.Enroll(new("phone", "test", Convert.ToBase64String(otherKey.ExportSubjectPublicKeyInfo())))) == 409, "device limit"); }
        await controller.Approve(created.SessionId, new(created.ApprovalSecret, created.VerificationCode));
        var status = (JsonResult)await controller.Status(created.SessionId, new(created.PollSecret));
        Assert(((QrStatusResponse)status.Value!).Login is LoginResult { RequiresTwoFactor: true } && policies.SequenceEqual([true]), "Flow A redemption policy");
        // Fresh broker per proof variant keeps creation limits independent of the cryptographic matrix.
        foreach (var tamper in new[] { "none", "origin", "username", "deviceId", "code", "secret", "revoked", "inactive" })
        {
            var b = Broker(); var session = Create(b, username: "alice");
            var c = new QrLoginController(b, store, tokens, new UserAccessService(database), database, auth, flags, new EnvironmentStub()) { ControllerContext = controller.ControllerContext };
            var code = tamper == "code" ? "999999" : session.VerificationCode;
            var secret = tamper == "secret" ? QrLoginProtocol.Base64Url(new byte[32]) : session.ApprovalSecret;
            var signed = QrLoginProtocol.SigningMessage(session.SessionId, session.ApprovalSecret, tamper == "origin" ? "https://evil.test" : Origin,
                tamper == "username" ? "bob" : "alice", tamper == "deviceId" ? "wrong" : deviceId, session.VerificationCode);
            var signature = QrLoginProtocol.Base64Url(key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            enrolled = tamper != "revoked"; active = tamper != "inactive";
            var result = await c.Complete(session.SessionId, new(secret, code, deviceId, signature));
            if (tamper == "none")
            {
                Assert(Code(result) == 204 && touched == 1, "real device signature rejected");
                var response = (QrStatusResponse)((JsonResult)await c.Status(session.SessionId, new(session.PollSecret))).Value!;
                Assert(response.Login is LoginResult { RequiresTwoFactor: true } && policies.Last() == false, "Flow B 2FA policy");
            }
            else Assert(Code(result) == 401 && JsonSerializer.Serialize(((ObjectResult)result).Value, ApiProtocol.Json) == "{\"code\":\"qr_login_failed\"}", "non-uniform proof error: " + tamper);
            enrolled = true; active = true;
        }
        var burnedSession = Create(broker, "192.0.2.9");
        broker.Approve(burnedSession.SessionId, burnedSession.ApprovalSecret, burnedSession.VerificationCode, UserId, "alice");
        var failingAuth = Stub.Create<IAuthOrchestrationService>((_, _) => Task.FromException<LoginResult>(new InvalidOperationException("synthetic")));
        var failing = new QrLoginController(broker, store, tokens, new UserAccessService(database), database, failingAuth, flags, new EnvironmentStub()) { ControllerContext = controller.ControllerContext };
        Assert(Code(await failing.Status(burnedSession.SessionId, new(burnedSession.PollSecret))) == 503 && broker.Status(burnedSession.SessionId, burnedSession.PollSecret, out _) == "consumed", "issuance failure did not burn session");
        Console.WriteLine("PASS QR ControllerProofAndPolicyAsync: flags/HTTPS/bearers, enrollment, real P-256 proof, canonical-field tampering, revoked/inactive, Flow A/B policy and fail-closed issuance");
    }
    internal static int Code(IActionResult result) => result switch { ObjectResult r => r.StatusCode ?? 200, JsonResult r => r.StatusCode ?? 200, StatusCodeResult r => r.StatusCode, _ => 0 };
    internal sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "QR tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
}
