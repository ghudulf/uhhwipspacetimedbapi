using System.Security.Cryptography;
using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

public static class QrOrigin
{
    public static string Normalize(string apiBase) => ClientEndpoints.ServerRoot(apiBase).AbsoluteUri.TrimEnd('/');
    public static bool IsSameServer(string payloadOrigin, string apiBase)
    {
        try
        {
            if (!QrLoginProtocol.ValidOrigin(payloadOrigin)) return false;
            var payload = new Uri(payloadOrigin);
            var expected = ClientEndpoints.ServerRoot(apiBase);
            return payload.Scheme == expected.Scheme && payload.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase)
                && payload.Port == expected.Port && payload.AbsolutePath.TrimEnd('/') == expected.AbsolutePath.TrimEnd('/');
        }
        catch { return false; }
    }
}

public sealed record QrEnrollment(string Origin, string Username, string DeviceId, byte[] Pkcs8PrivateKey);
public static class QrDeviceKey
{
    public static (byte[] Pkcs8PrivateKey, byte[] Spki, string DeviceId) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = key.ExportSubjectPublicKeyInfo();
        return (key.ExportPkcs8PrivateKey(), spki, QrLoginProtocol.DeviceIdFor(spki));
    }
    public static string Sign(byte[] pkcs8, string sessionId, string approvalSecret, string origin, string username, string deviceId, string verificationCode)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(pkcs8, out var read);
            if (read != pkcs8.Length || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value || QrLoginProtocol.DeviceIdFor(key.ExportSubjectPublicKeyInfo()) != deviceId)
                throw new QrLoginException("invalid_key");
            return QrLoginProtocol.Base64Url(key.SignData(QrLoginProtocol.SigningMessage(sessionId, approvalSecret, origin, username, deviceId, verificationCode), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }
        catch { throw new QrLoginException("invalid_key"); }
    }
    public static string ToSpkiBase64(byte[] spki) => Convert.ToBase64String(spki);
}
