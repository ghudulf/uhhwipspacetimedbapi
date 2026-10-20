using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TicketSalesApp.Services.Implementations;

/// <summary>Stable API connection credentials, scoped to one server and database. Never log contents.</summary>
public static class DatabaseCredentialFile
{
    public sealed record Credential(string Host, string Database, string Identity, string Token);
    public static string DefaultPath(string host, string database)
    {
        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeHost(host) + "\n" + database)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BRU", "SpacetimeDB", scope + ".json");
    }
    public static string NormalizeHost(string host)
    {
        var uri = new Uri(host, UriKind.Absolute);
        if (uri.Scheme is not ("http" or "https" or "ws" or "wss") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Database host must be an HTTP(S) or WS(S) URL without credentials, query or fragment.");
        var builder = new UriBuilder(uri) { Scheme = uri.Scheme is "ws" or "http" ? "http" : "https" };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }
    public static Credential? Read(string path, string host, string database)
    {
        if (!File.Exists(path)) return null;
        RejectLink(path);
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
            throw new IOException("Database credential file must be private to the service user (chmod 600).");
        var value = JsonSerializer.Deserialize<Credential>(File.ReadAllText(path)) ?? throw new IOException("Invalid database credential file.");
        if (value.Host != NormalizeHost(host) || value.Database != database || string.IsNullOrWhiteSpace(value.Token) || string.IsNullOrWhiteSpace(value.Identity))
            throw new IOException("Database credential file does not match the configured host and database.");
        return value;
    }
    public static void Save(string path, string host, string database, string identity, string token)
    {
        path = Path.GetFullPath(path);
        var previous = Read(path, host, database);
        if (previous != null)
        {
            if (previous.Identity != identity) throw new IOException("Refusing to replace another API identity. Stop the other process and select its credential file.");
            return; // Existing valid identity token remains stable across restart.
        }
        var directory = Path.GetDirectoryName(path)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporary = Path.Combine(directory, ".credential-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(file, new Credential(NormalizeHost(host), database, identity, token));
                file.Flush(true);
            }
            File.Move(temporary, path, false); // Atomic and never overwrite a concurrent first connection.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget != null) throw new IOException("Database credential file must not be a symbolic link.");
    }
}
