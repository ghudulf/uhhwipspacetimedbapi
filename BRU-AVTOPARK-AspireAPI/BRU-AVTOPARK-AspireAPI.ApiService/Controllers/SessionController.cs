using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TicketSalesApp.AdminServer.Controllers;

[ApiController, Route("api/session"), AllowAnonymous]
public sealed class SessionController(BRU_AVTOPARK_AspireAPI.ApiService.Application.ApplicationOperations operations) : BaseController
{
    [HttpGet("access")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Access()
    {
        if (!await IsAuthenticatedAsync()) return Unauthorized();
        var identity = await GetSpacetimeIdentityAsync();
        if (string.IsNullOrEmpty(identity)) return Unauthorized();
        try
        {
            var access = await operations.ExecuteAsync("session.access", SpacetimeDB.Identity.FromHexString(identity),
                System.Text.Json.JsonSerializer.SerializeToElement(new BRU.Avtopark.Contracts.EmptyRequest()), null, HttpContext.RequestAborted);
            return new JsonResult(access, BRU.Avtopark.Contracts.ApiProtocol.Json);
        }
        catch (BRU_AVTOPARK_AspireAPI.ApiService.Application.OperationException ex)
        {
            return StatusCode(ex.Code switch { "forbidden" => 403, "not_found" or "unsupported_operation" => 404, "database_unavailable" => 503, "timeout" => 504, _ => 400 }, new { code = ex.Code });
        }
        catch (TicketSalesApp.Services.Interfaces.DatabaseUnavailableException) { return StatusCode(503, new { code = "database_unavailable" }); }
        catch (TimeoutException) { return StatusCode(504, new { code = "timeout" }); }
    }
}
