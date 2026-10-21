using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TicketSalesApp.AdminServer.Experimental.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

[ApiController, Route("api/auth/providers"), AllowAnonymous, DisableCors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProvidersAuthenticationController(IOptions<EntraOptions> entra, IConfiguration configuration, IFeatureFlagService flags,
    IOptions<PasskeyOptions> passkeys) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        Response.Headers.CacheControl = "no-store";
        var qr = false;
        try { qr = await flags.GetFlagAsync("QRLoginGenerateEndpointEnabled") && await flags.GetFlagAsync("QRLoginStatusEndpointEnabled"); } catch { }
        return Ok(new { entra = entra.Value.Enabled, passkeys = passkeys.Value.Enabled, windows = configuration.GetValue<bool>("Authentication:Windows:Enabled"), qr });
    }
}
