using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

public sealed class EntraOptions
{
    public bool Enabled { get; set; }
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }
    public string? Authority { get; set; }
    public bool AllowMultiTenant { get; set; }
    public string[] AllowedTenants { get; set; } = [];
    public string AuthorityUrl => Authority ?? $"https://login.microsoftonline.com/{TenantId}/v2.0";
    public HashSet<string> Tenants => (Guid.TryParse(TenantId, out var tenant) ? new[] { tenant.ToString() } : AllowedTenants.Select(value => Guid.Parse(value).ToString())).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public static bool HttpsUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
}

public sealed class EntraOptionsValidator(IHostEnvironment environment) : IValidateOptions<EntraOptions>
{
    public ValidateOptionsResult Validate(string? name, EntraOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        if (!Guid.TryParse(options.ClientId, out _) || options.AllowedTenants.Any(value => !Guid.TryParse(value, out _)) ||
            (!Guid.TryParse(options.TenantId, out _) && !(options.AllowMultiTenant && options.TenantId is "common" or "organizations" or "consumers" && options.AllowedTenants.Length > 0)))
            return ValidateOptionsResult.Fail("Entra requires a GUID ClientId and an explicit tenant GUID or an explicitly allowed multi-tenant set.");
        if (!EntraOptions.HttpsUri(options.AuthorityUrl) || new Uri(options.AuthorityUrl).Query.Length != 0)
            return ValidateOptionsResult.Fail("Entra Authority must be an HTTPS URL without credentials, query or fragment.");
        if (options.RedirectUri != null && !SafeRedirect(options.RedirectUri, environment.IsDevelopment()))
            return ValidateOptionsResult.Fail("Entra RedirectUri must be HTTPS (HTTP loopback only in Development) without credentials, query or fragment.");
        return ValidateOptionsResult.Success;
    }
    public static bool SafeRedirect(string value, bool development) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "https" || development && uri.Scheme == "http" && uri.IsLoopback) && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
}

public sealed record EntraStartResponse(string FlowId, string FlowSecret, string AuthorizationUrl, DateTimeOffset ExpiresAt, int PollIntervalMs);

