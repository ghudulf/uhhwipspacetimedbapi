using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Implementations;

public sealed record SaleRoute(uint RouteId, string StartPoint, string EndPoint);
public sealed record SaleTicket(uint TicketId, uint RouteId, double TicketPrice, SaleRoute? Route);
public sealed record SaleDetails(uint SaleId, DateTime SaleDate, uint TicketId, string TicketSoldToUser,
    string TicketSoldToUserPhone, string? SellerId, SaleTicket? Ticket, decimal? RecordedAmount,
    bool AmountKnown, ulong Revision);

public static class SalePresentation
{
    public static decimal? Amount(DbConnection connection, Sale sale)
    {
        var revision = connection.Db.SaleRevision.SaleId.Find(sale.SaleId);
        if (revision != null) return revision.AmountKnown ? checked((decimal)revision.RecordedAmount) : null;
        // Legacy rows with an explicitly recorded positive amount are usable. Zero was also the
        // historical default, so it cannot be distinguished from a free sale without evidence.
        return double.IsFinite(sale.TotalAmount) && sale.TotalAmount > 0 ? checked((decimal)sale.TotalAmount) : null;
    }

    public static SaleDetails Map(DbConnection connection, Sale sale)
    {
        var ticket = connection.Db.Ticket.TicketId.Find(sale.TicketId);
        var route = ticket == null ? null : connection.Db.Route.RouteId.Find(ticket.RouteId);
        var amount = Amount(connection, sale);
        return new(sale.SaleId, DateTimeOffset.FromUnixTimeMilliseconds(checked((long)sale.SaleDate)).UtcDateTime,
            sale.TicketId, sale.TicketSoldToUser, sale.TicketSoldToUserPhone, sale.SellerId?.ToString(),
            ticket == null ? null : new(ticket.TicketId, ticket.RouteId, ticket.TicketPrice,
                route == null ? null : new(route.RouteId, route.StartPoint, route.EndPoint)),
            amount, amount.HasValue, connection.Db.SaleRevision.SaleId.Find(sale.SaleId)?.Revision ?? 0);
    }
}

public sealed record IncomeSummary(decimal KnownTotal, int SaleCount, int UnknownAmountCount)
{
    public bool IsComplete => UnknownAmountCount == 0;
}
public sealed class HistoricalAmountUnknownException(uint? saleId) : Exception(
    saleId.HasValue ? $"Recorded amount is unknown for historical sale {saleId}." : "Some historical sale amounts are unknown.");
