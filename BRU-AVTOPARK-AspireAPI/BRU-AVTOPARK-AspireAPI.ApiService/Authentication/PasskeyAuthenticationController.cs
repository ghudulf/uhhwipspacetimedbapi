using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using SpacetimeDB;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

public sealed class PasskeyRequestValidationAttribute : ActionFilterAttribute
{
    public PasskeyRequestValidationAttribute() => Order = int.MinValue;
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (((PasskeyAuthenticationController)context.Controller).Boundary() is { } boundary) { context.Result = boundary; return; }
        if (!context.ModelState.IsValid) { context.Result = new BadRequestObjectResult(new { code = "invalid_request" }); return; }
        await next();
    }
}
public sealed record PasskeyStartResponse(string FlowId, string FlowSecret, string BrowserUrl, DateTimeOffset ExpiresAt, int PollIntervalMs);

[ApiController, Route("api/auth/passkey"), AllowAnonymous, DisableCors, PasskeyRequestValidation]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(32768)]
public sealed class PasskeyAuthenticationController(IOptions<PasskeyOptions> options, ProviderFlowBroker broker, IWebAuthnService webauthn,
    IAuthWebSocketTokenValidator tokens, UserAccessService access, ISpacetimeDBService database, IAuthOrchestrationService authentication,
    IHostEnvironment environment) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)] public sealed record StartRequest(string Purpose, string? Username = null);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)] public sealed record BrowserRequest(string? BrowserToken);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)] public sealed record AssertionRequest(string? BrowserToken, JsonElement Credential);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)] public sealed record SecretRequest(string? FlowSecret);
    internal IActionResult? Boundary()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.XContentTypeOptions = "nosniff";
        if (!options.Value.Enabled) return NotFound();
        if (!Request.IsHttps && !(environment.IsDevelopment() && HttpContext.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address)))
            return BadRequest(new { code = "https_required" });
        return null;
    }
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return Boundary() ?? await action(); }
        catch (ProviderFlowException ex) { return StatusCode(ex.Status, new { code = ex.Code }); }
        catch { return StatusCode(503, new { code = "passkey_unavailable" }); }
    }
    private async Task<Identity?> Actor()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || header.Length > 32768) return null;
        try
        {
            var claims = await tokens.ValidateTokenDirectAsync(header[7..], HttpContext.RequestAborted);
            if (claims == null || !claims.TryGetValue("identity", out var value) || value is not string identity) return null;
            var actor = Identity.FromHexString(identity);
            return (await access.GetAsync(actor, HttpContext.RequestAborted)).IsActive ? actor : null;
        }
        catch { return null; }
    }
    [HttpPost("start")]
    public Task<IActionResult> Start(StartRequest request) => Run(async () =>
    {
        if (request.Purpose is not ("login" or "register") || request.Username?.Length > 128 || request.Username?.Any(char.IsControl) == true ||
            request.Purpose == "register" && request.Username != null) return BadRequest(new { code = "invalid_request" });
        var actor = request.Purpose == "register" ? await Actor() : null;
        if (request.Purpose == "register" && actor == null) return Unauthorized(new { code = "unauthorized" });
        var flow = broker.Create("passkey", request.Purpose, actor, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        var browserToken = ProviderFlowBroker.RandomSecret();
        broker.BindBrowser(flow.FlowId, browserToken, request.Username);
        var url = options.Value.PublicOrigin.TrimEnd('/') + Request.PathBase + "/api/auth/passkey/browser?f=" + flow.FlowId + "&b=" + browserToken;
        return new JsonResult(new PasskeyStartResponse(flow.FlowId, flow.FlowSecret, url, flow.ExpiresAt, flow.PollIntervalMs), ApiProtocol.Json) { StatusCode = 201 };
    });
    [HttpGet("browser")]
    public Task<IActionResult> Browser([FromQuery] string f, [FromQuery] string b) => Run(() =>
    {
        broker.ValidateBrowser(f, b);
        var nonce = ProviderFlowBroker.RandomSecret();
        Response.Headers.ContentSecurityPolicy = $"default-src 'none'; connect-src 'self'; script-src 'nonce-{nonce}'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        return Task.FromResult<IActionResult>(Content(PasskeyBrowserPage.Html.Replace("__NONCE__", nonce, StringComparison.Ordinal), "text/html; charset=utf-8"));
    });
    [HttpPost("flows/{id}/options")]
    public Task<IActionResult> Options(string id, BrowserRequest request) => Run(async () =>
    {
        var context = broker.BeginBrowserOptions(id, request.BrowserToken);
        try
        {
            string json;
            if (context.Purpose == "register") json = (await webauthn.CreateRegistrationOptionsAsync(context.Actor!.Value)).ToJson();
            else json = (await webauthn.CreateAssertionOptionsAsync(context.Username)).ToJson();
            broker.SetBrowserOptions(id, json);
            var publicKey = JsonNode.Parse(json)!;
            if (context.Purpose == "register") publicKey["authenticatorSelection"]!["residentKey"] = "preferred";
            return new JsonResult(new { purpose = context.Purpose, publicKey }, ApiProtocol.Json);
        }
        catch { broker.Fail(id); return BadRequest(new { code = "passkey_failed" }); }
    });
    [HttpPost("flows/{id}/assert")]
    public Task<IActionResult> Assert(string id, AssertionRequest request) => Run(async () =>
    {
        var context = broker.BeginBrowserAssertion(id, request.BrowserToken);
        try
        {
            Identity actor; byte[] credentialId;
            if (context.Purpose == "register")
            {
                var credential = request.Credential.Deserialize<AuthenticatorAttestationRawResponse>() ?? throw new JsonException();
                actor = context.Actor!.Value; credentialId = credential.Id;
                await webauthn.VerifyRegistrationAsync(actor, CredentialCreateOptions.FromJson(context.OptionsJson!), credential);
            }
            else
            {
                var credential = request.Credential.Deserialize<AuthenticatorAssertionRawResponse>() ?? throw new JsonException();
                credentialId = credential.Id;
                actor = (await webauthn.VerifyAssertionAsync(AssertionOptions.FromJson(context.OptionsJson!), credential)).UserId;
            }
            if (!broker.Authenticate(id, "passkey", credentialId: Convert.ToBase64String(credentialId), authenticatedUser: actor)) throw new InvalidOperationException();
            return Ok(new { state = "authenticated" });
        }
        catch { broker.Fail(id); return BadRequest(new { code = "passkey_failed" }); }
    });
    [HttpPost("flows/{id}/complete")]
    public Task<IActionResult> Complete(string id, SecretRequest request) => Run(async () =>
    {
        var state = broker.Complete(id, "passkey", request.FlowSecret, out var proof);
        if (proof == null) return new JsonResult(new { state }, ApiProtocol.Json);
        if (proof.AuthenticatedUser is not { } actor || !(await access.GetAsync(actor, HttpContext.RequestAborted)).IsActive)
            return new JsonResult(new { state = "failed", code = "passkey_failed" }, ApiProtocol.Json);
        var credentialId = Convert.FromBase64String(proof.CredentialId!);
        if (!await database.ReadAsync(c => c.Db.WebAuthnCredential.Iter().Any(item => item.IsActive && item.UserId == actor && item.CredentialId.SequenceEqual(credentialId))))
            return new JsonResult(new { state = "failed", code = "passkey_failed" }, ApiProtocol.Json);
        if (proof.Purpose == "register") return new JsonResult(new { state = "completed", registered = true }, ApiProtocol.Json);
        var login = await authentication.CompleteQrLoginAsync(actor, false); // Same primary-factor policy; UV never bypasses configured 2FA.
        return login.Success || login.RequiresTwoFactor ? new JsonResult(new { state = "completed", login }, ApiProtocol.Json) :
            new JsonResult(new { state = "failed", code = "passkey_failed" }, ApiProtocol.Json);
    });
    [HttpPost("flows/{id}/cancel")]
    public Task<IActionResult> Cancel(string id, SecretRequest request) => Run(() =>
    { broker.Cancel(id, "passkey", request.FlowSecret); return Task.FromResult<IActionResult>(NoContent()); });
}
