using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BRU.Avtopark.Client;
using BRU.Avtopark.Contracts;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;

public interface IDeviceKeyStore
{
    Task SaveAsync(QrEnrollment enrollment, CancellationToken cancellationToken = default);
    Task<QrEnrollment?> LoadAsync(string origin, string username, CancellationToken cancellationToken = default);
    Task DeleteAsync(string origin, string username, CancellationToken cancellationToken = default);
}
public static class DeviceKeyStore
{
    public static IDeviceKeyStore Current { get; set; } = new DefaultDeviceKeyStore();
}
public sealed class DefaultDeviceKeyStore(string? directory = null, bool memoryOnly = false) : IDeviceKeyStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, byte[]> Memory = new();
    private static int warned;
    private readonly string directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BRU.Avtopark.TicketSalesApp", "qr-devices");
    private bool Persist => OperatingSystem.IsWindows() && !memoryOnly;
    private static (string Origin, string Username, string Id, byte[] Entropy) Key(string origin, string username)
    {
        var normalized = QrOrigin.Normalize(origin);
        var name = username.Trim().ToLowerInvariant();
        if (name.Length is 0 or > 128) throw new QrLoginException("invalid_account");
        var entropy = SHA256.HashData(Encoding.UTF8.GetBytes("bru-qr-device/1\n" + normalized + "\n" + name));
        return (normalized, name, Convert.ToHexString(entropy), entropy);
    }
    public async Task SaveAsync(QrEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        var entry = Key(enrollment.Origin, enrollment.Username);
        using (var key = ECDsa.Create())
        {
            key.ImportPkcs8PrivateKey(enrollment.Pkcs8PrivateKey, out var read);
            if (read != enrollment.Pkcs8PrivateKey.Length || QrLoginProtocol.DeviceIdFor(key.ExportSubjectPublicKeyInfo()) != enrollment.DeviceId) throw new QrLoginException("invalid_key");
        }
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (Persist)
            {
                var path = Path.Combine(directory, entry.Id + ".dpapi");
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    Directory.CreateDirectory(directory);
                    // The only bytes written to disk have already been protected with CurrentUser DPAPI.
                    var encrypted = ProtectedData.Protect(enrollment.Pkcs8PrivateKey, entry.Entropy, DataProtectionScope.CurrentUser);
                    await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
                    File.Move(temporary, path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            else
            {
                if (Interlocked.Exchange(ref warned, 1) == 0) Log.Warning("Secure platform persistence unavailable; enrollment lasts until restart");
                if (Memory.Remove(entry.Id, out var previous)) CryptographicOperations.ZeroMemory(previous);
                Memory[entry.Id] = (byte[])enrollment.Pkcs8PrivateKey.Clone();
            }
        }
        finally { Gate.Release(); }
    }
    public async Task<QrEnrollment?> LoadAsync(string origin, string username, CancellationToken cancellationToken = default)
    {
        var entry = Key(origin, username);
        await Gate.WaitAsync(cancellationToken);
        byte[]? bytes = null;
        try
        {
            if (Persist)
            {
                var path = Path.Combine(directory, entry.Id + ".dpapi");
                if (!File.Exists(path)) return null;
                if (new FileInfo(path).Length > 4096) throw new QrLoginException("invalid_key");
                bytes = ProtectedData.Unprotect(await File.ReadAllBytesAsync(path, cancellationToken), entry.Entropy, DataProtectionScope.CurrentUser);
            }
            else if (Memory.TryGetValue(entry.Id, out var saved)) bytes = (byte[])saved.Clone();
            if (bytes == null) return null;
            using var key = ECDsa.Create(); key.ImportPkcs8PrivateKey(bytes, out _);
            var enrollment = new QrEnrollment(entry.Origin, entry.Username, QrLoginProtocol.DeviceIdFor(key.ExportSubjectPublicKeyInfo()), bytes);
            bytes = null; // Ownership transfers to caller; caller must zero it after use.
            return enrollment;
        }
        finally { if (bytes != null) CryptographicOperations.ZeroMemory(bytes); Gate.Release(); }
    }
    public async Task DeleteAsync(string origin, string username, CancellationToken cancellationToken = default)
    {
        var entry = Key(origin, username);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (Memory.Remove(entry.Id, out var bytes)) CryptographicOperations.ZeroMemory(bytes);
            if (Persist) { var path = Path.Combine(directory, entry.Id + ".dpapi"); if (File.Exists(path)) File.Delete(path); }
        }
        finally { Gate.Release(); }
    }
}
