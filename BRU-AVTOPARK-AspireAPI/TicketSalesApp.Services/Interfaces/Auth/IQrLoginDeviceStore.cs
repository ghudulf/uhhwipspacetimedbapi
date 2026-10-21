using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces;

public interface IQrLoginDeviceStore
{
    Task<UserProfile?> FindUserAsync(string username);
    Task<QrLoginDevice?> FindAsync(Identity userId, string deviceId);
    Task<IReadOnlyList<QrLoginDevice>> ListAsync(Identity userId);
    Task RegisterAsync(Identity userId, string deviceId, string publicKey, string deviceName, string platform);
    Task RevokeAsync(Identity userId, string deviceId);
    Task TouchAsync(string deviceId);
}
