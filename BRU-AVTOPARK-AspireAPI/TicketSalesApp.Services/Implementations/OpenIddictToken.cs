using System.Collections.Immutable;
using System.Text.Json;
namespace TicketSalesApp.Services.Implementations;

/// <summary>Stable OpenIddict identity is independent of the mutable reference token hash.</summary>
public sealed class OpenIddictToken
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public uint? DatabaseId { get; internal set; }
    public ulong Revision { get; internal set; }
    public ImmutableDictionary<string, JsonElement> Properties { get; set; } = ImmutableDictionary<string, JsonElement>.Empty;
    public string? ApplicationId { get; set; }
    public string? AuthorizationId { get; set; }
    public DateTimeOffset? CreationDate { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? Payload { get; set; }
    public DateTimeOffset? RedemptionDate { get; set; }
    public string? ReferenceId { get; set; }
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
}
