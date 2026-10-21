using System.Net;
using System.Security.Cryptography;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using QRCoder;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.AdminServer.Experimental.Services.Interfaces;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

// Runs before ApiController's automatic ModelState filter, preserving the protocol error shape.
public sealed class QrRequestValidationAttribute : ActionFilterAttribute
{
    public QrRequestValidationAttribute() => Order = int.MinValue;
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        if (!context.ModelState.IsValid)
        {
            context.ActionDescriptor.RouteValues.TryGetValue("action", out var action);
            var flag = action switch { "Create" => "QRLoginGenerateEndpointEnabled", "Status" => "QRLoginStatusEndpointEnabled", "Cancel" => "QRLoginCancelEndpointEnabled",
                "Peek" or "Approve" or "Deny" => "QRLoginValidateEndpointEnabled", _ => "QRLoginDirectEndpointEnabled" };
            context.Result = await ((QrLoginController)context.Controller).BoundaryAsync(flag) ?? new BadRequestObjectResult(new { code = "invalid_request" });
            return;
        }
        await next();
    }
}

[ApiController, Route("api/auth/qr"), AllowAnonymous, DisableCors, QrRequestValidation]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(4096)]
public sealed class QrLoginController(QrLoginBroker broker, IQrLoginDeviceStore devices, IAuthWebSocketTokenValidator tokens,
    UserAccessService access, ISpacetimeDBService database, IAuthOrchestrationService authentication,
    IFeatureFlagService flags, IHostEnvironment environment) : ControllerBase
{
    private static readonly byte[] DecoyKey = CreateDecoy();
    private static byte[] CreateDecoy() { using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); return key.ExportSubjectPublicKeyInfo(); }
    private IActionResult Error(int status, string code) => StatusCode(status, new { code });
    internal async Task<IActionResult?> BoundaryAsync(string flag)
    {
        try
        {
            if (!await flags.GetFlagAsync(flag)) return Error(503, "endpoint_unavailable");
            _ = environment; // Retained constructor compatibility; transport policy is environment-independent.
            if (!Security.HttpSecurityPolicy.IsTransportAllowed(HttpContext)) return Error(400, "https_required");
            return null;
        }
        catch { return Error(503, "qr_login_unavailable"); }
    }
    private async Task<IActionResult> Run(string flag, Func<Task<IActionResult>> action)
    {
        try
        {
            if (await BoundaryAsync(flag) is { } boundary) return boundary;
            return await action();
        }
        catch (QrLoginException ex)
        {
            return ex.AttemptsRemaining is { } remaining ? StatusCode(ex.Status, new { code = ex.Code, attemptsRemaining = remaining }) : Error(ex.Status, ex.Code);
        }
        catch { return Error(503, "qr_login_unavailable"); } // Never log request secrets or exception messages containing reducer arguments.
    }
    private async Task<UserProfile?> Actor()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var claims = await tokens.ValidateTokenDirectAsync(header[7..], HttpContext.RequestAborted);
            if (claims == null || !claims.TryGetValue("identity", out var value) || value is not string identity) return null;
            var actor = Identity.FromHexString(identity);
            if (!(await access.GetAsync(actor, HttpContext.RequestAborted)).IsActive) return null;
            return await database.ReadAsync(c => c.Db.UserProfile.UserId.Find(actor));
        }
        catch { return null; }
    }
    [HttpPost("sessions")]
    public Task<IActionResult> Create(QrCreateRequest request) => Run("QRLoginGenerateEndpointEnabled", async () =>
    {
        var decoy = false;
        if (request.Username != null)
        {
            var user = await devices.FindUserAsync(request.Username.Trim().ToLowerInvariant());
            decoy = user?.IsActive != true || (request.DeviceId != null && await devices.FindAsync(user.UserId, request.DeviceId) == null);
        }
        var origin = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var created = broker.Create(request, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", origin, decoy);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(created.QrPayload, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return new JsonResult(created with { QrPng = Convert.ToBase64String(png.GetGraphic(6)) }, ApiProtocol.Json) { StatusCode = 201 };
    });
    [HttpPost("sessions/{id}/status")]
    public Task<IActionResult> Status(string id, QrPollRequest request) => Run("QRLoginStatusEndpointEnabled", async () =>
    {
        var state = broker.Status(id, request.PollSecret, out var redemption);
        if (redemption == null) return new JsonResult(new QrStatusResponse(state), ApiProtocol.Json);
        var login = await authentication.CompleteQrLoginAsync(redemption.UserId, redemption.ApprovedByAuthenticatedSession);
        if (!login.Success && !login.RequiresTwoFactor) return Error(401, "qr_login_failed");
        return new JsonResult(new QrStatusResponse("completed", login), ApiProtocol.Json);
    });
    [HttpPost("sessions/{id}/cancel")]
    public Task<IActionResult> Cancel(string id, QrPollRequest request) => Run("QRLoginCancelEndpointEnabled", () =>
    { broker.Cancel(id, request.PollSecret); return Task.FromResult<IActionResult>(NoContent()); });
    [HttpPost("sessions/{id}/peek")]
    public Task<IActionResult> Peek(string id, QrSecretRequest request) => Run("QRLoginValidateEndpointEnabled", () =>
        Task.FromResult<IActionResult>(new JsonResult(broker.Peek(id, request.ApprovalSecret), ApiProtocol.Json)));
    [HttpPost("sessions/{id}/approve")]
    public Task<IActionResult> Approve(string id, QrApproveRequest request) => Run("QRLoginValidateEndpointEnabled", async () =>
    {
        var actor = await Actor();
        if (actor == null) return Error(401, "unauthorized");
        broker.Approve(id, request.ApprovalSecret, request.VerificationCode, actor.UserId, actor.Login);
        return NoContent();
    });
    [HttpPost("sessions/{id}/deny")]
    public Task<IActionResult> Deny(string id, QrSecretRequest request) => Run("QRLoginValidateEndpointEnabled", () =>
    { broker.Deny(id, request.ApprovalSecret); return Task.FromResult<IActionResult>(NoContent()); });
    [HttpPost("sessions/{id}/complete")]
    public Task<IActionResult> Complete(string id, QrCompleteRequest request) => Run("QRLoginDirectEndpointEnabled", async () =>
    {
        var challenge = broker.Challenge(id);
        UserProfile? user = null;
        QrLoginDevice? device = null;
        var signatureValid = false;
        try
        {
            user = await devices.FindUserAsync(challenge?.Username ?? "");
            device = await devices.FindAsync(user?.UserId ?? default, request.DeviceId ?? "");
        }
        catch { /* Still execute the decoy signature verification, then burn an attempt. */ }
        using (var key = ECDsa.Create())
        {
            try { key.ImportSubjectPublicKeyInfo(device == null ? DecoyKey : Convert.FromBase64String(device.PublicKey), out _); }
            catch { key.ImportSubjectPublicKeyInfo(DecoyKey, out _); device = null; }
            var signatureParsed = QrLoginProtocol.TryDecode(request.Signature, 64, out var signature);
            var message = QrLoginProtocol.SigningMessage(id, request.ApprovalSecret ?? "", challenge?.Origin ?? "https://invalid.invalid", challenge?.Username ?? "", request.DeviceId ?? "", request.VerificationCode ?? "");
            try { signatureValid = key.VerifyData(message, signatureParsed ? signature : new byte[64], HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation); }
            catch (CryptographicException) { signatureValid = false; }
            signatureValid &= signatureParsed && device != null && user?.IsActive == true && challenge?.Decoy == false;
        }
        if (signatureValid)
        {
            try { await devices.TouchAsync(request.DeviceId ?? ""); }
            catch { signatureValid = false; }
        }
        return broker.Complete(id, request.ApprovalSecret ?? "", request.VerificationCode ?? "", request.DeviceId ?? "", user?.UserId, signatureValid)
            ? NoContent() : Error(401, "qr_login_failed");
    });
    [HttpPost("devices")]
    public Task<IActionResult> Enroll(QrEnrollRequest request) => Run("QRLoginDirectEndpointEnabled", async () =>
    {
        var actor = await Actor();
        if (actor == null) return Error(401, "unauthorized");
        byte[] spki;
        try
        {
            if (request.PublicKey.Length > 256) return Error(400, "invalid_key");
            spki = Convert.FromBase64String(request.PublicKey);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read != spki.Length || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value) return Error(400, "invalid_key");
            spki = key.ExportSubjectPublicKeyInfo();
        }
        catch { return Error(400, "invalid_key"); }
        var deviceId = QrLoginProtocol.DeviceIdFor(spki);
        var current = await devices.ListAsync(actor.UserId);
        if (current.Any(d => d.DeviceId == deviceId)) return Error(409, "device_exists");
        if (current.Count >= 5) return Error(409, "device_limit");
        try { await devices.RegisterAsync(actor.UserId, deviceId, Convert.ToBase64String(spki), QrLoginBroker.Sanitize(request.DeviceName, 64), QrLoginBroker.Sanitize(request.Platform, 32)); }
        catch (ReducerRejectedException ex)
        { return Error(409, ex.Message.Contains("device_limit", StringComparison.Ordinal) ? "device_limit" : "device_exists"); }
        return new JsonResult(new QrDeviceResponse(deviceId), ApiProtocol.Json) { StatusCode = 201 };
    });
    [HttpGet("devices")]
    public Task<IActionResult> List() => Run("QRLoginDirectEndpointEnabled", async () =>
    {
        var actor = await Actor();
        if (actor == null) return Error(401, "unauthorized");
        return new JsonResult((await devices.ListAsync(actor.UserId)).Select(d => new QrDeviceInfo(d.DeviceId, d.DeviceName, d.Platform, d.CreatedAt, d.LastUsedAt)), ApiProtocol.Json);
    });
    [HttpDelete("devices/{deviceId}")]
    public Task<IActionResult> Revoke(string deviceId) => Run("QRLoginDirectEndpointEnabled", async () =>
    {
        var actor = await Actor();
        if (actor == null) return Error(401, "unauthorized");
        if (await devices.FindAsync(actor.UserId, deviceId) == null) return Error(404, "not_found");
        await devices.RevokeAsync(actor.UserId, deviceId);
        return NoContent();
    });
}
