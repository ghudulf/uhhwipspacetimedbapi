using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations;

public sealed class QrLoginDeviceStore(ISpacetimeDBService database) : IQrLoginDeviceStore
{
    public Task<UserProfile?> FindUserAsync(string username) => database.ReadAsync(c => c.Db.UserProfile.Login.Find(username));
    public Task<QrLoginDevice?> FindAsync(Identity userId, string deviceId) => database.ReadAsync(c =>
    {
        var device = c.Db.QrLoginDevice.DeviceId.Find(deviceId);
        return c.Db.UserProfile.UserId.Find(userId)?.IsActive == true && device?.UserId == userId && device.RevokedAt == null ? device : null;
    });
    public Task<IReadOnlyList<QrLoginDevice>> ListAsync(Identity userId) => database.ReadAsync<IReadOnlyList<QrLoginDevice>>(c =>
        c.Db.UserProfile.UserId.Find(userId)?.IsActive == true
            ? c.Db.QrLoginDevice.Iter().Where(d => d.UserId == userId && d.RevokedAt == null).ToArray() : Array.Empty<QrLoginDevice>());
    public async Task RegisterAsync(Identity userId, string deviceId, string publicKey, string deviceName, string platform) =>
        _ = await database.ExecuteReducerAsync(new Reducer.RegisterQrLoginDevice(deviceId, publicKey, deviceName, platform, userId), _ => true);
    public async Task RevokeAsync(Identity userId, string deviceId) =>
        _ = await database.ExecuteReducerAsync(new Reducer.RevokeQrLoginDevice(deviceId, userId), _ => true);
    public async Task TouchAsync(string deviceId) =>
        _ = await database.ExecuteReducerAsync(new Reducer.TouchQrLoginDevice(deviceId), _ => true);
}
