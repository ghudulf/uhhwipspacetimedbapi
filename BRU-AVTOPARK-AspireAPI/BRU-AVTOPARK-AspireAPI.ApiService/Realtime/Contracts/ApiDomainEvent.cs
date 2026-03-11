namespace BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;

public sealed record ApiDomainEvent(
    string EventName,
    string Resource,
    string HttpMethod,
    int StatusCode,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string? UserId,
    string? UserName,
    string? Tenant,
    string SourceIp,
    IReadOnlyDictionary<string, string> Metadata);
