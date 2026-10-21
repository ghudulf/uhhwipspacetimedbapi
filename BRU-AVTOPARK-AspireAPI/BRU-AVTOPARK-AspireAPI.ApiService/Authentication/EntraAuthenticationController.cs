using System.Net;
using System.Text.Json.Serialization;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

public sealed class EntraRequestValidationAttribute : ActionFilterAttribute
{
    public EntraRequestValidationAttribute() => Order = int.MinValue;
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        var controller = (EntraAuthenticationController)context.Controller;
        if (controller.Boundary() is { } boundary) { context.Result = boundary; return; }
        if (!context.ModelState.IsValid) { context.Result = new BadRequestObjectResult(new { code = "invalid_request" }); return; }
        await next();
    }
}

[ApiController, Route("api/auth/entra"), AllowAnonymous, DisableCors, EntraRequestValidation]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(4096)]
public sealed class EntraAuthenticationController(IOptions<EntraOptions> options, ProviderFlowBroker broker,
    EntraAuthenticationService provider, IAuthWebSocketTokenValidator tokens, UserAccessService access,
    ISpacetimeDBService database, IAuthOrchestrationService authentication, IHostEnvironment environment) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record StartRequest(string Purpose);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record SecretRequest(string? FlowSecret);
    internal IActionResult? Boundary()
    {
        Response.Headers.CacheControl = "no-store";
        if (!options.Value.Enabled) return NotFound();
        _ = environment; // Retained constructor compatibility; transport policy is environment-independent.
        if (!Security.HttpSecurityPolicy.IsTransportAllowed(HttpContext))
            return BadRequest(new { code = "https_required" });
        return null;
    }
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return Boundary() ?? await action(); }
        catch (ProviderFlowException ex) { return StatusCode(ex.Status, new { code = ex.Code }); }
        catch { return StatusCode(503, new { code = "provider_unavailable" }); }
    }
    private async Task<Identity?> Actor()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || header.Length > 32 * 1024) return null;
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
        if (request.Purpose is not ("login" or "link")) return BadRequest(new { code = "invalid_request" });
        var actor = request.Purpose == "link" ? await Actor() : null;
        if (request.Purpose == "link" && actor == null) return Unauthorized(new { code = "unauthorized" });
        var redirect = options.Value.RedirectUri ?? $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/auth/entra/callback";
        if (!EntraOptionsValidator.SafeRedirect(redirect, environment.IsDevelopment())) return BadRequest(new { code = "invalid_origin" });
        var flow = await provider.StartAsync(request.Purpose, actor, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", redirect, HttpContext.RequestAborted);
        return new JsonResult(flow, ApiProtocol.Json) { StatusCode = 201 };
    });
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (Boundary() is { } boundary) return boundary;
        var success = false;
        try { success = await provider.CallbackAsync(code, state, error, HttpContext.RequestAborted); } catch { }
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.XContentTypeOptions = "nosniff";
        return Content("<!doctype html><html lang=\"ru\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Вход</title><body><main><h1>" +
            (success ? "Вход выполнен. Вернитесь в приложение." : "Не удалось выполнить вход. Закройте окно и повторите попытку.") + "</h1></main></body></html>", "text/html; charset=utf-8");
    }
    [HttpPost("flows/{flowId}/complete")]
    public Task<IActionResult> Complete(string flowId, SecretRequest request) => Run(async () =>
    {
        var state = broker.Complete(flowId, "entra", request.FlowSecret, out var proof);
        if (proof == null) return new JsonResult(new { state }, ApiProtocol.Json);
        if (proof.Purpose == "login")
        {
            var login = await authentication.LoginExternalAsync("entra", proof.ExternalIdentity!.Issuer, proof.ExternalIdentity.Subject);
            return login.Success || login.RequiresTwoFactor ? new JsonResult(new { state = "completed", login }, ApiProtocol.Json) :
                new JsonResult(new { state = "failed", code = "not_linked" }, ApiProtocol.Json);
        }
        if (proof.Actor is not { } actor || !(await access.GetAsync(actor, HttpContext.RequestAborted)).IsActive)
            return new JsonResult(new { state = "failed", code = "unauthorized" }, ApiProtocol.Json);
        try
        {
            await database.ExecuteReducerAsync(new Reducer.LinkOwnExternalAccount("entra", proof.ExternalIdentity!.Issuer, proof.ExternalIdentity.Subject, actor), _ => true, HttpContext.RequestAborted);
            return new JsonResult(new { state = "completed", linked = true }, ApiProtocol.Json);
        }
        catch (ReducerRejectedException) { return new JsonResult(new { state = "failed", code = "already_linked" }, ApiProtocol.Json); }
    });
    [HttpPost("flows/{flowId}/cancel")]
    public Task<IActionResult> Cancel(string flowId, SecretRequest request) => Run(() =>
    { broker.Cancel(flowId, "entra", request.FlowSecret); return Task.FromResult<IActionResult>(NoContent()); });
    [HttpPost("link/status")]
    public Task<IActionResult> LinkStatus() => Run(async () =>
    {
        if (await Actor() is not { } actor) return Unauthorized(new { code = "unauthorized" });
        var linked = await database.ReadAsync(c => c.Db.ExternalAccountLink.Iter().Any(link => link.Provider == "entra" && link.UserId == actor), HttpContext.RequestAborted);
        return new JsonResult(new { state = linked ? "linked" : "unlinked" }, ApiProtocol.Json);
    });
    [HttpPost("unlink")]
    public Task<IActionResult> Unlink() => Run(async () =>
    {
        if (await Actor() is not { } actor) return Unauthorized(new { code = "unauthorized" });
        try
        {
            await database.ExecuteReducerAsync(new Reducer.UnlinkOwnExternalAccount("entra", actor), _ => true, HttpContext.RequestAborted);
            return Ok(new { unlinked = true });
        }
        catch (ReducerRejectedException) { return NotFound(new { code = "not_found" }); }
    });
}
