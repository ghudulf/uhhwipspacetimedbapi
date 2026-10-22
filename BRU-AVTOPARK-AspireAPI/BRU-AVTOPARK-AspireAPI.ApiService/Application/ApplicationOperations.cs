using System.Diagnostics;
using System.Text.Json;
using BRU.Avtopark.Contracts;
using SpacetimeDB;
using TicketSalesApp.Services.Authorization;
using TicketSalesApp.Services.Implementations;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Application;

public sealed class OperationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Explicit application boundary shared by HTTP and v2. No client-selected reflection.</summary>
public sealed partial class ApplicationOperations
{
    private readonly Dictionary<string, Handler> _handlers = new(StringComparer.Ordinal);
    private readonly UserAccessService _access;
    private readonly ILogger<ApplicationOperations> _logger;
    private readonly TrafficOptions _traffic;
    private sealed record Handler(OperationCapability Capability, Func<Identity, JsonElement, string?, CancellationToken, Task<object?>> Execute);

    public ApplicationOperations(ISpacetimeDBService database, ITicketSalesService sales, IBusService buses, ILogger<ApplicationOperations> logger, IConfiguration? configuration = null, IRouteService? routes = null, IEmployeeService? employees = null, IMaintenanceService? maintenance = null)
    {
        _access = new(database);
        _logger = logger;
        _traffic = configuration == null ? TicketSalesApp.Services.Diagnostics.TrafficDiagnostics.Options : TicketSalesApp.Services.Diagnostics.TrafficDiagnostics.ServerOptions(configuration);
        RegisterFleet(database, buses);
        RegisterCatalog(database, routes, employees, maintenance);
        RegisterAdmin(database);
        RegisterCatalogReads(database);
        var businessZone = TimeZoneInfo.FindSystemTimeZoneById(configuration?["Business:TimeZone"] ?? "UTC");
        Register<SalesReportRequest>("reports.sales", "sales.view", false, async (_, request, _, ct) =>
            await SalesReportProjection.ReadAsync(database, request, businessZone, ct));
        Register<EmptyRequest>("session.access", null, false, async (actor, _, _, ct) =>
        {
            var access = await _access.GetAsync(actor, ct);
            return new AccessSnapshot(access.IsActive, access.IsAdministrator, access.Roles, access.Permissions);
        });
        RegisterSalesReads(database);
        Register<CreateSaleCommand>("sales.create", "sales.create", true, async (actor, request, id, ct) =>
        {
            if (request.TicketId == 0) throw new OperationException("validation_failed");
            ValidateSale(request.BuyerName, request.BuyerPhone, request.Location, request.Notes);
            if (string.IsNullOrWhiteSpace(request.BuyerName) || request.BuyerPhone == null) throw new OperationException("validation_failed");
            await sales.CreateSaleAsync(request.TicketId, request.BuyerName, request.BuyerPhone, request.Location, request.Notes, actor, id, ct);
            return await CommittedReceipt(database, actor, id!, ct);
        });
        Register<UpdateSaleCommand>("sales.update", "sales.edit", true, async (actor, request, id, ct) =>
        {
            if (request.Id == 0) throw new OperationException("validation_failed");
            ValidateSale(request.BuyerName, request.BuyerPhone, request.Location, request.Notes);
            await sales.UpdateSaleAsync(request.Id, request.BuyerName, request.BuyerPhone, request.Location, request.Notes, actor, id, request.ExpectedRevision, ct);
            return await CommittedReceipt(database, actor, id!, ct);
        });
        Register<DeleteSaleCommand>("sales.delete", "sales.delete", true, async (actor, request, id, ct) =>
        {
            if (request.Id == 0) throw new OperationException("validation_failed");
            await sales.DeleteSaleAsync(request.Id, actor, id, request.ExpectedRevision, ct);
            return await CommittedReceipt(database, actor, id!, ct);
        });
        Register<ReceiptRequest>("mutations.receipt", null, false, async (actor, request, _, ct) =>
        {
            ValidateOperationId(request.OperationId);
            // Actor-scoped lookup; a missing receipt is not proof that a submitted reducer cannot still commit.
            return await Receipt(database, actor, request.OperationId, ct);
        });
        Register<MonthRequest>("reports.income", "sales.view", false, async (_, request, _, ct) =>
        {
            if (request.Year is < 1970 or > 9998 || request.Month is < 1 or > 12) throw new OperationException("validation_failed");
            var result = await sales.GetIncomeSummaryAsync(request.Year, request.Month, ct);
            return new RevenueSummary(result.KnownTotal, result.SaleCount, result.UnknownAmountCount);
        });
    }

    public OperationCapability[] Capabilities => _handlers.Values.Select(h => h.Capability).OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
    public bool Contains(string name) => _handlers.ContainsKey(name);

