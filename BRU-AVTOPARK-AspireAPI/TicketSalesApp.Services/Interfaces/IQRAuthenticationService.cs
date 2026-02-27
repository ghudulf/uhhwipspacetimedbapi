using System;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{ 
    public interface IQRAuthenticationService
    {
        Task<string> GenerateQRLoginTokenAsync(UserProfile user);
        Task<(bool success, UserProfile? user)> ValidateQRLoginTokenAsync(string token);
        Task<string> GenerateQRCodeAsync(UserProfile user);
        Task<(string qrCode, string rawData)> GenerateQRCodeWithDataAsync(UserProfile user);

        // Direct QR login methods
        Task<(string qrCode, string rawData)> GenerateDirectLoginQRCodeAsync(string username, string deviceType);
        Task<(bool success, UserProfile? user, string deviceId)> ValidateDirectLoginTokenAsync(string token, string deviceType);
        Task<bool> NotifyDeviceLoginSuccessAsync(string deviceId, string token);
    }
}