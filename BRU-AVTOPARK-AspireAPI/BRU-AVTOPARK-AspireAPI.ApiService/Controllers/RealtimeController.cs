using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FlexibleApiAccess")]
public sealed class RealtimeController : ControllerBase
{
    private readonly IRealtimeEventBus _eventBus;

    public RealtimeController(IRealtimeEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    [HttpGet("events")]
    public IActionResult GetRecentEvents([FromQuery] int maxCount = 100)
    {
        return Ok(_eventBus.GetRecentEvents(maxCount));
    }

    [HttpPost("broadcast-test")]
    public async Task<IActionResult> BroadcastTest(CancellationToken cancellationToken)
    {
        var evt = new ApiDomainEvent(
            EventName: "system.broadcast-test",
            Resource: "system",
            HttpMethod: HttpMethods.Post,
            StatusCode: StatusCodes.Status200OK,
            OccurredAt: DateTimeOffset.UtcNow,
            CorrelationId: HttpContext.TraceIdentifier,
            UserId: User.FindFirst("sub")?.Value,
            UserName: User.Identity?.Name,
            Tenant: User.FindFirst("tenant")?.Value,
            SourceIp: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            Metadata: new Dictionary<string, string>
            {
                ["purpose"] = "connectivity-check",
                ["path"] = HttpContext.Request.Path,
                ["trigger"] = "manual"
            });

        await _eventBus.PublishAsync(evt, cancellationToken);
        return Accepted(new { message = "Realtime event enqueued.", correlationId = evt.CorrelationId });
    }
}
