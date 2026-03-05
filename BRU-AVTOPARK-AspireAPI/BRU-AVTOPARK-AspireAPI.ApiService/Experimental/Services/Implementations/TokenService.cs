using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Centralizes all JWT token operations that were previously scattered
/// across the AuthController (GenerateJwtToken, IsAdmin, HasPermission, etc.).
/// Injected as a singleton; the signing key is loaded once from configuration.
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;

    public TokenService(IConfiguration configuration, SymmetricSecurityKey signingKey)
    {
        _signingKey = signingKey ?? throw new ArgumentNullException(nameof(signingKey));
        _issuer = configuration["Jwt:Issuer"] ?? "BRU_AVTOPARK";
        _audience = configuration["Jwt:Audience"] ?? "BRU_AVTOPARK_API";
        _expirationMinutes = int.TryParse(configuration["Jwt:ExpirationMinutes"], out var min) ? min : 1440;
    }

    /// <inheritdoc />
    public string GenerateToken(UserTokenPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, payload.UserId),
            new(JwtRegisteredClaimNames.UniqueName, payload.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("primary_role", payload.PrimaryRole.ToString()),
        };

        if (payload.Email is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, payload.Email));

        if (payload.PhoneNumber is not null)
            claims.Add(new Claim("phone_number", payload.PhoneNumber));

        foreach (var role in payload.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        foreach (var perm in payload.Permissions)
            claims.Add(new Claim("permission", perm));

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc />
    public ClaimsPrincipal? ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token)) return null;

        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                IssuerSigningKey = _signingKey,
                ClockSkew = TimeSpan.FromMinutes(2)
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
                UserId = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value ?? "",
                Username = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)?.Value ?? "",
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
    public string GenerateRandomToken(int byteLength = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes);
    }
}
