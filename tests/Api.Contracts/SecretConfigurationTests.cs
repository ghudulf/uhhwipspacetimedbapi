using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using TicketSalesApp.Services.Implementations;

internal static class SecretConfigurationTests
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bru-secrets-" + Guid.NewGuid().ToString("N"));
        OwnerOnlyFile.CreateDirectory(directory);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var path = Path.Combine(directory, "JwtSettings__Secret");
        void Write(string value) { File.WriteAllText(path, value); OwnerOnlyFile.Restrict(path); }
        void Reject(Action action)
        {
            try { action(); } catch (IOException) { return; } catch (InvalidOperationException) { return; }
            throw new Exception("Secret configuration accepted unsafe input.");
        }
        try
        {
            Write(secret + "\r\n");
            Directory.CreateDirectory(Path.Combine(directory, "ignored"));
            Check(SecretConfiguration.ReadDirectory(directory)["JwtSettings:Secret"] == secret, "file name mapping/newline");
            Write(secret + "\n\n");
            Check(SecretConfiguration.ReadDirectory(directory)["JwtSettings:Secret"] == secret + "\n", "only one newline stripped");
            Write(new string('x', 8193)); Reject(() => SecretConfiguration.ReadDirectory(directory));
            Write(secret);
            if (!OperatingSystem.IsWindows())
            {
                Check(File.GetUnixFileMode(directory) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), "directory permissions");
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead);
                Reject(() => SecretConfiguration.ReadDirectory(directory));
                OwnerOnlyFile.Restrict(path);
                var outside = Path.Combine(Path.GetDirectoryName(directory)!, "outside-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(outside, secret); OwnerOnlyFile.Restrict(outside);
                try
                {
                    File.CreateSymbolicLink(Path.Combine(directory, "outside"), outside);
                    Reject(() => SecretConfiguration.ReadDirectory(directory));
                }
                finally { File.Delete(Path.Combine(directory, "outside")); File.Delete(outside); }
                File.CreateSymbolicLink(Path.Combine(directory, "inside"), path);
                Check(SecretConfiguration.ReadDirectory(directory)["inside"] == secret, "internal symbolic link");
                File.Delete(Path.Combine(directory, "inside"));
            }
            var oldDir = Environment.GetEnvironmentVariable("BRU_SECRETS_DIR");
            var oldJwt = Environment.GetEnvironmentVariable("JwtSettings__Secret");
            try
            {
                Environment.SetEnvironmentVariable("BRU_SECRETS_DIR", directory);
                Environment.SetEnvironmentVariable("JwtSettings__Secret", "environment-value");
                var config = new ConfigurationManager();
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:Secret"] = "appsettings-value" });
                SecretConfiguration.AddSecretFiles(config);
                Check(config["JwtSettings:Secret"] == "environment-value", "environment precedence");
                Environment.SetEnvironmentVariable("JwtSettings__Secret", null);
                config = new ConfigurationManager(); SecretConfiguration.AddSecretFiles(config);
                Check(config["JwtSettings:Secret"] == secret, "file precedence");
            }
            finally { Environment.SetEnvironmentVariable("BRU_SECRETS_DIR", oldDir); Environment.SetEnvironmentVariable("JwtSettings__Secret", oldJwt); }

            ConfigurationManager Configuration(string? jwt, string? qr = null, string? token = null)
            {
                var config = new ConfigurationManager();
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:Secret"] = jwt, ["QRLogin:EncryptionKey"] = qr, ["SpacetimeDB:AdminToken"] = token });
                return config;
            }
            foreach (var jwt in new[] { null, "short", new string('x', 48) })
            {
                var config = Configuration(jwt); Reject(() => SecretConfiguration.ApplyPolicy(config, config, false, _ => { }));
            }
            var warnings = new List<string>();
            var dev = Configuration(null); SecretConfiguration.ApplyPolicy(dev, dev, true, warnings.Add);
            Check(warnings.Count == 1 && Encoding.UTF8.GetByteCount(dev["JwtSettings:Secret"]!) >= 32, "Development ephemeral secret");
            var a = Configuration(secret); SecretConfiguration.ApplyPolicy(a, a, false, warnings.Add);
            var b = Configuration(secret); SecretConfiguration.ApplyPolicy(b, b, false, warnings.Add);
            Check(a["QRLogin:EncryptionKey"] == b["QRLogin:EncryptionKey"], "HKDF deterministic");
            var weakQr = Configuration(secret, "short"); Reject(() => SecretConfiguration.ApplyPolicy(weakQr, weakQr, false, warnings.Add));
            var invalidToken = Configuration(secret, token: "invalid"); SecretConfiguration.ApplyPolicy(invalidToken, invalidToken, false, warnings.Add);
            Check(invalidToken["SpacetimeDB:AdminToken"] == "" && warnings.Count == 2, "invalid owner token disabled");
            // Test the exact hash-only revocation path without embedding historical secrets.
            var revoked = (HashSet<string>)typeof(SecretConfiguration).GetField("Revoked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
            revoked.Add(digest);
            try
            {
                var config = Configuration(secret); Reject(() => SecretConfiguration.ApplyPolicy(config, config, true, warnings.Add));
                var configQr = Configuration(dev["JwtSettings:Secret"], secret); Reject(() => SecretConfiguration.ApplyPolicy(configQr, configQr, false, warnings.Add));
                Check(SecretConfiguration.IsRevoked(secret), "revoked digest lookup");
            }
            finally { revoked.Remove(digest); }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS secret files: mapping, precedence, bounds, permissions, symlinks; startup policy, HKDF and hash-only revocation");
    }
    private static void Check(bool condition, string description) { if (!condition) throw new Exception(description); }
}
