 using System.Text;
using SpacetimeDB;

public static partial class Module
{ 
    
    [SpacetimeDB.Table(Public = true)]
    public partial class Discounts
    {
        [PrimaryKey]
        public uint DiscountId;
        public string DiscountType;
        public double DiscountPercentage;
        public ulong StartDate;
        public ulong EndDate;
        public bool IsActive;
        public string? Description;
        public string? RequiredDocuments;
        public string? CreatedBy;
        public ulong CreatedAt;
        public string? UpdatedBy;
        public ulong? UpdatedAt;
    }

    [SpacetimeDB.Reducer]
    public static void CreateTicket(ReducerContext ctx, uint routeId, double price, uint seatNumber, string paymentMethod, ulong? purchaseTime = null, Identity? actingUser = null)
        => CreateTicketCore(ctx, routeId, price, seatNumber, paymentMethod, purchaseTime, actingUser);

    private static uint CreateTicketCore(ReducerContext ctx, uint routeId, double price, uint seatNumber, string paymentMethod, ulong? purchaseTime, Identity? actingUser)
    {
        ValidateTicketFields(price, seatNumber, paymentMethod, purchaseTime);
        if (!double.IsFinite(price) || price < 0 || price > 1e12) throw new Exception("Invalid ticket price.");
        Log.Info($"[CreateTicket] Starting for route: {routeId}, seat: {seatNumber}, price: {price}");
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = ResolveEffectiveUser(ctx, actingUser);
        
        // Authorization check - verify the effective user has the required permission
        if (!HasPermission(ctx, effectiveUser, "create_ticket")) // CreateTicket PERM CHECK
        {
            Log.Error($"[CreateTicket] Unauthorized: User {effectiveUser} missing create_ticket permission");
            throw new Exception("Unauthorized: Missing CREATE_TICKET permission");
        }

        // Validate that the route exists
        if (!ctx.Db.Route.Iter().Any(r => r.RouteId == routeId))
        {
            Log.Error($"[CreateTicket] Route not found: {routeId}");
            throw new Exception("Route not found");
        }

        // Check if the seat is already taken
        if (ctx.Db.Ticket.Iter().Any(t => t.RouteId == routeId && t.SeatNumber == seatNumber && t.IsActive))
        {
            Log.Error($"[CreateTicket] Seat {seatNumber} already taken on route {routeId}");
            throw new Exception("Seat is already taken");
        }

        // Get the next ticket ID from the counter
        uint ticketId = 0;
        var counter = ctx.Db.TicketIdCounter.Key.Find("ticketId");
        if (counter == null)
        {
            // If counter doesn't exist, create it with initial value 1
            counter = ctx.Db.TicketIdCounter.Insert(new TicketIdCounter { Key = "ticketId", NextId = 1 });
        }
        else
        {
            // Increment the counter
            counter.NextId++;
            ctx.Db.TicketIdCounter.Key.Update(counter);
        }
        ticketId = counter.NextId;  // Use the counter value

        // Create a new ticket with the provided information
        var ticket = new Ticket
        {
            TicketId = ticketId,         // Set the ticket ID
            RouteId = routeId,           // Set the route ID
            TicketPrice = price,         // Set the ticket price
            SeatNumber = seatNumber,     // Set the seat number
            PaymentMethod = paymentMethod, // Set the payment method
            IsActive = true,             // Set the ticket as active
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, // Set creation time
            UpdatedAt = null,            // No updates yet
            UpdatedBy = null,            // No updates yet
            PurchaseTime = purchaseTime ?? (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, // Set purchase time
            TicketType = "Regular",      // Default to regular ticket
            TicketStatus = "Valid",      // Default to valid status
            ValidationMethod = null,     // Not validated yet
            ValidationTime = null,       // Not validated yet
            ValidationLocation = null,   // Not validated yet
            ValidatedByEmployeeId = null, // Not validated yet
            IsReturn = null,             // Not a return ticket by default
            ReturnTicketId = null,       // No return ticket
            DiscountType = null,         // No discount by default
            DiscountAmount = null,       // No discount amount
            DiscountReason = null,       // No discount reason
            RefundStatus = null,         // Not refunded
            RefundAmount = null,         // No refund amount
            RefundTime = null,           // No refund time
            RefundReason = null,         // No refund reason
            DiscountId = null,           // No discount ID
            SeatType = "Standard",       // Default seat type
            IsReserved = false,          // Not reserved by default
            ReservationStatus = null,    // No reservation status
            ReservationExpiry = null     // No reservation expiry
        };
        // Insert the new ticket into the database
        ctx.Db.Ticket.Insert(ticket);
        Log.Info($"[CreateTicket] Successfully created ticket ID: {ticketId} for route: {routeId} by user: {effectiveUser}");
        AdvanceEntityRevision(ctx, "tickets", ticketId);
        return ticketId;
    }

    [SpacetimeDB.Reducer]
    public static void CreateSale(ReducerContext ctx, uint ticketId, string buyerName, string buyerPhone, string? saleLocation = null, string? saleNotes = null)
    {
        CreateSaleCore(ctx, ticketId, buyerName, buyerPhone, saleLocation, saleNotes, ctx.Sender);
    }

    private static uint CreateSaleCore(ReducerContext ctx, uint ticketId, string buyerName, string buyerPhone,
        string? saleLocation, string? saleNotes, Identity actor)
    {
        if (!HasPermission(ctx, actor, "sales.create")) throw new Exception("Missing sales.create permission.");
        ValidateSaleFields(buyerName, buyerPhone, saleLocation, saleNotes);
        if (string.IsNullOrWhiteSpace(buyerName)) throw new Exception("Buyer name is required.");
        var ticket = ctx.Db.Ticket.TicketId.Find(ticketId) ?? throw new Exception("Ticket not found.");
        if (!ticket.IsActive) throw new Exception("Ticket is unavailable.");
        if (!double.IsFinite(ticket.TicketPrice) || ticket.TicketPrice < 0) throw new Exception("Invalid ticket amount.");
        if (ctx.Db.Sale.Iter().Any(s => s.TicketId == ticketId)) throw new Exception("Ticket already sold.");

        uint saleId = 0;
        var counter = ctx.Db.SaleIdCounter.Key.Find("saleId");
        if (counter == null)
        {
            counter = ctx.Db.SaleIdCounter.Insert(new SaleIdCounter { Key = "saleId", NextId = 1 });
        }
        else
        {
            counter.NextId++;
            ctx.Db.SaleIdCounter.Key.Update(counter);
        }
        saleId = counter.NextId;

        var sale = new Sale
        {
            SaleId = saleId,
            TicketId = ticketId,
            SaleDate = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            TicketSoldToUser = buyerName,
            TicketSoldToUserPhone = buyerPhone,
            SellerId = actor,
            TotalAmount = ticket.TicketPrice,
            PaymentMethod = ticket.PaymentMethod,
            PaymentStatus = "Completed",
            SaleLocation = saleLocation,
            SaleNotes = saleNotes
        };
        ctx.Db.Sale.Insert(sale);
        ctx.Db.SaleRevision.Insert(new SaleRevision { SaleId = saleId, Revision = 1, RecordedAmount = ticket.TicketPrice, AmountKnown = true });

        // Publish ticket sale event
        ctx.Db.TicketSaleEvent.Insert(new TicketSaleEvent
        {
            SaleId = saleId,
            TicketId = ticketId,
            RouteId = ticket.RouteId,
            BuyerId = actor,
            Amount = ticket.TicketPrice,
            Timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            PaymentMethod = ticket.PaymentMethod
        });
        Log.Info($"[CreateSale] Committed sale {saleId}, ticket {ticketId}, seller {actor}");
        return saleId;
    }

    [SpacetimeDB.Reducer]
    public static void CancelTicket(ReducerContext ctx, uint ticketId, Identity? actingUser = null)
    {
        Log.Info($"[CancelTicket] Starting for ticket ID: {ticketId}");
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = ResolveEffectiveUser(ctx, actingUser);
        
        var ticket = ctx.Db.Ticket.Iter().FirstOrDefault(t => t.TicketId == ticketId);
        if (ticket == null)
        {
            Log.Error($"[CancelTicket] Ticket not found: {ticketId}");
            throw new Exception("Ticket not found");
        }

        // Only ticket owner or admin can cancel
        if (!HasPermission(ctx, effectiveUser, "cancel_ticket")) // CancelTicket PERM CHECK
        {
            Log.Error($"[CancelTicket] Unauthorized: User {effectiveUser} missing cancel_ticket permission");
            throw new Exception("Unauthorized: Cannot cancel ticket");
        }

        // Find the associated sale
        var sale = ctx.Db.Sale.Iter().FirstOrDefault(s => s.TicketId == ticketId);

        // Update the ticket to set IsActive to false and record the update details
        ticket.IsActive = false;
        ticket.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ticket.UpdatedBy = effectiveUser.ToString();

        ctx.Db.Ticket.TicketId.Update(ticket);

        Log.Info($"Ticket {ticketId} cancelled by {effectiveUser}");
        AdvanceEntityRevision(ctx, "tickets", ticketId);

        // Publish ticket sale cancellation event
        ctx.Db.TicketSaleEvent.Insert(new TicketSaleEvent
        {
            SaleId = sale?.SaleId ?? 0,
            TicketId = ticketId,
            RouteId = ticket.RouteId,
            BuyerId = effectiveUser,
            Amount = ticket.TicketPrice,
            Timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
            PaymentMethod = "Cancelled"
        });
        Log.Info($"[CancelTicket] Successfully cancelled ticket ID: {ticketId} by user: {effectiveUser}");
	}

    [SpacetimeDB.Reducer]
    public static void UpdateTicket(ReducerContext ctx, uint ticketId, uint? routeId, uint? seatNumber, double? ticketPrice, string? paymentMethod, bool? isActive, Identity? actingUser = null)
        => UpdateTicketCore(ctx, ticketId, routeId, seatNumber, ticketPrice, paymentMethod, isActive, actingUser);

    private static void UpdateTicketCore(ReducerContext ctx, uint ticketId, uint? routeId, uint? seatNumber, double? ticketPrice, string? paymentMethod, bool? isActive, Identity? actingUser)
    {
        ValidateTicketFields(ticketPrice, seatNumber, paymentMethod, null);
        if (ticketPrice.HasValue && (!double.IsFinite(ticketPrice.Value) || ticketPrice < 0)) throw new Exception("Invalid ticket price.");
        Log.Info($"[UpdateTicket] Starting for ticket ID: {ticketId}");
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = ResolveEffectiveUser(ctx, actingUser);
        
        if (!HasPermission(ctx, effectiveUser, "tickets.edit")) // UpdateTicket PERM CHECK
        {
            Log.Error($"[UpdateTicket] Unauthorized: User {effectiveUser} missing tickets.edit permission");
            throw new Exception("Unauthorized: You do not have permission to edit tickets.");
        }
        var ticket = ctx.Db.Ticket.TicketId.Find(ticketId);
        if (ticket == null)
        {
            Log.Error($"[UpdateTicket] Ticket not found: {ticketId}");
            throw new Exception("Ticket not found.");
        }
        if (routeId.HasValue)
        {
            // Validate route
            if (!ctx.Db.Route.Iter().Any(r => r.RouteId == routeId))
            {
                Log.Error($"[UpdateTicket] Route not found: {routeId}");
                throw new Exception("Route not found");
            }
            ticket.RouteId = routeId.Value;
        }
        if (ticketPrice.HasValue)
        {
            ticket.TicketPrice = ticketPrice.Value;
        }
        if (seatNumber.HasValue)
        {
            ticket.SeatNumber = seatNumber.Value;
        }
        if (paymentMethod != null)
        {
            ticket.PaymentMethod = paymentMethod;
        }
        if (isActive.HasValue)
        {
            ticket.IsActive = isActive.Value;
        }

        if (ticket.IsActive && ctx.Db.Ticket.Iter().Any(t => t.TicketId != ticketId && t.RouteId == ticket.RouteId && t.SeatNumber == ticket.SeatNumber && t.IsActive))
            throw new Exception("Seat is already taken.");
        ticket.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ticket.UpdatedBy = effectiveUser.ToString();

        ctx.Db.Ticket.TicketId.Update(ticket);
        AdvanceEntityRevision(ctx, "tickets", ticketId);
        Log.Info($"Ticket {ticketId} updated");
        Log.Info($"[UpdateTicket] Successfully updated ticket ID: {ticketId} by user: {effectiveUser}");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteTicket(ReducerContext ctx, uint ticketId, Identity? actingUser = null)
        => DeleteTicketCore(ctx, ticketId, actingUser);

    private static void DeleteTicketCore(ReducerContext ctx, uint ticketId, Identity? actingUser)
    {
        if (ctx.Db.Sale.Iter().Any(s => s.TicketId == ticketId)) throw new Exception("Ticket is referenced by an existing sale.");
        Log.Info($"[DeleteTicket] Starting for ticket ID: {ticketId}");
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = ResolveEffectiveUser(ctx, actingUser);
        
        if (!HasPermission(ctx, effectiveUser, "tickets.delete")) // DeleteTicket PERM CHECK
        {
            Log.Error($"[DeleteTicket] Unauthorized: User {effectiveUser} missing tickets.delete permission");
           throw new Exception("Unauthorized: You do not have permission to delete tickets.");
        }
        if (ctx.Db.Ticket.TicketId.Find(ticketId) == null)
        {
            Log.Error($"[DeleteTicket] Ticket not found: {ticketId}");
            throw new Exception("Ticket not found.");
        }
        ctx.Db.Ticket.TicketId.Delete(ticketId);
        AdvanceEntityRevision(ctx, "tickets", ticketId);
        Log.Info($"Ticket {ticketId} has been deleted.");
        Log.Info($"[DeleteTicket] Successfully deleted ticket ID: {ticketId} by user: {effectiveUser}");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateSale(ReducerContext ctx, uint saleId, uint? ticketId, string? ticketSoldToUser, string? ticketSoldToUserPhone, string? saleLocation, string? saleNotes, Identity? actingUser = null)
    {
        Log.Info($"[UpdateSale] Starting for sale ID: {saleId}");
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = ResolveEffectiveUser(ctx, actingUser);
        
        if (!HasPermission(ctx, effectiveUser, "sales.edit")) // UpdateSale PERM CHECK
        {
            Log.Error($"[UpdateSale] Unauthorized: User {effectiveUser} missing sales.edit permission");
            throw new Exception("Unauthorized: You do not have permission to edit sales.");
        }
        var sale = ctx.Db.Sale.SaleId.Find(saleId);
        if (sale == null)
        {
            Log.Error($"[UpdateSale] Sale not found: {saleId}");
            throw new Exception("Sale not found.");
        }

        ValidateSaleFields(ticketSoldToUser, ticketSoldToUserPhone, saleLocation, saleNotes);
        if (ticketId.HasValue && ticketId.Value != sale.TicketId)
            throw new Exception("A sale's original ticket cannot be changed.");
        if (ticketSoldToUser != null)
        {
            sale.TicketSoldToUser = ticketSoldToUser;
        }
        if (ticketSoldToUserPhone != null)
        {
            sale.TicketSoldToUserPhone = ticketSoldToUserPhone;
        }
        if (saleLocation != null)
        {
            sale.SaleLocation = saleLocation;
        }
        if (saleNotes != null)
        {
            sale.SaleNotes = saleNotes;
        }

        ctx.Db.Sale.SaleId.Update(sale);
        var revision = ctx.Db.SaleRevision.SaleId.Find(saleId);
        if (revision == null) ctx.Db.SaleRevision.Insert(new SaleRevision { SaleId = saleId, Revision = 1, RecordedAmount = sale.TotalAmount, AmountKnown = sale.TotalAmount > 0 });
        else { revision.Revision = checked(revision.Revision + 1); ctx.Db.SaleRevision.SaleId.Update(revision); }
        Log.Info($"Sale {saleId} updated");

        // Get the ticket to extract route and price information
        var ticket = ctx.Db.Ticket.TicketId.Find(sale.TicketId);
        if (ticket != null)
        {
            // Publish ticket sale update event
            ctx.Db.TicketSaleEvent.Insert(new TicketSaleEvent
            {
                SaleId = saleId,
                TicketId = sale.TicketId,
                RouteId = ticket.RouteId,
                BuyerId = effectiveUser,
                Amount = ticket.TicketPrice,
                Timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                PaymentMethod = ticket.PaymentMethod
            });
        }
        Log.Info($"[UpdateSale] Successfully updated sale ID: {saleId} by user: {effectiveUser}");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteSale(ReducerContext ctx, uint saleId, Identity? actingUser = null)
    {
        Log.Info($"[DeleteSale] Starting for sale ID: {saleId}");
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = ResolveEffectiveUser(ctx, actingUser);
        
        if (!HasPermission(ctx, effectiveUser, "sales.delete")) // DeleteSale PERM CHECK
        {
            Log.Error($"[DeleteSale] Unauthorized: User {effectiveUser} missing sales.delete permission");
            throw new Exception("Unauthorized: You do not have permission to delete sales.");
        }
        // Check if the sale exists
        if (ctx.Db.Sale.SaleId.Find(saleId) == null)
        {
            Log.Error($"[DeleteSale] Sale not found: {saleId}");
            throw new Exception("Sale not found.");
        }
        ctx.Db.Sale.SaleId.Delete(saleId);
        Log.Info($"Sale {saleId} has been deleted.");
        Log.Info($"[DeleteSale] Successfully deleted sale ID: {saleId} by user: {effectiveUser}");
    }
}
