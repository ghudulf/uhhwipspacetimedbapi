using System.Text.Json;
using System.Text.Json.Serialization;

namespace BRU.Avtopark.Contracts;

public static class ApiProtocol
{
    public const string Subprotocol = "bru.api.v2";
    public const int MaxMessageBytes = 64 * 1024;
    public const int MaxResponseBytes = 1024 * 1024;
    public const int MaxOutstanding = 8;
    public const int MaxPageSize = 200;
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record CommandEnvelope(string RequestId, string Operation, JsonElement Payload, string? OperationId = null);
public sealed record ResultEnvelope(string Type, string? RequestId, bool Success, JsonElement? Data,
    string? Code, string TraceId, long Generation);
public sealed record OperationCapability(string Name, bool Mutation, string? Permission);
public sealed record Capabilities(string Protocol, OperationCapability[] Operations, string[] AuthenticationMethods,
    int MaxMessageBytes, int MaxOutstanding, string DatabaseState, string ConnectionId);
public sealed record EmptyRequest;
public sealed record PageRequest(int Page = 1, int PageSize = 100, string? Search = null);
public sealed record PageResult<T>(T[] Items, int Page, int PageSize, int TotalCount);
public sealed record EntityRequest(uint Id);
public sealed record AccessSnapshot(bool IsActive, bool IsAdministrator, string[] Roles, string[] Permissions);
public sealed record AttachRequest(string Token);
public sealed record LoginCommand(string Username, string Password);
public sealed record TotpCommand(string Code);
public sealed record CancelCommand(string RequestId);
public sealed record AuthenticationReply(string State, string? Token = null, bool TotpEnabled = false, bool WebAuthnEnabled = false);
public sealed record CreateSaleCommand(uint TicketId, string? BuyerName, string? BuyerPhone, string? Location = null, string? Notes = null);
public sealed record UpdateSaleCommand(uint Id, ulong? ExpectedRevision, string? BuyerName = null, string? BuyerPhone = null, string? Location = null, string? Notes = null);
public sealed record DeleteSaleCommand(uint Id, ulong? ExpectedRevision = null);
public sealed record ReceiptRequest(string OperationId);
public sealed record MutationResult(string OperationId, string Operation, uint EntityId, ulong Revision, DateTimeOffset CommittedAt);
public sealed record MonthRequest(int Year, int Month);
public sealed record RevenueSummary(decimal KnownTotal, int SaleCount, int UnknownAmountCount)
{
    public bool IsComplete => UnknownAmountCount == 0;
    public decimal? AverageKnownAmount => SaleCount > UnknownAmountCount ? KnownTotal / (SaleCount - UnknownAmountCount) : null;
}
public sealed record SaleRow(uint SaleId, DateTimeOffset SaleDate, uint TicketId, string BuyerName, string BuyerPhone,
    string? SellerId, decimal? RecordedAmount, ulong Revision);
