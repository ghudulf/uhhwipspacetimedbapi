using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

public class TicketService(ISpacetimeDBService spacetimeDBService, ILogger<TicketService> logger) : ITicketService
{
    public Task<List<Ticket>> GetAllTicketsAsync() => spacetimeDBService.ReadAsync(c => c.Db.Ticket.Iter().ToList());
    public Task<Ticket?> GetTicketByIdAsync(uint ticketId) => spacetimeDBService.ReadAsync(c => c.Db.Ticket.TicketId.Find(ticketId));
    public Task<List<Ticket>> GetTicketsByRouteIdAsync(uint routeId) => spacetimeDBService.ReadAsync(c => c.Db.Ticket.Iter().Where(t => t.RouteId == routeId).ToList());
    // PassengerId is absent from the current schema; preserve this legacy projection.
    public Task<List<Ticket>> GetTicketsByPassengerIdAsync(uint passengerId) => GetAllTicketsAsync();
    public Task<List<Ticket>> GetActiveTicketsAsync() => spacetimeDBService.ReadAsync(c => c.Db.Ticket.Iter().Where(t => t.IsActive).ToList());
    public Task<List<Ticket>> GetTicketsByDateRangeAsync(ulong startDate, ulong endDate) => spacetimeDBService.ReadAsync(c =>
        c.Db.Ticket.Iter().Where(t => t.PurchaseTime >= startDate && t.PurchaseTime <= endDate).OrderBy(t => t.PurchaseTime).ToList());

    public async Task<bool> CreateTicketAsync(uint routeId, uint seatNumber, double ticketPrice, string paymentMethod, ulong purchaseTime, Identity loggedinuser)
    {
        if (!double.IsFinite(ticketPrice) || ticketPrice < 0 || ticketPrice > 1e12)
            throw new ArgumentException("Ticket price must be finite and between 0 and 1e12.", nameof(ticketPrice));
        if (!await spacetimeDBService.ReadAsync(c => c.Db.Route.RouteId.Find(routeId) != null &&
            !c.Db.Ticket.Iter().Any(t => t.RouteId == routeId && t.SeatNumber == seatNumber))) return false;
        await spacetimeDBService.ExecuteReducerAsync(new Reducer.CreateTicket(routeId, ticketPrice, seatNumber, paymentMethod,
            (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), loggedinuser), _ => true);
        return true;
    }

    public async Task<bool> UpdateTicketAsync(uint ticketId, uint? routeId = null, double? ticketPrice = null, uint? seatNumber = null,
        string? paymentMethod = null, bool? isActive = null, ulong? updatedAt = null, string? updatedBy = null, Identity? actingUser = null)
    {
        var ticket = await GetTicketByIdAsync(ticketId);
        if (ticket == null) return false;
        if (seatNumber.HasValue && await spacetimeDBService.ReadAsync(c => c.Db.Ticket.Iter().Any(t =>
            t.RouteId == ticket.RouteId && t.SeatNumber == seatNumber && t.TicketId != ticketId))) return false;
        await spacetimeDBService.ExecuteReducerAsync(new Reducer.UpdateTicket(ticketId, routeId ?? ticket.RouteId,
            seatNumber ?? ticket.SeatNumber, ticketPrice ?? ticket.TicketPrice, paymentMethod ?? ticket.PaymentMethod, isActive, actingUser), _ => true);
        return true;
    }

    public async Task<bool> DeleteTicketAsync(uint ticketId, Identity? actingUser = null)
    {
        if (await GetTicketByIdAsync(ticketId) == null) return false;
        await spacetimeDBService.ExecuteReducerAsync(new Reducer.DeleteTicket(ticketId, actingUser), _ => true);
        return true;
    }

    public async Task<bool> CancelTicketAsync(uint ticketId, Identity? actingUser = null)
    {
        if (await GetTicketByIdAsync(ticketId) == null) return false;
        await spacetimeDBService.ExecuteReducerAsync(new Reducer.CancelTicket(ticketId, actingUser), _ => true);
        return true;
    }

    public async Task<bool> CreateSaleAsync(uint ticketId, string paymentMethod, string paymentStatus, string? paymentReference = null, string? notes = null)
    {
        if (await GetTicketByIdAsync(ticketId) == null) return false;
        await spacetimeDBService.ExecuteReducerAsync(new Reducer.CreateSale(ticketId, paymentMethod, paymentStatus, paymentReference, notes), _ => true);
        return true;
    }

    public Task<bool> UpdateSaleAsync(uint saleId, string? paymentMethod = null, string? paymentStatus = null, string? paymentReference = null, string? notes = null)
    {
        // These legacy payment fields no longer exist. Do not claim an unsubmitted write succeeded.
        logger.LogWarning("Legacy sale payment-field update is unsupported; sale {SaleId}", saleId);
        return Task.FromResult(false);
    }

    public async Task<bool> DeleteSaleAsync(uint saleId)
    {
        if (!await spacetimeDBService.ReadAsync(c => c.Db.Sale.SaleId.Find(saleId) != null)) return false;
        await spacetimeDBService.ExecuteReducerAsync(new Reducer.DeleteSale(saleId, null), _ => true);
        return true;
    }
}
