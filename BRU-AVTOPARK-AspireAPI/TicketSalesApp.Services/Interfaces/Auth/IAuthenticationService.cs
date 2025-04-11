// Services/Interfaces/IAuthenticationService.cs
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IAuthenticationService
    {
        Task<UserProfile?> AuthenticateAsync(string login, string password);
        Task<bool> RegisterAsync(string login, string password, int role, string? email = null, string? phoneNumber = null, Identity? actingUser = null, string? newUserIdentity = null);
        Task<UserProfile?> AuthenticateDirectQRAsync(string login, string validationToken);
        int GetUserRole(Identity userId);
        Task<Identity?> GetUserIdentityByLoginAsync(string login);
    }
}