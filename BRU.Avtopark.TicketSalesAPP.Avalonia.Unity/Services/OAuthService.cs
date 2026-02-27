using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Linq;
using Serilog;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services
{
    public class OAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _authorizationEndpoint;
        private readonly string _tokenEndpoint;
        private readonly string _redirectUri;
        private readonly TokenStorageService _tokenStorage;

        public OAuthService(
            string clientId,
            string clientSecret,
            string authorizationEndpoint,
            string tokenEndpoint,
            string redirectUri,
            TokenStorageService tokenStorage)
        {
            _httpClient = new HttpClient();
            _clientId = clientId;
            _clientSecret = clientSecret;
            _authorizationEndpoint = authorizationEndpoint;
            _tokenEndpoint = tokenEndpoint;
            _redirectUri = redirectUri;
            _tokenStorage = tokenStorage;
        }

        public string GenerateAuthorizationUrl(string[] scopes, out string state, out string codeVerifier)
        {
            // Generate state for CSRF protection
            state = GenerateRandomString(32);

            // Generate PKCE code verifier and challenge
            codeVerifier = GenerateRandomString(64);
            var codeChallenge = GenerateCodeChallenge(codeVerifier);

            var scopeString = string.Join(" ", scopes);
            var queryParams = new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["response_type"] = "code",
                ["redirect_uri"] = _redirectUri,
                ["scope"] = scopeString,
                ["state"] = state,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256"
            };

            var queryString = string.Join("&", queryParams.Select(kvp => 
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            return $"{_authorizationEndpoint}?{queryString}";
        }

        public async Task<OAuthTokenResponse> ExchangeCodeForTokenAsync(string code, string codeVerifier)
        {
            Log.Information("Exchanging authorization code for tokens");
            Log.Debug("Token endpoint: {Endpoint}", _tokenEndpoint);
            Log.Debug("Code length: {Length}, CodeVerifier length: {VerifierLength}", code.Length, codeVerifier.Length);
            
            // CRITICAL: For public clients (desktop/mobile apps), DO NOT send client_secret
            // Public clients use PKCE (code_verifier) for security instead of client secrets
            // Sending a client_secret makes OpenIddict treat this as a confidential client
            var requestData = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _redirectUri,
                ["client_id"] = _clientId,
                // ["client_secret"] = _clientSecret,  // REMOVED: Public clients don't send secrets
                ["code_verifier"] = codeVerifier  // PKCE provides security for public clients
            };

            Log.Debug("Token request data: grant_type={GrantType}, redirect_uri={RedirectUri}, client_id={ClientId}", 
                requestData["grant_type"], requestData["redirect_uri"], requestData["client_id"]);
            Log.Debug("Using PKCE code_verifier for public client authentication");

            var content = new FormUrlEncodedContent(requestData);
            
            try
            {
                var response = await _httpClient.PostAsync(_tokenEndpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                Log.Debug("Token endpoint response status: {StatusCode}", response.StatusCode);
                
                if (!response.IsSuccessStatusCode)
                {
                    Log.Error("Token exchange failed with status {StatusCode}: {Response}", response.StatusCode, responseContent);
                    
                    // Check if this is an authorization-related error
                    if (responseContent.Contains("authorization") || 
                        responseContent.Contains("invalid_grant") ||
                        responseContent.Contains("authorization_pending") ||
                        response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        Log.Warning("Authorization error detected, clearing stored authorization data to allow retry");
                        // Clear any stored authorization state that might be causing issues
                        await _tokenStorage.ClearTokensAsync();
                        
                        // Throw a specific exception that the caller can catch and retry
                        throw new OAuthAuthorizationException($"Authorization failed: {responseContent}. Stored data cleared, please retry the authorization flow.");
                    }
                    
                    throw new Exception($"Token exchange failed: {responseContent}");
                }

                Log.Debug("Token response content: {Content}", responseContent);

                var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (tokenResponse != null)
                {
                    Log.Information("Token deserialized successfully, saving to storage");
                    tokenResponse.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
                    
                    // LOGGING: Parse and log OAuth token claims
                    LogTokenClaims(tokenResponse.AccessToken, "OAuth Access Token");
                    
                    if (!string.IsNullOrEmpty(tokenResponse.IdToken))
                    {
                        LogTokenClaims(tokenResponse.IdToken, "OAuth ID Token");
                    }
                    
                    // Store tokens persistently
                    await _tokenStorage.SaveTokensAsync(tokenResponse);
                    Log.Debug("Tokens saved to storage");
                }
                else
                {
                    Log.Error("Failed to deserialize token response");
                }

                return tokenResponse ?? throw new Exception("Failed to deserialize token response");
            }
            catch (OAuthAuthorizationException)
            {
                // Re-throw authorization exceptions without wrapping
                throw;
            }
            catch (HttpRequestException ex)
            {
                Log.Error(ex, "HTTP request error during token exchange: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error during token exchange: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<OAuthTokenResponse?> RefreshTokenAsync(string refreshToken)
        {
            Log.Information("Attempting to refresh access token");
            
            // CRITICAL: For public clients, DO NOT send client_secret
            // Public clients are authenticated by the refresh token itself
            var requestData = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _clientId
                // ["client_secret"] = _clientSecret  // REMOVED: Public clients don't send secrets
            };

            var content = new FormUrlEncodedContent(requestData);
            
            try
            {
                var response = await _httpClient.PostAsync(_tokenEndpoint, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Warning("Token refresh failed with status {StatusCode}: {Error}", response.StatusCode, errorContent);
                    
                    // If refresh token is invalid or expired, clear stored tokens
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || 
                        response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        Log.Warning("Refresh token is invalid or expired, clearing stored tokens");
                        await _tokenStorage.ClearTokensAsync();
                    }
                    
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (tokenResponse != null)
                {
                    tokenResponse.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
                    
                    // LOGGING: Parse and log refreshed token claims
                    LogTokenClaims(tokenResponse.AccessToken, "Refreshed OAuth Access Token");
                    
                    // Update stored tokens
                    await _tokenStorage.SaveTokensAsync(tokenResponse);
                    Log.Information("Token refreshed successfully");
                }

                return tokenResponse;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error refreshing token: {Message}", ex.Message);
                return null;
            }
        }

        public async Task<string?> GetValidAccessTokenAsync()
        {
            var tokens = await _tokenStorage.GetTokensAsync();
            if (tokens == null)
            {
                return null;
            }

            // Check if access token is still valid (with 5 minute buffer)
            if (tokens.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            {
                return tokens.AccessToken;
            }

            // Try to refresh the token
            if (!string.IsNullOrEmpty(tokens.RefreshToken))
            {
                var refreshedTokens = await RefreshTokenAsync(tokens.RefreshToken);
                return refreshedTokens?.AccessToken;
            }

            return null;
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            var token = await GetValidAccessTokenAsync();
            return !string.IsNullOrEmpty(token);
        }

        public async Task LogoutAsync()
        {
            await _tokenStorage.ClearTokensAsync();
        }

        private string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
            var random = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(random);
            }
            return new string(random.Select(b => chars[b % chars.Length]).ToArray());
        }

        private string GenerateCodeChallenge(string codeVerifier)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            return Convert.ToBase64String(hash)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Parses and logs JWT token claims for debugging and auditing
        /// </summary>
        private void LogTokenClaims(string token, string tokenType)
        {
            try
            {
                Log.Information("=== {TokenType} Claims ===", tokenType);
                
                // Parse JWT token (format: header.payload.signature)
                var parts = token.Split('.');
                if (parts.Length != 3)
                {
                    Log.Warning("Invalid JWT token format for {TokenType}", tokenType);
                    return;
                }
                
                // Decode payload (Base64Url encoded)
                var payload = parts[1];
                
                // Add padding if needed for Base64 decoding
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                
                // Replace URL-safe characters
                payload = payload.Replace('-', '+').Replace('_', '/');
                
                // Decode and parse JSON
                var payloadBytes = Convert.FromBase64String(payload);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);
                
                Log.Debug("Token payload JSON: {Payload}", payloadJson);
                
                // Parse as JSON document for structured logging
                using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;
                
                // Log standard claims
                if (root.TryGetProperty("sub", out var sub))
                    Log.Information("  Subject (sub): {Value}", sub.GetString());
                
                if (root.TryGetProperty("name", out var name))
                    Log.Information("  Name: {Value}", name.GetString());
                
                if (root.TryGetProperty("email", out var email))
                    Log.Information("  Email: {Value}", email.GetString());
                
                if (root.TryGetProperty("email_verified", out var emailVerified))
                    Log.Information("  Email Verified: {Value}", emailVerified.GetString());
                
                if (root.TryGetProperty("phone_number", out var phone))
                    Log.Information("  Phone: {Value}", phone.GetString());
                
                // Log roles
                if (root.TryGetProperty("role", out var roles))
                {
                    if (roles.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var roleList = new List<string>();
                        foreach (var role in roles.EnumerateArray())
                        {
                            roleList.Add(role.GetString() ?? "");
                        }
                        Log.Information("  Roles: {Roles}", string.Join(", ", roleList));
                    }
                    else
                    {
                        Log.Information("  Role: {Role}", roles.GetString());
                    }
                }
                
                // Log permissions
                if (root.TryGetProperty("permission", out var permissions))
                {
                    if (permissions.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var permList = new List<string>();
                        foreach (var perm in permissions.EnumerateArray())
                        {
                            permList.Add(perm.GetString() ?? "");
                        }
                        Log.Information("  Permissions: {Permissions}", string.Join(", ", permList));
                    }
                    else
                    {
                        Log.Information("  Permission: {Permission}", permissions.GetString());
                    }
                }
                
                // Log custom claims
                if (root.TryGetProperty("primary_role", out var primaryRole))
                    Log.Information("  Primary Role: {Value}", primaryRole.GetString());
                
                if (root.TryGetProperty("identity", out var identity))
                    Log.Information("  Identity: {Value}", identity.GetString());
                
                if (root.TryGetProperty("xuid", out var xuid))
                    Log.Information("  XUID: {Value}", xuid.GetString());
                
                // Log token metadata
                if (root.TryGetProperty("iat", out var iat))
                {
                    var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat.GetInt64());
                    Log.Information("  Issued At: {Value}", issuedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                }
                
                if (root.TryGetProperty("exp", out var exp))
                {
                    var expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
                    Log.Information("  Expires At: {Value}", expiresAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                }
                
                if (root.TryGetProperty("iss", out var issuer))
                    Log.Information("  Issuer: {Value}", issuer.GetString());
                
                if (root.TryGetProperty("aud", out var audience))
                    Log.Information("  Audience: {Value}", audience.GetString());
                
                // Log scopes
                if (root.TryGetProperty("scope", out var scope))
                    Log.Information("  Scopes: {Value}", scope.GetString());
                
                Log.Information("=== End {TokenType} Claims ===", tokenType);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error parsing {TokenType} claims: {Message}", tokenType, ex.Message);
            }
        }
    }

    public class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
        
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";
        
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        
        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
        
        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
        
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// Exception thrown when OAuth authorization fails and stored data needs to be cleared for retry
    /// </summary>
    public class OAuthAuthorizationException : Exception
    {
        public OAuthAuthorizationException(string message) : base(message)
        {
        }

        public OAuthAuthorizationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
