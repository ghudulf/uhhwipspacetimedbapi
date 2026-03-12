namespace TicketSalesApp.AdminServer.Models
{
    /// <summary>
    /// Safe data transfer object for user entities, excluding sensitive fields like PasswordHash.
    /// </summary>
    public class SafeUserDto
    {
        public required uint LegacyUserId { get; set; }
        public required string UserId { get; set; }
        public required string Login { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public required bool IsActive { get; set; }
        public required ulong CreatedAt { get; set; }
        public ulong? LastLoginAt { get; set; }
        public string? LegacyGuid { get; set; }
        public required bool EmailConfirmed { get; set; }
    }
}
