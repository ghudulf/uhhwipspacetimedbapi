using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for managing user authentication settings (TOTP enabled, WebAuthn enabled, etc.)
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// Retrieves user settings or creates default settings if none exist
        /// </summary>
        /// <param name="userId">The user identity</param>
        /// <returns>The user settings</returns>
        Task<UserSettings> GetOrCreateUserSettingsAsync(Identity userId);

        /// <summary>
        /// Marks TOTP as enabled for the user
        /// </summary>
        /// <param name="userId">The user identity</param>
        /// <returns>True if the operation was successful</returns>
        Task<bool> EnableTotpAsync(Identity userId);

        /// <summary>
        /// Marks TOTP as disabled for the user
        /// </summary>
        /// <param name="userId">The user identity</param>
        /// <returns>True if the operation was successful</returns>
        Task<bool> DisableTotpAsync(Identity userId);

        /// <summary>
        /// Marks WebAuthn as enabled for the user
        /// </summary>
        /// <param name="userId">The user identity</param>
        /// <returns>True if the operation was successful</returns>
        Task<bool> EnableWebAuthnAsync(Identity userId);

        /// <summary>
        /// Marks WebAuthn as disabled for the user
        /// </summary>
        /// <param name="userId">The user identity</param>
        /// <returns>True if the operation was successful</returns>
        Task<bool> DisableWebAuthnAsync(Identity userId);

        /// <summary>
        /// Updates user settings with new values
        /// </summary>
        /// <param name="userId">The user identity</param>
        /// <param name="settings">The updated settings</param>
        /// <returns>True if the operation was successful</returns>
        Task<bool> UpdateSettingsAsync(Identity userId, UserSettings settings);
    }
}
