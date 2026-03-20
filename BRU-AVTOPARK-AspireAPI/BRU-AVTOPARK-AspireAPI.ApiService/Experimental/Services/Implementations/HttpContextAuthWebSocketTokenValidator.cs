using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text.Json;
using BRU_AVTOPARK.Services.Interfaces;
using TicketSalesApp.AdminServer.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Validates bearer tokens for the WebSocket auth service using the current HTTP context.
/// Routes JWE (5-segment) tokens to the local /connect/tokeninfo endpoint and validates
/// plain JWT (3-segment) tokens locally via symmetric signature verification.
/// Mirrors the logic in BaseController.ValidateTokenDirectAsync without inheriting from it.
/// </summary>
public sealed class HttpContextAuthWebSocketTokenValidator : IAuthWebSocketTokenValidator
{
    private const int TokenInfoTimeoutSeconds = 10;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<JwtSettings>? _jwtOptions;
    private readonly IConfiguration _config;
    private readonly ILogger<HttpContextAuthWebSocketTokenValidator> _logger;

    public HttpContextAuthWebSocketTokenValidator(
        IHttpContextAccessor httpContextAccessor,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<HttpContextAuthWebSocketTokenValidator> logger,
        IOptions<JwtSettings>? jwtOptions = null)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jwtOptions = jwtOptions;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, object>?> ValidateTokenDirectAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 20)
        {
            _logger.LogWarning("ValidateTokenDirectAsync - Token too short or empty");
            return null;
        }

        try
        {
            var parts = token.Split('.');
            if (parts.Length == 5)
                return await ValidateJweAsync(token, cancellationToken);
            if (parts.Length == 3)
                return ValidateJwt(token);

            _logger.LogWarning("ValidateTokenDirectAsync - Invalid token format: {Count} segments", parts.Length);
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ValidateTokenDirectAsync - Validation cancelled");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ValidateTokenDirectAsync - Unexpected error");
            return null;
        }
    }

    // ── JWE: call /connect/tokeninfo ──────────────────────────────────────────

    private async Task<Dictionary<string, object>?> ValidateJweAsync(string token, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            _logger.LogError("ValidateJweAsync - No HttpContext available");
            return null;
        }

        var httpClient = _httpClientFactory.CreateClient("TokenInfo");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TokenInfoTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase.ToUriComponent()}";
        var tokeninfoUrl = $"{baseUrl}/connect/tokeninfo";

        _logger.LogDebug("ValidateJweAsync - GET {Url}", tokeninfoUrl);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(tokeninfoUrl, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("ValidateJweAsync - tokeninfo call timed out");
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(linkedCts.Token);
                _logger.LogWarning("ValidateJweAsync - tokeninfo returned {Status}: {Body}", (int)response.StatusCode, errorBody);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(linkedCts.Token);

            JsonElement tokenInfo;
            try { tokenInfo = JsonSerializer.Deserialize<JsonElement>(content); }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "ValidateJweAsync - Failed to parse tokeninfo JSON");
                return null;
            }

            if (!tokenInfo.TryGetProperty("claims", out var claimsElement))
            {
                _logger.LogWarning("ValidateJweAsync - Response has no 'claims' property");
                return null;
            }

            var claims = ExtractClaims(claimsElement);

            // Defence-in-depth expiry check
            if (claims.TryGetValue("exp", out var expObj) && long.TryParse(expObj?.ToString(), out var expUnix))
            {
                if (DateTimeOffset.FromUnixTimeSeconds(expUnix) < DateTimeOffset.UtcNow)
                {
                    _logger.LogWarning("ValidateJweAsync - Token is expired");
                    return null;
                }
            }

            _logger.LogInformation("ValidateJweAsync - Extracted {Count} claims from JWE tokeninfo", claims.Count);
            return claims;
        }
    }

    // ── JWT: local signature validation ───────────────────────────────────────

    private Dictionary<string, object>? ValidateJwt(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        if (!tokenHandler.CanReadToken(token))
        {
            _logger.LogWarning("ValidateJwt - Cannot read token (malformed)");
            return null;
        }

        var jwtOpts = _jwtOptions?.Value;
        var secret = jwtOpts?.Secret ?? _config["JwtSettings:Secret"];

        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogError("ValidateJwt - JwtSettings:Secret not configured");
            return null;
        }

        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret));
        var clockSkew = TimeSpan.FromMinutes(jwtOpts?.ClockSkewMinutes ?? 5);

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = jwtOpts?.ValidateIssuer ?? false,
            ValidIssuer = jwtOpts?.Issuer ?? _config["JwtSettings:Issuer"],
            ValidateAudience = jwtOpts?.ValidateAudience ?? false,
            ValidAudience = jwtOpts?.Audience ?? _config["JwtSettings:Audience"],
            ValidateLifetime = true,
            RequireExpirationTime = jwtOpts?.RequireExpiration ?? true,
            ClockSkew = clockSkew
        };

        try
        {
            tokenHandler.ValidateToken(token, validationParams, out var validatedToken);
            if (validatedToken is not JwtSecurityToken jwt)
            {
                _logger.LogWarning("ValidateJwt - Validated token is not a JwtSecurityToken");
                return null;
            }

            var claims = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var claim in jwt.Claims)
            {
                if (claims.ContainsKey(claim.Type))
                {
                    // Merge multi-value claims into a list
                    if (claims[claim.Type] is List<string> list)
                        list.Add(claim.Value);
                    else
                        claims[claim.Type] = new List<string> { claims[claim.Type].ToString()!, claim.Value };
                }
                else
                {
                    claims[claim.Type] = claim.Value;
                }
            }

            _logger.LogDebug("ValidateJwt - Extracted {Count} claims from JWT", claims.Count);
            return claims;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("ValidateJwt - Token validation failed: {Msg}", ex.Message);
            return null;
        }
    }

    // ── Claim extraction helper ───────────────────────────────────────────────

    private static Dictionary<string, object> ExtractClaims(JsonElement claimsElement)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in claimsElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => (object)(prop.Value.GetString() ?? string.Empty),
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => prop.Value.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText())
                    .ToList<object>(),
                _ => prop.Value.GetRawText()
            };
        }

        return result;
    }
}
