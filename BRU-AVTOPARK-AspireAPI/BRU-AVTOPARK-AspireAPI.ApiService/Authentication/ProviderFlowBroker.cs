using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SpacetimeDB;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

public sealed class ProviderFlowOptions
{
    private int seconds = 300;
    public int FlowSeconds { get => seconds; set => seconds = Math.Clamp(value, 60, 600); }
}

public enum ProviderFlowState { Pending, Authenticated, Redeemed, Failed, Expired, Cancelled }
public sealed record ProviderFlowStart(string FlowId, string FlowSecret, DateTimeOffset ExpiresAt, int PollIntervalMs);
public sealed record ProviderIdentity(string Issuer, string Subject);
public sealed record ProviderRedemption(string Purpose, Identity? Actor, ProviderIdentity? ExternalIdentity, string? CredentialId);
public sealed record ProviderOidcProof(string FlowId, string Nonce, string Verifier, string RedirectUri);
public sealed class ProviderFlowException(string code, int status = 400) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

/// <summary>Process-local, bounded provider ceremonies. All state transitions and redemption are serialized.</summary>
public sealed class ProviderFlowBroker(TimeProvider time, IOptions<ProviderFlowOptions> options)
{
    private sealed class Flow
    {
        public required string Id, Provider, Purpose, Address;
        public required byte[] SecretHash;
        public required DateTimeOffset ExpiresAt;
        public Identity? Actor;
        public ProviderFlowState State;
        public byte[]? StateHash;
        public ProviderOidcProof? OidcProof;
        public ProviderIdentity? ExternalIdentity;
        public string? CredentialId;
        public readonly Queue<DateTimeOffset> Polls = new();
    }
    private const int Capacity = 2000;
    private readonly object gate = new();
    private readonly Dictionary<string, Flow> flows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<DateTimeOffset>> creations = new(StringComparer.Ordinal);
    public static string RandomSecret(int bytes = 32) => Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(bytes));
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static bool Matches(byte[] hash, string? value) => value is { Length: <= 128 } && CryptographicOperations.FixedTimeEquals(hash, Hash(value));
    private static bool Live(Flow flow) => flow.State is ProviderFlowState.Pending or ProviderFlowState.Authenticated;
    private void Trim(Queue<DateTimeOffset> queue)
    {
        while (queue.TryPeek(out var oldest) && oldest <= time.GetUtcNow().AddMinutes(-1)) queue.Dequeue();
    }
    private static void ClearProof(Flow flow) { flow.StateHash = null; flow.OidcProof = null; }
    private void Sweep()
    {
        var now = time.GetUtcNow();
        foreach (var flow in flows.Values)
            if (now >= flow.ExpiresAt && Live(flow)) { flow.State = ProviderFlowState.Expired; ClearProof(flow); }
        foreach (var key in flows.Where(pair => now >= pair.Value.ExpiresAt.AddMinutes(1)).Select(pair => pair.Key).ToArray()) flows.Remove(key);
        foreach (var key in creations.Keys.ToArray())
        {
            Trim(creations[key]);
            if (creations[key].Count == 0) creations.Remove(key);
        }
    }
    public ProviderFlowStart Create(string provider, string purpose, Identity? actor, string address,
        string? state = null, string? nonce = null, string? verifier = null, string? redirectUri = null)
    {
        lock (gate)
        {
            Sweep();
            if (provider is not ("entra" or "passkey") || purpose is not ("login" or "link" or "register") ||
                (purpose != "login" && actor == null) || address.Length > 128 ||
                (provider == "entra" && (purpose == "register" || state == null || nonce == null || verifier == null || redirectUri == null)) ||
                (provider == "passkey" && purpose == "link")) throw new ProviderFlowException("invalid_request");
            if (!creations.TryGetValue(address, out var queue))
            {
                if (creations.Count >= Capacity * 10) throw new ProviderFlowException("rate_limited", 429);
                creations[address] = queue = new();
            }
            if (queue.Count >= 10) throw new ProviderFlowException("rate_limited", 429);
            queue.Enqueue(time.GetUtcNow());
            if (flows.Values.Count(Live) >= Capacity || flows.Count >= Capacity * 4) throw new ProviderFlowException("capacity", 429);
            if (flows.Values.Count(flow => Live(flow) && flow.Address == address) >= 5) throw new ProviderFlowException("address_limit", 429);
            var id = RandomSecret(16);
            var secret = RandomSecret();
            var flow = new Flow { Id = id, Provider = provider, Purpose = purpose, Actor = actor, Address = address,
                SecretHash = Hash(secret), ExpiresAt = time.GetUtcNow().AddSeconds(options.Value.FlowSeconds),
                StateHash = state == null ? null : Hash(state),
                OidcProof = state == null ? null : new(id, nonce!, verifier!, redirectUri!) };
            flows.Add(id, flow);
            return new(id, secret, flow.ExpiresAt, 1000);
        }
    }
    /// <summary>Consumes state before any IdP request. Unknown, terminal and replayed states are indistinguishable.</summary>
    public ProviderOidcProof? ConsumeState(string? state)
    {
        lock (gate)
        {
            Sweep();
            if (state is not { Length: <= 128 }) return null;
            var flow = flows.Values.FirstOrDefault(flow => flow.Provider == "entra" && flow.State == ProviderFlowState.Pending && flow.StateHash != null && Matches(flow.StateHash, state));
            if (flow == null) return null;
            var proof = flow.OidcProof;
            ClearProof(flow);
            return proof;
        }
    }
    public bool Authenticate(string id, string provider, ProviderIdentity? identity = null, string? credentialId = null)
    {
        lock (gate)
        {
            Sweep();
            if (!flows.TryGetValue(id, out var flow) || flow.Provider != provider || flow.State != ProviderFlowState.Pending) return false;
            if (provider == "entra" && (identity == null || flow.StateHash != null)) return false;
            flow.ExternalIdentity = identity;
            flow.CredentialId = credentialId;
            flow.State = ProviderFlowState.Authenticated;
            ClearProof(flow);
            return true;
        }
    }
    public void Fail(string id)
    {
        lock (gate)
        {
            Sweep();
            if (flows.TryGetValue(id, out var flow) && flow.State == ProviderFlowState.Pending) { flow.State = ProviderFlowState.Failed; ClearProof(flow); }
        }
    }
    private Flow Get(string id, string provider, string? secret)
    {
        Sweep();
        if (!flows.TryGetValue(id, out var flow) || flow.Provider != provider || !Matches(flow.SecretHash, secret)) throw new ProviderFlowException("not_found", 404);
        return flow;
    }
    public string Complete(string id, string provider, string? secret, out ProviderRedemption? redemption)
    {
        lock (gate)
        {
            var flow = Get(id, provider, secret);
            Trim(flow.Polls);
            if (flow.Polls.Count >= 90) throw new ProviderFlowException("rate_limited", 429);
            flow.Polls.Enqueue(time.GetUtcNow());
            redemption = null;
            if (flow.State == ProviderFlowState.Authenticated)
            {
                flow.State = ProviderFlowState.Redeemed;
                redemption = new(flow.Purpose, flow.Actor, flow.ExternalIdentity, flow.CredentialId);
                flow.ExternalIdentity = null;
                flow.CredentialId = null;
                return "completed";
            }
            return flow.State == ProviderFlowState.Redeemed ? "consumed" : flow.State.ToString().ToLowerInvariant();
        }
    }
    public void Cancel(string id, string provider, string? secret)
    {
        lock (gate) { var flow = Get(id, provider, secret); if (Live(flow)) { flow.State = ProviderFlowState.Cancelled; ClearProof(flow); } }
    }
}
