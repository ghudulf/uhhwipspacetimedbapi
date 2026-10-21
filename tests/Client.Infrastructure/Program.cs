using BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

PackagingPolicyTests.Run();
await QrClientTests.RunAsync();

var directory = Path.Combine(Path.GetTempPath(), "bru-client-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    await File.WriteAllTextAsync(Path.Combine(directory, "tokens.dat"), "synthetic legacy ciphertext");
    await File.WriteAllTextAsync(Path.Combine(directory, ".key"), "synthetic legacy key");
    var first = new TokenStorageService("https://one.invalid/api/", directory);
    var second = new TokenStorageService("https://two.invalid/api/", directory);
    var expires = DateTime.UtcNow.AddMinutes(10);
    await first.SaveTokensAsync(new OAuthTokenResponse { AccessToken = "synthetic-token", RefreshToken = "synthetic-refresh", ExpiresAt = expires });
    Assert(!File.Exists(Path.Combine(directory, "tokens.dat")) && !File.Exists(Path.Combine(directory, ".key")), "legacy unbound tokens retained");
    Assert(await second.GetTokensAsync() == null, "session leaked to a different server");
    var restored = await new TokenStorageService("https://one.invalid/", directory).GetTokensAsync();
    Assert(restored?.AccessToken == "synthetic-token" && restored.ExpiresAt == expires, "session not shared by same origin or lifetime extended");
    restored!.AccessToken = "caller-mutation";
    Assert((await first.GetTokensAsync())?.AccessToken == "synthetic-token", "mutable caller changed stored session");
    if (!OperatingSystem.IsWindows()) Assert(Directory.GetFiles(directory).Length == 0, "unprotected persistence on unsupported platform");
    await first.ClearTokensAsync();
    Assert(await first.GetTokensAsync() == null, "logout retained session");
    Console.WriteLine("PASS origin isolation, expiry preservation, detached sessions, legacy cleanup, logout and platform storage policy");
    Console.WriteLine(OperatingSystem.IsWindows() ? "Windows DPAPI path exercised." : "Linux memory fallback exercised; Windows DPAPI requires Windows validation.");
}
finally { Directory.Delete(directory, recursive: true); }
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

var safe = TicketSalesApp.Services.Diagnostics.SafeDiagnostics.JsonShape("""{"access_token":{"nested":"synthetic-secret"},"state":"synthetic-state","sub":"synthetic-user","expires_in":3600,"nested":{"password":"synthetic-password","valid":true},"unknown":"synthetic-value"}""");
Assert(!safe.Contains("synthetic-", StringComparison.Ordinal), "diagnostics leaked values");
using (var shape = System.Text.Json.JsonDocument.Parse(safe))
{
    Assert(shape.RootElement.GetProperty("expires_in").GetInt32() == 3600, "safe numeric metadata lost");
    Assert(shape.RootElement.GetProperty("sub").GetString()!.StartsWith("fingerprint:"), "identifier correlation missing");
    Assert(shape.RootElement.GetProperty("nested").GetProperty("valid").GetBoolean(), "safe shape lost");
}
Assert(TicketSalesApp.Services.Diagnostics.SafeDiagnostics.JsonShape("not-json secret") == "[non-JSON content omitted]", "malformed body leaked");
Assert(TicketSalesApp.Services.Diagnostics.SafeDiagnostics.JsonShape(new string('x', 65537)) == "[oversized JSON omitted]", "oversized body accepted");
Console.WriteLine("PASS diagnostic redaction, nested credential containers, keyed identifiers and bounded malformed input");

var legacy = System.Text.Json.Nodes.JsonNode.Parse("""{"$id":"1","$values":[{"$id":"2","id":7},{"$ref":"2"}]}""");
var normalized = BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization.CollectionPayload.ReadArray(legacy);
Assert(normalized.Count == 2 && normalized[1]!["id"]!.GetValue<int>() == 7, "legacy reference not resolved");
Assert(BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization.CollectionPayload.ReadArray(System.Text.Json.Nodes.JsonNode.Parse("[1,2]")).Count == 2, "plain array unsupported");
Assert(BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization.CollectionPayload.UnixMilliseconds(System.Text.Json.Nodes.JsonValue.Create("1970-01-01T01:00:00+01:00")) == 0, "timestamp offset changed instant");
try
{
    BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization.CollectionPayload.ReadArray(System.Text.Json.Nodes.JsonNode.Parse("""{"$id":"1","$values":[{"$ref":"1"}]}"""));
    throw new Exception("cyclic reference accepted");
}
catch (System.Text.Json.JsonException) { }
Console.WriteLine("PASS plain/legacy collection normalization, reference resolution/cycle rejection and explicit timestamp offsets");

Assert(TicketSalesApp.Services.Diagnostics.SafeDiagnostics.Url("https://name:password@example.invalid/callback?code=synthetic-secret#token") == "https://example.invalid/callback", "URL diagnostics retained userinfo, query or fragment");
Console.WriteLine("PASS OAuth navigation URL redaction");

