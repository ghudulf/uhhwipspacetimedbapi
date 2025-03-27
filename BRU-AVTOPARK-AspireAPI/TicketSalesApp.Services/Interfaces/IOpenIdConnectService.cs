using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for handling OpenID Connect operations
    /// </summary>
    public interface IOpenIdConnectService
    {
        Task<(bool success, object? application, string? errorMessage)> GetApplicationByClientIdAsync(string clientId);
        Task<(bool success, List<object>? authorizations, string? errorMessage)> GetAuthorizationsAsync(string subject, object application, string status, string type, IEnumerable<string> scopes);
        Task<(bool success, ClaimsIdentity? identity, string? errorMessage)> CreateIdentityFromUserAsync(UserProfile user, IEnumerable<string> scopes);
        Task<(bool success, object? authorization, string? errorMessage)> CreateAuthorizationAsync(ClaimsIdentity identity, string subject, object application, string type, IEnumerable<string> scopes);
        Task<(bool success, string? id, string? errorMessage)> GetAuthorizationIdAsync(object authorization);
        Task<(bool success, List<string>? resources, string? errorMessage)> GetResourcesAsync(IEnumerable<string> scopes);
        Task<(bool success, string? errorMessage)> RegisterClientApplicationAsync(string clientId, string clientSecret, string displayName, string[] redirectUris, string[] postLogoutRedirectUris, string[] allowedScopes, bool requireConsent);
        Task<(bool success, string? errorMessage)> UpdateClientApplicationAsync(string clientId, string? clientSecret, string? displayName, string[]? redirectUris, string[]? postLogoutRedirectUris, string[]? allowedScopes, bool? requireConsent);
        Task<(bool success, string? errorMessage)> DeleteClientApplicationAsync(string clientId);
        Task<(bool success, List<object>? applications, string? errorMessage)> GetAllClientApplicationsAsync();
        Task<(bool success, object? application, string? errorMessage)> GetClientApplicationAsync(string clientId);
        IEnumerable<string> GetDestinations(Claim claim);
    }
}

