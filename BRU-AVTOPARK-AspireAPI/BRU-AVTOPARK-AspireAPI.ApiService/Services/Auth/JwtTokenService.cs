using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Services.Auth
{
    /// <summary>
    /// Concrete implementation of <see cref="ITokenService"/> that produces
    /// HMAC-SHA256 signed JWTs and cryptographic random tokens.
    /// All SpacetimeDB interaction is handled through <see cref="ISpacetimeDBService"/>.
    /// </summary>
    public sealed class JwtTokenService : ITokenService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IConfiguration _configuration;
        private readonly SymmetricSecurityKey _signingKey;
        private readonly ILogger<JwtTokenService> _logger;

        public JwtTokenService(
            ISpacetimeDBService spacetimeService,
            IConfiguration configuration,
            SymmetricSecurityKey signingKey,
            ILogger<JwtTokenService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _signingKey = signingKey ?? throw new ArgumentNullException(nameof(signingKey));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string GenerateJwtToken(UserProfile userProfile)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var expirationMinutes = double.Parse(
                _configuration["JwtSettings:ExpirationInMinutes"] ?? "120");

            var conn = _spacetimeService.GetConnection();

            // Resolve roles
            var userRoles = conn.Db.UserRole.Iter()
                .Where(ur => ur.UserId.Equals(userProfile.UserId))
                .Select(ur => ur.RoleId)
                .ToList();

            var roles = conn.Db.Role.Iter()
                .Where(r => userRoles.Contains(r.RoleId) && r.IsActive)
                .ToList();

            // Resolve permissions
            var rolePermissionIds = conn.Db.RolePermission.Iter()
                .Where(rp => userRoles.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToList();

            var permissions = conn.Db.Permission.Iter()
                .Where(p => rolePermissionIds.Contains(p.PermissionId) && p.IsActive)
                .ToList();

            // Build claims list
            var claims = new List<Claim>
            {
                new("unique_name", userProfile.Login),
                new(ClaimTypes.Name, userProfile.Login),
                new("sub", userProfile.LegacyUserId.ToString()),
                new("identity", userProfile.UserId.ToString()),
                new("xuid", userProfile.Xuid?.ToString() ?? ""),
                new("token_usage", "access_token"),
                new("oi_tkn_id", Guid.NewGuid().ToString())
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Name));
                claims.Add(new Claim("role", role.LegacyRoleId.ToString()));
            }

            foreach (var permission in permissions)
            {
                claims.Add(new Claim("permission", permission.Name));
            }

            var highestPriorityRole = roles.OrderByDescending(r => r.Priority).FirstOrDefault();
            if (highestPriorityRole != null)
            {
                claims.Add(new Claim("primary_role", highestPriorityRole.LegacyRoleId.ToString()));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
                Issuer = "https://localhost:5001",
                Audience = "https://localhost:5001",
                SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);

            _logger.LogInformation(
                "Generated JWT for user {Username} with {RoleCount} roles and {PermissionCount} permissions",
                userProfile.Login, roles.Count, permissions.Count);

            return tokenHandler.WriteToken(token);
        }

        /// <inheritdoc />
        public Dictionary<string, object> ExtractTokenClaims(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                var result = new Dictionary<string, object>();

                foreach (var claim in jwtToken.Claims)
                {
                    if (result.TryGetValue(claim.Type, out var existing))
                    {
                        if (existing is List<string> list)
                        {
                            list.Add(claim.Value);
                        }
                        else
                        {
                            result[claim.Type] = new List<string> { existing.ToString()!, claim.Value };
                        }
                    }
                    else
                    {
                        result[claim.Type] = claim.Value;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting token claims");
                return new Dictionary<string, object>();
            }
        }

        /// <inheritdoc />
        public string GenerateTemporaryRegistrationToken()
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var keyBytes = Encoding.ASCII.GetBytes(
                _configuration["Jwt:Key"] ?? "DefaultSecureKeyForTemporaryRegistrationToken");
            var key = new SymmetricSecurityKey(keyBytes);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Iss, "temporary-registration-issuer"),
                new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        /// <inheritdoc />
        public string GenerateRandomToken()
        {
            var randomBytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }

        /// <inheritdoc />
        public ClaimsPrincipal? ValidateToken(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _signingKey,
                    ValidateIssuer = true,
                    ValidIssuer = "https://localhost:5001",
                    ValidateAudience = true,
                    ValidAudience = "https://localhost:5001",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                return tokenHandler.ValidateToken(token, validationParameters, out _);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token validation failed");
                return null;
            }
        }
    }
}
