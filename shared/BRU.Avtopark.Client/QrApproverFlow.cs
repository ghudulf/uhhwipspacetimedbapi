using BRU.Avtopark.Contracts;

namespace BRU.Avtopark.Client;

public sealed record QrApprovalRequest(QrPayload Payload, QrPeekResponse Details);
public sealed class QrApproverFlow(IQrLoginApi api)
{
    private void Check(QrApprovalRequest request)
    {
        if (!QrOrigin.IsSameServer(request.Payload.Origin, api.ApiBase) || !QrOrigin.IsSameServer(request.Details.Origin, api.ApiBase)) throw new QrLoginException("origin_mismatch");
    }
    public async Task<QrApprovalRequest> ParseAndPeekAsync(string scannedText, string currentApiBase, CancellationToken cancellationToken = default)
    {
        if (!QrLoginProtocol.TryParsePayload(scannedText, out var payload)) throw new QrLoginException("invalid_payload");
        if (!QrOrigin.IsSameServer(payload.Origin, currentApiBase) || !QrOrigin.IsSameServer(payload.Origin, api.ApiBase)) throw new QrLoginException("origin_mismatch");
        var details = await api.PeekAsync(payload.SessionId, payload.ApprovalSecret, cancellationToken).ConfigureAwait(false);
        var request = new QrApprovalRequest(payload, details);
        Check(request);
        return request;
    }
    public Task ApproveWithSessionAsync(QrApprovalRequest request, string bearer, string code, CancellationToken cancellationToken = default)
    {
        Check(request);
        return api.ApproveAsync(request.Payload.SessionId, request.Payload.ApprovalSecret, code, bearer, cancellationToken);
    }
    public Task ApproveWithDeviceKeyAsync(QrApprovalRequest request, QrEnrollment enrollment, string code, CancellationToken cancellationToken = default)
    {
        Check(request);
        if (!QrOrigin.IsSameServer(enrollment.Origin, api.ApiBase)) throw new QrLoginException("origin_mismatch");
        if (request.Details.BoundUsername == null || !string.Equals(request.Details.BoundUsername, enrollment.Username, StringComparison.OrdinalIgnoreCase)) throw new QrLoginException("wrong_account");
        var signature = QrDeviceKey.Sign(enrollment.Pkcs8PrivateKey, request.Payload.SessionId, request.Payload.ApprovalSecret, request.Payload.Origin, enrollment.Username, enrollment.DeviceId, code);
        return api.CompleteAsync(request.Payload.SessionId, request.Payload.ApprovalSecret, code, enrollment.DeviceId, signature, cancellationToken);
    }
    public Task DenyAsync(QrApprovalRequest request, CancellationToken cancellationToken = default)
    { Check(request); return api.DenyAsync(request.Payload.SessionId, request.Payload.ApprovalSecret, cancellationToken); }
}
