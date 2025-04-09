using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for handling Magic Link authentication operations
    /// </summary>
    public interface IMagicLinkService
    {
        Task<(bool success, string? errorMessage)> SendMagicLinkAsync(string email, string? userAgent, string? ipAddress);
        Task<(bool success, UserProfile? user, string? errorMessage)> ValidateMagicLinkAsync(string token);
        Task<bool> MarkMagicLinkAsUsedAsync(string token);
    }
}

