using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

/// <summary>Origin-bound sessions. Windows persists using the signed-in user's DPAPI key;
/// other platforms keep sessions in memory until a platform vault is configured.</summary>
public sealed class TokenStorageService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _memoryOrigin;
    private static byte[]? _memorySession;
    private readonly string _origin;
    private readonly string _directory;
    private readonly string _path;
    private readonly byte[] _entropy;

    public TokenStorageService(string serverUrl, string? storageDirectory = null)
    {
        var server = new Uri(serverUrl, UriKind.Absolute);
        if (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("A configured HTTP(S) server is required.", nameof(serverUrl));
        if (!string.IsNullOrEmpty(server.UserInfo)) throw new ArgumentException("Credentials in server URLs are not supported.", nameof(serverUrl));
        _origin = server.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes("bru-session-v2:" + _origin));
        _directory = storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BRU.Avtopark.TicketSalesApp");
        _path = Path.Combine(_directory, "session-" + Convert.ToHexString(_entropy) + ".dpapi");
    }

    public async Task SaveTokensAsync(OAuthTokenResponse tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        await Gate.WaitAsync();
        try
        {
            if (tokens.ExpiresAt == default) tokens.ExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
            ReplaceMemory(JsonSerializer.SerializeToUtf8Bytes(tokens));
            DeleteLegacyFiles();
            if (!OperatingSystem.IsWindows())
            {
                Log.Information("Session stored in memory; secure platform persistence unavailable");
                return;
            }
            var temporaryPath = _path + ".tmp";
            try
            {
                Directory.CreateDirectory(_directory);
                var protectedBytes = ProtectedData.Protect(_memorySession!, _entropy, DataProtectionScope.CurrentUser);
                await File.WriteAllBytesAsync(temporaryPath, protectedBytes);
                File.Move(temporaryPath, _path, overwrite: true);
                Log.Information("Session persisted with platform protection; expires {ExpiresAt}", tokens.ExpiresAt);
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            {
                File.Delete(_path); // Surface failure if an old persisted session cannot be removed.
                Log.Warning("Secure session persistence unavailable; using memory only ({Failure})", ex.GetType().Name);
            }
            finally { DeleteFile(temporaryPath); }
        }
        finally { Gate.Release(); }
    }

    public async Task<OAuthTokenResponse?> GetTokensAsync()
    {
        await Gate.WaitAsync();
        try
        {
            DeleteLegacyFiles(); // Legacy ciphertext and adjacent key had no trusted server binding.
            if (_memoryOrigin == _origin && _memorySession != null)
                return JsonSerializer.Deserialize<OAuthTokenResponse>(_memorySession);
            if (!OperatingSystem.IsWindows() || !File.Exists(_path)) return null;
            byte[]? plaintext = null;
            try
            {
                var file = new FileInfo(_path);
                if (file.Length > 128 * 1024) throw new CryptographicException("Invalid session file size.");
                plaintext = ProtectedData.Unprotect(await File.ReadAllBytesAsync(_path), _entropy, DataProtectionScope.CurrentUser);
                var tokens = JsonSerializer.Deserialize<OAuthTokenResponse>(plaintext);
                if (tokens != null) ReplaceMemory((byte[])plaintext.Clone());
                return tokens;
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Warning("Stored session could not be restored; sign-in required ({Failure})", ex.GetType().Name);
                DeleteFile(_path);
                return null;
            }
            finally { if (plaintext != null) CryptographicOperations.ZeroMemory(plaintext); }
        }
        finally { Gate.Release(); }
    }

    public async Task ClearTokensAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_memoryOrigin == _origin) ReplaceMemory(null);
            // Surface a failed logout deletion to the caller: otherwise restart could restore it.
            if (File.Exists(_path)) File.Delete(_path);
            DeleteLegacyFiles();
            Log.Information("Stored session cleared");
        }
        finally { Gate.Release(); }
    }

    private void ReplaceMemory(byte[]? session)
    {
        if (_memorySession != null) CryptographicOperations.ZeroMemory(_memorySession);
        _memoryOrigin = _origin;
        _memorySession = session;
    }

    private void DeleteLegacyFiles()
    {
        DeleteFile(Path.Combine(_directory, "tokens.dat"));
        DeleteFile(Path.Combine(_directory, ".key"));
    }

    private static void DeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Warning("Session file cleanup failed ({Failure})", ex.GetType().Name); }
    }
}
