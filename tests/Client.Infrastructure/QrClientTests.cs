using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;

internal static class QrClientTests
{
    public static async Task RunAsync()
    {
        foreach (var origin in new[] { "https://EXAMPLE.test/base", "https://example.test:443/base/" })
            Check(QrOrigin.IsSameServer(origin, "https://example.test/base/api/"), "origin normalization");
        foreach (var origin in new[] { "http://example.test/base", "https://example.test.evil/base", "https://user@example.test/base", "https://example.test/base?x=1", "https://example.test/base#x", "https://example.test/BASE", "https://example.test:444/base", "invalid" })
            Check(!QrOrigin.IsSameServer(origin, "https://example.test/base/api"), "unsafe origin accepted");
        Check(QrOrigin.IsSameServer("http://localhost:80/", "http://LOCALHOST/api"), "default HTTP port");
        var key = QrDeviceKey.Generate();
        try
        {
            var signature = QrDeviceKey.Sign(key.Pkcs8PrivateKey, "session", "secret", "https://api.test/base", "ALICE", key.DeviceId, "000123");
            using var verifier = ECDsa.Create(); verifier.ImportSubjectPublicKeyInfo(key.Spki, out _);
            var canonical = Encoding.UTF8.GetBytes("bru-qr/1\nsession\nsecret\nhttps://api.test/base\nalice\n" + key.DeviceId + "\n000123");
            Check(QrLoginProtocol.TryDecode(signature, 64, out var bytes) && verifier.VerifyData(canonical, bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), "canonical signature vector");
            foreach (var field in Enumerable.Range(0, 7))
            {
                var fields = Encoding.UTF8.GetString(canonical).Split('\n'); fields[field] += "x";
                Check(!verifier.VerifyData(Encoding.UTF8.GetBytes(string.Join('\n', fields)), bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), "tampered canonical field accepted");
            }
        }
        finally { CryptographicOperations.ZeroMemory(key.Pkcs8PrivateKey); }
        var id = QrLoginProtocol.Base64Url(new byte[16]);
        var seen = new List<(string Path, string? Bearer, string Body)>();
        using var http = new HttpClient(new Handler(async request =>
        {
            seen.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter, request.Content == null ? "" : await request.Content.ReadAsStringAsync()));
            if (request.RequestUri.AbsolutePath.EndsWith("/sessions", StringComparison.Ordinal)) return Json(new QrCreateResponse(id, "poll", "approval", "payload", "png", "123456", DateTimeOffset.UtcNow.AddMinutes(1), 1000));
            if (request.RequestUri.AbsolutePath.EndsWith("/status", StringComparison.Ordinal)) return Json(new QrPoll("completed", JsonSerializer.SerializeToElement(new { requiresTwoFactor = true, tempToken = "synthetic-temp" })));
            if (request.RequestUri.AbsolutePath.EndsWith("/peek", StringComparison.Ordinal)) return Json(new QrPeekResponse("Desktop", "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), "192.0.2.x", "https://example.test/base", null, 3));
            if (request.Method == HttpMethod.Get) return Json(Array.Empty<QrDeviceInfo>());
            if (request.RequestUri.AbsolutePath.EndsWith("/devices", StringComparison.Ordinal)) return Json(new QrDeviceResponse("device"));
            return new(HttpStatusCode.NoContent);
        }));
        var client = new QrLoginClient(http, "https://example.test/base/api/");
        await client.CreateSessionAsync("Desktop", "test", "alice", "device");
        await client.StatusAsync(id, "poll-secret"); await client.CancelAsync(id, "poll-secret"); await client.PeekAsync(id, "approval-secret");
        await client.ApproveAsync(id, "approval-secret", "123456", "bearer-secret"); await client.DenyAsync(id, "approval-secret");
        await client.CompleteAsync(id, "approval-secret", "123456", "device", "signature-secret");
        await client.EnrollDeviceAsync("bearer-secret", "Phone", "test", "spki"); await client.ListDevicesAsync("bearer-secret"); await client.RevokeDeviceAsync("bearer-secret", "device");
        Check(seen.Select(x => x.Path).SequenceEqual(new[] { "sessions", $"sessions/{id}/status", $"sessions/{id}/cancel", $"sessions/{id}/peek", $"sessions/{id}/approve", $"sessions/{id}/deny", $"sessions/{id}/complete", "devices", "devices", "devices/device" }.Select(p => "/base/api/auth/qr/" + p)), "exact routes");
        for (var i = 0; i < seen.Count; i++) Check((seen[i].Bearer != null) == (i is 4 or 7 or 8 or 9), "bearer leakage");
        Check(seen[0].Body == "{\"deviceName\":\"Desktop\",\"platform\":\"test\",\"username\":\"alice\",\"deviceId\":\"device\"}" && seen[1].Body == "{\"pollSecret\":\"poll-secret\"}" && seen[4].Body == "{\"approvalSecret\":\"approval-secret\",\"verificationCode\":\"123456\"}", "request contracts");
        foreach (var (status, content, expected) in new[] { (302, "secret", "redirect_rejected"), (200, new string('x', 65537), "invalid_response"), (400, "{\"code\":\"verification_failed\",\"attemptsRemaining\":2}", "verification_failed"), (401, "{\"code\":\"secret-token-123456\"}", "invalid_response"), (200, "secret", "invalid_response") })
        {
            using var failing = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(content) })));
            try { await new QrLoginClient(failing, "https://example.test/api").StatusAsync(id, "poll-secret"); throw new Exception("failure accepted"); }
            catch (QrLoginException ex) { Check(ex.Code == expected && !ex.Message.Contains("secret") && (expected != "verification_failed" || ex.AttemptsRemaining == 2), "safe error mapping"); }
        }
        try { _ = new QrLoginClient(http, "http://example.test/api"); throw new Exception("insecure endpoint accepted"); } catch (QrLoginException ex) { Check(ex.Code == "invalid_server", "endpoint error"); }
        using var network = new HttpClient(new Handler(_ => throw new HttpRequestException("poll-secret")));
        try { await new QrLoginClient(network, "http://localhost/api").StatusAsync(id, "poll-secret"); throw new Exception("network failure accepted"); }
        catch (QrLoginException ex) { Check(ex.Message == "network_error", "network exception leaked input"); }
        Console.WriteLine("PASS QR origin matrix, canonical P-256/tamper vector, exact HTTP routes/bodies/bearer isolation, bounded/redirect/network/error responses");
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, ApiProtocol.Json)) };
    private static void Check(bool value, string message) { if (!value) throw new Exception("QR client: " + message); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request); }
}
