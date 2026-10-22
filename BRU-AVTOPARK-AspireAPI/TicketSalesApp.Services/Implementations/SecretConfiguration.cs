using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace TicketSalesApp.Services.Implementations;

public static class SecretConfiguration
{
    public static void AddSecretFiles(ConfigurationManager configuration)
    {
        var directory = Environment.GetEnvironmentVariable("BRU_SECRETS_DIR") ?? configuration["Secrets:Directory"];
        if (string.IsNullOrWhiteSpace(directory)) directory = Directory.Exists("/run/secrets") ? "/run/secrets" : null;
        if (directory != null) configuration.AddInMemoryCollection(ReadDirectory(directory));
        // Reapply environment variables so mounted files never override them.
        configuration.AddEnvironmentVariables();
    }

    public static Dictionary<string, string?> ReadDirectory(string directory)
    {
        var root = Path.GetFullPath(directory);
        var info = new DirectoryInfo(root);
        if (info.LinkTarget != null) root = info.ResolveLinkTarget(true)!.FullName;
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root))
        {
            var entry = new FileInfo(file);
            var resolved = entry.LinkTarget == null ? entry : entry.ResolveLinkTarget(true) as FileInfo;
            if (resolved == null || !Path.GetFullPath(resolved.FullName).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException($"Secret file '{entry.Name}' resolves outside Secrets:Directory.");
            if (resolved.Length > 8192) throw new IOException($"Secret file '{entry.Name}' exceeds 8 KiB.");
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(resolved.FullName) &
                (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0)
                throw new IOException($"Secret file '{entry.Name}' must be owner-only (chmod 600).");
            using var stream = new FileStream(resolved.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bytes = new byte[8193];
            var length = stream.ReadAtLeast(bytes, bytes.Length, false);
            if (length > 8192) throw new IOException($"Secret file '{entry.Name}' exceeds 8 KiB.");
            var text = new UTF8Encoding(false, true).GetString(bytes, 0, length);
            if (text.EndsWith('\n')) text = text[..^1].TrimEndSingleCarriageReturn();
            values[entry.Name.Replace("__", ":")] = text;
        }
        return values;
    }

    private static string TrimEndSingleCarriageReturn(this string value) => value.EndsWith('\r') ? value[..^1] : value;

    public static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..8];

    // Compromised historical values are represented only by SHA-256 digests.
    private static readonly HashSet<string> Revoked = new(StringComparer.OrdinalIgnoreCase)
    {
        "68835d90e0e094b7076aa5d97667a2a473b72ee323887680f7c8383541dd8df9",
        "bc95736a7bba698568eb386eb360a50e13eb47f5098a3d93749bb0b06e2403ba",
        "28a4f03aec5fa4b6fa5c89cbe4452db7eeeda374ec5be3f2c70f8e18b7051522"
    };

    public static bool IsRevoked(string value) => Revoked.Contains(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))));

    public static void ApplyPolicy(IConfigurationBuilder builder, IConfiguration configuration, bool development, Action<string> warning)
    {
        var jwt = configuration["JwtSettings:Secret"];
        if (string.IsNullOrEmpty(jwt) && development)
        {
            jwt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            warning("JwtSettings:Secret is ephemeral in Development; tokens do not survive process restarts.");
        }
        if (string.IsNullOrEmpty(jwt) || Encoding.UTF8.GetByteCount(jwt) < 32 || jwt.Distinct().Count() < 12 || IsRevoked(jwt))
            throw new InvalidOperationException("JwtSettings:Secret is missing, weak or revoked. Generate a fresh secret with DatabaseBootstrap generate-secrets and configure a secret file or environment variable.");
        var qr = configuration["QRLogin:EncryptionKey"];
        if (string.IsNullOrEmpty(qr))
            qr = Convert.ToBase64String(HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(jwt), 32, info: Encoding.UTF8.GetBytes("bru-qr-legacy/1")));
        else if (Encoding.UTF8.GetByteCount(qr) < 32 || IsRevoked(qr))
            throw new InvalidOperationException("QRLogin:EncryptionKey is weak or revoked. Remove it to derive a key or configure a fresh secret.");
        var token = configuration["SpacetimeDB:AdminToken"];
        if (!string.IsNullOrEmpty(token) && (IsRevoked(token) || !Regex.IsMatch(token, @"\AeyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))))
        {
            warning("SpacetimeDB:AdminToken is invalid or revoked; log fetching is disabled.");
            token = "";
        }
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = jwt, ["QRLogin:EncryptionKey"] = qr, ["SpacetimeDB:AdminToken"] = token ?? ""
        });
    }
}
