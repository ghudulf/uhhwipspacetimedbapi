using System;
using TicketSalesApp.AdminServer.Models;
using SpacetimeDB.Types;

namespace TicketSalesApp.AdminServer.Mappers
{
    /// <summary>
    /// Provides mapping functionality to convert user entities to safe DTOs.
    /// </summary>
    public static class UserMapper
    {
        /// <summary>
        /// Converts a UserProfile entity to a SafeUserDto, excluding sensitive fields like PasswordHash.
        /// </summary>
        /// <param name="user">The UserProfile entity to convert.</param>
        /// <returns>A SafeUserDto containing safe user properties with UserId normalized to string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when user parameter is null.</exception>
        /// <exception cref="ArgumentException">Thrown when mapping fails due to invalid or missing data.</exception>
        public static SafeUserDto MapToSafeUserDto(UserProfile user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user), "User entity cannot be null");
            }

            try
            {
                return new SafeUserDto
                {
                    LegacyUserId = user.LegacyUserId,
                    UserId = user.UserId.ToString(),
                    Login = user.Login ?? throw new ArgumentException("User Login cannot be null", nameof(user)),
                    Email = user.Email,
                    PhoneNumber = user.PhoneNumber,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt,
                    LegacyGuid = user.LegacyGuid,
                    EmailConfirmed = user.EmailConfirmed ?? false
                };
            }
            catch (ArgumentException)
            {
                // Re-throw ArgumentException as-is
                throw;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to map UserProfile to SafeUserDto: {ex.Message}", nameof(user), ex);
            }
        }
    }
}