if (BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization.RecordedRevenue.Average(new decimal?[] { 10m, null, 0m, 20m }) != 10m) throw new Exception("Unknown amounts diluted the recorded average");
if (BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization.RecordedRevenue.Average(new decimal?[] { null }) != null) throw new Exception("Unknown historical average must remain unknown");
Console.WriteLine("PASS recorded averages distinguish unknown amounts and genuine zero-price sales");

Assert(BRU.Avtopark.Client.ClientEndpoints.ServerRoot("https://example.invalid/fleet/API/").AbsoluteUri == "https://example.invalid/fleet/", "server-root normalization lost prefix");
Assert(BRU.Avtopark.Client.ClientEndpoints.ServerRoot("https://example.invalid/capi/").AbsolutePath == "/capi/", "API normalization matched a partial segment");
try { BRU.Avtopark.Client.ClientEndpoints.ServerRoot("https://example.invalid/api/?token=synthetic"); throw new Exception("Credential-bearing base accepted"); }
catch (ArgumentException) { }
Console.WriteLine("PASS shared endpoint normalization preserves HTTPS/proxy prefixes and rejects credential-bearing bases");
foreach (var remote in new[] { "http://192.0.2.1/api/", "http://api.example.invalid/api/" })
{
    try { BRU.Avtopark.Client.ClientEndpoints.ServerRoot(remote); throw new Exception("Remote plaintext API credentials allowed"); }
    catch (ArgumentException) { }
}
Assert(BRU.Avtopark.Client.ClientEndpoints.ServerRoot("http://127.0.0.1:5000/api/").IsLoopback, "loopback development rejected");
Console.WriteLine("PASS remote HTTP API bases rejected before credentials; HTTPS/proxy paths and loopback development retained");

Assert(BRU.Avtopark.Client.ClientEndpoints.WebSocketEndpoint(new Uri("https://example.invalid/proxy/api/"), "/api/auth/ws").AbsoluteUri == "wss://example.invalid/proxy/api/auth/ws", "WebSocket normalization changed HTTPS/prefix");
try { BRU.Avtopark.Client.ClientEndpoints.WebSocketEndpoint(new Uri("http://192.168.1.1/api/"), "/api/auth/ws"); throw new Exception("Unencrypted LAN authentication allowed"); }
catch (ArgumentException) { }
Console.WriteLine("PASS realtime endpoints preserve secure origins and reject unencrypted LAN credential exchange");

var callback = "https://example.invalid/callback";
Assert(BRU.Avtopark.Client.ClientEndpoints.IsOAuthCallback(callback + "?code=synthetic&state=synthetic", callback), "valid callback rejected");
foreach (var candidate in new[] { callback + ".evil?code=synthetic", "https://example.invalid.evil/callback", "http://example.invalid/callback", "https://user:secret@example.invalid/callback", callback + "#code=synthetic", "https://example.invalid:444/callback" })
    Assert(!BRU.Avtopark.Client.ClientEndpoints.IsOAuthCallback(candidate, callback), "callback accepted a mismatched origin/path/credential/fragment");
Console.WriteLine("PASS exact OAuth callback origin/path matching with query-only code/state");

var trustedWindows = new Uri("https://example.invalid/");
Assert(BRU.Avtopark.Client.ClientEndpoints.WindowsAuthenticationEndpoint("https://example.invalid/fleet/api/", trustedWindows, true).AbsoluteUri
    == "https://example.invalid/fleet/api/auth/windows/link", "Windows linking lost deployment prefix");
Assert(BRU.Avtopark.Client.ClientEndpoints.WindowsAuthenticationEndpoint("https://example.invalid/api/", trustedWindows, false).AbsoluteUri
    == "https://example.invalid/api/auth/windows", "Windows login endpoint changed");
foreach (var selected in new[] { "https://other.invalid/api/", "http://example.invalid/api/", "https://example.invalid:444/api/", "https://user:secret@example.invalid/api/" })
{
    try { BRU.Avtopark.Client.ClientEndpoints.WindowsAuthenticationEndpoint(selected, trustedWindows, true); throw new Exception("Untrusted Windows credential destination accepted"); }
    catch (ArgumentException) { }
}
Console.WriteLine("PASS native Windows endpoints preserve proxy prefixes and enforce explicit trusted origin");

var oversized = new System.Text.Json.Nodes.JsonArray();
for (var i = 0; i < 100_001; i++) oversized.Add(i);
try { BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization.CollectionPayload.ReadArray(oversized); throw new Exception("oversized indexing accepted"); }
catch (System.Text.Json.JsonException) { }
System.Text.Json.Nodes.JsonNode deep = new System.Text.Json.Nodes.JsonArray();
for (var i = 0; i < 66; i++) deep = new System.Text.Json.Nodes.JsonArray(deep);
try { BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Serialization.CollectionPayload.ReadArray(deep); throw new Exception("deep indexing accepted"); }
catch (System.Text.Json.JsonException) { }
Console.WriteLine("PASS collection indexing enforces node and depth budgets before reference expansion");
