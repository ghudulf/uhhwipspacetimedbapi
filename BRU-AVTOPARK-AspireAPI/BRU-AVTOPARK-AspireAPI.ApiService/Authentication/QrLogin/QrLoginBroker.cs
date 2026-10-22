using System.Security.Cryptography;
using System.Text;
using BRU.Avtopark.Contracts;
using Microsoft.Extensions.Options;
using SpacetimeDB;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

public enum QrState { Pending, Scanned, Approved, Denied, Expired, Cancelled, Redeemed }
public enum QrApprovedVia { None, BearerApproval, DeviceKey }
public sealed record QrRedemption(Identity UserId, bool ApprovedByAuthenticatedSession);
public sealed record QrChallenge(string Id, string Origin, string? Username, string? DeviceId, bool Decoy);
public sealed class QrLoginException(string code, int status = 400, int? attemptsRemaining = null) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public int? AttemptsRemaining { get; } = attemptsRemaining;
}

/// <summary>Process-local, bounded, secret-hashed sessions. A single lock serializes all transitions.</summary>
public sealed class QrLoginBroker(TimeProvider time, IOptions<QrLoginOptions> options)
{
    private sealed class Session
    {
        public required string Id, VerificationCode, DeviceName, Platform, RequestedFromIp, Origin;
        public required byte[] PollSecretHash, ApprovalSecretHash;
        public string? BoundUsername, BoundDeviceId;
        public bool Decoy, Redeemed;
        public QrState State;
        public DateTimeOffset CreatedAt, ExpiresAt;
        public int CodeAttemptsRemaining = 3;
        public Identity? ApproverUserId;
        public QrApprovedVia ApprovedVia;
        public readonly Queue<DateTimeOffset> Polls = new();
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessions = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> creations = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> usernames = new(StringComparer.OrdinalIgnoreCase);
    public static string Sanitize(string? value, int max) => new string((value ?? "").Trim().Where(c => !char.IsControl(c)).Take(max).ToArray());
    private static byte[] Hash(string? value) => SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""));
    private static bool Matches(byte[] hash, string? value) => CryptographicOperations.FixedTimeEquals(hash, Hash(value));
    private static bool Live(Session s) => s.State is QrState.Pending or QrState.Scanned or QrState.Approved;
    private void Sweep()
    {
        var now = time.GetUtcNow();
        foreach (var s in sessions.Values) if (now >= s.ExpiresAt && Live(s)) s.State = QrState.Expired;
        foreach (var id in sessions.Where(p => now >= p.Value.ExpiresAt.AddMinutes(1)).Select(p => p.Key).ToArray()) sessions.Remove(id);
        foreach (var dictionary in new[] { creations, usernames })
            foreach (var key in dictionary.Keys.ToArray())
            {
                Trim(dictionary[key]);
                if (dictionary[key].Count == 0) dictionary.Remove(key);
            }
    }
    private void Trim(Queue<DateTimeOffset> queue) { while (queue.TryPeek(out var oldest) && oldest <= time.GetUtcNow().AddMinutes(-1)) queue.Dequeue(); }
    private void Rate(Dictionary<string, Queue<DateTimeOffset>> entries, string key, int max)
    {
        if (!entries.TryGetValue(key, out var queue))
        {
            if (entries.Count >= options.Value.MaxSessions * 10) throw new QrLoginException("rate_limited", 429);
            entries[key] = queue = new();
        }
        Trim(queue);
        if (queue.Count >= max) throw new QrLoginException("rate_limited", 429);
        queue.Enqueue(time.GetUtcNow());
    }
    public QrCreateResponse Create(QrCreateRequest request, string address, string origin, bool decoy = false)
    {
        lock (gate)
        {
            Sweep();
            if (!QrLoginProtocol.ValidOrigin(origin) || request.Username?.Length > 128 || request.DeviceId?.Length > 64) throw new QrLoginException("invalid_request");
            Rate(creations, address, 10);
            var username = request.Username?.Trim().ToLowerInvariant();
            if (username != null) { if (username.Length == 0 || username.Any(char.IsControl)) throw new QrLoginException("invalid_request"); Rate(usernames, username, 5); }
            if (sessions.Values.Count(Live) >= options.Value.MaxSessions || sessions.Count >= options.Value.MaxSessions * 4) throw new QrLoginException("capacity", 429);
            if (sessions.Values.Count(s => Live(s) && s.RequestedFromIp == address) >= options.Value.MaxSessionsPerAddress) throw new QrLoginException("address_limit", 429);
            var id = QrLoginProtocol.Base64Url(RandomNumberGenerator.GetBytes(16));
            var poll = QrLoginProtocol.Base64Url(RandomNumberGenerator.GetBytes(32));
            var approval = QrLoginProtocol.Base64Url(RandomNumberGenerator.GetBytes(32));
            var code = RandomNumberGenerator.GetInt32(1000000).ToString("D6");
            var session = new Session { Id = id, PollSecretHash = Hash(poll), ApprovalSecretHash = Hash(approval), VerificationCode = code,
                DeviceName = Sanitize(request.DeviceName, 64), Platform = Sanitize(request.Platform, 32), RequestedFromIp = address, Origin = origin,
                BoundUsername = username, BoundDeviceId = request.DeviceId, Decoy = decoy, CreatedAt = time.GetUtcNow(), ExpiresAt = time.GetUtcNow().AddSeconds(options.Value.SessionSeconds) };
            sessions.Add(id, session);
            return new(id, poll, approval, QrLoginProtocol.BuildPayload(id, approval, origin), "", code, session.ExpiresAt, 1000);
        }
    }
    private Session Get(string id, string? secret, bool poll = false)
    {
        Sweep();
        if (!sessions.TryGetValue(id, out var session) || !Matches(poll ? session.PollSecretHash : session.ApprovalSecretHash, secret)) throw new QrLoginException("not_found", 404);
        return session;
    }
    public QrPeekResponse Peek(string id, string? secret)
    {
        lock (gate)
        {
            var s = Get(id, secret);
            if (s.State == QrState.Pending) s.State = QrState.Scanned;
            return new(s.DeviceName, s.Platform, s.CreatedAt, s.ExpiresAt, QrLoginProtocol.MaskIp(s.RequestedFromIp), s.Origin, s.BoundUsername, s.CodeAttemptsRemaining);
        }
    }
    public string Status(string id, string? secret, out QrRedemption? redemption)
    {
        lock (gate)
        {
            var s = Get(id, secret, true);
            Trim(s.Polls);
            if (s.Polls.Count >= 90) throw new QrLoginException("rate_limited", 429);
            s.Polls.Enqueue(time.GetUtcNow());
            redemption = null;
            if (s.State == QrState.Approved && !s.Redeemed && s.ApproverUserId is { } user)
            {
                s.Redeemed = true;
                s.State = QrState.Redeemed;
                redemption = new(user, s.ApprovedVia == QrApprovedVia.BearerApproval);
                return "completed";
            }
            return s.State == QrState.Redeemed ? "consumed" : s.State.ToString().ToLowerInvariant();
        }
    }
    public void Cancel(string id, string? secret) { lock (gate) { var s = Get(id, secret, true); if (Live(s)) s.State = QrState.Cancelled; } }
    public void Deny(string id, string? secret) { lock (gate) { var s = Get(id, secret); if (s.State is QrState.Pending or QrState.Scanned) s.State = QrState.Denied; } }
    private static void Attempt(Session s) { if (--s.CodeAttemptsRemaining <= 0) s.State = QrState.Denied; }
    public void Approve(string id, string? secret, string? code, Identity user, string username)
    {
        lock (gate)
        {
            var s = Get(id, secret);
            if (s.State is not (QrState.Pending or QrState.Scanned)) throw new QrLoginException("invalid_state", 409);
            if (s.BoundUsername != null && !string.Equals(s.BoundUsername, username, StringComparison.OrdinalIgnoreCase)) throw new QrLoginException("wrong_account", 403);
            if (!Matches(Hash(s.VerificationCode), code)) { Attempt(s); throw new QrLoginException("verification_failed", 400, s.CodeAttemptsRemaining); }
            if (s.Decoy) throw new QrLoginException("qr_login_failed", 401);
            s.State = QrState.Approved; s.ApproverUserId = user; s.ApprovedVia = QrApprovedVia.BearerApproval;
        }
    }
    // Snapshot is secret-free. Complete rechecks every mutable condition under the same lock.
    public QrChallenge? Challenge(string id)
    {
        lock (gate) { Sweep(); return sessions.TryGetValue(id, out var s) ? new(s.Id, s.Origin, s.BoundUsername, s.BoundDeviceId, s.Decoy) : null; }
    }
    public bool Complete(string id, string secret, string code, string deviceId, Identity? user, bool signatureValid)
    {
        lock (gate)
        {
            Sweep();
            if (!sessions.TryGetValue(id, out var s)) return false;
            var secretValid = Matches(s.ApprovalSecretHash, secret);
            var codeValid = Matches(Hash(s.VerificationCode), code);
            if (s.State is not (QrState.Pending or QrState.Scanned)) return false;
            if (!secretValid || !codeValid || !signatureValid || user == null || s.Decoy || s.BoundUsername == null || (s.BoundDeviceId != null && s.BoundDeviceId != deviceId)) { Attempt(s); return false; }
            s.State = QrState.Approved; s.ApproverUserId = user; s.ApprovedVia = QrApprovedVia.DeviceKey;
            return true;
        }
    }
}
