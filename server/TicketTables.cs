 using System.Text;
using SpacetimeDB;

public static partial class Module
{ // ***** Ticket Management *****

    [SpacetimeDB.Table(Public = true)]
    public partial class Ticket
    {
        [PrimaryKey]
        public uint TicketId;           // Auto-incremented unique identifier for each ticket
        public uint RouteId;            // Foreign key referencing Route.RouteId
        public double TicketPrice;      // Price of the ticket, stored as a double for currency precision
        public uint SeatNumber;         // Assigned seat number on the bus
        public string PaymentMethod;    // Payment method used, e.g., "cash", "card"
        public bool IsActive;           // Indicates if the ticket is active and valid for use
        public ulong CreatedAt;         // Timestamp of when the ticket was created
        public ulong? UpdatedAt;        // Timestamp of the last update, if any
        public string? UpdatedBy;       // Identifier of the user who last updated the ticket
        public ulong PurchaseTime;      // Timestamp of when the ticket was purchased
        public string? TicketType;      // "Regular", "Student", "Senior", "Disabled"
        public string? TicketStatus;    // "Valid", "Used", "Expired", "Cancelled"
        public string? ValidationMethod; // "QR", "NFC", "Manual"
        public ulong? ValidationTime;   // When the ticket was validated
        public string? ValidationLocation; // Where the ticket was validated
        public uint? ValidatedByEmployeeId; // Who validated the ticket
        public bool? IsReturn;          // Whether this is a return ticket
        public uint? ReturnTicketId;    // ID of the return ticket
        public string? DiscountType;    // Type of discount applied
        public double? DiscountAmount;  // Amount of discount applied
        public string? DiscountReason;  // Reason for the discount
        public string? RefundStatus;    // "Not Refunded", "Refunded", "Partial Refund"
        public double? RefundAmount;    // Amount refunded
        public ulong? RefundTime;       // When the refund was processed
        public string? RefundReason;    // Reason for the refund
        public uint? DiscountId;
        public string? SeatType;
        public bool IsReserved;
        public string? ReservationStatus;
        public ulong? ReservationExpiry;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Sale
    {
        [PrimaryKey]
        public uint SaleId;             // Auto-incremented unique identifier for each sale
        public ulong SaleDate;          // Timestamp of when the sale occurred
        public uint TicketId;           // Foreign key referencing Ticket.TicketId
        public string TicketSoldToUser; // Name of the user to whom the ticket was sold
        public string TicketSoldToUserPhone; // Phone number of the user to whom the ticket was sold
        public Identity? SellerId;      // Identifier of the seller who processed the sale (can be null initially)
        public string? SaleLocation;    // Optional field to track the location where the sale was made
        public string? SaleNotes;       // Optional field for any additional notes related to the sale
        public string? PaymentMethod;   // "Cash", "Card", "Mobile Payment"
        public string? PaymentStatus;   // "Completed", "Pending", "Failed"
        public string? TransactionId;   // ID of the payment transaction
        public double? TaxAmount;       // Amount of tax included in the sale
        public string? InvoiceNumber;   // Invoice number for the sale
        public bool? IsSubscription;    // Whether this is a subscription sale
        public string? SubscriptionType; // Type of subscription
        public ulong? SubscriptionStartDate; // Start date of the subscription
        public ulong? SubscriptionEndDate; // End date of the subscription
        public bool? IsGift;            // Whether this is a gift purchase
        public string? GiftRecipient;   // Name of the gift recipient
        public string? PromotionCode;   // Promotion code used for the sale
        public double? DiscountAmount;  // Amount of discount applied
        public double TotalAmount;
        public string? PaymentTransactionId;
        public double? ChangeAmount;
        public string? PaymentProvider;
        public string? PaymentReference;
    }
    
}