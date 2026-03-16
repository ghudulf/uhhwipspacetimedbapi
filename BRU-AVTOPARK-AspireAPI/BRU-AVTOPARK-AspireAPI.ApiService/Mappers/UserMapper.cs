using System;
using System.Collections.Generic;
using Serilog;
using TicketSalesApp.AdminServer.Models;
using SpacetimeDB.Types;

namespace TicketSalesApp.AdminServer.Mappers
{
    /// <summary>
    /// Provides mapping functionality to convert user entities to safe DTOs.
    /// </summary>
    public static class UserMapper
    {
        private static readonly Serilog.ILogger _log = Log.ForContext(typeof(UserMapper));

        /// <summary>
        /// Converts a UserProfile entity to a SafeUserDto, excluding sensitive fields like PasswordHash.
        /// Nullable fields are coerced to safe defaults; all applied defaults are logged at Debug level.
        /// </summary>
        /// <param name="user">The UserProfile entity to convert.</param>
        /// <returns>A SafeUserDto containing safe user properties with UserId normalized to string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when user is null.</exception>
        /// <exception cref="ArgumentException">Thrown when Login is missing or mapping fails.</exception>
        public static SafeUserDto MapToSafeUserDto(UserProfile user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user), "User entity cannot be null");

            if (string.IsNullOrWhiteSpace(user.Login))
            {
                _log.Warning("UserMapper.MapToSafeUserDto: Login is null/empty for UserId={UserId}", user.UserId);
                throw new ArgumentException("User Login cannot be null or empty", nameof(user));
            }

            var defaults = new List<string>();

            string? email       = CoerceString(user.Email,       null,   nameof(user.Email),       defaults);
            string? phoneNumber = CoerceString(user.PhoneNumber, null,   nameof(user.PhoneNumber), defaults);
            string? legacyGuid  = CoerceString(user.LegacyGuid,  null,   nameof(user.LegacyGuid),  defaults);
            bool emailConfirmed = user.EmailConfirmed ?? DefaultBool(nameof(user.EmailConfirmed), false, defaults);

            if (defaults.Count > 0)
                _log.Debug("UserMapper.MapToSafeUserDto UserId={UserId}: {Count} nullable field(s) defaulted: {Fields}",
                    user.UserId, defaults.Count, string.Join(", ", defaults));
            else
                _log.Debug("UserMapper.MapToSafeUserDto UserId={UserId}: all fields present", user.UserId);

            try
            {
                return new SafeUserDto
                {
                    LegacyUserId   = user.LegacyUserId,
                    UserId         = user.UserId.ToString(),
                    Login          = user.Login,
                    Email          = email,
                    PhoneNumber    = phoneNumber,
                    IsActive       = user.IsActive,
                    CreatedAt      = user.CreatedAt,
                    LastLoginAt    = user.LastLoginAt,
                    LegacyGuid     = legacyGuid,
                    EmailConfirmed = emailConfirmed
                };
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "UserMapper.MapToSafeUserDto unexpected error for UserId={UserId}", user.UserId);
                throw new InvalidOperationException("Failed to map UserProfile to SafeUserDto.", ex);
            }
        }

        private static string? CoerceString(string? value, string? fallback, string fieldName, List<string> defaults)
        {
            if (value is null) { defaults.Add(fieldName); return fallback; }
            return value;
        }

        private static bool DefaultBool(string fieldName, bool fallback, List<string> defaults)
        {
            defaults.Add(fieldName);
            return fallback;
        }
    }
}