/// <summary>All IdP I/O is HTTPS, bounded, redirect-free and isolated from application credentials.</summary>
public sealed class EntraAuthenticationService
{
    private readonly EntraOptions options;
    private readonly ProviderFlowBroker broker;
    private readonly HttpClient client;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> discovery;
    private readonly IDocumentRetriever documents;
    private readonly string metadataAddress;
    public EntraAuthenticationService(IOptions<EntraOptions> options, ProviderFlowBroker broker, IHttpClientFactory clients)
    {
        this.options = options.Value;
        this.broker = broker;
        client = clients.CreateClient("entra");
        documents = new BoundedDocumentRetriever(client, new Uri(this.options.AuthorityUrl));
        metadataAddress = this.options.AuthorityUrl.TrimEnd('/') + "/.well-known/openid-configuration";
        discovery = new(metadataAddress, new OpenIdConnectConfigurationRetriever(), documents);
    }
    public async Task<EntraStartResponse> StartAsync(string purpose, SpacetimeDB.Identity? actor, string address, string redirectUri, CancellationToken cancellationToken)
    {
        var metadata = await discovery.GetConfigurationAsync(cancellationToken);
        CheckEndpoint(metadata.AuthorizationEndpoint);
        var state = ProviderFlowBroker.RandomSecret();
        var nonce = ProviderFlowBroker.RandomSecret();
        var verifier = ProviderFlowBroker.RandomSecret();
        var flow = broker.Create("entra", purpose, actor, address, state, nonce, verifier, redirectUri);
        var url = QueryHelpers.AddQueryString(metadata.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId, ["response_type"] = "code", ["redirect_uri"] = redirectUri,
            ["scope"] = "openid profile email", ["state"] = state, ["nonce"] = nonce,
            ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256", ["response_mode"] = "query", ["prompt"] = "select_account"
        });
        return new(flow.FlowId, flow.FlowSecret, url, flow.ExpiresAt, flow.PollIntervalMs);
    }
    public async Task<bool> CallbackAsync(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        var proof = broker.ConsumeState(state);
        if (proof == null) return false;
        try
        {
            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || code.Length > 4096) throw new SecurityTokenException();
            var metadata = await discovery.GetConfigurationAsync(cancellationToken);
            CheckEndpoint(metadata.TokenEndpoint);
            var fields = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = options.ClientId, ["code"] = code,
                ["redirect_uri"] = proof.RedirectUri, ["code_verifier"] = proof.Verifier
            };
            if (!string.IsNullOrEmpty(options.ClientSecret)) fields["client_secret"] = options.ClientSecret;
            using var request = new HttpRequestMessage(HttpMethod.Post, metadata.TokenEndpoint) { Content = new FormUrlEncodedContent(fields) };
            var json = await BoundedDocumentRetriever.SendAsync(client, request, 64 * 1024, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var token = document.RootElement.GetProperty("id_token").GetString();
            if (string.IsNullOrEmpty(token) || token.Length > 32 * 1024) throw new SecurityTokenException();
            ProviderIdentity identity;
            try { identity = Validate(token, proof.Nonce, metadata); }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                discovery.RequestRefresh();
                // ConfigurationManager can return its last-known-good configuration while refreshing in
                // the background. One bounded foreground retrieval allows a genuine signing-key rotation
                // to succeed immediately without accepting a token against stale or unverified keys.
                identity = Validate(token, proof.Nonce, await OpenIdConnectConfigurationRetriever.GetAsync(metadataAddress, documents, cancellationToken));
            }
            return broker.Authenticate(proof.FlowId, "entra", identity);
        }
        catch { broker.Fail(proof.FlowId); return false; } // Never log IdP payloads or exception messages.
    }
    private void CheckEndpoint(string endpoint)
    {
        if (!EntraOptions.HttpsUri(endpoint) || new Uri(endpoint).Authority != new Uri(options.AuthorityUrl).Authority) throw new SecurityTokenException();
    }
    private ProviderIdentity Validate(string token, string nonce, OpenIdConnectConfiguration metadata)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 32 * 1024 };
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            RequireSignedTokens = true, RequireExpirationTime = true, ValidateIssuerSigningKey = true,
            IssuerSigningKeys = metadata.SigningKeys, TryAllIssuerSigningKeys = false, ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateAudience = true, ValidAudience = options.ClientId, ValidateIssuer = true,
            IssuerValidator = (issuer, securityToken, _) =>
            {
                var tid = ((JwtSecurityToken)securityToken).Claims.FirstOrDefault(claim => claim.Type == "tid")?.Value;
                if (!Guid.TryParse(tid, out var tenant) || !options.Tenants.Contains(tenant.ToString()) || issuer != $"https://login.microsoftonline.com/{tenant}/v2.0") throw new SecurityTokenInvalidIssuerException();
                return issuer;
            },
            ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(2)
        }, out var validated);
        string One(string type)
        {
            var values = principal.FindAll(type).ToArray();
            return values.Length == 1 ? values[0].Value : throw new SecurityTokenException();
        }
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(One("nonce")), Encoding.UTF8.GetBytes(nonce)) ||
            !Guid.TryParse(One("oid"), out var subject) || !Guid.TryParse(One("tid"), out _)) throw new SecurityTokenException();
        return new(validated.Issuer, subject.ToString());
    }

    private sealed class BoundedDocumentRetriever(HttpClient client, Uri authority) : IDocumentRetriever
    {
        public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
        {
            // Discovery cannot redirect JWKS retrieval to an arbitrary host or local network service.
            if (!EntraOptions.HttpsUri(address) || new Uri(address).Authority != authority.Authority) throw new SecurityTokenException();
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            return await SendAsync(client, request, 256 * 1024, cancel);
        }
        public static async Task<string> SendAsync(HttpClient client, HttpRequestMessage request, int maxBytes, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes) throw new SecurityTokenException();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, timeout.Token)) != 0)
            {
                if (buffer.Length + read > maxBytes) throw new SecurityTokenException();
                buffer.Write(bytes, 0, read);
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
