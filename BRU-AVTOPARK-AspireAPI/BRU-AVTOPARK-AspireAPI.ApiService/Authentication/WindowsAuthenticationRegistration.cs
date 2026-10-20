using Microsoft.AspNetCore.Authentication.Negotiate;

namespace TicketSalesApp.AdminServer.Authentication;

public static class WindowsAuthenticationRegistration
{
    public static IServiceCollection AddWindowsAccountAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Authentication:Windows:Enabled")) return services;
        var realm = configuration["Authentication:Windows:Realm"];
        if (string.IsNullOrWhiteSpace(realm) || realm.Length > 512 || realm.Contains('\n') || realm.Contains('\r'))
            throw new InvalidOperationException("Authentication:Windows:Realm must identify the trusted domain.");
        // An explicit scheme avoids changing existing JWT, cookie and OpenIddict defaults.
        services.AddAuthentication().AddNegotiate(NegotiateDefaults.AuthenticationScheme, _ => { });
        return services;
    }
}
