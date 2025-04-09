using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface ITicketService
    {
        Task<List<Ticket>> GetAllTicketsAsync();
        Task<Ticket?> GetTicketByIdAsync(uint ticketId);
        Task<List<Ticket>> GetTicketsByRouteIdAsync(uint routeId);
        Task<bool> CreateTicketAsync(uint routeId, uint seatNumber, double ticketPrice, string paymentMethod, ulong purchaseTime,Identity loggedinuser);
        Task<bool> UpdateTicketAsync(uint ticketId, uint? routeId = null, double? ticketPrice = null, uint? seatNumber = null, string? paymentMethod = null, bool? isActive = null, ulong? updatedAt = null, string? updatedBy = null, Identity? actingUser = null);
        Task<bool> DeleteTicketAsync(uint ticketId, Identity? actingUser = null);
        Task<bool> CancelTicketAsync(uint ticketId, Identity? actingUser = null);
        Task<bool> CreateSaleAsync(uint ticketId, string buyerName, string buyerPhone, string? saleLocation = null, string? saleNotes = null);
    }
} 