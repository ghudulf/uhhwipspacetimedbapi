using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SpacetimeDB.Types;
using TicketSalesApp.AdminServer.Configuration;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Centralizes all JWT token operations that were previously scattered
/// across the AuthController (GenerateJwtToken, IsAdmin, HasPermission, etc.).
/// Injected as a singleton; the signing key is loaded once from configuration.
///
/// Token generation guarantees:
///   - "exp" (expiration) is always present, controlled by JwtSettings.ExpirationInMinutes.
///   - "nbf" (not-before) is always present, set to (now - JwtSettings.NotBeforeOffsetSeconds)
///     so tokens are valid immediately while providing a small grace window for clock skew.
///   - "iat" (issued-at) is always present, set to the current UTC time.
///   - Issuer and Audience are always embedded when configured.
/// </summary>
public class TokenService : ITokenService
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly ISpacetimeDBService _spacetimeService;
    private readonly ILogger<TokenService> _logger;
    private readonly JwtSettings _jwtSettings;

    public TokenService(
        IOptions<JwtSettings> jwtOptions,
        SymmetricSecurityKey signingKey,
        ISpacetimeDBService spacetimeService,
        ILogger<TokenService> logger)
    {
        _signingKey = signingKey ?? throw new ArgumentNullException(nameof(signingKey));
        _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jwtSettings = jwtOptions?.Value ?? throw new ArgumentNullException(nameof(jwtOptions));
    }

    /// <inheritdoc />
    public string GenerateToken(SpacetimeDB.Identity userId)
    {
        ArgumentNullException.ThrowIfNull(userId);

        var conn = _spacetimeService.GetConnection();
        
        // Get user profile
        var userProfile = conn.Db.UserProfile.Iter()
            .FirstOrDefault(u => u.UserId.Equals(userId) && u.IsActive);
        
        if (userProfile == null)
        {
            throw new InvalidOperationException($"User not found or inactive: {userId}");
        }

        return GenerateTokenForUser(userProfile);
    }

    /// <inheritdoc />
    public string GenerateToken(UserTokenPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var tokenHandler = new JwtSecurityTokenHandler();
        var claims = BuildAuthControllerCompatibleClaims(payload);

        // Create and sign token using centralized helper
        var (token, expires, notBefore) = CreateSignedToken(tokenHandler, claims);

        _logger.LogInformation(
            "Generated JWT token from payload for user {Username} with {RoleCount} roles and {PermissionCount} permissions (exp={Exp}, nbf={Nbf})",
            payload.Username, payload.Roles.Count, payload.Permissions.Count,
            expires.ToString("o"), notBefore.ToString("o"));

        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Builds a claim set that is compatible with AuthController's GenerateTokenForUser output.
    /// Includes sub, identity, xuid, jti, iat, legacy role claims, and permission claims.
    /// </summary>
    private List<Claim> BuildAuthControllerCompatibleClaims(UserTokenPayload payload)
    {
        var claims = new List<Claim>
        {
            new Claim("unique_name", payload.Username),
            new Claim(ClaimTypes.Name, payload.Username),
            new Claim("sub", payload.LegacyUserId ?? payload.Identity ?? payload.UserId),
            new Claim("identity", payload.Identity ?? payload.UserId),
            new Claim("xuid", payload.Xuid ?? payload.LegacyUserId ?? payload.UserId),
            new Claim("token_usage", "access_token"),
            new Claim("oi_tkn_id", Guid.NewGuid().ToString()),
            new Claim("jti", Guid.NewGuid().ToString()),
            new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        // Add email if present
        if (!string.IsNullOrEmpty(payload.Email))
            claims.Add(new Claim(ClaimTypes.Email, payload.Email));

        // Add phone if present
        if (!string.IsNullOrEmpty(payload.PhoneNumber))
            claims.Add(new Claim("phone_number", payload.PhoneNumber));

        // Add role name claims
        foreach (var role in payload.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        // Add legacy numeric role ID claims (matches GenerateTokenForUser behavior)
        foreach (var roleId in payload.RoleIds)
            claims.Add(new Claim("role", roleId));

        // Add permission claims
        foreach (var permission in payload.Permissions)
            claims.Add(new Claim("permission", permission));

        // Add primary role
        if (payload.PrimaryRole > 0)
            claims.Add(new Claim("primary_role", payload.PrimaryRole.ToString()));

        return claims;
    }

    /// <summary>
    /// Generates JWT token matching EXACT AuthController behavior.
    /// This is the authoritative token generation method that matches AuthController line-by-line.
    /// </summary>
    private string GenerateTokenForUser(SpacetimeDB.Types.UserProfile userProfile)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var conn = _spacetimeService.GetConnection();
        
        // Get user's roles - EXACT match to AuthController lines 5016-5023
        var userRoles = conn.Db.UserRole.Iter()
            .Where(ur => ur.UserId.Equals(userProfile.UserId))
            .Select(ur => ur.RoleId)
            .ToList();
        
        // Get role details - EXACT match to AuthController lines 5025-5027
        var roles = conn.Db.Role.Iter()
            .Where(r => userRoles.Contains(r.RoleId) && r.IsActive)
            .ToList();
        
        // Get role permissions - EXACT match to AuthController lines 5029-5033
        var rolePermissions = conn.Db.RolePermission.Iter()
            .Where(rp => userRoles.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .Distinct()
            .ToList();
        
        // Get permission details - EXACT match to AuthController lines 5035-5037
        var permissions = conn.Db.Permission.Iter()
            .Where(p => rolePermissions.Contains(p.PermissionId) && p.IsActive)
            .ToList();
        
        // Create claims - EXACT match to AuthController lines 5039-5048
        var claims = new List<Claim>
        {
            new Claim("unique_name", userProfile.Login),
            new Claim(ClaimTypes.Name, userProfile.Login), // Keep for backward compatibility
            new Claim("sub", userProfile.LegacyUserId.ToString()),
            new Claim("identity", userProfile.UserId.ToString()),
            new Claim("xuid", userProfile.Xuid?.ToString() ?? ""),
            new Claim("token_usage", "access_token"), // OpenIddict expects this
            new Claim("oi_tkn_id", Guid.NewGuid().ToString()) // OpenIddict token ID
        };
        
        // Add role claims - EXACT match to AuthController lines 5050-5054
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Name));
            claims.Add(new Claim("role", role.LegacyRoleId.ToString())); // Keep legacy role ID for backward compatibility
        }
        
        // Add permission claims - EXACT match to AuthController lines 5056-5059
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission.Name));
        }
        
        // Add highest priority role for IsAdmin checks - EXACT match to AuthController lines 5061-5065
        var highestPriorityRole = roles.OrderByDescending(r => r.Priority).FirstOrDefault();
        if (highestPriorityRole != null)
        {
            claims.Add(new Claim("primary_role", highestPriorityRole.LegacyRoleId.ToString()));
        }

        // Create and sign token using centralized helper
        var (token, expires, notBefore) = CreateSignedToken(tokenHandler, claims);

        // ENHANCEMENT: Log token generation with claims summary
        _logger.LogInformation(
            "Generated JWT token for user {Username} with {RoleCount} roles and {PermissionCount} permissions (exp={Exp}, nbf={Nbf})",
            userProfile.Login, roles.Count, permissions.Count,
            expires.ToString("o"), notBefore.ToString("o"));

        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Centralizes token descriptor creation and signing logic.
    /// Ensures consistent timing (exp, nbf, iat) and credentials across all token generation paths.
    /// </summary>
    private (SecurityToken token, DateTime expires, DateTime notBefore) CreateSignedToken(
        JwtSecurityTokenHandler tokenHandler,
        List<Claim> claims)
    {
        var now = DateTime.UtcNow;
        var notBefore = now.AddSeconds(-_jwtSettings.NotBeforeOffsetSeconds);
        var expires = now.AddMinutes(_jwtSettings.ExpirationInMinutes);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = notBefore,
            IssuedAt = now,
            Expires = expires,
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return (token, expires, notBefore);
    }

    /// <inheritdoc />
    public ClaimsPrincipal? ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token)) return null;

        try
        {
            var clockSkew = TimeSpan.FromMinutes(_jwtSettings.ClockSkewMinutes);

            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = _signingKey,

                ValidateIssuer   = _jwtSettings.ValidateIssuer,
                ValidIssuer      = _jwtSettings.Issuer,

                ValidateAudience = _jwtSettings.ValidateAudience,
                ValidAudience    = _jwtSettings.Audience,

                // Enable lifetime validation; all exp/nbf logic is handled by the custom
                // LifetimeValidator below which provides enhanced clock-skew and nbf checking.
                ValidateLifetime      = true,
                RequireExpirationTime = _jwtSettings.RequireExpiration,

                ClockSkew = TimeSpan.Zero, // handled inside LifetimeValidator

                RequireSignedTokens = true,

                LifetimeValidator = (notBefore, expires, securityToken, parameters) =>
                {
                    var now = DateTimeOffset.UtcNow;

                    // (a) exp presence check
                    if (_jwtSettings.RequireExpiration && expires == null)
                        return false;

                    // (b) reject tokens where nbf is after exp (malformed)
                    if (notBefore.HasValue && expires.HasValue && notBefore.Value > expires.Value)
                        return false;

                    // (c) exp validity check
                    if (expires != null && now > new DateTimeOffset(expires.Value) + clockSkew)
                        return false;

                    // (d) nbf enforcement
                    if (_jwtSettings.ValidateNbf && notBefore != null)
                    {
                        if (new DateTimeOffset(notBefore.Value) - clockSkew > now)
                            return false;
                    }

                    return true;
                },
            }, out _);

            return principal;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public UserTokenPayload? ReadTokenPayload(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token)) return null;

        try
        {
            var jwt = handler.ReadJwtToken(token);
            return new UserTokenPayload
            {
                UserId = jwt.Claims.FirstOrDefault(c => c.Type == "identity")?.Value ?? 
                         jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value ?? "",
                Username = jwt.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value ?? 
                           jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)?.Value ?? "",
                Email = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value,
                PhoneNumber = jwt.Claims.FirstOrDefault(c => c.Type == "phone_number")?.Value,
                PrimaryRole = int.TryParse(
                    jwt.Claims.FirstOrDefault(c => c.Type == "primary_role")?.Value, out var r) ? r : 0,
                Roles = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList(),
                Permissions = jwt.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToList(),
            };
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Dictionary<string, object> ExtractTokenClaims(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            
            var claimsDict = new Dictionary<string, object>();
            
            foreach (var claim in jwtToken.Claims)
            {
                // Group multiple claims with the same type into arrays
                if (claimsDict.ContainsKey(claim.Type))
                {
                    if (claimsDict[claim.Type] is List<string> list)
                    {
                        list.Add(claim.Value);
                    }
                    else
                    {
                        var existingValue = claimsDict[claim.Type].ToString();
                        claimsDict[claim.Type] = new List<string> { existingValue!, claim.Value };
                    }
                }
                else
                {
                    claimsDict[claim.Type] = claim.Value;
                }
            }
            
            return claimsDict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting token claims");
            return new Dictionary<string, object>();
        }
    }

    /// <inheritdoc />
    public string GenerateRandomToken(int byteLength = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes);
    }
}