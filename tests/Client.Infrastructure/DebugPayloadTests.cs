using System.Text;
using System.Text.Json;
using BRU.Avtopark.Contracts;

internal static class DebugPayloadTests
{
    public static void Run()
    {
        var raw = """{"name":"Alice","email":"alice@example.test","phone":"123","zero":0,"number":123456789012345678901234567890,"exponent":1.2300e+99,"ok":true,"nil":null,"items":[{"Access_Token":"proof","new-password":"pass","passwordHash":"hash"}],"token":"one","token":"two","secret":{"nested":"proof"},"unicode":"Бус😀","escaped":"a\nb"}""";
        var masked = DebugPayload.Json(raw);
        using var document = JsonDocument.Parse(masked);
        Check(masked.Contains("alice@example.test") && masked.Contains("123456789012345678901234567890") && masked.Contains("1.2300e+99"), "business/numeric fidelity");
        Check(document.RootElement.GetProperty("nil").ValueKind == JsonValueKind.Null && document.RootElement.GetProperty("zero").GetInt32() == 0 && document.RootElement.GetProperty("ok").GetBoolean(), "primitive fidelity");
        Check(!masked.Contains("proof") && !masked.Contains("pass\"") && masked.Contains("***(5)") && masked.Split("\"token\"").Length == 3, "nested/duplicate/hash credentials");
        foreach (var name in new[] { "PASSWORD", "current_password", "new-password", "passwd", "client-secret", "refresh_token", "idToken", "tempToken", "Authorization", "Cookie", "set-cookie", "flowSecret", "pollSecret", "approvalSecret", "browserToken", "verificationCode", "totp_code", "otp", "pin", "signature", "assertion", "private_key", "api-key", "customToken", "otherSecret", "previousPassword", "tokenHash" })
            Check(!DebugPayload.Json(JsonSerializer.Serialize(new Dictionary<string, object?> { [name] = "sentinel" })).Contains("sentinel"), name);
        Check(DebugPayload.Json(raw, TrafficLevel.Full) == raw && DebugPayload.Json(raw, TrafficLevel.Off) == "", "levels");
        Check(DebugPayload.Json("password=sentinel&name=Alice").Contains("password=***(8)") && !DebugPayload.Json("{\"token\":\"sentinel").Contains("sentinel"), "partial/form masking");
        Check(DebugPayload.Json("not JSON, bus 42") == "not JSON, bus 42", "non JSON retained");
        Check(!DebugPayload.Json("{\"password\":\"multi word sentinel").Contains("sentinel"), "partial quoted credential");
        Check(!DebugPayload.Json("{\"code\":\"sentinel\"}", authentication: true).Contains("sentinel"), "auth-only generic TOTP code");
        Check(DebugPayload.Binary(42) == "[binary 42 bytes]", "binary summary");
        Check(DebugPayload.Truncate("a😀b", 3) == "a…[truncated 5 bytes]" && DebugPayload.Truncate("abc", 3) == "abc", "UTF8 truncation");
        var url = DebugPayload.Url("https://host/api?name=Alice&access_token=sentinel&state=state&code=123&limit=20");
        Check(!url.Contains("sentinel") && !url.Contains("=state") && url.Contains("name=Alice") && url.Contains("limit=20"), "query masking");
        var headers = DebugPayload.Headers(new Dictionary<string, IEnumerable<string>> { ["Authorization"] = ["Bearer sentinel"], ["Cookie"] = ["sid=sentinel"], ["X-Api-Key"] = ["sentinel"], ["Content-Type"] = ["application/json"] });
        Check(headers["Authorization"] == "Bearer sha256:" + DebugPayload.Fingerprint("Bearer sentinel") && !string.Join(' ', headers.Values).Contains("sentinel") && headers["Cookie"].StartsWith("sid sha256:"), "header contract");
        Check(DebugPayload.Fingerprint("same") == DebugPayload.Fingerprint("same") && DebugPayload.Fingerprint("same").Length == 8, "stable fingerprint");
        Check(TrafficOptions.ParseLevel(null) == TrafficLevel.Redacted && TrafficOptions.ParseLevel("FULL") == TrafficLevel.Full, "configuration defaults");
        Console.WriteLine("PASS DebugPayload credential-only masking, business data, numeric fidelity, duplicate/nested/array values, UTF8 cap, malformed/text/binary, levels, URLs and stable header fingerprints");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("DebugPayload: " + message); }
}
