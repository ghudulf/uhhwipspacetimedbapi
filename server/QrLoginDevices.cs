#pragma warning disable STDB_UNSTABLE
using SpacetimeDB;
using System.Linq;

public static partial class Module
{
    [SpacetimeDB.Table(Public = true)]
    public partial class QrLoginDevice
    {
        [PrimaryKey] public string DeviceId = "";
        [SpacetimeDB.Index.BTree] public Identity UserId;
        public string PublicKey = "";
        public string DeviceName = "";
        public string Platform = "";
        public ulong CreatedAt;
        public ulong? LastUsedAt;
        public ulong? RevokedAt;
        [SpacetimeDB.Default(0u), SpacetimeDB.Index.BTree] public uint ApiAccessPartition;
    }

    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter QrLoginDeviceAccess = new Filter.Sql(
        "SELECT QrLoginDevice.* FROM QrLoginDevice JOIN TrustedApiIdentity ON QrLoginDevice.ApiAccessPartition = TrustedApiIdentity.ApiAccessPartition WHERE TrustedApiIdentity.Identity = :sender");

    [SpacetimeDB.Reducer]
    public static void RegisterQrLoginDevice(ReducerContext ctx, string deviceId, string publicKey, string deviceName, string platform, Identity actingUser)
    {
        RequireTrustedApi(ctx);
        var user = ResolveEffectiveUser(ctx, actingUser);
        if (ctx.Db.UserProfile.UserId.Find(user)?.IsActive != true) throw new Exception("Active account required.");
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 64 || string.IsNullOrWhiteSpace(publicKey) || publicKey.Length > 256 || deviceName.Length > 64 || platform.Length > 64)
            throw new Exception("Invalid device.");
        if (ctx.Db.QrLoginDevice.DeviceId.Find(deviceId) != null) throw new Exception("device_exists");
        if (ctx.Db.QrLoginDevice.UserId.Filter(user).Count(d => d.RevokedAt == null) >= 5) throw new Exception("device_limit");
        ctx.Db.QrLoginDevice.Insert(new QrLoginDevice { DeviceId = deviceId, UserId = user, PublicKey = publicKey,
            DeviceName = deviceName, Platform = platform, CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 });
    }

    [SpacetimeDB.Reducer]
    public static void RevokeQrLoginDevice(ReducerContext ctx, string deviceId, Identity actingUser)
    {
        RequireTrustedApi(ctx);
        var user = ResolveEffectiveUser(ctx, actingUser);
        if (ctx.Db.UserProfile.UserId.Find(user)?.IsActive != true) throw new Exception("Active account required.");
        var device = ctx.Db.QrLoginDevice.DeviceId.Find(deviceId);
        if (device == null || device.UserId != user) throw new Exception("Device not found.");
        if (device.RevokedAt != null) return;
        device.RevokedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ctx.Db.QrLoginDevice.DeviceId.Update(device);
    }

    [SpacetimeDB.Reducer]
    public static void TouchQrLoginDevice(ReducerContext ctx, string deviceId)
    {
        RequireTrustedApi(ctx);
        var device = ctx.Db.QrLoginDevice.DeviceId.Find(deviceId);
        if (device == null || device.RevokedAt != null) return;
        device.LastUsedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ctx.Db.QrLoginDevice.DeviceId.Update(device);
    }
}
