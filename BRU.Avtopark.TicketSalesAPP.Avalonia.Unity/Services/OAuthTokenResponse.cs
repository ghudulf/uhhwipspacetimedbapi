using System;
using System.Text.Json.Serialization;
namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services;
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
