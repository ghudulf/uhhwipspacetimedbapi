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
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        // Authorization check - verify the effective user has the required permission
        if (!HasPermission(ctx, effectiveUser, "create_ticket")) // CreateTicket PERM CHECK
        {
            throw new Exception("Unauthorized: Missing CREATE_TICKET permission");
        }

        // Validate that the route exists
        if (!ctx.Db.Route.Iter().Any(r => r.RouteId == routeId))
        {
            throw new Exception("Route not found");
        }

        // Check if the seat is already taken
        if (ctx.Db.Ticket.Iter().Any(t => t.RouteId == routeId && t.SeatNumber == seatNumber && t.IsActive))
        {
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
            PurchaseTime = purchaseTime ?? (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 // Set purchase time
        };
        // Insert the new ticket into the database
        ctx.Db.Ticket.Insert(ticket);
    }

    [SpacetimeDB.Reducer]
    public static void CreateSale(ReducerContext ctx, uint ticketId, string buyerName, string buyerPhone, string? saleLocation = null, string? saleNotes = null)
    {
        // Validate that the ticket exists
        if (!ctx.Db.Ticket.Iter().Any(t => t.TicketId == ticketId))
            throw new Exception("Ticket not found.");

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
            SellerId = ctx.Sender,
            SaleLocation = saleLocation,
            SaleNotes = saleNotes
        };
        ctx.Db.Sale.Insert(sale);
    }

    [SpacetimeDB.Reducer]
    public static void CancelTicket(ReducerContext ctx, uint ticketId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        var ticket = ctx.Db.Ticket.Iter().FirstOrDefault(t => t.TicketId == ticketId);
        if (ticket == null)
        {
            throw new Exception("Ticket not found");
        }

        // Only ticket owner or admin can cancel
        if (!HasPermission(ctx, effectiveUser, "cancel_ticket")) // CancelTicket PERM CHECK
        {
            throw new Exception("Unauthorized: Cannot cancel ticket");
        }

        // Update the ticket to set IsActive to false and record the update details
        ticket.IsActive = false;
        ticket.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ticket.UpdatedBy = effectiveUser.ToString();

        ctx.Db.Ticket.TicketId.Update(ticket);

        Log.Info($"Ticket {ticketId} cancelled by {effectiveUser}");
	}

    [SpacetimeDB.Reducer]
    public static void UpdateTicket(ReducerContext ctx, uint ticketId, uint? routeId, uint? seatNumber, double? ticketPrice, string? paymentMethod, bool? isActive, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "tickets.edit")) // UpdateTicket PERM CHECK
        {
            throw new Exception("Unauthorized: You do not have permission to edit tickets.");
        }
        var ticket = ctx.Db.Ticket.TicketId.Find(ticketId);
        if (ticket == null)
        {
            throw new Exception("Ticket not found.");
        }
        if (routeId.HasValue)
        {
            // Validate route
            if (!ctx.Db.Route.Iter().Any(r => r.RouteId == routeId))
            {
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

        ticket.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ticket.UpdatedBy = effectiveUser.ToString();

        ctx.Db.Ticket.TicketId.Update(ticket);
        Log.Info($"Ticket {ticketId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteTicket(ReducerContext ctx, uint ticketId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "tickets.delete")) // DeleteTicket PERM CHECK
        {
           throw new Exception("Unauthorized: You do not have permission to delete tickets.");
        }
        if (ctx.Db.Ticket.TicketId.Find(ticketId) == null)
        {
            throw new Exception("Ticket not found.");
        }
        ctx.Db.Ticket.TicketId.Delete(ticketId);
        Log.Info($"Ticket {ticketId} has been deleted.");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateSale(ReducerContext ctx, uint saleId, uint? ticketId, string? ticketSoldToUser, string? ticketSoldToUserPhone, string? saleLocation, string? saleNotes, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "sales.edit")) // UpdateSale PERM CHECK
        {
            throw new Exception("Unauthorized: You do not have permission to edit sales.");
        }
        var sale = ctx.Db.Sale.SaleId.Find(saleId);
        if (sale == null)
        {
            throw new Exception("Sale not found.");
        }

        // Update only if new value is not null
        if (ticketId.HasValue)
        {
            if (!ctx.Db.Ticket.Iter().Any(t => t.TicketId == ticketId))
            {
                throw new Exception("Ticket not found.");
            }
            sale.TicketId = ticketId.Value;
        }
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
        Log.Info($"Sale {saleId} updated");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteSale(ReducerContext ctx, uint saleId, Identity? actingUser = null)
    {
        // Get the effective user identity - either the provided actingUser or ctx.Sender
        // This is a workaround because ctx.Sender will return the API server identity
        // when called through the API, not the actual logged-in user's identity
        var effectiveUser = actingUser ?? ctx.Sender;
        
        if (!HasPermission(ctx, effectiveUser, "sales.delete")) // DeleteSale PERM CHECK
        {
            throw new Exception("Unauthorized: You do not have permission to delete sales.");
        }
        // Check if the sale exists
        if (ctx.Db.Sale.SaleId.Find(saleId) == null)
        {
            throw new Exception("Sale not found.");
        }
        ctx.Db.Sale.SaleId.Delete(saleId);
        Log.Info($"Sale {saleId} has been deleted.");
    }
}