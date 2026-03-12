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

    /// <summary>
    /// Initializes a new RealtimeController that uses the provided realtime event bus to publish and retrieve events.
    /// </summary>
    /// <param name="eventBus">The realtime event bus used to publish domain events and retrieve recent events.</param>
    public RealtimeController(IRealtimeEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    /// <summary>
    /// Retrieve recent realtime domain events from the event bus.
    /// </summary>
    /// <param name="maxCount">Maximum number of events to return. Defaults to 100.</param>
    /// <returns>An OkObjectResult containing a collection of recent domain events, limited to <paramref name="maxCount"/> items.</returns>
    [HttpGet("events")]
    public IActionResult GetRecentEvents([FromQuery] int maxCount = 100)
    {
        return Ok(_eventBus.GetRecentEvents(maxCount));
    }

    /// <summary>
    /// Publishes a synthetic "system.broadcast-test" domain event to the realtime event bus and responds with an acknowledgement.
    /// </summary>
    /// <param name="cancellationToken">Token forwarded to the event bus publish operation to cancel the request.</param>
    /// <returns>An HTTP 202 Accepted response containing a JSON payload with `message` and `correlationId`.</returns>
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
