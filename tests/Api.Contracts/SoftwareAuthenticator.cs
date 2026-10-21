using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Microsoft.IdentityModel.Tokens;

/// <summary>Test-only ES256 authenticator: none attestation and actual DER assertion signatures.</summary>
internal sealed class SoftwareAuthenticator : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public byte[] Id { get; } = RandomNumberGenerator.GetBytes(32);
    public byte[] Handle = [];
    private static byte[] ClientData(string type, byte[] challenge, string origin) => JsonSerializer.SerializeToUtf8Bytes(new
    { type, challenge = Base64UrlEncoder.Encode(challenge), origin, crossOrigin = false });
    public AuthenticatorAttestationRawResponse Register(CredentialCreateOptions options, string origin, string? rp = null, bool uv = true, byte[]? challenge = null)
    {
        Handle = options.User.Id;
        var clientData = ClientData("webauthn.create", challenge ?? options.Challenge, origin);
        var parameters = key.ExportParameters(false);
        var cose = new CborWriter(); cose.WriteStartMap(5);
        cose.WriteInt32(1); cose.WriteInt32(2); cose.WriteInt32(3); cose.WriteInt32(-7);
        cose.WriteInt32(-1); cose.WriteInt32(1); cose.WriteInt32(-2); cose.WriteByteString(parameters.Q.X!); cose.WriteInt32(-3); cose.WriteByteString(parameters.Q.Y!); cose.WriteEndMap();
        var authData = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(rp ?? options.Rp.Id)));
        authData.Add((byte)(uv ? 0x45 : 0x41)); authData.AddRange(new byte[4]); authData.AddRange(new byte[16]);
        var length = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)Id.Length);
        authData.AddRange(length); authData.AddRange(Id); authData.AddRange(cose.Encode());
        var attestation = new CborWriter(); attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
        attestation.WriteTextString("authData"); attestation.WriteByteString(authData.ToArray()); attestation.WriteEndMap();
        return JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(JsonSerializer.Serialize(new
        {
            id = Base64UrlEncoder.Encode(Id), rawId = Base64UrlEncoder.Encode(Id), type = "public-key", extensions = new { },
            response = new { attestationObject = Base64UrlEncoder.Encode(attestation.Encode()), clientDataJSON = Base64UrlEncoder.Encode(clientData), transports = new[] { "internal" } }
        }))!;
    }
    public AuthenticatorAssertionRawResponse Assert(AssertionOptions options, string origin, string rp, uint counter = 1, bool uv = true, byte[]? challenge = null, byte[]? handle = null)
    {
        var clientData = ClientData("webauthn.get", challenge ?? options.Challenge, origin);
        var authData = new byte[37]; SHA256.HashData(Encoding.UTF8.GetBytes(rp)).CopyTo(authData, 0);
        authData[32] = (byte)(uv ? 5 : 1); BinaryPrimitives.WriteUInt32BigEndian(authData.AsSpan(33), counter);
        var signed = authData.Concat(SHA256.HashData(clientData)).ToArray();
        var signature = key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(JsonSerializer.Serialize(new
        {
            id = Base64UrlEncoder.Encode(Id), rawId = Base64UrlEncoder.Encode(Id), type = "public-key", extensions = new { },
            response = new { authenticatorData = Base64UrlEncoder.Encode(authData), clientDataJSON = Base64UrlEncoder.Encode(clientData), signature = Base64UrlEncoder.Encode(signature), userHandle = Base64UrlEncoder.Encode(handle ?? Handle) }
        }))!;
    }
    public void Dispose() => key.Dispose();
}