    public async Task<object?> ExecuteAsync(string operation, Identity actor, JsonElement payload, string? operationId, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(operation, out var handler)) throw new OperationException("unsupported_operation");
        if (handler.Capability.Mutation) ValidateOperationId(operationId);
        var started = Stopwatch.GetTimestamp();
        // v2 already measures its outer bru.operation span. HTTP and v1 adapters enter here directly.
        var measure = Activity.Current?.OperationName != "bru.operation";
        using var activity = measure ? OperationTelemetry.Traces.StartActivity("bru.operation") : null;
        activity?.SetTag("bru.operation", operation);
        var outcome = "success";
        if (_traffic.Level != TrafficLevel.Off)
            _logger.LogDebug("Operation {Operation} Actor={ActorFingerprint} OperationId={OperationId} Payload={Payload}", operation, DebugPayload.Fingerprint(actor.ToString()), operationId, DebugPayload.Json(payload.GetRawText(), _traffic.Level, _traffic.MaxBodyBytes));
        try
        {
            await AuthorizeAsync(operation, actor, ct);
            var result = await handler.Execute(actor, payload, operationId, ct);
            if (_traffic.Level != TrafficLevel.Off)
                _logger.LogDebug("Operation {Operation} Actor={ActorFingerprint} OperationId={OperationId} Result={Result} ElapsedMs={ElapsedMs} ErrorCode={ErrorCode}", operation, DebugPayload.Fingerprint(actor.ToString()), operationId,
                    DebugPayload.Json(JsonSerializer.Serialize(result, ApiProtocol.Json), _traffic.Level, _traffic.MaxBodyBytes), Stopwatch.GetElapsedTime(started).TotalMilliseconds, "success");
            _logger.LogInformation("Operation {Operation} committed/completed in {ElapsedMs} ms", operation, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (JsonException) { outcome = "validation_failed"; throw new OperationException("validation_failed"); }
        catch (Exception ex)
        {
            outcome = ex switch
            {
                OperationException error => error.Code, ReducerRejectedException => "conflict",
                CommitOutcomeUnknownException => "commit_outcome_unknown", DatabaseUnavailableException => "database_unavailable",
                OperationCanceledException => "cancelled", ArgumentException => "validation_failed", _ => "internal_error"
            };
            throw;
        }
        finally
        {
            if (outcome != "success" && _traffic.Level != TrafficLevel.Off)
                _logger.LogDebug("Operation {Operation} Actor={ActorFingerprint} OperationId={OperationId} ElapsedMs={ElapsedMs} ErrorCode={ErrorCode}", operation, DebugPayload.Fingerprint(actor.ToString()), operationId, Stopwatch.GetElapsedTime(started).TotalMilliseconds, outcome);
            if (measure)
            {
                var tags = new System.Diagnostics.TagList { { "operation", operation }, { "outcome", outcome } };
                OperationTelemetry.Completed.Add(1, tags);
                OperationTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
            }
        }
    }

    public async Task AuthorizeAsync(string operation, Identity actor, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(operation, out var handler)) throw new OperationException("unsupported_operation");
        var access = await _access.GetAsync(actor, ct);
        if (!access.IsActive || (handler.Capability.Permission is { } permission && !access.Allows(permission)))
            throw new OperationException("forbidden");
    }

    private void Register<T>(string name, string? permission, bool mutation, Func<Identity, T, string?, CancellationToken, Task<object?>> execute) =>
        _handlers.Add(name, new(new(name, mutation, permission), (actor, payload, id, ct) =>
            execute(actor, payload.Deserialize<T>(ApiProtocol.Json) ?? throw new OperationException("validation_failed"), id, ct)));

    public static void ValidatePage(PageRequest request)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > ApiProtocol.MaxPageSize ||
            ((long)request.Page - 1) * request.PageSize > int.MaxValue || request.Search?.Length > 200)
            throw new OperationException("validation_failed");
    }
    private static void ValidateSale(string? name, string? phone, string? location, string? notes)
    {
        if (name != null && string.IsNullOrWhiteSpace(name) || phone is { Length: > 0 } && string.IsNullOrWhiteSpace(phone)
            || name?.Length > 200 || phone?.Length > 50 || location?.Length > 200 || notes?.Length > 2000)
            throw new OperationException("validation_failed");
    }
    private static void ValidateOperationId(string? id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new OperationException("invalid_operation_id");
    }
    private static async Task<object?> Read<T>(ISpacetimeDBService database, Func<SpacetimeDB.Types.DbConnection, T> read, CancellationToken ct) => await database.ReadAsync(read, ct);
    private static async Task<MutationResult> CommittedReceipt(ISpacetimeDBService database, Identity actor, string id, CancellationToken ct)
    {
        try { return await Receipt(database, actor, id, ct) ?? throw new CommitOutcomeUnknownException(id); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or DatabaseUnavailableException)
        { throw new CommitOutcomeUnknownException(id, ex); }
    }
    private static Task<MutationResult?> Receipt(ISpacetimeDBService database, Identity actor, string id, CancellationToken ct) =>
        database.ReadAsync(c => c.Db.MutationReceipt.Key.Find(actor + ":" + id) is { } receipt
            ? new MutationResult(id, receipt.Operation, receipt.EntityId, receipt.Revision, DateTimeOffset.FromUnixTimeMilliseconds(checked((long)receipt.CommittedAt))) : null, ct);
}
