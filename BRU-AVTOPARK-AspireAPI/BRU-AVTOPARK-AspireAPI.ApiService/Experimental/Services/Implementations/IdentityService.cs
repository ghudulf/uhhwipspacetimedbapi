using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SpacetimeDB.Types;
using TicketSalesApp.Services.client.module_bindings;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Handles SpacetimeDB identity generation and retrieval.
/// Extracted from AuthController helper methods.
/// </summary>
public sealed class IdentityService : IIdentityService
{
    private readonly ISpacetimeDBService _spacetimeService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentityService> _logger;

    public IdentityService(
        ISpacetimeDBService spacetimeService,
        IConfiguration configuration,
        ILogger<IdentityService> logger)
    {
        _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> GenerateIdentityAsync()
    {
        try
        {
            _logger.LogDebug("Creating new HttpClient instance with SSL disabled");

            // Create handler that explicitly disables SSL certificate validation
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                SslProtocols = System.Security.Authentication.SslProtocols.None,
                CheckCertificateRevocationList = false
            };

            // Create client with the custom handler
            using var client = new HttpClient(handler);

            // Generate JWT token for registration to authenticate with SpacetimeDB
            var registrationJwt = await GenerateJwtForRegistrationAsync();
            _logger.LogDebug("Generated JWT for SpacetimeDB identity request");

            // Create request message with JWT authentication
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri("http://localhost:3000/v1/identity"),
                Headers = { { "Authorization", $"Bearer {registrationJwt}" } }
            };

            // Explicitly set HTTP/1.1 to avoid HTTP/2 which might try to use SSL
            request.Version = new System.Version(1, 1);

            _logger.LogInformation("Sending POST request to generate identity with HTTP/1.1 and JWT authentication");
            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to generate identity, response status code: {StatusCode}", response.StatusCode);
                var errorMessage = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error message from response: {ErrorMessage}", errorMessage);
                return null;
            }

            _logger.LogInformation("Successfully received response for identity generation");
            var jsonResponse = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Raw JSON response: {JsonResponse}", jsonResponse);

            try
            {
                _logger.LogDebug("Attempting to deserialize JSON response");
                var identityResponse = JsonSerializer.Deserialize<IdentityResponse>(jsonResponse);

                if (identityResponse == null || string.IsNullOrEmpty(identityResponse.Identity))
                {
                    _logger.LogError("Received null or empty identity from server");

                    // Try alternative parsing if the structure doesn't match
                    _logger.LogDebug("Attempting alternative JSON parsing");
                    using var document = JsonDocument.Parse(jsonResponse);

                    if (document.RootElement.TryGetProperty("identity", out var identityElement))
                    {
                        var identity = identityElement.GetString();
                        _logger.LogInformation("Identity extracted using alternative parsing: {Identity}", identity);
                        return identity;
                    }

                    return null;
                }

                _logger.LogInformation("Identity generated successfully: {Identity}", identityResponse.Identity);
                return identityResponse.Identity;
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON deserialization failed. Raw response: {JsonResponse}", jsonResponse);

                // Try manual parsing as fallback
                try
                {
                    using var document = JsonDocument.Parse(jsonResponse);
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        _logger.LogDebug("Found JSON property: {PropertyName} with value type: {ValueKind}",
                            property.Name, property.Value.ValueKind);
                    }

                    if (document.RootElement.TryGetProperty("identity", out var identityElement))
                    {
                        var identity = identityElement.GetString();
                        _logger.LogInformation("Identity extracted using manual parsing: {Identity}", identity);
                        return identity;
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogError(parseEx, "Manual JSON parsing also failed");
                }

                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating identity");
            return null;
        }
    }

    /// <inheritdoc />
    public Identity? GetUserIdentity(ClaimsPrincipal user)
    {
        var identityString = user.FindFirst("identity")?.Value;
        if (string.IsNullOrEmpty(identityString))
        {
            return null;
        }

        try
        {
            var conn = _spacetimeService.GetConnection();
            return conn.Db.UserProfile.Iter()
                .FirstOrDefault(u => u.LegacyUserId.ToString() == identityString)?.UserId;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<UserProfile?> GetUserByIdentityAsync(Identity? userId)
    {
        if (userId == null)
            return null;

        var conn = _spacetimeService.GetConnection();
        var user = conn.Db.UserProfile.Iter()
            .FirstOrDefault(u => u.UserId.Equals(userId));

        return user;
    }

    /// <inheritdoc />
    public async Task<string> GenerateJwtForRegistrationAsync()
    {
        _logger.LogInformation("Generating temporary JWT for registration");

        // Create a temporary JWT token handler
        var tokenHandler = new JwtSecurityTokenHandler();

        // Generate a secure key for signing
        var keyBytes = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"] ?? "DefaultSecureKeyForTemporaryRegistrationToken");
        var key = new SymmetricSecurityKey(keyBytes);

        // Create minimal claims for identity generation
        var claims = new List<Claim>
        {
            // Claims for SpacetimeDB identity generation
            new Claim(JwtRegisteredClaimNames.Iss, "temporary-registration-issuer"),
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),

            // Additional claims that might be useful
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        // Create token descriptor with short expiration time
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(5), // Short expiration for security
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
        };

        _logger.LogDebug("Creating temporary JWT token with minimal claims");
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        _logger.LogInformation("Temporary JWT token generated successfully");
        return tokenString;
    }

    private sealed class IdentityResponse
    {
        public string Identity { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
    }
}

