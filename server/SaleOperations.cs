using System.Security.Cryptography;
using System.Text;
using SpacetimeDB;
#pragma warning disable STDB_UNSTABLE // Pinned 2.1.0 RLS API; local publish verifies filters.

public static partial class Module
{
    [SpacetimeDB.Table(Public = true)]
    public partial class SaleRevision
    {
        [PrimaryKey] public uint SaleId;
        public ulong Revision;
        public double RecordedAmount;
        public bool AmountKnown;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class MutationReceipt
    {
        [PrimaryKey] public string Key = "";
        public Identity Actor;
        public string Operation = "";
        public string PayloadHash = "";
        public uint EntityId;
        public ulong Revision;
        public ulong CommittedAt;
        [SpacetimeDB.Default(0u), SpacetimeDB.Index.BTree] public uint ApiAccessPartition;
    }

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter MutationReceiptApiAccess = new Filter.Sql(
        "SELECT MutationReceipt.* FROM MutationReceipt JOIN TrustedApiIdentity ON MutationReceipt.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    private static string MutationHash(params string?[] values)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        foreach (var value in values) { writer.Write(value != null); if (value != null) writer.Write(value); }
        writer.Flush();
        return ComputeSha256(Convert.ToBase64String(stream.ToArray()));
    }

    private static string ReceiptKey(Identity actor, string operationId)
    {
        if (!Guid.TryParseExact(operationId, "N", out _)) throw new Exception("Invalid mutation operation ID.");
        return actor.ToString() + ":" + operationId;
    }

    private static bool AlreadyCommitted(ReducerContext ctx, string key, string operation, string payloadHash)
    {
        var receipt = ctx.Db.MutationReceipt.Key.Find(key);
        if (receipt == null) return false;
        if (receipt.Operation != operation || receipt.PayloadHash != payloadHash)
            throw new Exception("Operation ID was reused with different arguments.");
        return true;
    }

    private static void SaveReceipt(ReducerContext ctx, string key, Identity actor, string operation, string payloadHash, uint entityId, ulong revision)
    {
        ctx.Db.MutationReceipt.Insert(new MutationReceipt { Key = key, Actor = actor, Operation = operation,
            PayloadHash = payloadHash, EntityId = entityId, Revision = revision,
            CommittedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 });
        Log.Info($"Mutation committed operation={operation}, actor={actor}, sender={ctx.Sender}, entity={entityId}, revision={revision}");
    }

    [SpacetimeDB.Reducer]
    public static void CreateSaleV2(ReducerContext ctx, string operationId, Identity actingUser, uint ticketId,
        string buyerName, string buyerPhone, string? saleLocation, string? saleNotes)
    {
        var actor = ResolveEffectiveUser(ctx, actingUser);
        if (!HasPermission(ctx, actor, "sales.create")) throw new Exception("Missing sales.create permission.");
        var key = ReceiptKey(actor, operationId);
        var hash = MutationHash(ticketId.ToString(), buyerName, buyerPhone, saleLocation, saleNotes);
        if (AlreadyCommitted(ctx, key, "sales.create", hash)) return;
        var saleId = CreateSaleCore(ctx, ticketId, buyerName, buyerPhone, saleLocation, saleNotes, actor);
        SaveReceipt(ctx, key, actor, "sales.create", hash, saleId, 1);
    }

    [SpacetimeDB.Reducer]
    public static void UpdateSaleV2(ReducerContext ctx, string operationId, Identity actingUser, uint saleId,
        ulong? expectedRevision, string? buyerName, string? buyerPhone, string? saleLocation, string? saleNotes)
    {
        var actor = ResolveEffectiveUser(ctx, actingUser);
        if (!HasPermission(ctx, actor, "sales.edit")) throw new Exception("Missing sales.edit permission.");
        var key = ReceiptKey(actor, operationId);
        var hash = MutationHash(saleId.ToString(), expectedRevision?.ToString(), buyerName, buyerPhone, saleLocation, saleNotes);
        if (AlreadyCommitted(ctx, key, "sales.edit", hash)) return;
        var revision = ctx.Db.SaleRevision.SaleId.Find(saleId)?.Revision ?? 0;
        if (expectedRevision.HasValue && expectedRevision != revision) throw new Exception("Sale revision conflict.");
        UpdateSale(ctx, saleId, null, buyerName, buyerPhone, saleLocation, saleNotes, actor);
        SaveReceipt(ctx, key, actor, "sales.edit", hash, saleId, ctx.Db.SaleRevision.SaleId.Find(saleId)!.Revision);
    }

    [SpacetimeDB.Reducer]
    public static void DeleteSaleV2(ReducerContext ctx, string operationId, Identity actingUser, uint saleId, ulong? expectedRevision)
    {
        var actor = ResolveEffectiveUser(ctx, actingUser);
        if (!HasPermission(ctx, actor, "sales.delete")) throw new Exception("Missing sales.delete permission.");
        var key = ReceiptKey(actor, operationId);
        var hash = MutationHash(saleId.ToString(), expectedRevision?.ToString());
        if (AlreadyCommitted(ctx, key, "sales.delete", hash)) return;
        var revision = ctx.Db.SaleRevision.SaleId.Find(saleId)?.Revision ?? 0;
        if (expectedRevision.HasValue && expectedRevision != revision) throw new Exception("Sale revision conflict.");
        DeleteSale(ctx, saleId, actor);
        SaveReceipt(ctx, key, actor, "sales.delete", hash, saleId, checked(revision + 1));
    }

    private static void ValidateSaleFields(string? buyerName, string? buyerPhone, string? location, string? notes)
    {
        if (buyerName?.Length > 200 || buyerPhone?.Length > 50 || location?.Length > 200 || notes?.Length > 2000)
            throw new Exception("Sale field length exceeded.");
    }
}
