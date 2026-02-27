using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for handling TOTP (Time-based One-Time Password) operations
    /// </summary>
    public interface ITotpService
    {
        Task<(bool success, string? secretKey, string? qrCodeUri, string? errorMessage)> SetupTotpAsync(Identity userId, string username);
        Task<(bool success, string? errorMessage)> EnableTotpAsync(Identity userId, string verificationCode, string secretKey);
        Task<(bool success, string? errorMessage)> DisableTotpAsync(Identity userId);
        Task<(bool success, string? errorMessage)> ValidateTotpAsync(Identity userId, string code);
        Task<(bool success, string? errorMessage)> ValidateTotpWithTokenAsync(string tempToken, string code);
        Task<bool> IsTotpEnabledAsync(Identity userId);
        Task<string> GenerateTotpSecretKeyAsync();
        string GenerateTotpQrCodeUri(string username, string secretKey);
        bool VerifyTotpCode(string secretKey, string code);
    }
}

