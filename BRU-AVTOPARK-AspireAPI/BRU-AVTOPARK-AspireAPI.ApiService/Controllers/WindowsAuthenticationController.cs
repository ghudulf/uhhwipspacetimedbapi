using System.Security.Claims;
using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace TicketSalesApp.AdminServer.Controllers;

[ApiController, Route("api/auth/windows"), AllowAnonymous, DisableCors]
public sealed class WindowsAuthenticationController(IConfiguration configuration, IAuthOrchestrationService authentication,
    ILogger<WindowsAuthenticationController> logger, TicketSalesApp.Services.Interfaces.ISpacetimeDBService database) : BaseController
{
    public sealed record LinkRequest(string ApplicationToken);

    [HttpPost("link")]
    [RequestSizeLimit(32 * 1024)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Link([FromBody] LinkRequest request, CancellationToken cancellationToken)
    {
        var (proof, failure) = await VerifyLinkProofAsync(request, cancellationToken);
        if (failure != null) return failure;
        var subject = proof!.Subject;
        var actor = proof.Actor;
        try
        {
            await database.ExecuteReducerAsync(new SpacetimeDB.Types.Reducer.LinkOwnExternalAccount(
                "windows", configuration["Authentication:Windows:Realm"]!, subject, actor), _ => true, cancellationToken);
            logger.LogInformation("Windows account self-link committed; trace {TraceId}", HttpContext.TraceIdentifier);
            return Ok(new { linked = true });
        }
        catch (TicketSalesApp.Services.Interfaces.ReducerRejectedException) { return Conflict(new { error = "account_already_linked_or_inactive" }); }
    }

    [HttpPost("link/status")]
    [RequestSizeLimit(32 * 1024)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LinkStatus([FromBody] LinkRequest request, CancellationToken cancellationToken)
    {
        var (proof, failure) = await VerifyLinkProofAsync(request, cancellationToken);
        if (failure != null) return failure;
        var key = "windows\n" + configuration["Authentication:Windows:Realm"] + "\n" + proof!.Subject;
        var state = await database.ReadAsync(c => c.Db.ExternalAccountLink.Key.Find(key) is { } link
            ? (link.UserId == proof.Actor ? "linked" : "linked_elsewhere") : "unlinked", cancellationToken);
        logger.LogInformation("Windows link status checked; state {State}, trace {TraceId}", state, HttpContext.TraceIdentifier);
        return Ok(new { state });
    }

    private sealed record LinkProof(SpacetimeDB.Identity Actor, string Subject);
    private async Task<(LinkProof? Proof, IActionResult? Failure)> VerifyLinkProofAsync(LinkRequest request, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Authentication:Windows:Enabled")) return (null, NotFound());
        if (!Request.IsHttps || Request.Headers.ContainsKey("Origin") || Request.Headers["X-BRU-Native-Login"] != "1") return (null, BadRequest(new { error = "native_https_required" }));
        if (string.IsNullOrWhiteSpace(request.ApplicationToken) || request.ApplicationToken.Length > 24 * 1024)
            return (null, Unauthorized());
        var provider = await HttpContext.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
        if (!provider.Succeeded || provider.Principal?.Identity?.IsAuthenticated != true)
            return (null, Challenge(NegotiateDefaults.AuthenticationScheme));
        var subject = ProviderSubject(provider.Principal);
        if (string.IsNullOrWhiteSpace(subject)) return (null, Unauthorized());
        // Negotiate owns the Authorization header; the application-session proof travels in the TLS-protected body.
        var claims = await ValidateTokenDirectAsync(request.ApplicationToken, cancellationToken);
        if (claims == null || !claims.TryGetValue("identity", out var identityValue) || identityValue is not string identity)
            return (null, Unauthorized());
        SpacetimeDB.Identity actor;
        try { actor = SpacetimeDB.Identity.FromHexString(identity); }
        catch (FormatException) { return (null, Unauthorized()); }
        var access = await new TicketSalesApp.Services.Authorization.UserAccessService(database).GetAsync(actor, cancellationToken);
        if (!access.IsActive) return (null, StatusCode(403));
        return (new LinkProof(actor, subject), null);
    }

    private static string? ProviderSubject(ClaimsPrincipal principal)
    {
        var sid = OperatingSystem.IsWindows() && principal.Identity is System.Security.Principal.WindowsIdentity windowsIdentity
            ? windowsIdentity.User?.Value : null;
        return sid ?? principal.FindFirstValue(ClaimTypes.PrimarySid) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.Identity?.Name;
    }

    [HttpPost]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Login()
    {
        if (!configuration.GetValue<bool>("Authentication:Windows:Enabled")) return NotFound();
        if (!Request.IsHttps) return BadRequest(new { error = "https_required" });
        if (Request.Headers.ContainsKey("Origin") || Request.Headers["X-BRU-Native-Login"] != "1") return BadRequest(new { error = "native_client_required" });
        var result = await HttpContext.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true)
            return Challenge(NegotiateDefaults.AuthenticationScheme);
        var principal = result.Principal;
        var subject = ProviderSubject(principal);
        if (string.IsNullOrWhiteSpace(subject)) return Unauthorized();
        logger.LogInformation("Windows provider authenticated; mechanism {Mechanism}, trace {TraceId}",
            principal.Identity.AuthenticationType, HttpContext.TraceIdentifier);
        var login = await authentication.LoginExternalAsync("windows", configuration["Authentication:Windows:Realm"]!, subject);
        return new JsonResult(login, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            { StatusCode = login.Success || login.RequiresTwoFactor ? 200 : 403 };
    }
}
