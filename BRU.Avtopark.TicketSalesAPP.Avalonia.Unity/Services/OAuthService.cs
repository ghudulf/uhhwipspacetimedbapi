using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
            
            var requestData = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _redirectUri,
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["code_verifier"] = codeVerifier
            };

            Log.Debug("Token request data: grant_type={GrantType}, redirect_uri={RedirectUri}, client_id={ClientId}", 
                requestData["grant_type"], requestData["redirect_uri"], requestData["client_id"]);

            var content = new FormUrlEncodedContent(requestData);
            
            try
            {
                var response = await _httpClient.PostAsync(_tokenEndpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                Log.Debug("Token endpoint response status: {StatusCode}", response.StatusCode);
                
                if (!response.IsSuccessStatusCode)
                {
                    Log.Error("Token exchange failed with status {StatusCode}: {Response}", response.StatusCode, responseContent);
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
            var requestData = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret
            };

            var content = new FormUrlEncodedContent(requestData);
            var response = await _httpClient.PostAsync(_tokenEndpoint, content);
            
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (tokenResponse != null)
            {
                // Update stored tokens
                await _tokenStorage.SaveTokensAsync(tokenResponse);
            }

            return tokenResponse;
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
    }

    public class OAuthTokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public string TokenType { get; set; } = "Bearer";
        public int ExpiresIn { get; set; }
        public string? IdToken { get; set; }
        public string? Scope { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
