using System.Text.Json;
using BRU.Avtopark.Contracts;
using BRU_AVTOPARK.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpacetimeDB;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.AdminServer.Controllers;

[ApiController, Route("api/operations"), AllowAnonymous]
public sealed class ApplicationOperationsController(ApplicationOperations operations, IAuthWebSocketTokenValidator tokens) : ControllerBase
{
    [HttpPost("{operation}")]
    [RequestSizeLimit(ApiProtocol.MaxMessageBytes)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Execute(string operation, [FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Unauthorized();
        var claims = await tokens.ValidateTokenDirectAsync(header[7..], cancellationToken);
        if (claims == null || !claims.TryGetValue("identity", out var value) || value is not string identity) return Unauthorized();
        Identity actor;
        try { actor = Identity.FromHexString(identity); } catch (Exception ex) when (ex is ArgumentException or FormatException) { return Unauthorized(); }
        try
        {
            var supplied = Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (supplied != null && !Guid.TryParse(supplied, out _)) throw new OperationException("validation_failed");
            var operationId = supplied == null ? Guid.NewGuid().ToString("N") : Guid.Parse(supplied).ToString("N");
            Response.Headers["Operation-Id"] = operationId;
            var result = await operations.ExecuteAsync(operation, actor, payload, operationId, cancellationToken);
            return new JsonResult(result, ApiProtocol.Json);
        }
        catch (Exception ex) when (ex is OperationException or ReducerRejectedException or CommitOutcomeUnknownException or DatabaseUnavailableException or TimeoutException)
        {
            var code = ex switch { OperationException op => op.Code, ReducerRejectedException => "conflict", CommitOutcomeUnknownException => "commit_outcome_unknown", TimeoutException => "timeout", _ => "database_unavailable" };
            var status = code switch { "forbidden" => 403, "not_found" or "unsupported_operation" => 404, "conflict" => 409, "timeout" => 504, "commit_outcome_unknown" or "database_unavailable" => 503, _ => 400 };
            return StatusCode(status, new { code, traceId = HttpContext.TraceIdentifier });
        }
    }
}
