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
/// Validates JWE (5-segment) tokens through the local OpenIddict pipeline and validates
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
            _logger.LogDebug("ValidateTokenDirectAsync - Validation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("ValidateTokenDirectAsync - Failure type {ErrorType}", ex.GetType().Name);
            return null;
        }
    }

    // ── JWE: call /connect/tokeninfo ──────────────────────────────────────────

    private async Task<Dictionary<string, object>?> ValidateJweAsync(string token, CancellationToken cancellationToken)
    {
        var services = _httpContextAccessor.HttpContext?.RequestServices
            ?? throw new InvalidOperationException("Request services unavailable.");
        var claims = await BRU_AVTOPARK_AspireAPI.ApiService.Authentication.LocalTokenValidation.ValidateAsync(services, token, cancellationToken);
        _logger.LogDebug("Encrypted socket token validated locally; claim count {Count}", claims.Count);
        return claims;
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
            _logger.LogWarning("ValidateJwt - Rejected token; failure type {ErrorType}", ex.GetType().Name);
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
